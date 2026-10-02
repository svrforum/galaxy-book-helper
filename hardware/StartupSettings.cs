using System;
using System.IO;
using System.Security.Principal;
using System.Web.Script.Serialization;
using System.Windows.Forms;

namespace GalaxyHardware
{
    sealed class AppliedSettings
    {
        public int Version {get;set;}
        public double? Pl1 {get;set;}
        public double? Pl2 {get;set;}
        public FanCurve Curve {get;set;}
        internal static string PathName {get{return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),"GalaxyHelper","last-applied.json");}}
        internal static AppliedSettings Load(string path){if(!File.Exists(path))return new AppliedSettings{Version=1};var s=new JavaScriptSerializer().Deserialize<AppliedSettings>(File.ReadAllText(path));if(s==null||s.Version!=1||s.Pl1.HasValue!=s.Pl2.HasValue||s.Pl1.HasValue&&(Double.IsNaN(s.Pl1.Value)||Double.IsNaN(s.Pl2.Value)||s.Pl1<5||s.Pl1>80||s.Pl2<s.Pl1||s.Pl2>80))throw new InvalidDataException("자동 적용 설정 파일이 올바르지 않습니다.");return s;}
        internal void Save(string path){Directory.CreateDirectory(Path.GetDirectoryName(path));string temp=path+"."+Guid.NewGuid().ToString("N")+".tmp";try{File.WriteAllText(temp,new JavaScriptSerializer().Serialize(this));if(File.Exists(path))File.Replace(temp,path,path+".bak");else File.Move(temp,path);}finally{if(File.Exists(temp))File.Delete(temp);}}
    }
    static class StartupTask
    {
        static string Sid {get{return WindowsIdentity.GetCurrent().User.Value;}}
        internal static string Name {get{return "GalaxyHelper-"+Sid;}}
        static dynamic Root(){dynamic service=Activator.CreateInstance(Type.GetTypeFromProgID("Schedule.Service"));service.Connect();return service.GetFolder("\\");}
        internal static bool Enabled(){try{return (bool)Root().GetTask(Name).Enabled;}catch(Exception ex){if(ex.HResult==unchecked((int)0x80070002))return false;throw;}}
        internal static void Set(bool enabled)
        {
            dynamic root=Root();if(!enabled){if(Enabled())root.DeleteTask(Name,0);return;}
            // An elevated logon task must not execute from a user-writable checkout.
            string destination=Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles),"GalaxyHelper","startup",Sid);
            Directory.CreateDirectory(destination);Directory.CreateDirectory(Path.Combine(destination,"modules"));
            string exe=Path.Combine(destination,"GalaxyHelper.exe");
            if(!String.Equals(Application.ExecutablePath,exe,StringComparison.OrdinalIgnoreCase))File.Copy(Application.ExecutablePath,exe,true);
            foreach(string file in Directory.GetFiles(Path.Combine(Application.StartupPath,"modules"),"*.bin")){string target=Path.Combine(destination,"modules",Path.GetFileName(file));if(!String.Equals(file,target,StringComparison.OrdinalIgnoreCase))File.Copy(file,target,true);}
            dynamic service=Activator.CreateInstance(Type.GetTypeFromProgID("Schedule.Service"));service.Connect();dynamic task=service.NewTask(0);
            task.RegistrationInfo.Description="Galaxy Helper: 로그인 후 마지막으로 적용한 전력 및 팬 커브 복원";
            task.Principal.UserId=Sid;task.Principal.LogonType=3;task.Principal.RunLevel=1;
            task.Settings.Enabled=true;task.Settings.DisallowStartIfOnBatteries=false;task.Settings.StopIfGoingOnBatteries=false;task.Settings.ExecutionTimeLimit="PT0S";task.Settings.MultipleInstances=2;
            dynamic trigger=task.Triggers.Create(9);trigger.UserId=Sid;trigger.Delay="PT30S";
            dynamic action=task.Actions.Create(0);action.Path=exe;action.Arguments="--startup";action.WorkingDirectory=destination;
            root.RegisterTaskDefinition(Name,task,6,Sid,null,3,null);
            if(!Enabled())throw new IOException("Windows 시작 작업 등록을 확인하지 못했습니다.");
        }
    }
    sealed partial class ControlForm
    {
        internal void NotifyStartupUpdate(string message){NotifyError("자동 시작용 앱 갱신 실패: "+message);}
        void RememberApplied(bool power,FanCurve curve=null)
        {
            if(smokeMode)return;
            try{var saved=AppliedSettings.Load(AppliedSettings.PathName);if(power){saved.Pl1=Rapl.Pl1(expected,units);saved.Pl2=Rapl.Pl2(expected,units);}else saved.Curve=curve==null?null:FanPresetStore.Copy(curve);saved.Save(AppliedSettings.PathName);}
            catch(Exception ex){NotifyError("마지막 설정 저장 실패: "+ex.Message);}
        }
        void AddStartupMenu()
        {
            var item=new ToolStripMenuItem("Windows 시작 시 마지막 설정 적용");
            try{item.Checked=StartupTask.Enabled();}catch(Exception ex){item.Enabled=false;item.ToolTipText=ex.Message;}
            item.Click+=delegate{try{StartupTask.Set(!item.Checked);item.Checked=StartupTask.Enabled();if(item.Checked)NotifyError("로그인 후 마지막 적용값을 복원합니다. 전력·커브를 한 번 적용해 저장하세요.");}catch(Exception ex){NotifyError("자동 시작 설정 실패: "+ex.Message);}};
            quickMenu.Items.Add(item);
        }
        internal void VerifyResume(string output)
        {
            smokeMode=true;var saved=AppliedSettings.Load(AppliedSettings.PathName);
            var watch=System.Diagnostics.Stopwatch.StartNew();var check=new System.Windows.Forms.Timer{Interval=500};
            var samples=new System.Collections.Generic.List<FanControlState>();int goodSamples=0;
            ResumeAtLogon(false);
            check.Tick+=delegate {
                try {
                    bool power=!saved.Pl1.HasValue || ownsSetting && Math.Abs(Rapl.Pl1(msr.ReadMsr(0x610),units)-saved.Pl1.Value)<0.13 && Math.Abs(Rapl.Pl2(msr.ReadMsr(0x610),units)-saved.Pl2.Value)<0.13;
                    bool curve=saved.Curve==null;
                    if(saved.Curve!=null && fanClient!=null && !fanBusy && fanCurvePolicy!=null) {
                        var state=FanControlClient.Read();samples.Add(state);
                        curve=state.Manual && state.MaxStep==FanCalibration.Load().MaxStep && fanCurvePolicy.Snapshot.Signature==saved.Curve.Signature;
                        if(saved.Curve.IsZeroHold)curve=curve && state.Step==0 && state.Fan1Rpm==0 && state.Fan2Rpm==0 && state.ZeroLimitC==saved.Curve.ZeroStopTemperature;
                        else curve=curve && state.Step==fanCurvePolicy.Step && state.Fan1Rpm>0 && state.Fan2Rpm>0;
                    }
                    goodSamples=power&&curve?goodSamples+1:0;
                    if(watch.Elapsed.TotalSeconds<8 || goodSamples<3 && watch.Elapsed.TotalSeconds<65)return;
                    check.Stop();check.Dispose();
                    File.WriteAllText(output,new JavaScriptSerializer().Serialize(new {Success=goodSamples>=3,PowerVerified=power,CurveWasSaved=saved.Curve!=null,CurveVerified=saved.Curve==null?(bool?)null:curve,Samples=samples,Status=status.Text,FanStatus=fanControlStatus.Text}));Close();
                } catch(Exception ex) {check.Stop();check.Dispose();File.WriteAllText(output,new JavaScriptSerializer().Serialize(new {Success=false,Error=ex.ToString(),Samples=samples}));Close();}
            };
            Shown+=delegate{check.Start();};
        }
        internal void ResumeAtLogon(bool background=true)
        {
            if(background)Shown+=delegate {BeginInvoke((Action)delegate{Hide();});};
            var wait=new System.Windows.Forms.Timer{Interval=2000};int attempts=0;
            wait.Tick+=delegate {
                try {
                    if(background && !StartupTask.Enabled()){wait.Stop();wait.Dispose();return;}
                    var saved=AppliedSettings.Load(AppliedSettings.PathName);
                    if((!readingHealthy||fanBusy||setupRunning||saved.Curve!=null&&!FanControlClient.IsReady())&&++attempts<30)return;
                    wait.Stop();wait.Dispose();
                    if(!readingHealthy||fanBusy||setupRunning)throw new IOException("센서 준비를 확인하지 못했습니다.");
                    if(File.Exists(Program.Journal)){var recovery=new JavaScriptSerializer().Deserialize<Program.Recovery>(File.ReadAllText(Program.Journal));if(recovery==null||String.IsNullOrWhiteSpace(recovery.Boot)||recovery.Boot==Program.Boot())throw new IOException("현재 부팅의 복원 기록이 남아 있습니다. 수동 확인이 필요합니다.");File.Move(Program.Journal,Program.Journal+".previous-boot-"+Guid.NewGuid().ToString("N"));}
                    if(saved.Curve!=null){var profile=FanCalibration.Load();saved.Curve.Validate(profile);new FanCurvePolicy(profile,saved.Curve,Program.Temperature(msr));if(!FanControlClient.IsReady()||saved.Curve.HasZero&&!FanControlClient.IsZeroHoldReady())throw new IOException("팬 드라이버가 준비되지 않았습니다.");}
                    if(saved.Pl1.HasValue){pl1.Value=(decimal)saved.Pl1.Value;pl2.Value=(decimal)saved.Pl2.Value;Apply();if(!ownsSetting)throw new IOException("전력 적용을 확인하지 못했습니다.");}
                    if(saved.Curve!=null){fanReady=true;inlineGraph.SetCurve(saved.Curve);saved.Curve.Save(FanCalibration.Load());StartCurve();}
                }catch(Exception ex){wait.Stop();wait.Dispose();NotifyError("시작 설정 자동 적용 중단: "+ex.Message);}
            };
            Shown+=delegate{wait.Start();};
        }
    }
}
