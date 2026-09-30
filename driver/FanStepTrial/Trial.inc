/* Explicit, fixed step-2 / 15-second experiment. Included only in the separate
 * GALAXY_FAN_TRIAL build. No arbitrary step, RPM, packet or duration input.
 * The kernel callback owns the deadline and restoration even if the UI exits.
 * D0Exit stops new work and drains this callback before power transition.
 * Firmware noncompletion or an OS crash cannot be given a software guarantee. */
static VOID SaveTrial(WDFDEVICE device)
{
    FAN_CONTEXT* context = FanContext(device);
    WDFKEY key;
    UNICODE_STRING name;
    KeQuerySystemTimePrecise(&context->Trial.Timestamp);
    if (!NT_SUCCESS(WdfDriverOpenParametersRegistryKey(WdfDeviceGetDriver(device),
        KEY_SET_VALUE, WDF_NO_OBJECT_ATTRIBUTES, &key))) return;
    RtlInitUnicodeString(&name, L"FanTrialResult");
    (VOID)WdfRegistryAssignValue(key, &name, REG_BINARY, sizeof(context->Trial), &context->Trial);
    WdfRegistryClose(key);
}

static BOOLEAN TrialTemperature(LONG* temperature)
{
    ULONGLONG target, thermal;
    LONG tjMax, delta;
    /* Only reached after the exact PAMB model/BIOS/hardware guard. These same
     * package MSRs were verified through PawnIO on this machine. Read only. */
    __try { target = __readmsr(0x1a2); thermal = __readmsr(0x1b1); }
    __except (EXCEPTION_EXECUTE_HANDLER) { return FALSE; }
    if (!(thermal & (1ULL << 31))) return FALSE;
    tjMax = (LONG)((target >> 16) & 0xff);
    delta = (LONG)((thermal >> 16) & 0x7f);
    if (tjMax < 80 || tjMax > 125 || delta > tjMax) return FALSE;
    *temperature = tjMax - delta;
    return TRUE;
}

static VOID TrialRestore(WDFDEVICE device)
{
    FAN_CONTEXT* context = FanContext(device);
    UCHAR reply[21];
    NTSTATUS status = QueryFan(device, 4, reply); /* 2C / 80 = OEM auto */
    context->Trial.RestoreStatus = (ULONG)status;
    FanContext(device)->TrialRestoreNeeded = !NT_SUCCESS(status);
    context->Trial.State = NT_SUCCESS(status) ? 4 : 5;
    SaveTrial(device);
}

static VOID RunFanTrial(WDFDEVICE device)
{
    FAN_CONTEXT* context = FanContext(device);
    UCHAR reply[21];
    BOOLEAN active;
    NTSTATUS status;
    LARGE_INTEGER delay;
    ULONGLONG deadline;
    RtlZeroMemory(&context->Trial, sizeof(context->Trial));
    context->Trial.Version = 1;
    context->Trial.Sequence = context->TrialSequence;
    context->Trial.State = 1; /* preparing; 2=applied, 3=restoring, 4=auto, 5=restore failure, 6=rejected */
    context->Trial.ApplyStatus = (ULONG)STATUS_PENDING;
    context->Trial.RestoreStatus = (ULONG)STATUS_PENDING;
    context->Trial.Rpm1 = context->Trial.Rpm2 = MAXULONG;
    context->Trial.Temperature = -1;
    KeQuerySystemTimePrecise(&context->Trial.Started);
    SaveTrial(device);
    status = QueryFan(device, 0, reply);
    if (!NT_SUCCESS(status) || reply[5] != 0xdd || reply[6] != 0xcc) goto Reject;
    status = QueryFan(device, 2, reply);
    if (!NT_SUCCESS(status) || (reply[7] != 3 && reply[7] != 6)) goto Reject;
    if (!TrialTemperature(&context->Trial.Temperature) || context->Trial.Temperature >= 75) goto Reject;
    WdfWaitLockAcquire(context->TimerLock, NULL);
    active = context->Active;
    WdfWaitLockRelease(context->TimerLock);
    if (!active) goto Reject;
    deadline = KeQueryInterruptTime() + 15ULL * 10000000;
    /* Set ownership before the call, so a failed/ambiguous set still restores. */
    context->TrialRestoreNeeded = TRUE;
    status = QueryFan(device, 3, reply); /* only 2C / 02 */
    context->Trial.ApplyStatus = (ULONG)status;
    context->Trial.State = 2;
    SaveTrial(device);
    if (!NT_SUCCESS(status)) { context->Trial.StopReason = 5; goto Restore; }
    context->Trial.StopReason = 1; /* deadline; 2=power, 3=thermal, 4=RPM, 5=set, 6=preflight */
    delay.QuadPart = -5000000; /* half-second sensor/cancellation interval */
    while (KeQueryInterruptTime() < deadline) {
        WdfWaitLockAcquire(context->TimerLock, NULL);
        active = context->Active;
        WdfWaitLockRelease(context->TimerLock);
        if (!active) { context->Trial.StopReason = 2; break; }
        if (!TrialTemperature(&context->Trial.Temperature) || context->Trial.Temperature >= 80) { context->Trial.StopReason = 3; break; }
        status = QueryFan(device, 1, reply);
        if (!NT_SUCCESS(status) || reply[7] != 2) { context->Trial.StopReason = 4; break; }
        context->Trial.Rpm1 = (reply[8] << 8) | reply[9];
        context->Trial.Rpm2 = (reply[10] << 8) | reply[11];
        SaveTrial(device);
        (VOID)KeDelayExecutionThread(KernelMode, FALSE, &delay);
    }
Restore:
    context->Trial.State = 3;
    SaveTrial(device);
    TrialRestore(device);
    return;
Reject:
    context->Trial.ApplyStatus = (ULONG)STATUS_NOT_SUPPORTED;
    context->Trial.StopReason = 6;
    context->Trial.State = 6;
    SaveTrial(device);
}
