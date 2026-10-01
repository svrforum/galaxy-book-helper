using System;
using System.Threading;

namespace GalaxyHardware
{
    sealed class FanSetupProgress
    {
        internal readonly string Message;
        internal readonly int Percent;
        internal FanSetupProgress(string message,int percent) { Message=message;Percent=Math.Max(0,Math.Min(100,percent)); }
    }
    sealed class FanDiagnosticSession
    {
        readonly CancellationToken cancellation;
        readonly Action<FanSetupProgress> progress;
        string stage;
        int start,end;
        internal bool DeferVerification;
        internal bool Cancelled { get { return cancellation.IsCancellationRequested; } }
        internal FanDiagnosticSession(CancellationToken token,Action<FanSetupProgress> report=null)
        { cancellation=token;progress=report; }
        internal void Check() { cancellation.ThrowIfCancellationRequested(); }
        internal void Wait(int milliseconds)
        { if(cancellation.WaitHandle.WaitOne(milliseconds)) Check(); }
        internal void Report(string message,int percent)
        { if(progress!=null)try {progress(new FanSetupProgress(message,percent));}catch { /* Progress must never prevent hardware restoration. */ } }
        internal void Begin(string message,int from,int to)
        { Check();stage=message;start=from;end=to;Report(message,from); }
        internal void Advance(int current,int total)
        { Report(stage+" · "+current+" / "+total+"초",start+(end-start)*current/Math.Max(1,total)); }
    }
    sealed class FanSetupResult
    {
        public bool Success { get; set; }
        public bool Cancelled { get; set; }
        public bool PowerRestored { get; set; }
        public string Error { get; set; }
        public FanVerificationReport Verification { get; set; }
        public object OriginalPower { get; set; }
        public object VerificationPower { get; set; }
        public object PowerSamples { get; set; }
    }
    static class FanSetupWorkflow
    {
        // Restoration is not cancellable: even partial apply failures must unwind.
        internal static FanSetupResult Run(FanDiagnosticSession session,Action apply,
            Func<FanVerificationReport> verify,Action restore)
        {
            var result=new FanSetupResult();
            bool attempted=false;
            try {
                session.Check();attempted=true;apply();
                session.Check();result.Verification=verify();session.Check();
                if(result.Verification==null || !result.Verification.Success)
                    result.Error=result.Verification==null ? "팬 검증 결과가 없습니다." : result.Verification.Error??"팬 검증을 완료하지 못했습니다.";
            } catch(OperationCanceledException) { result.Cancelled=true;result.Error="보정·검증을 취소했습니다."; }
            catch(Exception ex) { result.Error=ex.Message; }
            finally {
                if(attempted) {
                    session.Report("팬 검증 종료 · 원래 전력 복원 중",98);
                    try { restore();result.PowerRestored=true; }
                    catch(Exception ex) { result.Error=(result.Error??"")+" 전력 복원 실패: "+ex.Message; }
                }
            }
            result.Cancelled=result.Cancelled || session.Cancelled;
            result.Success=result.Error==null && !result.Cancelled && result.PowerRestored && result.Verification!=null && result.Verification.Success;
            if(result.Success)session.Report("보정·검증 및 설정 복원 완료",100);
            return result;
        }
    }
}
