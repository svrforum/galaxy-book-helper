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
        readonly Label curveSelection=new Label();
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
        void FitWithoutScroll(FlowLayoutPanel stack)
        {
            PerformLayout();stack.PerformLayout();
            int available=Screen.FromControl(this).WorkingArea.Height-(Height-ClientSize.Height)-24;
            int wanted=stack.GetPreferredSize(new Size(ClientSize.Width,0)).Height;
            if(wanted>available && inlineGraph!=null) {inlineGraph.Height=Math.Max(110,inlineGraph.Height-(wanted-available));PerformLayout();stack.PerformLayout();wanted=stack.GetPreferredSize(new Size(ClientSize.Width,0)).Height;}
            ClientSize=new Size(ClientSize.Width,Math.Min(wanted,available));
            detailsTip.SetToolTip(status,status.Text);detailsTip.SetToolTip(fanControlStatus,fanControlStatus.Text);
            status.TextChanged+=delegate {detailsTip.SetToolTip(status,status.Text);};fanControlStatus.TextChanged+=delegate {detailsTip.SetToolTip(fanControlStatus,fanControlStatus.Text);};
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
                inlineProfile=FanCalibration.Load();
                var curve=FanCurve.Load(inlineProfile);
                savedCurveSignature=curve.Signature;
                inlineGraph=new CurveGraph(curve,inlineProfile){Width=488,Height=170,Margin=new Padding(0,0,0,6)};
                var presets=Row();
                foreach(string name in new[]{"0 RPM","극저소음","최적화","평균","냉각 우선"}) {
                    string preset=name;presets.Controls.Add(QuickButton(preset,91,delegate {inlineGraph.SetPreset(preset);fanControlStatus.Text=preset=="0 RPM"?"0 RPM 선택 · 적용 시 전력 5 / 10W도 함께 설정":"프리셋 선택 · 적용을 누르면 반영됩니다.";}));
                }
                parent.Controls.Add(presets);
                parent.Controls.Add(inlineGraph);
                var row=Row();row.Height=31;
                curveSelection.Width=185;curveSelection.Padding=new Padding(0,4,0,0);curveSelection.ForeColor=Color.LightSteelBlue;row.Controls.Add(curveSelection);
                var input=new NumericUpDown {Minimum=FanCurve.Round(inlineProfile.Entries[0].ConservativeRpm),Maximum=FanCurve.Round(inlineProfile.Entries[2].ConservativeRpm),Increment=50};Number(input,90);row.Controls.Add(input);
                var unit=new Label {Width=185,Padding=new Padding(0,4,0,0),ForeColor=Color.LightSlateGray};row.Controls.Add(unit);parent.Controls.Add(row);
                bool syncing=false;
                Action changed=delegate {syncing=true;input.Minimum=0;input.Maximum=10000;input.Minimum=curve.IsZeroHold?45:FanCurve.Round(inlineProfile.Entries[0].ConservativeRpm);input.Maximum=curve.IsZeroHold?90:FanCurve.Round(inlineProfile.Entries[2].ConservativeRpm);input.Increment=curve.IsZeroHold?1:50;input.Value=curve.IsZeroHold?curve.ZeroStopTemperature:curve.Rpms[inlineGraph.Selected];curveSelection.Text=curve.IsZeroHold?"팬 정지 해제 온도":curve.Temperatures[inlineGraph.Selected]+"°C에서 목표";unit.Text=curve.IsZeroHold?"°C · 도달하면 자동 냉각":"RPM · 이웃 점 함께 조정";syncing=false;curveButton.Text=curve.IsZeroHold?"0 RPM + 5/10W 적용":curve.Signature==savedCurveSignature?"커브 적용":"변경한 커브 적용";};
                inlineGraph.SelectionChanged+=changed;input.ValueChanged+=delegate {if(!syncing){if(curve.IsZeroHold)inlineGraph.SetZeroTemperature((int)input.Value);else inlineGraph.SetRpm((int)input.Value);}};changed();
                Line(parent,new Label {Text="0 RPM은 5/10W 연동 · 온도 도달 후 자동 정지 재진입 없음",ForeColor=Color.LightSlateGray},20);
            } catch(Exception ex) {
                inlineGraph=null;Line(parent,new Label {Text="커브를 사용하려면 RPM 보정이 필요합니다.\n"+ex.Message,ForeColor=Color.LightSteelBlue},70);
            }
        }
        void ApplyInlineCurve()
        {
            if(inlineGraph==null) return;
            try {
                if(inlineGraph.Curve.IsZeroHold && !FanControlClient.IsZeroHoldReady())throw new IOException("0 RPM 유지 드라이버 적용 대기입니다. Windows 재시작 후 적용해 주세요.");
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
