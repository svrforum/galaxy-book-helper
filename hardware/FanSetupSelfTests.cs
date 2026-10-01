using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Threading;
using System.Windows.Forms;

namespace GalaxyHardware
{
    static class FanSetupSelfTests
    {
        static void Assert(bool value,string message) {if(!value)throw new InvalidOperationException(message);}
        internal static List<string> WorkflowCases()
        {
            var cases=new List<string>();
            var session=new FanDiagnosticSession(CancellationToken.None);
            var order=new List<string>();
            var result=FanSetupWorkflow.Run(session,delegate{order.Add("apply");},
                delegate{order.Add("verify");return new FanVerificationReport {Success=true};},delegate{order.Add("restore");});
            Assert(result.Success&&result.PowerRestored&&String.Join(",",order)=="apply,verify,restore","Setup success skipped restoration.");
            cases.Add("setup succeeds only after verification and power restoration");
            bool restored=false;
            result=FanSetupWorkflow.Run(session,delegate{throw new IOException("partial write");},
                delegate{throw new Exception("must not verify");},delegate{restored=true;});
            Assert(!result.Success&&restored&&result.Error.Contains("partial write"),"Partial apply did not restore.");
            cases.Add("partial power apply failure restores and preserves the original error");
            result=FanSetupWorkflow.Run(session,delegate{},delegate{return new FanVerificationReport {Error="fan restore failed"};},delegate{});
            Assert(!result.Success&&result.PowerRestored&&result.Error=="fan restore failed","Fan failure hidden.");
            cases.Add("fan verification or restoration failure cannot report success");
            result=FanSetupWorkflow.Run(session,delegate{},delegate{return new FanVerificationReport {Success=true};},delegate{throw new IOException("restore readback");});
            Assert(!result.Success&&!result.PowerRestored&&result.Error.Contains("restore readback"),"Power restoration failure hidden.");
            cases.Add("power restoration failure prevents successful setup");
            using(var cancel=new CancellationTokenSource()) {
                restored=false;bool verified=false;session=new FanDiagnosticSession(cancel.Token);
                result=FanSetupWorkflow.Run(session,delegate{cancel.Cancel();},delegate{verified=true;return null;},delegate{restored=true;});
                Assert(result.Cancelled&&!result.Success&&restored&&!verified,"Cancellation did not unwind apply.");
                restored=false;result=FanSetupWorkflow.Run(session,delegate{throw new Exception("must not apply");},delegate{return null;},delegate{restored=true;});
                Assert(result.Cancelled&&!restored,"Pre-start cancellation touched hardware.");
            }
            cases.Add("cancellation after apply restores; cancellation before start never applies");
            session=new FanDiagnosticSession(CancellationToken.None,delegate{throw new IOException("closed progress view");});
            restored=false;result=FanSetupWorkflow.Run(session,delegate{},delegate{return new FanVerificationReport {Success=true};},delegate{restored=true;});
            Assert(result.Success&&restored,"Progress observer blocked restoration.");
            cases.Add("closed or failed progress view cannot prevent restoration");
            return cases;
        }
        static void PumpUntil(Func<bool> completed)
        {
            var clock=Stopwatch.StartNew();
            while(!completed()) {if(clock.Elapsed.TotalSeconds>8)throw new TimeoutException("Setup dialog test timed out.");Application.DoEvents();Thread.Sleep(10);}
        }
        internal static object Run()
        {
            var cases=WorkflowCases();int starts=0;
            using(var dialog=new FanSetupDialog(delegate(FanDiagnosticSession session){
                Interlocked.Increment(ref starts);session.Report("test progress",35);session.Wait(100);
                return FanSetupWorkflow.Run(session,delegate{},delegate{return new FanVerificationReport {Success=true};},delegate{});
            },true)) {
                dialog.Show();PumpUntil(()=>dialog.Result!=null);
                Assert(starts==1&&dialog.Result.Success&&dialog.ProgressValue==100,"Automatic startup did not finish exactly once.");
            }
            cases.Add("automatic setup starts once and closes after successful background verification");
            using(var dialog=new FanSetupDialog(delegate(FanDiagnosticSession session){
                return FanSetupWorkflow.Run(session,delegate{},delegate{session.Report("waiting",25);session.Wait(5000);return null;},delegate{Thread.Sleep(100);});
            },true)) {
                dialog.Show();PumpUntil(()=>dialog.ProgressValue==25);dialog.Close();
                Assert(dialog.Visible&&dialog.Running,"Closing setup did not wait for restoration.");
                PumpUntil(()=>dialog.Result!=null);
                Assert(dialog.Result.Cancelled&&dialog.Result.PowerRestored&&!dialog.Result.Success,"UI cancellation was reported as success.");
                dialog.Close();
            }
            cases.Add("closing a running setup cancels and waits for restoration before allowing close");
            using(var dialog=new FanSetupDialog(delegate(FanDiagnosticSession session){throw new IOException("driver not ready");},true)) {
                dialog.Show();PumpUntil(()=>dialog.Result!=null);
                Assert(!dialog.Result.Success&&dialog.StageText.Contains("driver not ready")&&dialog.Visible,"Setup error is not reviewable.");dialog.Close();
            }
            cases.Add("preflight failure stays visible with its reason and no success claim");
            return new {Success=true,Passed=cases.Count,Cases=cases,HardwareAccess=false,UserFilesModified=false};
        }
    }
}
