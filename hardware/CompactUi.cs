using System;
using System.Drawing;
using System.IO;
using System.Windows.Forms;

namespace GalaxyHardware
{
    sealed partial class ControlForm
    {
        internal bool smokeMode;
        bool exitRequested;
        DateTime lastFanPoll=DateTime.UtcNow;
        Action afterFanRestore;
        readonly Button curveButton=new SoftButton();
        readonly ContextMenuStrip quickMenu=new ContextMenuStrip();
        readonly Color surface=Color.White;
        readonly Color accent=Color.FromArgb(49,130,246);
        FlowLayoutPanel Card(FlowLayoutPanel parent,string title)
        {
            var card=new RoundedCard {Width=424,AutoSize=true,AutoSizeMode=AutoSizeMode.GrowAndShrink,FlowDirection=FlowDirection.TopDown,WrapContents=false,Padding=new Padding(8),BackColor=surface,Margin=new Padding(0,0,0,6)};
            parent.Controls.Add(card);
            var heading=new Panel();heading.Controls.Add(new PictureBox {Image=PanelIcons.Draw(title=="전력"?"power":"fan",accent,18),Bounds=new Rectangle(0,1,18,18)});heading.Controls.Add(new Label {Text=title,ForeColor=Color.FromArgb(51,61,75),Font=new Font(Font,FontStyle.Bold),Bounds=new Rectangle(26,1,200,20)});Line(card,heading,24);
            return card;
        }
        static void Line(FlowLayoutPanel parent,Control control,int height)
        { control.Size=new Size(408,height); control.Margin=new Padding(0,0,0,3); parent.Controls.Add(control); }
        FlowLayoutPanel Row() { return new FlowLayoutPanel {Width=408,Height=36,Margin=new Padding(0,0,0,3),WrapContents=false}; }
        Button QuickButton(string text,int width,Action action)
        { var b=new SoftButton(); Style(b,text,width); b.FlatAppearance.BorderSize=0; b.Margin=new Padding(0,0,6,0); b.Click+=delegate { action(); }; return b; }
        void Number(NumericUpDown n,int width)
        { n.Width=width; n.ForeColor=ForeColor; n.BackColor=Color.FromArgb(242,244,247); n.BorderStyle=BorderStyle.FixedSingle; n.Margin=new Padding(0,4,8,0); }
        void BuildCompactUi()
        {
            Text="Galaxy Helper";Font=new Font("맑은 고딕",9.5f);BackColor=Color.FromArgb(242,244,247);ForeColor=Color.FromArgb(35,43,55);
            AutoScaleMode=AutoScaleMode.Dpi;ClientSize=new Size(440,640);MinimumSize=new Size(440,0);FormBorderStyle=FormBorderStyle.None;MaximizeBox=false;StartPosition=FormStartPosition.CenterScreen;
            var stack=new FlowLayoutPanel {Dock=DockStyle.Fill,Padding=new Padding(8),FlowDirection=FlowDirection.TopDown,WrapContents=false,AutoScroll=false};Controls.Add(stack);
            var header=new Panel {Width=424,Height=32,Margin=new Padding(0,0,0,6)};
            header.Controls.Add(new Label {Text="Galaxy Helper",Font=new Font("Segoe UI",13,FontStyle.Bold),Bounds=new Rectangle(0,0,150,32)});
            measured.Font=new Font("Segoe UI",11);measured.ForeColor=accent;measured.TextAlign=ContentAlignment.MiddleRight;measured.Bounds=new Rectangle(146,0,200,32);header.Controls.Add(measured);stack.Controls.Add(header);
            Shown+=delegate {FitWithoutScroll(stack);};
            BuildRefreshRow(stack);BuildBrightnessRow(stack);
            var power=Card(stack,"전력");
            BuildPowerPresets(power);
            Configure(pl1,15);Configure(pl2,20);Number(pl1,78);Number(pl2,78);
            var watts=new Panel {Width=408,Height=84,Margin=new Padding(0,4,0,4)};
            watts.Controls.Add(new WattSlider("지속 전력",pl1){Location=new Point(0,0),Width=194,Font=Font});watts.Controls.Add(new WattSlider("단기 전력",pl2){Location=new Point(212,0),Width=194,Font=Font});
            Style(apply,"전력 적용",194);apply.BackColor=accent;apply.ForeColor=Color.White;apply.Location=new Point(0,51);watts.Controls.Add(apply);Style(restore,"원래대로",194);restore.Location=new Point(212,51);watts.Controls.Add(restore);power.Controls.Add(watts);
            status.ForeColor=Color.FromArgb(107,118,132);status.AutoEllipsis=true;Line(power,status,20);
            var fan=Card(stack,"팬 속도");fanReading.Font=new Font("Segoe UI",13,FontStyle.Bold);Line(fan,fanReading,28);
            curveDetails=new FlowLayoutPanel {Width=408,AutoSize=true,AutoSizeMode=AutoSizeMode.GrowAndShrink,FlowDirection=FlowDirection.TopDown,WrapContents=false,Margin=Padding.Empty};fan.Controls.Add(curveDetails);BuildInlineCurve(curveDetails);curveDetails.Visible=true;
            var controls=Row();Style(curveButton,inlineGraph!=null && inlineGraph.Curve.IsZeroHold?"0 RPM + 5/10W 적용":"커브 적용",144);curveButton.BackColor=accent;curveButton.ForeColor=Color.White;curveButton.Click+=delegate {ApplyInlineCurve();};controls.Controls.Add(curveButton);
            Style(fanAuto,"자동",74);controls.Controls.Add(fanAuto);Style(fanRefresh,"조회",68);controls.Controls.Add(fanRefresh);Style(fanSetup,"보정",98);controls.Controls.Add(fanSetup);fan.Controls.Add(controls);
            fanControlStatus.ForeColor=Color.FromArgb(107,118,132);fanControlStatus.AutoEllipsis=true;Line(fan,fanControlStatus,24);
            fanSetup.Click+=delegate {OpenFanSetup();};fanSetup.AccessibleName="팬 정밀 보정 및 검증";
            detailsTip.SetToolTip(fanSetup,"팬 단계 측정 · RPM 목표 유지 · 자동 복귀 검증 · 약 7~9분");
            Shown+=delegate {if(!smokeMode)BeginInvoke(new Action(TryAutomaticFanSetup));};
            fanSteps.Minimum=1;fanSteps.Maximum=3;fanSteps.Value=2;
            fanTarget.Minimum=1000;fanTarget.Maximum=6500;fanTarget.Increment=100;fanTarget.Value=3400;Number(fanTarget,100);Style(fanCap,"고정 목표 적용",170);
            detailsTip.SetToolTip(curveButton,"일반 커브 80°C 보호 · 0 RPM은 지정 온도에서 자동 냉각");
            apply.Click+=delegate {Apply();};restore.Click+=delegate {try {Restore();}catch(Exception ex){NotifyError(ex.Message);}};
            fanApply.Click+=delegate {StartFan(false);};fanCap.Click+=delegate {StartFan(true);};fanAuto.Click+=delegate {RestoreFan();};fanRefresh.Click+=delegate {RefreshFan();};
            fanTimer.Interval=1000;fanTimer.Tick+=delegate {TickFan();};
            SetupTrayPanel(header);DecorateButtons(stack);tray.Text="Galaxy Helper";tray.ContextMenuStrip=quickMenu;tray.Visible=true;
            quickMenu.Opening+=delegate {PopulateQuickMenu();};tray.MouseClick+=delegate(object sender,MouseEventArgs e){if(e.Button==MouseButtons.Left){if(Visible)Hide();else if((DateTime.UtcNow-lastDismiss).TotalMilliseconds>250)ShowPanel();}};
            Resize+=delegate {if(WindowState==FormWindowState.Minimized)Hide();};FormClosing+=ClosingForm;
        }
        void ShowPanel() { dismissTimer.Stop(); PositionPanel(); Show(); WindowState=FormWindowState.Normal; Activate(); }
        void EditCurve() {ShowPanel(); if(inlineGraph!=null) {curveDetails.Visible=true;FitWithoutScroll((FlowLayoutPanel)Controls[0]);PositionPanel();inlineGraph.Focus();((ScrollableControl)inlineGraph.Parent).ScrollControlIntoView(inlineGraph);} }
        void StartCurve()
        {
            if(fanReadInFlight) {afterFanRead=delegate {StartCurve();};fanControlStatus.Text="조회 완료 후 커브를 적용합니다…";return;}
            if(fanBusy || !fanReady) {NotifyError("팬 조회 또는 설정 중입니다. 잠시 후 커브 적용을 다시 선택하세요.");return;}
            if(fanClient!=null) {afterFanRestore=delegate {StartFan(false,true);};RestoreFan();} else StartFan(false,true);
        }
        void NotifyError(string text) { status.Text=text; tray.ShowBalloonTip(4000,"Galaxy Helper",text,ToolTipIcon.Warning); }
        void QuickPower(int sustained,int burst)
        {
            if(busy || !readingHealthy) { NotifyError("전력 센서 확인 후 다시 적용하세요."); return; }
            try {
                if(ownsSetting) Restore();
                if(File.Exists(Program.Journal)) throw new IOException("먼저 기존 전력 복원 기록을 복원하세요.");
                ulong raw=msr.ReadMsr(0x610); Rapl.EncodeReduction(raw,units,sustained,burst);
                pl1.Value=sustained; pl2.Value=burst; Apply();
            } catch(Exception ex) { NotifyError(ex.Message); }
        }
        void QuickFan(int value,bool cap)
        {
            if(fanBusy || !fanReady) { NotifyError("팬 조회 또는 설정 중입니다. 잠시 후 다시 선택하세요."); return; }
            Action start=delegate { if(cap) fanTarget.Value=value; else fanSteps.Value=value; StartFan(cap); };
            if(fanClient!=null) { afterFanRestore=start; RestoreFan(); } else start();
        }
        void PopulateQuickMenu()
        {
            quickMenu.Items.Clear();
            quickMenu.Items.Add("Galaxy Helper  ·  "+measured.Text).Enabled=false;
            quickMenu.Items.Add("패널 열기",null,delegate { ShowPanel(); });
            var power=new ToolStripMenuItem("전력 제한"); quickMenu.Items.Add(power);
            foreach(var pair in new[] {new[]{10,10},new[]{15,20},new[]{25,35}}) {
                int a=pair[0],b=pair[1]; var item=new ToolStripMenuItem(a+" / "+b+" W",null,delegate { QuickPower(a,b); });
                item.Enabled=readingHealthy && !busy; item.Checked=ownsSetting && Math.Abs(Rapl.Pl1(expected,units)-a)<0.1 && Math.Abs(Rapl.Pl2(expected,units)-b)<0.1; power.DropDownItems.Add(item);
            }
            power.DropDownItems.Add("원래 전력 복원",null,delegate { try { Restore(); } catch(Exception ex) { NotifyError(ex.Message); } }).Enabled=File.Exists(Program.Journal)&&!busy;
            var fan=new ToolStripMenuItem("팬 제어"); quickMenu.Items.Add(fan);
            var auto=new ToolStripMenuItem("자동",null,delegate { RestoreFan(); }); auto.Checked=fanClient==null; auto.Enabled=fanClient!=null&&!fanBusy; fan.DropDownItems.Add(auto);
            for(int i=1;i<=3;i++) { int step=i; var item=new ToolStripMenuItem(DescribeFanStep(step),null,delegate { QuickFan(step,false); }); item.Enabled=fanReady&&!fanBusy; item.Checked=fanClient!=null&&!fanGoal.HasValue&&fanCurvePolicy==null&&fanStep==step; fan.DropDownItems.Add(item); }
            var rpm=new ToolStripMenuItem("RPM 목표"); quickMenu.Items.Add(rpm);
            rpm.DropDownItems.Add("0 RPM · 5/10W · 90°C 해제",null,delegate {if(inlineGraph!=null){inlineGraph.SetPreset("0 RPM");ApplyInlineCurve();}}).Enabled=inlineGraph!=null && fanReady && (!fanBusy || fanReadInFlight);
            foreach(int target in new[]{3200,3400,3600,4000}) { int value=target; var item=new ToolStripMenuItem(value+" RPM",null,delegate { QuickFan(value,true); }); item.Enabled=fanReady&&!fanBusy&&value>=fanTarget.Minimum; item.Checked=fanClient!=null&&fanGoal==value; rpm.DropDownItems.Add(item); }
            rpm.DropDownItems.Add("직접 입력…",null,delegate {ShowFixedTarget();});
            var curveItem=new ToolStripMenuItem("저장된 온도 커브 적용",null,delegate {StartCurve();}); curveItem.Enabled=fanReady&&!fanBusy;curveItem.Checked=fanClient!=null&&fanCurvePolicy!=null;quickMenu.Items.Add(curveItem);
            quickMenu.Items.Add("온도 커브 편집",null,delegate {ShowPanel();EditCurve();}).Enabled=!fanBusy;
            quickMenu.Items.Add("정밀 팬 보정 및 검증…",null,delegate {OpenFanSetup();}).Enabled=fanReady&&!fanBusy&&!busy;
            quickMenu.Items.Add(new ToolStripSeparator());
            AddStartupMenu();
            quickMenu.Items.Add("종료 및 설정 복원",null,delegate { exitRequested=true; Close(); });
        }
        void UpdateQuickState()
        { string text="Galaxy Helper · "+measured.Text+" · "+(fanClient==null?"팬 자동":"팬 수동"); tray.Text=text.Length>63?text.Substring(0,63):text; }
    }
}
