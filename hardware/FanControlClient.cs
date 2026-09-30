using System;
using System.Diagnostics;
using System.IO;
using System.Management;
using System.Threading;
using Microsoft.Win32;

namespace GalaxyHardware
{
    sealed class FanControlState
    {
        public uint Sequence { get; set; }
        public ulong Owner { get; set; }
        public uint State { get; set; }
        public uint Step { get; set; }
        public uint Status { get; set; }
        public int TemperatureC { get; set; }
        public int? Fan1Rpm { get; set; }
        public int? Fan2Rpm { get; set; }
        public uint StopReason { get; set; }
        public uint MaxStep { get; set; }
        public DateTime SampleUtc { get; set; }
        public uint LeaseRemainingMs { get; set; }
        public int ZeroLimitC { get; set; }
        public bool Manual { get { return State == 1 && Status == 0; } }
    }
    sealed class FanControlClient : IDisposable
    {
        internal const string KeyPath = @"SYSTEM\CurrentControlSet\Services\GalaxyFanRead\Parameters";
        readonly object gate = new object();
        readonly ulong owner = BitConverter.ToUInt64(Guid.NewGuid().ToByteArray(), 0) | 1UL;
        uint sequence = (uint)(Environment.TickCount & Int32.MaxValue);
        bool disposed, requested;
        public bool Active { get; private set; }
        public bool HasRequest { get { return requested; } }
        internal int ZeroHoldLimit {get;set;}
        public static FanControlState Decode(byte[] bytes)
        {
            if (bytes == null || bytes.Length != 64 || BitConverter.ToUInt32(bytes, 0) != 1)
                throw new InvalidDataException("팬 제어 드라이버 결과 형식이 다릅니다.");
            uint state = BitConverter.ToUInt32(bytes, 16), step = BitConverter.ToUInt32(bytes, 20);
            if (state > 4 || step > 3) throw new InvalidDataException("잘못된 팬 제어 상태입니다.");
            uint rpm1 = BitConverter.ToUInt32(bytes,32), rpm2 = BitConverter.ToUInt32(bytes,36);
            return new FanControlState {
                Sequence=BitConverter.ToUInt32(bytes,4), Owner=BitConverter.ToUInt64(bytes,8), State=state, Step=step,
                Status=BitConverter.ToUInt32(bytes,24), TemperatureC=BitConverter.ToInt32(bytes,28),
                Fan1Rpm=rpm1 == UInt32.MaxValue ? null : (int?)checked((int)rpm1),
                Fan2Rpm=rpm2 == UInt32.MaxValue ? null : (int?)checked((int)rpm2),
                StopReason=BitConverter.ToUInt32(bytes,40), MaxStep=BitConverter.ToUInt32(bytes,44),
                SampleUtc=DateTime.FromFileTimeUtc(BitConverter.ToInt64(bytes,48)), LeaseRemainingMs=BitConverter.ToUInt32(bytes,56), ZeroLimitC=BitConverter.ToInt32(bytes,60)
            };
        }
        internal static byte[] Encode(uint sequence, ulong owner, uint mode, uint step, DateTime issued)
        {
            if (sequence == 0 || mode > 2 || (mode == 0 ? step != 0 : (owner == 0 || step > 3)))
                throw new ArgumentException("팬 제어 요청 범위를 벗어났습니다.");
            var bytes = new byte[32];
            BitConverter.GetBytes(1U).CopyTo(bytes,0); BitConverter.GetBytes(sequence).CopyTo(bytes,4);
            BitConverter.GetBytes(owner).CopyTo(bytes,8); BitConverter.GetBytes(mode).CopyTo(bytes,16);
            BitConverter.GetBytes(step).CopyTo(bytes,20); BitConverter.GetBytes(issued.ToUniversalTime().ToFileTimeUtc()).CopyTo(bytes,24);
            return bytes;
        }
        public static bool IsReady()
        {
            using (var key = Registry.LocalMachine.OpenSubKey(KeyPath)) {
                if (key == null || !(key.GetValue("FanControlReadyTime") is long)) return false;
                return DateTime.FromFileTimeUtc((long)key.GetValue("FanControlReadyTime")) >= ManagementDateTimeConverter.ToDateTime(Program.Boot()).ToUniversalTime();
            }
        }
        public static FanControlState Read()
        {
            using (var key = Registry.LocalMachine.OpenSubKey(KeyPath)) {
                if (key == null) throw new IOException("팬 제어 드라이버가 없습니다.");
                return Decode(key.GetValue("FanControlResult") as byte[]);
            }
        }
        public static bool IsZeroHoldReady()
        {
            if(!IsReady())return false;
            using(var key=Registry.LocalMachine.OpenSubKey(KeyPath)) return key!=null && key.GetValue("FanZeroHoldReadyTime") is long && Object.Equals(key.GetValue("FanZeroHoldReadyTime"),key.GetValue("FanControlReadyTime"));
        }
        internal static byte[] EncodeZeroHold(uint sequence,ulong owner,uint mode,int limit,DateTime issued)
        {
            if(mode==0 || limit<45 || limit>90)throw new ArgumentOutOfRangeException("limit");
            var bytes=Encode(sequence,owner,mode,0,issued);
            BitConverter.GetBytes(2U).CopyTo(bytes,0);BitConverter.GetBytes((uint)limit<<16).CopyTo(bytes,20);return bytes;
        }
        public static bool IsZeroReady()
        {
            if(!IsReady()) return false;
            using(var key=Registry.LocalMachine.OpenSubKey(KeyPath)) {
                return key!=null && key.GetValue("FanZeroReadyTime") is long && Object.Equals(key.GetValue("FanZeroReadyTime"),key.GetValue("FanControlReadyTime"));
            }
        }
        uint Send(uint mode, uint step)
        {
            lock (gate) {
                if (disposed && mode != 0) throw new ObjectDisposedException("FanControlClient");
                if (++sequence == 0) sequence = 1;
                using (var key = Registry.LocalMachine.OpenSubKey(KeyPath,true)) {
                    if (key == null) throw new IOException("팬 제어 요청 경로가 없습니다.");
                    if(mode!=0 && step==0 && (!(key.GetValue("FanZeroReadyTime") is long) || !Object.Equals(key.GetValue("FanZeroReadyTime"),key.GetValue("FanControlReadyTime")))) throw new IOException("0 RPM 시험 드라이버가 아직 적용되지 않았습니다.");
                    if(mode!=0 && step==0 && ZeroHoldLimit!=0 && !IsZeroHoldReady())throw new IOException("0 RPM 유지 드라이버 적용을 위해 Windows를 다시 시작해 주세요.");
                    key.SetValue("FanControlRequest",mode!=0 && step==0 && ZeroHoldLimit!=0 ? EncodeZeroHold(sequence,owner,mode,ZeroHoldLimit,DateTime.UtcNow) : Encode(sequence,owner,mode,step,DateTime.UtcNow),RegistryValueKind.Binary);
                }
                requested = true;
                return sequence;
            }
        }
        FanControlState Wait(uint expected, bool manual)
        {
            DateTime waitStarted=DateTime.UtcNow;
            var clock=Stopwatch.StartNew();
            while (clock.Elapsed.TotalSeconds < 4) {
                Thread.Sleep(100);
                var state=Read();
                if (state.Sequence != expected && !(!manual && state.State==0 && state.Status==0 && state.SampleUtc>=waitStarted)) continue;
                // The driver publishes the restoration obligation before the ACPI
                // request completes. This is progress, not a terminal failure.
                if (!manual && state.State==2) continue;
                if (manual && (!state.Manual || state.Owner != owner)) throw new IOException("팬 제어 거부: 상태 " + state.State + ", 원인 " + state.StopReason + ", NTSTATUS 0x" + state.Status.ToString("X8"));
                if (!manual && (state.State != 0 || state.Status != 0)) throw new IOException("팬 자동 복귀 실패: 0x" + state.Status.ToString("X8"));
                return state;
            }
            throw new TimeoutException("팬 제어 응답이 없습니다. 갱신을 중단하고 자동 복귀를 요청합니다.");
        }
        public FanControlState Start(int step)
        {
            if (!IsReady()) throw new IOException("지속 제어 드라이버 적용 대기 중입니다. 재시작 후 30초 기다려 주세요.");
            Program.CheckMachine();
            try {
                var state=Wait(Send(1,checked((uint)step)),true);
                lock (gate) {
                    if (disposed) throw new ObjectDisposedException("FanControlClient");
                    Active=true;
                }
                return state;
            } catch { try { Stop(); } catch { } throw; }
        }
        public FanControlState Heartbeat(int step)
        {
            var state=Read();
            if (!Active || !state.Manual || state.Owner != owner || (DateTime.UtcNow-state.SampleUtc).TotalSeconds > 3 || state.LeaseRemainingMs == 0) {
                Active=false;
                throw new IOException("팬 수동 제어가 종료됐거나 측정이 지연됐습니다. 원인 " + state.StopReason);
            }
            Send(2,checked((uint)step));
            return state;
        }
        public void Stop()
        {
            Active=false;
            if (requested) Send(0,0);
        }
        public FanControlState Restore()
        {
            Active=false;
            return Wait(Send(0,0),false);
        }
        public void Dispose()
        {
            lock (gate) { disposed=true; }
            Stop();
        }
    }
}
