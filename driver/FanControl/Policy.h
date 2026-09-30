/* Shared with the native offline tests; no OS or hardware side effects. */
static int ControlRequestValid(const CONTROL_REQUEST* request, LONGLONG now)
{
    if (!request->Sequence || request->Mode > 2) return 0;
    if (request->IssuedUtc.QuadPart > now || request->IssuedUtc.QuadPart < now - 30000000LL) return 0;
#ifdef GALAXY_FAN_ZERO_HOLD
    /* Version 2 is exclusively a sustained zero request. The upper word is
     * the Auto cutoff in Celsius; no arbitrary register values are accepted. */
    if (request->Version == 2) return request->Owner != 0 && request->Mode != 0 &&
        (request->Step & 0xffff) == 0 && (request->Step >> 16) >= 45 && (request->Step >> 16) <= 90;
#endif
    if (request->Version != 1) return 0;
    if (request->Mode == 0) return request->Step == 0;
#ifdef GALAXY_FAN_ZERO
    return request->Owner != 0 && request->Step <= 3;
#else
    return request->Owner != 0 && request->Step >= 1 && request->Step <= 3;
#endif
}
#ifdef GALAXY_FAN_ZERO_HOLD
static int ControlZeroHoldTemperatureAllowed(LONG temperature, ULONG limit, int starting)
{ return limit >= 45 && limit <= 90 && temperature >= 0 && temperature < (LONG)(limit - (starting ? 5 : 0)); }
static int ControlZeroHoldPowerAllowed(ULONGLONG units, ULONGLONG limits)
{
    ULONG scale = 1UL << (ULONG)(units & 15);
    return (limits & (1ULL << 15)) && (limits & (1ULL << 47)) &&
        (limits & 0x7fff) > 0 && ((limits >> 32) & 0x7fff) > 0 &&
        (limits & 0x7fff) <= 5ULL * scale && ((limits >> 32) & 0x7fff) <= 10ULL * scale;
}
#endif
#ifdef GALAXY_FAN_ZERO
static int ControlZeroStartAllowed(LONG temperature) { return temperature >= 0 && temperature < 40; }
static int ControlZeroContinueAllowed(LONG temperature, ULONGLONG deadline, ULONGLONG now)
{ return temperature >= 0 && temperature < 50 && now < deadline; }
#endif
static int ControlRenewAllowed(int manual, ULONGLONG owner, ULONGLONG deadline,
    const CONTROL_REQUEST* request, ULONGLONG now)
{
    return manual && request->Owner == owner && now < deadline;
}
