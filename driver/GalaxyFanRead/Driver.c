#include <ntddk.h>
#include <wdf.h>
#include <acpiioct.h>
#ifdef GALAXY_FAN_CONTROL
#include <intrin.h>
#include "../FanControl/ControlTypes.h"
#endif
#ifdef GALAXY_FAN_TRIAL
#include <intrin.h>
#include "../FanStepTrial/TrialTypes.h"
#endif

/* Experimental read-only probe. A service-registry sequence requests only
 * three compiled-in reads. No user IOCTL, raw addresses or fan writes. */
DRIVER_INITIALIZE DriverEntry;
EVT_WDF_DRIVER_DEVICE_ADD FanDeviceAdd;
EVT_WDF_DEVICE_D0_ENTRY FanD0Entry;
EVT_WDF_DEVICE_D0_EXIT FanD0Exit;
EVT_WDF_TIMER FanProbeTimer;

typedef struct _FAN_CONTEXT {
    WDFIOTARGET Acpi;
    BOOLEAN Probed;
    BOOLEAN Active;
    ULONG Sequence;
    WDFWAITLOCK TimerLock;
    WDFTIMER Timer;
#ifdef GALAXY_FAN_CONTROL
    CONTROL_CONTEXT Control;
#endif
#ifdef GALAXY_FAN_TRIAL
    TRIAL_RECORD Trial;
    BOOLEAN TrialInitialized;
    BOOLEAN TrialRestoreNeeded;
    ULONG TrialSequence;
#endif
} FAN_CONTEXT;
WDF_DECLARE_CONTEXT_TYPE_WITH_NAME(FAN_CONTEXT, FanContext);

static VOID TraceStage(WDFDEVICE device, ULONG stage, NTSTATUS status)
{
    WDFKEY key;
    UNICODE_STRING name;
    LARGE_INTEGER now;
    if (!NT_SUCCESS(WdfDriverOpenParametersRegistryKey(WdfDeviceGetDriver(device),
        KEY_SET_VALUE, WDF_NO_OBJECT_ATTRIBUTES, &key))) return;
    RtlInitUnicodeString(&name, L"LastStage");
    (VOID)WdfRegistryAssignULong(key, &name, stage);
    RtlInitUnicodeString(&name, L"LastStatus");
    (VOID)WdfRegistryAssignULong(key, &name, (ULONG)status);
    KeQuerySystemTimePrecise(&now);
    RtlInitUnicodeString(&name, L"LastStageTime");
    (VOID)WdfRegistryAssignValue(key, &name, REG_QWORD, sizeof(now), &now);
    WdfRegistryClose(key);
}

static VOID SaveProbe(WDFDEVICE device, NTSTATUS status, ULONG rpm1, ULONG rpm2)
{
    WDFKEY deviceKey, probeKey;
    UNICODE_STRING subkey, value;
    struct {
        ULONG Version;
        ULONG Status;
        ULONG Rpm1;
        ULONG Rpm2;
        LARGE_INTEGER Timestamp;
    } record;
    C_ASSERT(sizeof(record) == 24);
    record.Version = 1;
    record.Status = (ULONG)status;
    record.Rpm1 = NT_SUCCESS(status) ? rpm1 : MAXULONG;
    record.Rpm2 = NT_SUCCESS(status) ? rpm2 : MAXULONG;
    KeQuerySystemTimePrecise(&record.Timestamp);
    /* A second sink distinguishes missing device-key permissions from no query. */
    if (NT_SUCCESS(WdfDriverOpenParametersRegistryKey(WdfDeviceGetDriver(device),
        KEY_SET_VALUE, WDF_NO_OBJECT_ATTRIBUTES, &probeKey))) {
        RtlInitUnicodeString(&value, L"LastProbe");
        (VOID)WdfRegistryAssignValue(probeKey, &value, REG_BINARY, sizeof(record), &record);
        WdfRegistryClose(probeKey);
    }
    if (!NT_SUCCESS(WdfDeviceOpenRegistryKey(device, PLUGPLAY_REGKEY_DEVICE,
        KEY_CREATE_SUB_KEY, WDF_NO_OBJECT_ATTRIBUTES, &deviceKey))) return;
    RtlInitUnicodeString(&subkey, L"GalaxyFanRead");
    if (NT_SUCCESS(WdfRegistryCreateKey(deviceKey, &subkey, KEY_SET_VALUE,
        REG_OPTION_NON_VOLATILE, NULL, WDF_NO_OBJECT_ATTRIBUTES, &probeKey))) {
        RtlInitUnicodeString(&value, L"LastProbe");
        (VOID)WdfRegistryAssignValue(probeKey, &value, REG_BINARY, sizeof(record), &record);
        WdfRegistryClose(probeKey);
    }
    WdfRegistryClose(deviceKey);
}

static BOOLEAN BiosValueEquals(WDFDEVICE device, PCWSTR name, PCWSTR expected)
{
    UNICODE_STRING path, value, actual, wanted;
    OBJECT_ATTRIBUTES attributes;
    HANDLE key;
    WDFKEY diagnosticKey;
    BOOLEAN equal;
    NTSTATUS status;
    ULONG returned = 0;
    union { ULONGLONG Alignment; UCHAR Bytes[512]; } storage;
    PKEY_VALUE_PARTIAL_INFORMATION info = (PVOID)storage.Bytes;
    RtlInitUnicodeString(&path, L"\\Registry\\Machine\\HARDWARE\\DESCRIPTION\\System\\BIOS");
    InitializeObjectAttributes(&attributes, &path, OBJ_KERNEL_HANDLE | OBJ_CASE_INSENSITIVE, NULL, NULL);
    status = ZwOpenKey(&key, KEY_QUERY_VALUE, &attributes);
    TraceStage(device, 111, status);
    if (!NT_SUCCESS(status)) return FALSE;
    RtlInitUnicodeString(&value, name);
    status = ZwQueryValueKey(key, &value, KeyValuePartialInformation, info, sizeof(storage), &returned);
    ZwClose(key);
    TraceStage(device, 112, status);
    if (!NT_SUCCESS(status) || returned < (ULONG)FIELD_OFFSET(KEY_VALUE_PARTIAL_INFORMATION, Data) ||
        info->Type != REG_SZ || info->DataLength < sizeof(WCHAR) ||
        info->DataLength > returned - FIELD_OFFSET(KEY_VALUE_PARTIAL_INFORMATION, Data) ||
        (info->DataLength % sizeof(WCHAR)) != 0) return FALSE;
    actual.Buffer = (PWCHAR)info->Data;
    actual.Length = (USHORT)info->DataLength;
    if (actual.Buffer[actual.Length / sizeof(WCHAR) - 1] == L'\0') actual.Length -= sizeof(WCHAR);
    actual.MaximumLength = actual.Length;
    RtlInitUnicodeString(&wanted, expected);
    equal = RtlEqualUnicodeString(&actual, &wanted, FALSE);
    if (NT_SUCCESS(WdfDriverOpenParametersRegistryKey(WdfDeviceGetDriver(device),
        KEY_SET_VALUE, WDF_NO_OBJECT_ATTRIBUTES, &diagnosticKey))) {
        (VOID)WdfRegistryAssignValue(diagnosticKey, &value, REG_BINARY, info->DataLength, info->Data);
        WdfRegistryClose(diagnosticKey);
    }
    TraceStage(device, 114, equal ? STATUS_SUCCESS : STATUS_NOT_SUPPORTED);
    return equal;
}

static BOOLEAN IsVerifiedDevice(WDFDEVICE device)
{
    WDFMEMORY memory;
    WDF_OBJECT_ATTRIBUTES attributes;
    NTSTATUS status;
    PWCHAR ids;
    size_t bytes, offset, count, length;
    UNICODE_STRING actual, wanted;
    BOOLEAN found = FALSE;
    if (!BiosValueEquals(device, L"SystemProductName", L"Galaxy Book6 Pro - PAMB")) {
        return FALSE;
    }
    if (!BiosValueEquals(device, L"BIOSVersion", L"PAMB.1.5.74.371")) {
        return FALSE;
    }
    WDF_OBJECT_ATTRIBUTES_INIT(&attributes);
    attributes.ParentObject = device;
    status = WdfDeviceAllocAndQueryProperty(device, DevicePropertyHardwareID,
        NonPagedPoolNx, &attributes, &memory);
    if (!NT_SUCCESS(status)) { TraceStage(device, 13, status); return FALSE; }
    ids = WdfMemoryGetBuffer(memory, &bytes);
    count = bytes / sizeof(WCHAR);
    RtlInitUnicodeString(&wanted, L"ACPI\\SAM0430");
    for (offset = 0; offset < count && ids[offset]; offset += length + 1) {
        for (length = 0; length < count - offset && ids[offset + length]; ++length) { }
        if (length == count - offset || length > MAXUSHORT / sizeof(WCHAR)) break;
        actual.Buffer = ids + offset;
        actual.Length = (USHORT)(length * sizeof(WCHAR));
        actual.MaximumLength = actual.Length;
        if (RtlEqualUnicodeString(&actual, &wanted, TRUE)) { found = TRUE; break; }
    }
    WdfObjectDelete(memory);
    TraceStage(device, 14, found ? STATUS_SUCCESS : STATUS_NOT_SUPPORTED);
    return found;
}

static NTSTATUS ParseReply(PACPI_EVAL_OUTPUT_BUFFER output, ULONG_PTR returned, UCHAR subcommand, UCHAR reply[21])
{
    ULONG required = FIELD_OFFSET(ACPI_EVAL_OUTPUT_BUFFER, Argument) + ACPI_METHOD_ARGUMENT_LENGTH(21);
    if (returned < required || returned > 128 ||
        output->Signature != ACPI_EVAL_OUTPUT_BUFFER_SIGNATURE || output->Count != 1 ||
        output->Length < required || output->Length > returned ||
        output->Argument[0].Type != ACPI_METHOD_ARGUMENT_BUFFER ||
        output->Argument[0].DataLength != 21) return STATUS_DATA_ERROR;
    RtlCopyMemory(reply, output->Argument[0].Data, 21);
    if (reply[0] != 0x43 || reply[1] != 0x58 || reply[2] != subcommand ||
        reply[3] != 0 || reply[4] != 0xaa) return STATUS_DATA_ERROR;
    return STATUS_SUCCESS;
}

static VOID SaveQuery(WDFDEVICE device, ULONG query, NTSTATUS transport, NTSTATUS parsed,
    ULONG_PTR returned, const UCHAR packet[21], const UCHAR output[128])
{
    WDFKEY key;
    UNICODE_STRING name;
    const PCWSTR names[] = { L"SupportQuery", L"RpmQuery", L"MaxStepQuery"
#if defined(GALAXY_FAN_TRIAL) || defined(GALAXY_FAN_CONTROL)
        , L"TrialSetQuery", L"TrialAutoQuery"
#endif
#ifdef GALAXY_FAN_CONTROL
        , L"ControlStep1Query", L"ControlStep3Query"
#ifdef GALAXY_FAN_ZERO
        , L"ControlZeroQuery"
#endif
#endif
    };
    struct {
        ULONG Version, Query, TransportStatus, ParsedStatus, Returned, Sequence;
        LARGE_INTEGER Timestamp;
        UCHAR Input[21], Output[128], Padding[3];
    } record;
    C_ASSERT(sizeof(record) == 184);
    RtlZeroMemory(&record, sizeof(record));
    record.Version = 1;
    record.Query = query;
    record.TransportStatus = (ULONG)transport;
    record.ParsedStatus = (ULONG)parsed;
    record.Returned = (ULONG)returned;
    record.Sequence = FanContext(device)->Sequence;
    KeQuerySystemTimePrecise(&record.Timestamp);
    RtlCopyMemory(record.Input, packet, 21);
    RtlCopyMemory(record.Output, output, 128);
    if (!NT_SUCCESS(WdfDriverOpenParametersRegistryKey(WdfDeviceGetDriver(device),
        KEY_SET_VALUE, WDF_NO_OBJECT_ATTRIBUTES, &key))) return;
    RtlInitUnicodeString(&name, names[query]);
    (VOID)WdfRegistryAssignValue(key, &name, REG_BINARY, sizeof(record), &record);
    WdfRegistryClose(key);
}

static NTSTATUS QueryFan(WDFDEVICE device, ULONG query, UCHAR reply[21])
{
    /* Aligned fixed-size storage; exactly one ACPI buffer argument. */
    union { ULONGLONG Alignment; UCHAR Bytes[128]; } inputStorage, outputStorage;
    PACPI_EVAL_INPUT_BUFFER_COMPLEX input = (PVOID)inputStorage.Bytes;
    PACPI_EVAL_OUTPUT_BUFFER output = (PVOID)outputStorage.Bytes;
    WDF_MEMORY_DESCRIPTOR inDescriptor, outDescriptor;
    WDF_REQUEST_SEND_OPTIONS options;
    ULONG inputLength, argumentLength;
    ULONG_PTR returned = 0;
    NTSTATUS status, parsed;
    UCHAR packet[21] = {0x43, 0x58, 0x7a};
#ifdef GALAXY_FAN_CONTROL
#ifdef GALAXY_FAN_ZERO
    if (query > 7) return STATUS_INVALID_PARAMETER;
#else
    if (query > 6) return STATUS_INVALID_PARAMETER;
#endif
    if (query >= 3) {
        packet[2] = 0x2c;
        packet[5] = query == 4 ? 0x80 : (query == 5 ? 1 : (query == 6 ? 3 : 2));
#ifdef GALAXY_FAN_ZERO
        if (query == 7) packet[5] = 0;
#endif
    } else
#elif defined(GALAXY_FAN_TRIAL)
    if (query > 4) return STATUS_INVALID_PARAMETER;
    if (query >= 3) { packet[2] = 0x2c; packet[5] = query == 3 ? 2 : 0x80; }
    else
#else
    if (query > 2) return STATUS_INVALID_PARAMETER;
#endif
    if (query == 0) { packet[5] = 0xbb; packet[6] = 0xaa; }
    else { packet[5] = 0x82; packet[6] = 0xb5; packet[7] = query == 1 ? 0x80 : 0x81; }
    RtlZeroMemory(&inputStorage, sizeof(inputStorage));
    RtlZeroMemory(&outputStorage, sizeof(outputStorage));
    argumentLength = ACPI_METHOD_ARGUMENT_LENGTH(sizeof(packet));
    inputLength = FIELD_OFFSET(ACPI_EVAL_INPUT_BUFFER_COMPLEX, Argument) + argumentLength;
    if (inputLength > sizeof(inputStorage)) return STATUS_BUFFER_TOO_SMALL;
    input->Signature = ACPI_EVAL_INPUT_BUFFER_COMPLEX_SIGNATURE;
    RtlCopyMemory(input->MethodName, "CSFI", 4);
    /* Match Microsoft's DMF AcpiTarget implementation: Size is the whole
     * complex input buffer (the member reference's wording is ambiguous). */
    input->Size = inputLength;
    input->ArgumentCount = 1;
    input->Argument[0].Type = ACPI_METHOD_ARGUMENT_BUFFER;
    input->Argument[0].DataLength = sizeof(packet);
    RtlCopyMemory(input->Argument[0].Data, packet, sizeof(packet));
    WDF_MEMORY_DESCRIPTOR_INIT_BUFFER(&inDescriptor, input, inputLength);
    WDF_MEMORY_DESCRIPTOR_INIT_BUFFER(&outDescriptor, output, sizeof(outputStorage));
    WDF_REQUEST_SEND_OPTIONS_INIT(&options, WDF_REQUEST_SEND_OPTION_TIMEOUT);
    WDF_REQUEST_SEND_OPTIONS_SET_TIMEOUT(&options, WDF_REL_TIMEOUT_IN_SEC(3));
    status = WdfIoTargetSendIoctlSynchronously(FanContext(device)->Acpi, NULL, IOCTL_ACPI_EVAL_METHOD,
        &inDescriptor, &outDescriptor, &options, &returned);
    parsed = NT_SUCCESS(status) ? ParseReply(output, returned, packet[2], reply) : status;
    SaveQuery(device, query, status, parsed, returned, packet, outputStorage.Bytes);
    return parsed;
}

static VOID PerformProbe(WDFDEVICE device)
{
    FAN_CONTEXT* context = FanContext(device);
    UCHAR reply[21];
    NTSTATUS status;
    ULONG rpm1 = MAXULONG, rpm2 = MAXULONG;
    if (context->Acpi == NULL) return;
    TraceStage(device, 30, STATUS_SUCCESS);
    TraceStage(device, 31, STATUS_PENDING);
    status = QueryFan(device, 0, reply);
    TraceStage(device, 32, status);
    /* All three commands are independently observed OEM reads. Capability
     * mismatches remain in diagnostics; they do not suppress the RPM read.
     * A successful RPM response grants no permission to send a write. */
    status = QueryFan(device, 2, reply);
    TraceStage(device, 33, status);
    status = QueryFan(device, 1, reply);
    {
        TraceStage(device, 34, status);
        if (NT_SUCCESS(status) && reply[7] != 2) status = STATUS_DATA_ERROR;
        if (NT_SUCCESS(status)) {
            rpm1 = (ULONG)((reply[8] << 8) | reply[9]);
            rpm2 = (ULONG)((reply[10] << 8) | reply[11]);
            DbgPrintEx(DPFLTR_IHVDRIVER_ID, DPFLTR_ERROR_LEVEL,
                "GalaxyFanRead: RPM1=%lu RPM2=%lu\n",
                rpm1, rpm2);
        }
    }
    DbgPrintEx(DPFLTR_IHVDRIVER_ID, DPFLTR_ERROR_LEVEL, "GalaxyFanRead: probe status=0x%08lx\n", status);
    SaveProbe(device, status, rpm1, rpm2);
    TraceStage(device, 40, status);
    /* A diagnostic failure must not fail the OEM device's power transition. */
}

static NTSTATUS OpenAcpi(WDFDEVICE device)
{
    FAN_CONTEXT* context = FanContext(device);
    WDF_OBJECT_ATTRIBUTES attributes;
    WDF_IO_TARGET_OPEN_PARAMS open;
    NTSTATUS status;
    WDF_OBJECT_ATTRIBUTES_INIT(&attributes);
    attributes.ParentObject = device;
    status = WdfIoTargetCreate(device, &attributes, &context->Acpi);
    TraceStage(device, 20, status);
    if (!NT_SUCCESS(status)) { context->Acpi = NULL; return status; }
    WDF_IO_TARGET_OPEN_PARAMS_INIT_EXISTING_DEVICE(&open, WdfDeviceWdmGetPhysicalDevice(device));
    status = WdfIoTargetOpen(context->Acpi, &open);
    TraceStage(device, 21, status);
    if (!NT_SUCCESS(status)) { WdfObjectDelete(context->Acpi); context->Acpi = NULL; }
    return status;
}
static VOID CloseAcpi(WDFDEVICE device)
{
    FAN_CONTEXT* context = FanContext(device);
    WdfIoTargetClose(context->Acpi);
    WdfObjectDelete(context->Acpi);
    context->Acpi = NULL;
}
#ifdef GALAXY_FAN_TRIAL
#include "../FanStepTrial/Trial.inc"
#endif
#ifdef GALAXY_FAN_CONTROL
#include "../FanControl/Control.inc"
#endif

VOID FanProbeTimer(WDFTIMER timer)
{
    WDFDEVICE device = (WDFDEVICE)WdfTimerGetParentObject(timer);
    FAN_CONTEXT* context = FanContext(device);
    WDFKEY key;
    UNICODE_STRING name;
    ULONG sequence = 0;
    NTSTATUS status;
    BOOLEAN active;
    BOOLEAN probeRequested;
#ifdef GALAXY_FAN_CONTROL
    BOOLEAN controlWork = context->Control.Manual || context->Control.NeedsRestore;
#endif
#ifdef GALAXY_FAN_TRIAL
    ULONG trialSequence = 0;
    BOOLEAN trialRequested = FALSE;
#endif
    WdfWaitLockAcquire(context->TimerLock, NULL);
    active = context->Active;
    WdfWaitLockRelease(context->TimerLock);
    if (!active) return;
    if (NT_SUCCESS(WdfDriverOpenParametersRegistryKey(WdfDeviceGetDriver(device),
        KEY_QUERY_VALUE, WDF_NO_OBJECT_ATTRIBUTES, &key))) {
        RtlInitUnicodeString(&name, L"ProbeRequestSequence");
        (VOID)WdfRegistryQueryULong(key, &name, &sequence);
#ifdef GALAXY_FAN_CONTROL
        controlWork = ControlMailbox(device, key);
#endif
#ifdef GALAXY_FAN_TRIAL
        RtlInitUnicodeString(&name, L"FanTrialRequestSequence");
        (VOID)WdfRegistryQueryULong(key, &name, &trialSequence);
        if (context->TrialInitialized) {
            trialRequested = trialSequence != context->TrialSequence;
        } else {
            /* Ignore persisted requests at boot; writes require a new request. */
            context->TrialSequence = trialSequence;
            context->TrialInitialized = TRUE;
            {
                WDFKEY readyKey;
                LARGE_INTEGER now;
                if (NT_SUCCESS(WdfDriverOpenParametersRegistryKey(WdfDeviceGetDriver(device),
                    KEY_SET_VALUE, WDF_NO_OBJECT_ATTRIBUTES, &readyKey))) {
                    KeQuerySystemTimePrecise(&now);
                    RtlInitUnicodeString(&name, L"FanTrialReadyTime");
                    (VOID)WdfRegistryAssignValue(readyKey, &name, REG_QWORD, sizeof(now), &now);
                    WdfRegistryClose(readyKey);
                }
            }
        }
#endif
        WdfRegistryClose(key);
    }
    probeRequested = !context->Probed || sequence != context->Sequence;
    if (!probeRequested
#ifdef GALAXY_FAN_CONTROL
        && !controlWork
#endif
#ifdef GALAXY_FAN_TRIAL
        && !trialRequested && !context->TrialRestoreNeeded
#endif
        ) goto Rearm;
    context->Sequence = sequence;
    context->Probed = TRUE;
    /* BIOS registry values may not be published yet during early DeviceAdd.
     * Preserve the exact allowlist and validate before every requested read. */
    if (!IsVerifiedDevice(device)) {
        SaveProbe(device, STATUS_NOT_SUPPORTED, MAXULONG, MAXULONG);
        goto Complete;
    }
    status = OpenAcpi(device);
    if (!NT_SUCCESS(status)) { SaveProbe(device, status, MAXULONG, MAXULONG); goto Complete; }
    if (probeRequested) PerformProbe(device);
#ifdef GALAXY_FAN_CONTROL
    if (controlWork) ControlTick(device);
#endif
#ifdef GALAXY_FAN_TRIAL
    if (context->TrialRestoreNeeded) {
        TrialRestore(device);
        /* A rejected request must never become a delayed surprise write. */
        if (trialRequested) context->TrialSequence = trialSequence;
    } else if (trialRequested) {
        context->TrialSequence = trialSequence;
        RunFanTrial(device);
    }
#endif
    CloseAcpi(device);
Complete:
    /* Publish acknowledgement last, after all result writes. */
    if (NT_SUCCESS(WdfDriverOpenParametersRegistryKey(WdfDeviceGetDriver(device),
        KEY_SET_VALUE, WDF_NO_OBJECT_ATTRIBUTES, &key))) {
        RtlInitUnicodeString(&name, L"ProbeCompletedSequence");
        (VOID)WdfRegistryAssignULong(key, &name, sequence);
        WdfRegistryClose(key);
    }
Rearm:
    /* Share this lock with D0Exit so no callback can rearm after stop.
     * Idle ticks read only our registry mailbox, not firmware. */
    WdfWaitLockAcquire(context->TimerLock, NULL);
    if (context->Active) WdfTimerStart(timer,
#ifdef GALAXY_FAN_CONTROL
        WDF_REL_TIMEOUT_IN_SEC(1)
#else
        WDF_REL_TIMEOUT_IN_SEC(2)
#endif
    );
    WdfWaitLockRelease(context->TimerLock);
}

NTSTATUS FanD0Entry(WDFDEVICE device, WDF_POWER_DEVICE_STATE previous)
{
    FAN_CONTEXT* context = FanContext(device);
    UNREFERENCED_PARAMETER(previous);
    if (context->Timer != NULL) {
        WdfWaitLockAcquire(context->TimerLock, NULL);
        context->Active = TRUE;
        WdfTimerStart(context->Timer, WDF_REL_TIMEOUT_IN_SEC(30));
        WdfWaitLockRelease(context->TimerLock);
    }
    return STATUS_SUCCESS;
}

NTSTATUS FanD0Exit(WDFDEVICE device, WDF_POWER_DEVICE_STATE target)
{
    FAN_CONTEXT* context = FanContext(device);
    UNREFERENCED_PARAMETER(target);
    /* Drain any query before the ACPI device leaves D0. */
    if (context->Timer != NULL) {
        WdfWaitLockAcquire(context->TimerLock, NULL);
        context->Active = FALSE;
        WdfWaitLockRelease(context->TimerLock);
        WdfTimerStop(context->Timer, TRUE);
    }
#ifdef GALAXY_FAN_CONTROL
    ControlSuspend(device);
#endif
    return STATUS_SUCCESS;
}

NTSTATUS FanDeviceAdd(WDFDRIVER driver, PWDFDEVICE_INIT init)
{
    WDFDEVICE device;
    WDF_OBJECT_ATTRIBUTES attributes;
    WDF_PNPPOWER_EVENT_CALLBACKS callbacks;
    WDF_TIMER_CONFIG timerConfig;
    NTSTATUS status;
    FAN_CONTEXT* context;
    UNREFERENCED_PARAMETER(driver);
    WdfFdoInitSetFilter(init);
    WDF_PNPPOWER_EVENT_CALLBACKS_INIT(&callbacks);
    callbacks.EvtDeviceD0Entry = FanD0Entry;
    callbacks.EvtDeviceD0Exit = FanD0Exit;
    WdfDeviceInitSetPnpPowerEventCallbacks(init, &callbacks);
    WDF_OBJECT_ATTRIBUTES_INIT_CONTEXT_TYPE(&attributes, FAN_CONTEXT);
    attributes.ExecutionLevel = WdfExecutionLevelPassive;
    status = WdfDeviceCreate(&init, &attributes, &device);
    if (!NT_SUCCESS(status)) return status;
    TraceStage(device, 10, STATUS_SUCCESS);
    /* No queue/file callbacks: KMDF forwards existing OEM requests unchanged. */
    context = FanContext(device);
    WDF_OBJECT_ATTRIBUTES_INIT(&attributes);
    attributes.ParentObject = device;
    status = WdfWaitLockCreate(&attributes, &context->TimerLock);
    if (!NT_SUCCESS(status)) { TraceStage(device, 23, status); return STATUS_SUCCESS; }
    WDF_TIMER_CONFIG_INIT(&timerConfig, FanProbeTimer);
    timerConfig.AutomaticSerialization = FALSE;
    WDF_OBJECT_ATTRIBUTES_INIT(&attributes);
    attributes.ParentObject = device;
    attributes.ExecutionLevel = WdfExecutionLevelPassive;
    status = WdfTimerCreate(&timerConfig, &attributes, &context->Timer);
    TraceStage(device, 22, status);
    if (!NT_SUCCESS(status)) context->Timer = NULL;
    return STATUS_SUCCESS;
}

NTSTATUS DriverEntry(PDRIVER_OBJECT object, PUNICODE_STRING registryPath)
{
    WDF_DRIVER_CONFIG config;
    WDF_DRIVER_CONFIG_INIT(&config, FanDeviceAdd);
    return WdfDriverCreate(object, registryPath, WDF_NO_OBJECT_ATTRIBUTES, &config, WDF_NO_HANDLE);
}
