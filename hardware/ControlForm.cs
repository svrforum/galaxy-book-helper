using System;
using System.ComponentModel;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Security.Principal;
using System.Threading;
using System.Windows.Forms;

namespace GalaxyHardware
{
    // This UI uses the real RAPL backend. It never maps a percentage to a watt label.
    sealed partial class ControlForm : Form
    {
        readonly PawnDevice msr;
        readonly PawnDevice mmio;
        readonly ulong units;
        readonly NumericUpDown pl1 = new NumericUpDown();
        readonly NumericUpDown pl2 = new NumericUpDown();
        readonly Label measured = new Label();
        readonly Label limits = new Label();
        readonly Label mmioLimits = new Label();
        readonly Label status = new SingleLineStatus();
        readonly Label fanReading = new Label();
        readonly Button fanRefresh = new SoftButton();
        readonly Button fanApply = new SoftButton(), fanAuto = new SoftButton(), fanCap = new SoftButton();
        readonly NumericUpDown fanSteps = new NumericUpDown();
        readonly NumericUpDown fanTarget = new NumericUpDown();
        readonly System.Windows.Forms.Timer fanTimer = new System.Windows.Forms.Timer();
        FanControlClient fanClient;
        FanCalibration fanCalibration;
        int fanStep;
        RpmCapPolicy fanCapPolicy;
        FanCurvePolicy fanCurvePolicy;
        int? fanGoal;
        bool fanReady;
        readonly Label fanControlStatus = new SingleLineStatus();
        bool fanBusy;
        readonly Button apply = new SoftButton();
        readonly Button restore = new SoftButton();
        readonly System.Windows.Forms.Timer timer = new System.Windows.Forms.Timer();
        readonly NotifyIcon tray = new NotifyIcon();
        readonly Stopwatch sampleClock = new Stopwatch();
        uint energy;
        ulong expected;
        bool ownsSetting;
        bool busy;
        bool readingHealthy;
        bool hardwareDisposed;
        readonly FanCalibration previewCalibration;
        public ControlForm(bool layoutPreview=false,FanCalibration calibration=null)
        {
            previewCalibration=layoutPreview?calibration:null;
            if(layoutPreview) {
                smokeMode=true;BuildCompactUi();tray.Visible=false;
                measured.Text="— W   ·   — °C";fanReading.Text="— RPM     /     — RPM";
                status.Text="지속 / 단기 전력";
                fanControlStatus.Text="커브를 드래그해 조절하세요.";
                return;
            }
            Program.CheckMachine();
            msr = new PawnDevice("IntelMSR");
            try
            {
                units = msr.ReadMsr(0x606);
                try { mmio = new PawnDevice("IntelMCHBAR"); } catch { }
                BuildCompactUi();
                energy = (uint)msr.ReadMsr(0x611); sampleClock.Start();
                status.Text = File.Exists(Program.Journal) ? "이전 복원 기록이 있습니다. 복원 후 새 제한을 적용하세요." : "전력 제한 대기";
                RefreshReadings();
                try { ShowFan(FanReadBridge.Cached()); } catch (Exception ex) { fanReading.Text = ex.Message; }
                try {
                    fanReady = FanControlClient.IsReady();
                    fanControlStatus.Text = fanReady ? "자동 제어 중 · 그래프를 조절한 뒤 커브 적용을 누르세요." : "지속 제어 드라이버 적용 대기 · 재시작 후 30초 뒤 앱을 다시 여세요.";
                    if (fanReady) {
                        try {
                            var profile=FanCalibration.Load();
                            decimal minimum=(decimal)(Math.Ceiling(profile.Entries[0].ConservativeRpm/100.0)*100);
                            if (minimum>fanTarget.Maximum) throw new IOException("보정된 RPM이 입력 범위를 넘습니다.");
                            fanTarget.Minimum=minimum;
                            fanTarget.Value=Math.Min(fanTarget.Maximum,(decimal)(Math.Ceiling(profile.Entries[1].ConservativeRpm/100.0)*100));
                            fanControlStatus.Text="자동 냉각 중";
                        } catch (Exception ex) { fanControlStatus.Text="RPM 목표: "+ex.Message; }
                    }
                } catch (Exception ex) { fanReady = false; fanControlStatus.Text = ex.Message; }
                FanButtons();
                timer.Interval = 1000; timer.Tick += delegate { RefreshReadings(); }; timer.Start();
            }
            catch { if (mmio != null) mmio.Dispose(); msr.Dispose(); throw; }
        }
        static void Add(FlowLayoutPanel stack, Control item, int height) { item.Size = new Size(575, height); item.Margin = new Padding(0, 2, 0, 4); stack.Controls.Add(item); }
        static void Configure(NumericUpDown input, decimal initial) { input.Minimum = 5; input.Maximum = 80; input.DecimalPlaces = 1; input.Increment = 0.5M; input.Value = initial; input.Width = 90; input.ForeColor = Color.Black; }
        static void Style(Button button, string text, int width) { button.Text = text; button.Size = new Size(width, 32); button.Margin = new Padding(0, 0, 6, 0); button.FlatStyle = FlatStyle.Flat; button.BackColor = Color.FromArgb(232,239,250); }
        void ShowFan(FanSample sample)
        {
            fanReading.Text = sample.Success
                ? String.Format("{0:N0} RPM     /     {1:N0} RPM", sample.Fan1Rpm, sample.Fan2Rpm, sample.SampleUtc.ToLocalTime(), sample.FreshRequestCompleted ? "자동 갱신" : "저장된 측정값")
                : "팬 조회 실패: " + sample.NtStatus + " · RPM 알 수 없음";
        }
        void RefreshFan(bool silent = false)
        {
            if (fanBusy || fanClient!=null || hardwareDisposed) return;
            fanReadInFlight=true; fanBusy = true;
            FanButtons();
            fanRefresh.Enabled = false; if (!silent) fanReading.Text = "새 팬 RPM 조회 중…";
            var worker = new BackgroundWorker();
            worker.DoWork += delegate(object sender, DoWorkEventArgs e) { e.Result = FanReadBridge.Request(); };
            worker.RunWorkerCompleted += delegate(object sender, RunWorkerCompletedEventArgs e) {
                if (!IsDisposed && !Disposing) {
                    lastFanPoll=DateTime.UtcNow; fanReadInFlight=false; fanBusy = false; FanButtons();
                    fanRefresh.Enabled = true;
                    if (e.Error != null) fanReading.Text = "팬 조회 실패: " + e.Error.Message;
                    else ShowFan((FanSample)e.Result);
                    var pending=afterFanRead; afterFanRead=null;
                    if(pending!=null && e.Error==null) pending();
                }
                worker.Dispose();
            };
            worker.RunWorkerAsync();
        }
        void FanButtons()
        {
            fanSetup.Enabled=fanReady && !fanBusy && !busy;
            curveButton.Enabled=inlineGraph!=null && fanReady && (!fanBusy || fanReadInFlight);
            fanApply.Enabled=fanCap.Enabled=fanReady && !fanBusy && fanClient==null;
            fanSteps.Enabled=fanTarget.Enabled=!fanBusy && fanClient==null;
            fanAuto.Enabled=!fanBusy && fanClient!=null;
            fanRefresh.Enabled=!fanBusy && fanClient==null;
            foreach (var button in new[] {fanApply,fanAuto,fanCap}) button.BackColor=button.Enabled ? Color.FromArgb(232,239,250) : Color.LightSlateGray;
        }
        void StartFan(bool cap,bool curve=false)
        {
            if (fanBusy || fanClient!=null) return;
            try {
                fanCalibration=(cap || curve) ? FanCalibration.Load() : null;
                fanCurvePolicy=curve ? new FanCurvePolicy(fanCalibration,FanCurve.Load(fanCalibration),Program.Temperature(msr)) : null;
                if(fanCurvePolicy!=null && fanCurvePolicy.ZeroLimit!=0) {
                    if(!FanControlClient.IsZeroHoldReady())throw new IOException("0 RPM 유지 드라이버 적용 대기: Windows를 다시 시작해 주세요.");
                    if(Program.Temperature(msr)>=fanCurvePolicy.ZeroLimit-5)throw new IOException("정지 시작은 해제 온도보다 5°C 이상 낮을 때 가능합니다.");
                    QuickPower(5,10);
                    ulong powerNow=msr.ReadMsr(0x610);
                    if(Rapl.Pl1(powerNow,units)>5 || Rapl.Pl2(powerNow,units)>10)throw new IOException("0 RPM용 5/10W 전력 제한 적용을 확인하지 못했습니다.");
                }
                fanGoal=cap ? (int?)fanTarget.Value : null;
                fanStep=cap ? fanCalibration.SelectStep(fanGoal.Value) : (int)fanSteps.Value;
                if(curve) fanStep=fanCurvePolicy.Step;
                fanCapPolicy=cap ? new RpmCapPolicy(fanCalibration,fanGoal.Value,DateTime.UtcNow) : null;
            } catch (Exception ex) { fanControlStatus.Text=ex.Message; return; }
            fanBusy=true; fanClient=new FanControlClient(); FanButtons();
            if(fanCurvePolicy!=null)fanClient.ZeroHoldLimit=fanCurvePolicy.ZeroLimit;
            var client=fanClient;
            var worker=new BackgroundWorker();
            worker.DoWork += delegate(object sender,DoWorkEventArgs e) {
                var state=client.Start(fanStep);
                if (fanCalibration!=null && state.MaxStep!=fanCalibration.MaxStep) { client.Restore(); throw new InvalidOperationException("보정 때의 최대 단계와 다릅니다. 자동 복귀 후 다시 보정하세요."); }
                e.Result=state;
            };
            worker.RunWorkerCompleted += delegate(object sender, RunWorkerCompletedEventArgs e) {
                if (IsDisposed || Disposing) { try { client.Dispose(); } catch { } worker.Dispose(); return; }
                fanBusy=false;
                if (e.Error!=null) { StopFan(); fanControlStatus.Text=e.Error.Message; }
                else {
                    if (fanCapPolicy!=null) fanCapPolicy.MarkStarted(DateTime.UtcNow);
                    fanControlStatus.Text="팬 설정 적용 완료 · 실제 속도 안정화 중";
                    fanTimer.Start();RememberApplied(false,fanCurvePolicy==null?null:fanCurvePolicy.Snapshot);
                }
                FanButtons(); worker.Dispose();
            };
            worker.RunWorkerAsync();
        }
        void TickFan()
        {
            if (fanClient==null) return;
            try {
                var state=fanClient.Heartbeat(fanStep);
                fanReading.Text=String.Format("{0:N0} RPM     /     {1:N0} RPM",state.Fan1Rpm,state.Fan2Rpm,state.TemperatureC,state.SampleUtc.ToLocalTime());
                fanControlStatus.Text=fanGoal.HasValue ? "목표 " + fanGoal + " RPM · 실제 속도에 맞춰 조절 중" : DescribeFanStep(fanStep)+" · 자동 제어로 복귀하면 해제됩니다.";
                if (fanGoal.HasValue) {
                    fanStep=fanCapPolicy.Observe(state,DateTime.UtcNow);
                    if (fanCapPolicy.Settling(DateTime.UtcNow)) fanControlStatus.Text+=" · 안정화 중";
                }
                if(fanCurvePolicy!=null) {
                    fanStep=fanCurvePolicy.Observe(state,DateTime.UtcNow);
                    int supported=fanStep==0?0:Math.Max(fanCalibration.Entries[fanStep-1].Fan1Peak,fanCalibration.Entries[fanStep-1].Fan2Peak);
                    fanControlStatus.Text="커브 적용 중 · 요청 "+fanCurvePolicy.Target+" → 지원 약 "+supported+" RPM";
                }
            } catch (Exception ex) { StopFan(); fanControlStatus.Text=ex.Message + "\n자동 복귀 요청 · 원인 확인 후 다시 적용하세요."; FanButtons(); }
        }
        void StopFan()
        {
            fanTimer.Stop();
            fanCurvePolicy=null;
            var client=fanClient; fanClient=null;
            if (client!=null) try { client.Dispose(); } catch (Exception ex) { fanControlStatus.Text="복귀 요청 실패: " + ex.Message; }
        }
        void RestoreFan()
        {
            if (fanClient==null || fanBusy) return;
            fanTimer.Stop(); fanBusy=true; FanButtons();
            var client=fanClient;
            var worker=new BackgroundWorker();
            worker.DoWork += delegate(object sender,DoWorkEventArgs e) { e.Result=client.Restore(); };
            worker.RunWorkerCompleted += delegate(object sender,RunWorkerCompletedEventArgs e) {
                if (!IsDisposed && !Disposing) {
                    fanBusy=false; StopFan();
                    fanControlStatus.Text=e.Error==null ? "팬 자동 제어 복귀 응답 확인" : "자동 복귀 확인 실패: " + e.Error.Message;
                    FanButtons();
                    var next=afterFanRestore; afterFanRestore=null;
                    if(e.Error==null && next!=null) next();else if(e.Error==null)RememberApplied(false);
                }
                worker.Dispose();
            };
            worker.RunWorkerAsync();
        }
        void Buttons()
        {
            bool recovery = File.Exists(Program.Journal);
            apply.Enabled = !busy && readingHealthy && !recovery; restore.Enabled = !busy && recovery;
            pl1.Enabled = pl2.Enabled = !busy && !recovery;
            apply.BackColor = apply.Enabled ? Color.FromArgb(232,239,250) : Color.LightSlateGray;
            restore.BackColor = restore.Enabled ? Color.FromArgb(232,239,250) : Color.LightSlateGray;
        }
        void RefreshReadings()
        {
            if (busy || hardwareDisposed) return;
            TryAutomaticFanSetup();
            if(automaticSetupAttempted && !fanReady && !fanBusy && (DateTime.UtcNow-lastSetupReadyCheck).TotalSeconds>=5) {
                lastSetupReadyCheck=DateTime.UtcNow;
                try {fanReady=FanControlClient.IsReady();}catch{}
                FanButtons();
            }
            try
            {
                ulong raw = msr.ReadMsr(0x610); int temperature = Program.Temperature(msr);
                if(inlineGraph!=null) {inlineGraph.CurrentTemperature=temperature;inlineGraph.Invalidate();}
                uint current = (uint)msr.ReadMsr(0x611); double elapsed = sampleClock.Elapsed.TotalSeconds;
                if (elapsed >= 0.25)
                {
                    double watts = Rapl.Watts(energy, current, units, elapsed); energy = current; sampleClock.Restart();
                    measured.Text = String.Format("{0:F1} W   ·   {1} °C", watts, temperature);
                }
                else measured.Text = "센서 초기화 중…";
                limits.Text = String.Format("MSR 현재 제한     PL1 {0:F1} W  /  PL2 {1:F1} W\n펌웨어 잠금: {2}", Rapl.Pl1(raw, units), Rapl.Pl2(raw, units), Rapl.Locked(raw) ? "잠김" : "해제");
                if (mmio != null)
                {
                    try { ulong m = mmio.ReadMchbar(0x59A0), u = mmio.ReadMchbar(0x5938); mmioLimits.Text = String.Format("MMIO 제한 (조회)   PL1 {0:F1} W  /  PL2 {1:F1} W", Rapl.Pl1(m, u), Rapl.Pl2(m, u)); }
                    catch (Exception ex) { mmioLimits.Text = "MMIO 조회 실패: " + ex.Message; }
                }
                else mmioLimits.Text = "MMIO 조회 인터페이스를 열지 못했습니다.";
                readingHealthy = !Rapl.Locked(raw) && (raw & (1UL << 15)) != 0 && (raw & (1UL << 47)) != 0;
                if (ownsSetting && (raw & Rapl.PowerMask) != (expected & Rapl.PowerMask))
                    status.Text = "다른 프로그램/펌웨어가 제한을 변경했습니다. 자동 재적용하지 않습니다.\n복원 기록을 남겼습니다. 복원 시 현재값을 다시 검사합니다.";
                if (ownsSetting && temperature >= 85) status.Text = "온도 85°C 이상. 낮춘 전력 요청을 유지합니다. 현재 부하를 확인하세요.";
            }
            catch (Exception ex)
            {
                readingHealthy = false;
                status.Text = "센서 조회 실패: " + ex.Message;
                if (ownsSetting) try { Program.Restore(msr); ownsSetting = false; } catch (Exception recovery) { status.Text += "\n복원 실패: " + recovery.Message; }
            }
            Buttons();
            UpdateQuickState();
            if(fanReady && fanClient==null && !fanBusy && (DateTime.UtcNow-lastFanPoll).TotalSeconds>=2) { lastFanPoll=DateTime.UtcNow; RefreshFan(true); }
        }
        void Apply()
        {
            busy = true; Buttons();
            try
            {
                Program.CheckMachine();
                if (File.Exists(Program.Journal)) throw new IOException("먼저 이전 전력 설정을 복원하세요.");
                ulong original = msr.ReadMsr(0x610); expected = Rapl.EncodeReduction(original, units, (double)pl1.Value, (double)pl2.Value);
                Program.SaveNewJournal(new Program.Recovery { Boot = Program.Boot(), Original = original.ToString("X16"), Intended = expected.ToString("X16"), Units = units.ToString("X16") });
                ownsSetting = true;
                try
                {
                    if (msr.ReadMsr(0x610) != original) throw new IOException("적용 직전 다른 프로그램이 전력 제한을 변경했습니다.");
                    msr.WritePackageLimit(expected);
                    if (msr.ReadMsr(0x610) != expected) throw new IOException("적용 후 재조회 불일치");
                }
                catch { Program.Restore(msr); ownsSetting = false; throw; }
                RememberApplied(true);
                status.Text = "MSR 설정값 저장·재조회 완료. 실효는 위 실측 W를 확인하세요.\n앱 종료 시 복원합니다. 다른 값을 적용하려면 먼저 복원하세요.";
            }
            catch (Exception ex) { status.Text = "적용 실패: " + ex.Message; MessageBox.Show(this, status.Text, "Galaxy Helper", MessageBoxButtons.OK, MessageBoxIcon.Error); }
            finally { busy = false; RefreshReadings(); }
        }
        void Restore()
        {
            busy = true; Buttons();
            try { Program.Restore(msr); ownsSetting = false; status.Text = "원래 전력 제한으로 복원하고 재조회로 확인했습니다."; }
            catch (Exception ex) { status.Text = "복원 실패: " + ex.Message; throw; }
            finally { busy = false; Buttons(); }
        }
        internal void RunControlSmoke(string image,bool curveTest=false,bool zeroTest=false)
        {
            var check=new System.Windows.Forms.Timer { Interval=250 };
            var elapsed=Stopwatch.StartNew();
            var rows=new System.Collections.Generic.List<FanControlState>();
            var powerRows=new System.Collections.Generic.List<object>();
            int phase=0; double started=0, sampled=0;
            ulong original=msr.ReadMsr(0x610);
            check.Tick += delegate {
                try {
                    if (elapsed.Elapsed.TotalSeconds>90) throw new TimeoutException("UI control verification timed out: "+fanControlStatus.Text);
                    if (phase==0) {
                        if (File.Exists(Program.Journal) || !apply.Enabled || !fanApply.Enabled) throw new IOException("UI controls not ready for verification.");
                        PopulateQuickMenu(); ((ToolStripMenuItem)quickMenu.Items[2]).DropDownItems[0].PerformClick();
                        if (!ownsSetting || Rapl.Pl1(msr.ReadMsr(0x610),units)!=10 || Rapl.Pl2(msr.ReadMsr(0x610),units)!=10) throw new IOException("UI power apply failed.");
                        phase=1;
                    } else if (phase==1 && !fanBusy && Program.Temperature(msr)<=60) {
                        PopulateQuickMenu();
                        if(zeroTest) {PreviewZeroCurve();curveButton.PerformClick();}
                        else if(curveTest) {
                            if(inlineGraph==null) throw new IOException("Inline curve is unavailable.");
                            inlineGraph.SetPreset("평균");
                            inlineGraph.Selected=3;int originalRpm=inlineGraph.Curve.Rpms[3];
                            curveTemperature.Value=52;
                            int candidate=originalRpm<inlineGraph.Curve.Rpms[4] ? originalRpm+50 : originalRpm-50;
                            inlineGraph.SetRpm(candidate);
                            inlineGraph.SetRpm(originalRpm);
                            curveButton.PerformClick();
                            if(FanCurve.Load(fanCalibration??inlineProfile).Temperatures[3]!=52)throw new IOException("Edited temperature was not saved for application.");
                        } else ((ToolStripMenuItem)quickMenu.Items[3]).DropDownItems[2].PerformClick();
                        phase=2;
                    } else if (phase==2 && !fanBusy) {
                        if (fanClient==null || !fanTimer.Enabled) throw new IOException("UI fan start failed: "+fanControlStatus.Text);
                        started=elapsed.Elapsed.TotalSeconds; phase=3;
                    } else if (phase==3) {
                        if (fanClient==null || !fanTimer.Enabled) throw new IOException("UI fan heartbeat stopped: "+fanControlStatus.Text);
                        if (elapsed.Elapsed.TotalSeconds-sampled>=1) {
                            var state=FanControlClient.Read();
                            if (!state.Manual || (!curveTest && state.Step!=2) || (curveTest && fanCurvePolicy==null) || (DateTime.UtcNow-state.SampleUtc).TotalSeconds>3) throw new IOException("UI manual state not confirmed.");
                            rows.Add(state); sampled=elapsed.Elapsed.TotalSeconds;
                            powerRows.Add(new {SampleUtc=DateTime.UtcNow,Raw=msr.ReadMsr(0x610).ToString("X16"),Expected=expected.ToString("X16"),Status=status.Text});
                        }
                        if (elapsed.Elapsed.TotalSeconds-started>=(zeroTest?60:(curveTest?45:20))) {
                            var state=FanControlClient.Read();
                            if(zeroTest) {
                                if(rows.Count<5 || rows.GetRange(rows.Count-5,5).Exists(s=>s.Step!=0 || s.ZeroLimitC!=90 || s.Fan1Rpm!=0 || s.Fan2Rpm!=0))throw new IOException("Sustained zero RPM not confirmed.");
                                if(Rapl.Pl1(msr.ReadMsr(0x610),units)!=5 || Rapl.Pl2(msr.ReadMsr(0x610),units)!=10)throw new IOException("Zero UI power limits differ from 5/10W.");
                            } else if (state.Fan1Rpm<500 || state.Fan2Rpm<500) throw new IOException("Fan movement not confirmed.");
                            using (var bitmap=new Bitmap(Width,Height)) { DrawToBitmap(bitmap,new Rectangle(0,0,Width,Height)); bitmap.Save(image); }
                            SaveLayoutReport(image+".layout.json");
                            PopulateQuickMenu(); ((ToolStripMenuItem)quickMenu.Items[3]).DropDownItems[0].PerformClick(); phase=4;
                        }
                    } else if (phase==4 && !fanBusy) {
                        var state=FanControlClient.Read();
                        if (fanClient!=null || state.State!=0 || state.Status!=0) throw new IOException("UI Auto restore not confirmed.");
                        PopulateQuickMenu(); ((ToolStripMenuItem)quickMenu.Items[2]).DropDownItems[3].PerformClick();
                        bool powerRestored=msr.ReadMsr(0x610)==original && !File.Exists(Program.Journal);
                        if (!powerRestored) throw new IOException("UI power restoration not confirmed.");
                        File.WriteAllText(image+".json",new System.Web.Script.Serialization.JavaScriptSerializer().Serialize(new {Success=true,CurveMode=curveTest,ZeroHoldMode=zeroTest,PowerRestored=powerRestored,AppliedCurve=curveTest?inlineGraph.Curve:null,Samples=rows,PowerSamples=powerRows,Auto=state}));
                        check.Stop(); check.Dispose(); Close();
                    }
                } catch (Exception ex) {
                    check.Stop();
                    string error=ex.Message;
                    string failedPower=null;try {failedPower=msr.ReadMsr(0x610).ToString("X16");}catch(Exception readError){failedPower=readError.Message;}
                    string failedStatus=status.Text;
                    try { if (fanClient!=null) fanClient.Restore(); } catch (Exception restoreError) { error+=" Fan restore: "+restoreError.Message; }
                    StopFan();
                    try { if (ownsSetting) Restore(); } catch (Exception restoreError) { error+=" Power restore: "+restoreError.Message; }
                    File.WriteAllText(image+".json",new System.Web.Script.Serialization.JavaScriptSerializer().Serialize(new {Success=false,Error=error,FailedPower=failedPower,FailedStatus=failedStatus,Samples=rows,PowerSamples=powerRows}));
                    check.Dispose(); Close();
                }
            };
            Shown += delegate { check.Start(); };
        }
        void ClosingForm(object sender, FormClosingEventArgs e)
        {
            if(setupRunning) { e.Cancel=e.CloseReason!=CloseReason.WindowsShutDown;return; }
            if(e.CloseReason==CloseReason.UserClosing && !exitRequested && !smokeMode) { e.Cancel=true; Hide(); return; }
            StopFan();
            if (ownsSetting)
            {
                try { Restore(); }
                catch (Exception ex)
                {
                    // Keep the persistent recovery record; don't prevent Windows shutdown.
                    if (e.CloseReason != CloseReason.WindowsShutDown && e.CloseReason != CloseReason.TaskManagerClosing)
                    { MessageBox.Show(this, ex.Message + "\n복원 기록을 보존했습니다. 현재 전력값을 확인하세요.", "복원 실패"); }
                }
            }
        }
        protected override void WndProc(ref Message message)
        {
            if (message.Msg==0x218 && message.WParam.ToInt32()==4 && !setupRunning) StopFan();
            if (message.Msg == 0x218 && message.WParam.ToInt32() == 4 && ownsSetting)
            { try { Restore(); } catch (Exception ex) { status.Text = "절전 전 복원 실패: " + ex.Message; } }
            base.WndProc(ref message);
        }
        protected override void Dispose(bool disposing)
        {
            if (disposing && !hardwareDisposed)
            {
                hardwareDisposed = true; StopFan(); fanTimer.Dispose(); timer.Dispose(); tray.Dispose();
                detailsTip.Dispose();dismissTimer.Dispose();if(panelIcon!=null)panelIcon.Dispose();
                if (mmio != null) mmio.Dispose(); if(msr!=null) msr.Dispose();
            }
            base.Dispose(disposing);
        }
    }
    static class ControlProgram
    {
        [STAThread]
        static int Main(string[] args)
        {
            Application.EnableVisualStyles(); Application.SetCompatibleTextRenderingDefault(false);
            try
            {
                if(args.Length==2 && args[0]=="--display-save-current"){File.WriteAllText(args[1],new System.Web.Script.Serialization.JavaScriptSerializer().Serialize(DisplayRefresh.SaveCurrent()));return 0;}
                if(args.Length==2 && args[0]=="--display-check"){File.WriteAllText(args[1],new System.Web.Script.Serialization.JavaScriptSerializer().Serialize(DisplayRefresh.Inspect()));return 0;}
                if(args.Length==2 && args[0]=="--setup-self-test") {File.WriteAllText(args[1],new System.Web.Script.Serialization.JavaScriptSerializer().Serialize(FanSetupSelfTests.Run()));return 0;}
                if(args.Length==2 && args[0]=="--editor-self-test") {File.WriteAllText(args[1],new System.Web.Script.Serialization.JavaScriptSerializer().Serialize(EditorSelfTests.Run()));return 0;}
                if(args.Length==2 && (args[0]=="--layout-preview" || args[0]=="--zero-layout-preview" || args[0]=="--tray-layout-preview")) {
                    using(var preview=new ControlForm(true)) using(var capture=new System.Windows.Forms.Timer {Interval=600}) {
                        if(args[0]=="--zero-layout-preview")preview.PreviewZeroCurve();
                        if(args[0]=="--tray-layout-preview")preview.CollapseCurve();
                        capture.Tick+=delegate {capture.Stop();using(var bitmap=new Bitmap(preview.Width,preview.Height)){preview.DrawToBitmap(bitmap,new Rectangle(0,0,preview.Width,preview.Height));bitmap.Save(args[1]);}preview.SaveLayoutReport(args[1]+".layout.json");preview.Close();};
                        preview.Shown+=delegate {capture.Start();};Application.Run(preview);
                    }
                    return 0;
                }
                if(args.Length==2 && args[0]=="--curve-preview") {
                    using(var editor=new FanCurveEditor(FanCalibration.Load())) using(var capture=new System.Windows.Forms.Timer {Interval=600}) {
                        capture.Tick+=delegate {capture.Stop();using(var bitmap=new Bitmap(editor.Width,editor.Height)) {editor.DrawToBitmap(bitmap,new Rectangle(0,0,editor.Width,editor.Height));bitmap.Save(args[1]);}editor.Close();};
                        editor.Shown+=delegate {capture.Start();};Application.Run(editor);
                    }
                    return 0;
                }
                if (!(new WindowsPrincipal(WindowsIdentity.GetCurrent())).IsInRole(WindowsBuiltInRole.Administrator))
                {
                    if (args.Length != 0 && !(args.Length==1 && (args[0]=="--show-zero"||args[0]=="--startup"))) throw new UnauthorizedAccessException("Smoke mode requires an elevated caller.");
                    Process.Start(new ProcessStartInfo(Application.ExecutablePath,args.Length==1?args[0]:"") { UseShellExecute = true, Verb = "runas" }); return 0;
                }
                if(args.Length==2 && args[0]=="--startup-task-test"){if(StartupTask.Enabled())throw new InvalidOperationException("Existing startup preference preserved; test skipped.");try{StartupTask.Set(true);if(!StartupTask.Enabled())throw new IOException("Task enable failed.");StartupTask.Set(false);if(StartupTask.Enabled())throw new IOException("Task disable failed.");File.WriteAllText(args[1],"{\"Success\":true,\"EnabledThenDisabled\":true,\"HardwareSettingsChanged\":false}");}finally{if(StartupTask.Enabled())StartupTask.Set(false);}return 0;}
                bool owner;
                using (var gate = new Mutex(true, "Global\\GalaxyHelper-Rapl", out owner))
                {
                    if (!owner) throw new InvalidOperationException("전력 제어 또는 진단이 이미 실행 중입니다.");
                    try
                    {
                        using (var form = new ControlForm())
                        {
                            form.smokeMode=args.Length!=0 && !(args.Length==1 && (args[0]=="--show-zero"||args[0]=="--startup"));
                            if (args.Length == 2 && (args[0] == "--ui-smoke" || args[0] == "--ui-monitor-smoke"))
                            {
                                using (var timer = new System.Windows.Forms.Timer { Interval = args[0]=="--ui-monitor-smoke" ? 30000 : 2500 })
                                {
                                    timer.Tick += delegate { timer.Stop(); using (var bitmap = new Bitmap(form.Width, form.Height)) { form.DrawToBitmap(bitmap, new Rectangle(0, 0, form.Width, form.Height)); bitmap.Save(args[1]); } form.SaveLayoutReport(args[1]+".layout.json"); form.Close(); };
                                    form.Shown += delegate { timer.Start(); }; Application.Run(form);
                                }
                            }
                            else if (args.Length == 2 && args[0] == "--ui-control-smoke") { form.RunControlSmoke(args[1]); Application.Run(form); }
                            else if (args.Length == 2 && args[0] == "--ui-curve-smoke") { form.RunControlSmoke(args[1],true); Application.Run(form); }
                            else if (args.Length == 2 && args[0] == "--ui-zero-smoke") { form.RunControlSmoke(args[1],true,true); Application.Run(form); }
                            else if (args.Length==1 && args[0]=="--startup") {form.ResumeAtLogon();Application.Run(form);}
                            else if (args.Length == 0) Application.Run(form);
                            else if (args.Length == 1 && args[0]=="--show-zero") {form.PreviewZeroCurve();Application.Run(form);}
                            else throw new ArgumentException("Unknown arguments");
                        }
                    }
                    finally { gate.ReleaseMutex(); }
                }
                return 0;
            }
            catch (Exception ex)
            {
                if (args.Length == 2) File.WriteAllText(args[1] + ".error.txt", ex.ToString());
                else MessageBox.Show(ex.Message, "Galaxy Helper", MessageBoxButtons.OK, MessageBoxIcon.Error);
                return 1;
            }
        }
    }
}
