using System;
using System.Drawing;
using System.IO;
using System.Runtime.InteropServices;
using System.Windows.Forms;

namespace GalaxyHardware
{
    sealed partial class ControlForm
    {
        CurveGraph inlineGraph;
        FanCalibration inlineProfile;
        string savedCurveSignature;
        Label curveSelection=new Label();
        NumericUpDown curveTemperature=new NumericUpDown(),curveRpm=new NumericUpDown(),curveCutoff=new NumericUpDown();
        bool fanReadInFlight;
        Action afterFanRead;
        readonly ToolTip detailsTip=new ToolTip {AutoPopDelay=15000};
        internal void PreviewZeroCurve() {if(inlineGraph!=null){inlineGraph.SetPreset("0 RPM");pl1.Value=5;pl2.Value=10;}}
        internal void SaveLayoutReport(string path)
        {
            var issues=new System.Collections.Generic.List<string>();
            Action<Control> inspect=null;
            inspect=delegate(Control parent) {
                var scroll=parent as ScrollableControl;
                if(scroll!=null && (scroll.VerticalScroll.Visible || scroll.HorizontalScroll.Visible))issues.Add(parent.GetType().Name+": scrollbar");
                foreach(Control child in parent.Controls) {
                    if(!child.Visible)continue;
                    if((parent is FlowLayoutPanel || parent==this) && !parent.ClientRectangle.Contains(child.Bounds))issues.Add(child.GetType().Name+" "+child.Text+": "+child.Bounds+" outside "+parent.ClientRectangle);
                    inspect(child);
                }
            };
            inspect(this);
            File.WriteAllText(path,new System.Web.Script.Serialization.JavaScriptSerializer().Serialize(new {Success=issues.Count==0,ClientWidth=ClientSize.Width,ClientHeight=ClientSize.Height,Issues=issues}));
        }
        bool statusTipsAttached;
        internal int LayoutHeight(FlowLayoutPanel stack)
        { int bottom=stack.Padding.Top;foreach(Control c in stack.Controls)if(c.Visible)bottom=Math.Max(bottom,c.Bottom+c.Margin.Bottom);return bottom+stack.Padding.Bottom; }
        void FitWithoutScroll(FlowLayoutPanel stack)
        {
            stack.AutoScroll=false;PerformLayout();stack.PerformLayout();
            int available=Math.Max(200,Screen.FromControl(this).WorkingArea.Height-(Height-ClientSize.Height)-24);
            int wanted=LayoutHeight(stack);
            if(inlineGraph!=null && curveDetails.Visible && wanted>available) {
                inlineGraph.Height=Math.Max(90,inlineGraph.Height-(wanted-available));
                curveDetails.PerformLayout();curveDetails.Parent.PerformLayout();stack.PerformLayout();
                wanted=LayoutHeight(stack);
            }
            ClientSize=new Size(ClientSize.Width,Math.Min(wanted,available));
            stack.PerformLayout();wanted=LayoutHeight(stack);
            // Very small work areas keep every action reachable instead of clipping.
            stack.AutoScroll=wanted>available;
            if(stack.AutoScroll)stack.AutoScrollMinSize=new Size(0,wanted);else stack.AutoScrollMinSize=Size.Empty;
            detailsTip.SetToolTip(status,status.Text);detailsTip.SetToolTip(fanControlStatus,fanControlStatus.Text);
            if(!statusTipsAttached) {
                statusTipsAttached=true;
                status.TextChanged+=delegate {detailsTip.SetToolTip(status,status.Text);};fanControlStatus.TextChanged+=delegate {detailsTip.SetToolTip(fanControlStatus,fanControlStatus.Text);};
            }
        }
        void ShowFixedTarget()
        {
            using(var dialog=new Form {Text="고정 RPM 목표",ClientSize=new Size(330,115),Font=Font,StartPosition=FormStartPosition.CenterParent,FormBorderStyle=FormBorderStyle.FixedDialog,MaximizeBox=false,MinimizeBox=false}) {
                var input=new NumericUpDown {Minimum=fanTarget.Minimum,Maximum=fanTarget.Maximum,Increment=100,Value=fanTarget.Value,Bounds=new Rectangle(20,20,135,32)};
                var button=new Button {Text="적용",Bounds=new Rectangle(175,18,130,34),DialogResult=DialogResult.OK};dialog.Controls.Add(input);dialog.Controls.Add(button);
                dialog.Controls.Add(new Label {Text="온도 커브 대신 고정 목표를 적용합니다.",Bounds=new Rectangle(20,65,290,28)});
                if(dialog.ShowDialog(this)==DialogResult.OK)QuickFan((int)input.Value,true);
            }
        }
        void BuildInlineCurve(FlowLayoutPanel parent)
        {
            try {
                inlineProfile=previewCalibration??FanCalibration.Load();
                FanCurve curve;
                try {curve=previewCalibration==null?FanCurve.Load(inlineProfile):FanCurve.Default(inlineProfile);}
                catch(Exception ex) {curve=FanCurve.Default(inlineProfile);fanControlStatus.Text="이전 커브를 읽을 수 없어 기본 커브를 표시합니다: "+ex.Message;}
                inlineGraph=new CurveGraph(curve,inlineProfile){Width=488,Height=190,Margin=new Padding(0,0,0,6)};
                savedCurveSignature=curve.Signature;
                BuildPresetRow(parent);
                parent.Controls.Add(inlineGraph);
                var row=Row();row.Height=32;
                curveSelection.Width=43;curveSelection.Padding=new Padding(0,4,0,0);curveSelection.ForeColor=Color.FromArgb(85,104,127);row.Controls.Add(curveSelection);
                curveTemperature.Minimum=20;curveTemperature.Maximum=90;curveTemperature.Increment=1;Number(curveTemperature,61);curveTemperature.AccessibleName="선택점 온도";row.Controls.Add(curveTemperature);
                row.Controls.Add(new Label {Text="°C",Width=22,Padding=new Padding(0,4,0,0)});
                curveRpm.Minimum=0;curveRpm.Maximum=FanCurve.Round(inlineProfile.Entries[2].ConservativeRpm);curveRpm.Increment=50;Number(curveRpm,82);curveRpm.AccessibleName="선택점 RPM";row.Controls.Add(curveRpm);
                row.Controls.Add(new Label {Text="RPM",Width=36,Padding=new Padding(0,4,0,0)});
                row.Controls.Add(new Label {Text="정지 해제",Width=67,Padding=new Padding(0,4,0,0),ForeColor=Color.LightSlateGray});
                curveCutoff.Minimum=45;curveCutoff.Maximum=90;curveCutoff.Value=90;Number(curveCutoff,56);curveCutoff.AccessibleName="팬 정지 해제 온도";row.Controls.Add(curveCutoff);
                row.Controls.Add(new Label {Text="°C",Width=22,Padding=new Padding(0,4,0,0)});parent.Controls.Add(row);
                bool syncing=false;
                Action changed=delegate {
                    syncing=true;curveSelection.Text="점 "+(inlineGraph.Selected+1);curveTemperature.Value=curve.Temperatures[inlineGraph.Selected];curveRpm.Value=curve.Rpms[inlineGraph.Selected];curveCutoff.Value=curve.ZeroStopTemperature;curveCutoff.Enabled=curve.HasZero;syncing=false;
                    curveButton.Text=curve.HasZero?"커브 + 5/10W 적용":curve.Signature==savedCurveSignature?"커브 적용":"변경한 커브 적용";
                };
                inlineGraph.SelectionChanged+=changed;
                curveTemperature.ValueChanged+=delegate {if(!syncing)inlineGraph.SetPoint((int)curveTemperature.Value,curve.Rpms[inlineGraph.Selected]);};
                curveRpm.ValueChanged+=delegate {if(!syncing)inlineGraph.SetRpm((int)curveRpm.Value);};
                curveCutoff.ValueChanged+=delegate {if(!syncing)inlineGraph.SetZeroTemperature((int)curveCutoff.Value);};changed();
                detailsTip.SetToolTip(inlineGraph,"드래그: 온도·RPM / Shift: 전체 이동 / Ctrl+Z: 취소");
                Line(parent,new Label {Text="실선: 요청 · 점선: 기기 지원 속도 (0 또는 약 "+Math.Max(inlineProfile.Entries[0].Fan1Peak,inlineProfile.Entries[0].Fan2Peak)+" RPM 이상)",ForeColor=Color.LightSlateGray},20);
            } catch(Exception ex) {
                inlineGraph=null;Line(parent,new Label {Text="시작할 때 짧은 준비 상태 검사를 수행합니다.\n정확한 RPM 커브가 필요하면 ‘정밀 보정’을 실행하세요.\n"+ex.Message,ForeColor=Color.FromArgb(85,104,127)},85);
            }
        }
        void ApplyInlineCurve()
        {
            if(inlineGraph==null) return;
            try {
                if(inlineGraph.Curve.HasZero && !FanControlClient.IsZeroHoldReady())throw new IOException("0 RPM 유지 드라이버 적용 대기입니다. Windows 재시작 후 적용해 주세요.");
                inlineGraph.Curve.Save(inlineProfile);savedCurveSignature=inlineGraph.Curve.Signature;StartCurve();
            }
            catch(Exception ex){NotifyError(ex.Message);}
        }
        string DescribeFanStep(int step)
        {
            if(inlineProfile==null || step<1 || step>3)return "고정 속도 "+step;
            var item=inlineProfile.Entries[step-1];int rpm=(int)(Math.Round(Math.Max(item.Fan1Peak,item.Fan2Peak)/100.0)*100);
            return "약 "+rpm+" RPM 유지";
        }
        [DllImport("dwmapi.dll")] static extern int DwmSetWindowAttribute(IntPtr handle,int attribute,ref int value,int size);
        protected override void OnHandleCreated(EventArgs e)
        {
            base.OnHandleCreated(e);
            try {int dark=1;DwmSetWindowAttribute(Handle,20,ref dark,sizeof(int));}catch(DllNotFoundException){}catch(EntryPointNotFoundException){}
        }
    }
}
