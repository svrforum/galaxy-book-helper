#define assert(value) do { if (!(value)) return __LINE__; } while (0)


typedef unsigned long ULONG;
typedef long LONG;
typedef unsigned long long ULONGLONG;
typedef long long LONGLONG;
typedef unsigned char BOOLEAN;
typedef union { LONGLONG QuadPart; } LARGE_INTEGER;
#define C_ASSERT(value) typedef char assert_line_##__LINE__[(value) ? 1 : -1]
#define GALAXY_FAN_ZERO
#include "../FanControl/ControlTypes.h"
#include "../FanControl/Policy.h"
int mainCRTStartup(void)
{
    CONTROL_REQUEST r = {0};

    r.Version=1; r.Sequence=1; r.Owner=44; r.Mode=1; r.Step=2; r.IssuedUtc.QuadPart=100000000;
    assert(ControlRequestValid(&r,100000000));
    r.Step=0; assert(ControlRequestValid(&r,100000000));
    r.Step=4; assert(!ControlRequestValid(&r,100000000));
    r.Step=2; r.Owner=0; assert(!ControlRequestValid(&r,100000000));
    r.Owner=44; assert(!ControlRequestValid(&r,130000001));
    assert(!ControlRequestValid(&r,99999999));
    r.Version=2; assert(!ControlRequestValid(&r,100000000));
    r.Version=1; r.Sequence=0; assert(!ControlRequestValid(&r,100000000));
    r.Sequence=1; r.Mode=0; r.Step=0; r.Owner=0; assert(ControlRequestValid(&r,100000000));
    r.Step=2; assert(!ControlRequestValid(&r,100000000));
    r.Mode=2; r.Owner=44;
    assert(ControlRenewAllowed(1,44,200,&r,199));
    assert(!ControlRenewAllowed(1,44,200,&r,200));
    assert(!ControlRenewAllowed(0,44,200,&r,199));
    assert(!ControlRenewAllowed(1,45,200,&r,199));

    assert(ControlZeroStartAllowed(39));
    assert(!ControlZeroStartAllowed(40));
    assert(!ControlZeroStartAllowed(-1));
    assert(ControlZeroContinueAllowed(49,300,299));
    assert(!ControlZeroContinueAllowed(50,300,299));
    assert(!ControlZeroContinueAllowed(-1,300,299));
    assert(!ControlZeroContinueAllowed(35,300,300));
    assert(!ControlZeroContinueAllowed(35,300,301));
    return 0;
}
