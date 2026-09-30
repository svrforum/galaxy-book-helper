#include "Policy.h"

static NTSTATUS ControlJournal(WDFDEVICE device, ULONG needed)
{
    WDFKEY key;
    UNICODE_STRING name;
    NTSTATUS status = WdfDriverOpenParametersRegistryKey(WdfDeviceGetDriver(device), KEY_SET_VALUE, WDF_NO_OBJECT_ATTRIBUTES, &key);
    if (!NT_SUCCESS(status)) return status;
    RtlInitUnicodeString(&name, L"FanControlNeedsRestore");
    status = WdfRegistryAssignULong(key, &name, needed);
    WdfRegistryClose(key);
    return status;
}

static VOID ControlSave(WDFDEVICE device)
{
    CONTROL_CONTEXT* control = &FanContext(device)->Control;
    WDFKEY key;
    UNICODE_STRING name;
    ULONGLONG now = KeQueryInterruptTime();
    control->Record.Version = 1;
    KeQuerySystemTimePrecise(&control->Record.Timestamp);
    control->Record.LeaseRemainingMs = control->Manual && now < control->Deadline ? (ULONG)((control->Deadline - now) / 10000) : 0;
    if (!NT_SUCCESS(WdfDriverOpenParametersRegistryKey(WdfDeviceGetDriver(device), KEY_SET_VALUE, WDF_NO_OBJECT_ATTRIBUTES, &key))) return;
    RtlInitUnicodeString(&name, L"FanControlResult");
    (VOID)WdfRegistryAssignValue(key, &name, REG_BINARY, sizeof(control->Record), &control->Record);
    if (!control->Ready) {
#ifdef GALAXY_FAN_ZERO_HOLD
        RtlInitUnicodeString(&name, L"FanZeroHoldReadyTime");
        (VOID)WdfRegistryAssignValue(key, &name, REG_QWORD, sizeof(LARGE_INTEGER), &control->Record.Timestamp);
#endif
#ifdef GALAXY_FAN_ZERO
        RtlInitUnicodeString(&name, L"FanZeroReadyTime");
        (VOID)WdfRegistryAssignValue(key, &name, REG_QWORD, sizeof(LARGE_INTEGER), &control->Record.Timestamp);
#endif
        RtlInitUnicodeString(&name, L"FanControlReadyTime");
        if (NT_SUCCESS(WdfRegistryAssignValue(key, &name, REG_QWORD, sizeof(LARGE_INTEGER), &control->Record.Timestamp))) control->Ready = TRUE;
    }
    WdfRegistryClose(key);
}

static BOOLEAN ControlMailbox(WDFDEVICE device, WDFKEY key)
{
    CONTROL_CONTEXT* control = &FanContext(device)->Control;
    CONTROL_REQUEST request;
    UNICODE_STRING name;
    ULONG length = 0, type = 0, restore = 0;
    NTSTATUS status;
    RtlZeroMemory(&request, sizeof(request));
    RtlInitUnicodeString(&name, L"FanControlRequest");
    status = WdfRegistryQueryValue(key, &name, sizeof(request), &request, &length, &type);
    if (!control->Initialized) {
        control->Initialized = TRUE;
        control->Last = request; /* Never replay a stored request on boot/resume. */
        RtlInitUnicodeString(&name, L"FanControlNeedsRestore");
        if (NT_SUCCESS(WdfRegistryQueryULong(key, &name, &restore)) && restore) control->NeedsRestore = TRUE;
        control->Record.Rpm1 = control->Record.Rpm2 = MAXULONG;
        control->Record.Temperature = -1;
    } else if (NT_SUCCESS(status) && length == sizeof(request) && type == REG_BINARY &&
        RtlCompareMemory(&request, &control->Last, sizeof(request)) != sizeof(request)) {
        control->Last = control->Candidate = request;
        control->Pending = TRUE;
    }
    return !control->Ready || control->Pending || control->Manual || control->NeedsRestore;
}

static BOOLEAN ControlTemperature(LONG* temperature)
{
    ULONGLONG target, thermal;
    LONG maximum, delta;
    __try { target = __readmsr(0x1a2); thermal = __readmsr(0x1b1); }
    __except (EXCEPTION_EXECUTE_HANDLER) { return FALSE; }
    if (!(thermal & (1ULL << 31))) return FALSE;
    maximum = (LONG)((target >> 16) & 255);
    delta = (LONG)((thermal >> 16) & 127);
    if (maximum < 80 || maximum > 125 || delta > maximum) return FALSE;
    *temperature = maximum - delta;
    return TRUE;
}

/* State: 0 Auto, 1 manual, 2 restoring, 3 restore failure, 4 rejected.
 * StopReason: 1 user, 2 lease, 3 temperature/sensor, 4 RPM, 5 write,
 * 6 power, 7 capability change, 8 request rejected, 9 recovery journal. */
static VOID ControlRestore(WDFDEVICE device, ULONG reason)
{
    CONTROL_CONTEXT* control = &FanContext(device)->Control;
    UCHAR reply[21];
    NTSTATUS status;
    control->Record.State = 2;
    control->Record.StopReason = reason;
    control->Manual = FALSE;
    ControlSave(device);
    status = QueryFan(device, 4, reply);
    if (NT_SUCCESS(status)) status = ControlJournal(device, 0);
    control->NeedsRestore = !NT_SUCCESS(status);
    control->Record.Status = (ULONG)status;
    control->Record.State = NT_SUCCESS(status) ? 0 : 3;
    if (NT_SUCCESS(status)) control->Record.Step = 0;
    ControlSave(device);
}

static NTSTATUS ControlSensors(WDFDEVICE device, BOOLEAN starting, LONG temperatureLimit)
{
    CONTROL_CONTEXT* control = &FanContext(device)->Control;
    UCHAR reply[21];
    NTSTATUS status;
    if (!ControlTemperature(&control->Record.Temperature) || control->Record.Temperature >= temperatureLimit) {
        control->Record.StopReason = 3; return STATUS_DEVICE_NOT_READY;
    }
    status = QueryFan(device, 2, reply);
    if (!NT_SUCCESS(status) || (reply[7] != 3 && reply[7] != 6) || (!starting && reply[7] != control->Record.MaxStep)) {
        control->Record.StopReason = 7; return STATUS_NOT_SUPPORTED;
    }
    control->Record.MaxStep = reply[7];
    status = QueryFan(device, 1, reply);
    if (!NT_SUCCESS(status) || reply[7] != 2) { control->Record.StopReason = 4; return STATUS_DATA_ERROR; }
    control->Record.Rpm1 = (reply[8] << 8) | reply[9];
    control->Record.Rpm2 = (reply[10] << 8) | reply[11];
    SaveProbe(device, STATUS_SUCCESS, control->Record.Rpm1, control->Record.Rpm2);
    return STATUS_SUCCESS;
}

#ifdef GALAXY_FAN_ZERO_HOLD
static BOOLEAN ControlZeroHoldPower(VOID)
{
    ULONGLONG units, limits;
    __try { units = __readmsr(0x606); limits = __readmsr(0x610); }
    __except (EXCEPTION_EXECUTE_HANDLER) { return FALSE; }
    return ControlZeroHoldPowerAllowed(units, limits) ? TRUE : FALSE;
}
#endif

static VOID ControlTick(WDFDEVICE device)
{
    FAN_CONTEXT* context = FanContext(device);
    CONTROL_CONTEXT* control = &context->Control;
    CONTROL_REQUEST request;
    LARGE_INTEGER now;
    NTSTATUS status;
    UCHAR reply[21];
    BOOLEAN active;
    LONG sensorLimit;
#ifdef GALAXY_FAN_ZERO_HOLD
    BOOLEAN holdRequest;
    ULONG holdLimit;
#endif
    if (control->NeedsRestore && !control->Manual) {
        control->Pending = FALSE; ControlRestore(device, 9); return;
    }
    if (control->Manual) {
        /* Expiry is checked BEFORE reading any queued renewal. */
        if (KeQueryInterruptTime() >= control->Deadline) {
            control->Pending = FALSE; ControlRestore(device, 2); return;
        }
        sensorLimit = 80;
#ifdef GALAXY_FAN_ZERO_HOLD
        if (control->ZeroHold && control->Record.Step == 0) sensorLimit = (LONG)control->ZeroLimit;
#endif
        if (!NT_SUCCESS(ControlSensors(device, FALSE, sensorLimit))) {
            control->Pending = FALSE; ControlRestore(device, control->Record.StopReason); return;
        }
#ifdef GALAXY_FAN_ZERO
        if (control->Record.Step == 0 &&
#ifdef GALAXY_FAN_ZERO_HOLD
            !control->ZeroHold &&
#endif
            !ControlZeroContinueAllowed(control->Record.Temperature, control->ZeroDeadline, KeQueryInterruptTime())) {
            control->Pending = FALSE; ControlRestore(device, 10); return;
        }
#endif
#ifdef GALAXY_FAN_ZERO_HOLD
        if (control->ZeroHold && control->Record.Step == 0 && !ControlZeroHoldPower()) {
            control->Pending = FALSE; ControlRestore(device, 11); return;
        }
#endif
    }
    if (!control->Pending) { ControlSave(device); return; }
    request = control->Candidate;
    control->Pending = FALSE;
    control->Record.Sequence = request.Sequence;
    KeQuerySystemTimePrecise(&now);
    status = STATUS_INVALID_PARAMETER;
    if (!ControlRequestValid(&request, now.QuadPart)) goto Reject;
#ifdef GALAXY_FAN_ZERO_HOLD
    holdRequest = request.Version == 2;
    holdLimit = holdRequest ? request.Step >> 16 : 0;
#endif
    if (request.Mode == 0) {
        if (control->Manual && request.Owner != 0 && request.Owner != control->Record.Owner) { status = STATUS_ACCESS_DENIED; goto Reject; }
        ControlRestore(device, 1); return;
    }
    if (request.Mode == 2 && !ControlRenewAllowed(control->Manual, control->Record.Owner, control->Deadline, &request, KeQueryInterruptTime())) {
        status = STATUS_DEVICE_NOT_READY; goto Reject;
    }
    if (control->Manual && request.Owner != control->Record.Owner) { status = STATUS_DEVICE_BUSY; goto Reject; }
    if (!control->Manual) {
        status = QueryFan(device, 0, reply);
        if (!NT_SUCCESS(status) || reply[5] != 0xdd || reply[6] != 0xcc) { status = STATUS_NOT_SUPPORTED; goto Reject; }
        status = ControlSensors(device, TRUE,
#ifdef GALAXY_FAN_ZERO_HOLD
            holdRequest ? (LONG)holdLimit - 5 :
#endif
            75);
        if (!NT_SUCCESS(status)) goto Reject;
        status = ControlJournal(device, 1); /* Persist restoration obligation BEFORE the write. */
        if (!NT_SUCCESS(status)) goto Reject;
        control->NeedsRestore = TRUE;
    }
    WdfWaitLockAcquire(context->TimerLock, NULL);
    active = context->Active;
    WdfWaitLockRelease(context->TimerLock);
    if (!active) { if (control->NeedsRestore) ControlRestore(device, 6); return; }
    /* Sensor IO can take time. Do not revive an expired lease or stale start. */
    KeQuerySystemTimePrecise(&now);
    if (!ControlRequestValid(&request, now.QuadPart) ||
        (control->Manual && KeQueryInterruptTime() >= control->Deadline)) {
        if (control->NeedsRestore) ControlRestore(device, 2);
        return;
    }
    control->Deadline = KeQueryInterruptTime() + 50000000ULL; /* fixed 5-second lease */
#ifdef GALAXY_FAN_ZERO
#ifdef GALAXY_FAN_ZERO_HOLD
    if (holdRequest) {
        if (!ControlZeroHoldTemperatureAllowed(control->Record.Temperature, holdLimit, !control->Manual || control->Record.Step != 0) || !ControlZeroHoldPower()) {
            status = STATUS_DEVICE_NOT_READY; goto Reject;
        }
        if (control->Manual && control->Record.Step == 0 && (!control->ZeroHold || control->ZeroLimit != holdLimit)) {
            status = STATUS_INVALID_PARAMETER; goto Reject;
        }
        request.Step = 0;
    } else if (control->Manual && control->Record.Step == 0 && control->ZeroHold && request.Step == 0) {
        status = STATUS_INVALID_PARAMETER; goto Reject;
    }
    if (!holdRequest)
#endif
    if (request.Step == 0 && (!control->Manual || control->Record.Step != 0)) {
        if (!ControlZeroStartAllowed(control->Record.Temperature)) { status = STATUS_DEVICE_NOT_READY; goto Reject; }
        control->ZeroDeadline = KeQueryInterruptTime() + 300000000ULL; /* non-renewable 30-second initial verification window */
    }
#endif
    if (!control->Manual || request.Step != control->Record.Step) {
        control->Manual = TRUE;
        control->Record.Owner = request.Owner;
#ifdef GALAXY_FAN_ZERO
        status = QueryFan(device, request.Step == 0 ? 7 : (request.Step == 1 ? 5 : (request.Step == 2 ? 3 : 6)), reply);
#else
        status = QueryFan(device, request.Step == 1 ? 5 : (request.Step == 2 ? 3 : 6), reply);
#endif
        if (!NT_SUCCESS(status)) { ControlRestore(device, 5); return; }
    }
    control->Record.Owner = request.Owner;
#ifdef GALAXY_FAN_ZERO_HOLD
    control->ZeroHold = holdRequest;
    control->ZeroLimit = holdLimit;
    control->Record.Reserved = holdLimit;
#endif
    control->Record.Step = request.Step;
    control->Record.State = 1;
    control->Record.Status = STATUS_SUCCESS;
    control->Record.StopReason = 0;
    ControlSave(device);
    return;
Reject:
    control->Record.Status = (ULONG)status;
    control->Record.StopReason = 8;
    if (!control->Manual) control->Record.State = 4;
    ControlSave(device);
}

static VOID ControlSuspend(WDFDEVICE device)
{
    CONTROL_CONTEXT* control = &FanContext(device)->Control;
    if (control->Manual || control->NeedsRestore) {
        if (IsVerifiedDevice(device) && NT_SUCCESS(OpenAcpi(device))) {
            ControlRestore(device, 6);
            CloseAcpi(device);
        }
    }
    control->Manual = FALSE;
    control->Initialized = control->Ready = control->Pending = FALSE;
}
