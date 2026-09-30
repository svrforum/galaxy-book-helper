typedef struct _TRIAL_RECORD {
    ULONG Version, Sequence, State, ApplyStatus, RestoreStatus;
    LONG Temperature;
    ULONG Rpm1, Rpm2;
    LARGE_INTEGER Timestamp, Started;
    ULONG StopReason, Reserved;
} TRIAL_RECORD;
C_ASSERT(sizeof(TRIAL_RECORD) == 56);
