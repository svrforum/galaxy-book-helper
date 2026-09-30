using System;
using System.IO;

namespace GalaxyHardware
{
    sealed class RpmCapPolicy
    {
        readonly uint maxStep;
        int over;
        DateTime settlingUntil;
        public int Step { get; private set; }
        public int Target { get; private set; }
        public RpmCapPolicy(FanCalibration calibration,int target,DateTime now)
        {
            Step=calibration.SelectStep(target); Target=target; maxStep=calibration.MaxStep; MarkStarted(now);
        }
        public void MarkStarted(DateTime now) { settlingUntil=now.AddSeconds(15); over=0; }
        public bool Settling(DateTime now) { return now<settlingUntil; }
        public int Observe(FanControlState state,DateTime now)
        {
            if (state.MaxStep!=maxStep) throw new IOException("성능 모드/최대 단계가 달라져 RPM 보정을 다시 확인해야 합니다.");
            if (!state.Manual || !state.Fan1Rpm.HasValue || !state.Fan2Rpm.HasValue) throw new IOException("팬 제어 또는 RPM 측정이 유효하지 않습니다.");
            if (Settling(now)) return Step;
            over=Math.Max(state.Fan1Rpm.Value,state.Fan2Rpm.Value)>Target+100 ? over+1 : 0;
            if (over>=3) {
                if (Step==1) throw new IOException("최소 단계에서도 RPM 목표를 유지하지 못해 자동 복귀합니다.");
                Step--; MarkStarted(now);
            }
            return Step;
        }
    }
}
