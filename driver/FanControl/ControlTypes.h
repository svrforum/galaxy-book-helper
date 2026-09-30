typedef struct _CONTROL_REQUEST {
    ULONG Version, Sequence;
    ULONGLONG Owner;
    ULONG Mode, Step; /* 0=Auto, 1=explicit Start, 2=Renew; steps 1..3 only */
    LARGE_INTEGER IssuedUtc;
} CONTROL_REQUEST;
typedef struct _CONTROL_RECORD {
    ULONG Version, Sequence;
    ULONGLONG Owner;
    ULONG State, Step, Status;
    LONG Temperature;
    ULONG Rpm1, Rpm2, StopReason, MaxStep;
    LARGE_INTEGER Timestamp;
    ULONG LeaseRemainingMs, Reserved;
} CONTROL_RECORD;
typedef struct _CONTROL_CONTEXT {
    BOOLEAN Initialized, Ready, Pending, Manual, NeedsRestore;
    CONTROL_REQUEST Last, Candidate;
    CONTROL_RECORD Record;
    ULONGLONG Deadline;
#ifdef GALAXY_FAN_ZERO
    ULONGLONG ZeroDeadline;
#endif
#ifdef GALAXY_FAN_ZERO_HOLD
    BOOLEAN ZeroHold;
    ULONG ZeroLimit;
#endif
} CONTROL_CONTEXT;
C_ASSERT(sizeof(CONTROL_REQUEST) == 32);
C_ASSERT(sizeof(CONTROL_RECORD) == 64);
