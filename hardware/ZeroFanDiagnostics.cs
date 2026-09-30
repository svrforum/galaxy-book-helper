using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Threading;
using System.Web.Script.Serialization;
namespace GalaxyHardware
{
    sealed class ZeroFanResult
    {
        public bool Success,Stopped,Restarted,GuardExpired;
        public string Error;
        public FanControlState Restored;
        public List<object> Samples=new List<object>();
    }
    static class ZeroFanDiagnostics
    {
        static FanControlState Sample(ZeroFanResult result,FanControlClient client,int step,string phase)
        {
            Thread.Sleep(1000);if(FanControlDiagnostics.TestGuard!=null)FanControlDiagnostics.TestGuard();
            var state=FanControlClient.Read();result.Samples.Add(new {Phase=phase,State=state});
            if(!state.Manual)throw new IOException("Zero trial stopped: reason="+state.StopReason+", temperature="+state.TemperatureC);
            client.Heartbeat(step);return state;
        }
        static FanControlState WaitForCool(ZeroFanResult result,FanControlClient client,int step,string phase)
        {
            int stable=0;
            FanControlState state=null;
            for(int i=0;i<90;i++) {
                state=Sample(result,client,step,phase);
                stable=state.TemperatureC>=0 && state.TemperatureC<40?stable+1:0;
                if(stable>=3 && state.Fan1Rpm>500 && state.Fan2Rpm>500)return state;
            }
            throw new IOException(phase+": three consecutive samples below 40 C with both fans rotating were not observed.");
        }
        public static FanVerificationReport Verify()
        {
            var result=new ZeroFanResult();
            using(var client=new FanControlClient()) {
                try {
                    client.Start(3);
                    FanControlState state=null;
                    for(int i=0;i<15;i++)state=Sample(result,client,3,"before-stop");
                    state=WaitForCool(result,client,3,"cool-before-stop");
                    if(!state.Fan1Rpm.HasValue || !state.Fan2Rpm.HasValue || state.Fan1Rpm<500 || state.Fan2Rpm<500)throw new IOException("Initial fan rotation was not observed.");
                    if(state.TemperatureC>=40)throw new IOException("Fan-stop start requires below 40 C.");
                    client.Heartbeat(0);int stopped=0;
                    for(int i=0;i<20;i++) {state=Sample(result,client,0,"zero");stopped=state.Step==0 && state.Fan1Rpm==0 && state.Fan2Rpm==0?stopped+1:0;}
                    result.Stopped=stopped>=3;
                    if(!result.Stopped)throw new IOException("Both fans did not remain at 0 RPM for three samples.");
                    client.Heartbeat(2);
                    for(int i=0;i<20;i++)state=Sample(result,client,2,"restart");
                    result.Restarted=state.Step==2 && state.Fan1Rpm.HasValue && state.Fan2Rpm.HasValue && state.Fan1Rpm>500 && state.Fan2Rpm>500;
                    if(!result.Restarted)throw new IOException("Fan restart was not verified.");
                    state=WaitForCool(result,client,2,"cool-before-deadline");
                    client.Heartbeat(0);var clock=Stopwatch.StartNew();
                    while(clock.Elapsed.TotalSeconds<36) {
                        Thread.Sleep(1000);if(FanControlDiagnostics.TestGuard!=null)FanControlDiagnostics.TestGuard();
                        state=FanControlClient.Read();result.Samples.Add(new {Phase="zero-deadline",State=state});
                        if(state.State==2)continue;
                        if(!state.Manual) {result.GuardExpired=state.State==0 && state.Status==0 && state.StopReason==10 && state.TemperatureC<50 && clock.Elapsed.TotalSeconds>=29;break;}
                        client.Heartbeat(0);
                    }
                    if(!result.GuardExpired)throw new IOException("Non-renewable 30-second Auto deadline was not verified.");
                } catch(Exception ex) {result.Error=ex.Message;}
                finally {if(client.HasRequest)try {result.Restored=client.Restore();}catch(Exception ex){result.Error=(result.Error??"")+" Restore: "+ex.Message;}}
            }
            result.Success=result.Error==null && result.Stopped && result.Restarted && result.GuardExpired && result.Restored!=null && result.Restored.State==0 && result.Restored.Status==0;
            string folder=Path.GetFullPath(Path.Combine(AppDomain.CurrentDomain.BaseDirectory,"..","artifacts"));Directory.CreateDirectory(folder);
            File.WriteAllText(Path.Combine(folder,"fan-zero-trial.json"),new JavaScriptSerializer().Serialize(result));
            return new FanVerificationReport {Stage=result.Success?"complete":"zero-verification",Success=result.Success,Error=result.Error,TargetRpm=0,Target=result};
        }
    }
}
