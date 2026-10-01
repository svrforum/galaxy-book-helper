using System;
using System.ComponentModel;
using System.Drawing;
using System.IO;
using System.Threading;
using System.Windows.Forms;
using System.Web.Script.Serialization;

namespace GalaxyHardware
{
    sealed class FanSetupDialog : Form
    {
        readonly Func<FanDiagnosticSession,FanSetupResult> run;
        readonly CancellationTokenSource cancellation=new CancellationTokenSource();
        readonly BackgroundWorker worker=new BackgroundWorker {WorkerReportsProgress=true};
        readonly Label description=new Label();
        readonly TextBox stage=new TextBox {ReadOnly=true,Multiline=true,ScrollBars=ScrollBars.Vertical,BorderStyle=BorderStyle.None,TabStop=false};
        readonly ProgressBar progress=new ProgressBar();
        readonly Button start=new SoftButton(),cancel=new SoftButton();
        bool running,finished;
        internal FanSetupResult Result { get; private set; }
        internal FanSetupDialog(Func<FanDiagnosticSession,FanSetupResult> runner,bool automatic=false)
        {
            run=runner;Text="팬 자동 설정";Font=new Font("맑은 고딕",9.5f);
            ClientSize=new Size(480,300);FormBorderStyle=FormBorderStyle.FixedDialog;
            StartPosition=FormStartPosition.CenterParent;MaximizeBox=MinimizeBox=false;
            description.Text="이 PC의 팬 속도를 측정하고 자동 복귀를 검증합니다.\n약 7~9분 동안 전력을 임시로 낮추고 팬을 회전시킵니다.\n완료·취소 후 원래 전력과 자동 냉각으로 복원합니다.\n부하 작업을 닫고 삼성 성능 모드를 유지해 주세요.";
            description.SetBounds(20,18,440,100);Controls.Add(description);
            stage.SetBounds(20,126,440,75);stage.Text="보정·검증을 시작할 준비가 됐습니다.";Controls.Add(stage);
            progress.SetBounds(20,210,440,18);Controls.Add(progress);
            start.Text="보정 및 검증 시작";start.SetBounds(198,248,150,34);Controls.Add(start);
            cancel.Text="닫기";cancel.SetBounds(358,248,102,34);Controls.Add(cancel);
            start.Click+=delegate {StartSetup();};cancel.Click+=delegate {if(running)CancelSetup();else Close();};
            worker.DoWork+=delegate(object sender,DoWorkEventArgs e) {
                var session=new FanDiagnosticSession(cancellation.Token,delegate(FanSetupProgress p){worker.ReportProgress(p.Percent,p);});
                e.Result=run(session);
            };
            worker.ProgressChanged+=delegate(object sender,ProgressChangedEventArgs e) {
                if(IsDisposed||Disposing)return;
                var p=(FanSetupProgress)e.UserState;progress.Value=p.Percent;
                stage.Text=p.Message;
            };
            worker.RunWorkerCompleted+=delegate(object sender,RunWorkerCompletedEventArgs e) {
                running=false;if(IsDisposed||Disposing)return;finished=true;cancel.Enabled=true;cancel.Text="닫기";
                start.Visible=false;
                Result=e.Error==null?(FanSetupResult)e.Result:new FanSetupResult {Error=e.Error.Message};
                stage.Text=Result.Success?"설정 완료 · 커브를 사용할 수 있습니다.":
                    (Result.Cancelled?"설정을 취소했습니다.":"설정을 완료하지 못했습니다.")+"\n"+(Result.Error??"");
                description.Text=Result.PowerRestored ?
                    "원래 전력 복원을 확인했습니다.\n"+(Result.Success?"닫기를 누르면 커브가 바로 활성화됩니다.":"원인을 확인한 뒤 팬 설정 버튼으로 다시 시도할 수 있습니다."):
                    "설정 복원을 확인하지 못했습니다. 메인 화면의 ‘원래대로’와 상세 오류를 확인하세요.";
                if(Result.Success) {progress.Value=100;DialogResult=DialogResult.OK;Close();}
            };
            Shown+=delegate {if(automatic)BeginInvoke(new Action(StartSetup));};
        }
        internal string StageText {get{return stage.Text;}}
        internal int ProgressValue {get{return progress.Value;}}
        internal bool Running {get{return running;}}
        internal void StartSetup()
        {
            if(running||finished)return;
            running=true;start.Enabled=false;cancel.Text="취소 및 복원";worker.RunWorkerAsync();
        }
        internal void CancelSetup()
        { cancellation.Cancel();cancel.Enabled=false;stage.Text="취소 요청 · 팬과 전력 복원을 기다리는 중…"; }
        protected override void OnFormClosing(FormClosingEventArgs e)
        {
            if(running) {CancelSetup();e.Cancel=e.CloseReason!=CloseReason.WindowsShutDown;}
            base.OnFormClosing(e);
        }
        protected override void WndProc(ref Message m)
        {
            // Cancel before allowing sleep. The driver's lease protects the fan
            // if Windows suspends before the worker can finish restoration.
            if(m.Msg==0x218 && (m.WParam.ToInt32()==4 || m.WParam.ToInt32()==0))cancellation.Cancel();
            base.WndProc(ref m);
        }
        protected override void Dispose(bool disposing)
        {
            if(disposing && !running) {worker.Dispose();cancellation.Dispose();}
            base.Dispose(disposing);
        }
    }
    sealed partial class ControlForm
    {
        readonly Button fanSetup=new SoftButton();
        bool setupRunning,automaticSetupAttempted,automaticSetupQueued;
        DateTime lastSetupReadyCheck=DateTime.MinValue;
        internal bool CalibrationMissing()
        { try {FanCalibration.Load();return false;}catch {return true;} }
        void TryAutomaticFanSetup()
        {
            if(smokeMode||automaticSetupAttempted||automaticSetupQueued||setupRunning||hardwareDisposed||!Visible||!IsHandleCreated)return;
            if((DateTime.UtcNow-lastSetupReadyCheck).TotalSeconds<5)return;
            lastSetupReadyCheck=DateTime.UtcNow;
            if(!fanReady) {
                try {fanReady=FanControlClient.IsReady();}catch{return;}
                FanButtons();
            }
            if(!fanReady||fanBusy||busy)return;
            if(!CalibrationMissing()) {automaticSetupAttempted=true;return;}
            if(File.Exists(Program.Journal)) {
                automaticSetupAttempted=true;fanControlStatus.Text="이전 전력 기록을 복원한 뒤 팬 설정을 시작하세요.";return;
            }
            automaticSetupQueued=true;
            BeginInvoke(new Action(delegate {automaticSetupQueued=false;OpenFanSetup(true);}));
        }
        void OpenFanSetup(bool automatic=false)
        {
            if(smokeMode||setupRunning||fanBusy||busy)return;
            if(!fanReady) {NotifyError("팬 드라이버 적용 대기입니다. Windows 재시작 후 30초 기다려 주세요.");return;}
            if(fanClient!=null) {
                afterFanRestore=delegate {OpenFanSetup(automatic);};RestoreFan();return;
            }
            try {
                if(ownsSetting)Restore();
                if(File.Exists(Program.Journal))throw new IOException("먼저 ‘원래대로’로 이전 전력 기록을 복원하세요.");
            }catch(Exception ex){NotifyError(ex.Message);return;}
            automaticSetupAttempted=true;timer.Stop();setupRunning=busy=fanBusy=true;Buttons();FanButtons();dismissTimer.Stop();
            try {
                using(var dialog=new FanSetupDialog(delegate(FanDiagnosticSession session){return Program.VerifyFanWithReducedPower(false,false,session);},automatic)) {
                    dialog.ShowDialog(this);
                    if(dialog.Result!=null) {
                        string folder=Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),"GalaxyHelper","diagnostics");
                        try {Directory.CreateDirectory(folder);File.WriteAllText(Path.Combine(folder,"fan-setup.json"),new JavaScriptSerializer().Serialize(dialog.Result));}
                        catch(Exception ex){NotifyError("검증 기록 저장 실패: "+ex.Message);}
                        if(dialog.Result.Success) {
                            try {ReloadFanEditor();fanControlStatus.Text="보정·검증 완료 · 원하는 커브를 적용하세요.";}
                            catch(Exception ex){NotifyError("보정은 완료됐지만 화면 갱신에 실패했습니다. 앱을 다시 여세요: "+ex.Message);}
                        } else {
                            fanControlStatus.Text=dialog.Result.Error??"팬 설정을 완료하지 못했습니다.";
                            detailsTip.SetToolTip(fanSetup,fanControlStatus.Text);
                        }
                    }
                }
            }finally {
                setupRunning=busy=fanBusy=false;
                if(!IsDisposed&&!Disposing) {timer.Start();RefreshReadings();Buttons();FanButtons();}
            }
        }
        internal void ReloadFanEditor()
        {
            curveDetails.SuspendLayout();
            while(curveDetails.Controls.Count>0) {var c=curveDetails.Controls[0];curveDetails.Controls.Remove(c);c.Dispose();}
            curveTemperature=new NumericUpDown();curveRpm=new NumericUpDown();curveCutoff=new NumericUpDown();curveSelection=new Label();
            presetPicker=new ComboBox();savePresetButton=new SoftButton();deletePresetButton=new SoftButton();
            BuildInlineCurve(curveDetails);curveDetails.ResumeLayout(true);
            var profile=previewCalibration??FanCalibration.Load();
            fanTarget.Minimum=(decimal)(Math.Ceiling(profile.Entries[0].ConservativeRpm/100.0)*100);
            fanTarget.Value=Math.Min(fanTarget.Maximum,(decimal)(Math.Ceiling(profile.Entries[1].ConservativeRpm/100.0)*100));
            DecorateButtons(curveDetails);FitWithoutScroll((FlowLayoutPanel)Controls[0]);PositionPanel();
        }
    }
}
