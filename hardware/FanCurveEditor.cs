using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Windows.Forms;

namespace GalaxyHardware
{
    sealed class CurveGraph : Control
    {
        internal readonly FanCurve Curve;
        readonly FanCalibration profile;
        internal int Selected;
        internal int? CurrentTemperature;
        internal event Action SelectionChanged;
        readonly int minimum,maximum;
        bool dragging;
        internal CurveGraph(FanCurve curve,FanCalibration calibration)
        {
            Curve=curve; profile=calibration; minimum=FanCurve.Round(profile.Entries[0].ConservativeRpm); maximum=FanCurve.Round(profile.Entries[2].ConservativeRpm);
            DoubleBuffered=true; TabStop=true; BackColor=Color.FromArgb(22,28,38); ForeColor=Color.White; Cursor=Cursors.Hand;
            AccessibleName="온도별 팬 커브. 좌우 방향키로 지점 선택, 위아래로 RPM 변경";
        }
        RectangleF Plot {get {return new RectangleF(58,24,Width-85,Height-65);}}
        float X(int t) {return Plot.Left+(t-30)*Plot.Width/(Curve.IsZeroHold?60:50);}
        float Y(int rpm) {return Plot.Bottom-(rpm-(minimum-300))*Plot.Height/(maximum-minimum+500);}
        internal void SetRpm(int rpm)
        {
            Curve.EditPoint(Selected,rpm,profile);
            Invalidate(); if(SelectionChanged!=null) SelectionChanged();
        }
        internal void SetPreset(string name)
        {
            var value=FanCurve.Preset(profile,name);Curve.Version=value.Version;Curve.ZeroStopTemperature=value.ZeroStopTemperature;Array.Copy(value.Rpms,Curve.Rpms,6);Invalidate();if(SelectionChanged!=null)SelectionChanged();
        }
        internal void SetZeroTemperature(int value) {Curve.ZeroStopTemperature=Math.Max(45,Math.Min(90,value));Invalidate();if(SelectionChanged!=null)SelectionChanged();}
        protected override void OnPaint(PaintEventArgs e)
        {
            base.OnPaint(e); var g=e.Graphics; g.SmoothingMode=SmoothingMode.AntiAlias;
            if(Curve.IsZeroHold) {
                using(var grid=new Pen(Color.FromArgb(51,61,76))) using(var gray=new SolidBrush(Color.LightSlateGray)) using(var green=new Pen(Color.FromArgb(109,218,193),3)) using(var red=new Pen(Color.IndianRed,2)) {
                    for(int t=30;t<=90;t+=10) {g.DrawLine(grid,X(t),Plot.Top,X(t),Plot.Bottom);g.DrawString(t+"°",Font,gray,X(t)-12,Plot.Bottom+9);}
                    g.DrawString("0 RPM",Font,gray,3,Plot.Bottom-16);
                    g.DrawLine(green,X(30),Plot.Bottom-7,X(Curve.ZeroStopTemperature),Plot.Bottom-7);
                    g.DrawLine(red,X(Curve.ZeroStopTemperature),Plot.Top,X(Curve.ZeroStopTemperature),Plot.Bottom);
                    g.FillEllipse(Brushes.White,X(Curve.ZeroStopTemperature)-6,Plot.Bottom-13,12,12);
                    g.DrawString(Curve.ZeroStopTemperature+"°C 도달 → 삼성 자동 냉각",Font,gray,Plot.Left+8,Plot.Top+8);
                    g.DrawString("정지 해제 온도: 흰 점을 좌우로 드래그",Font,gray,Plot.Left+8,Plot.Top+33);
                    if(CurrentTemperature.HasValue)g.DrawLine(grid,X(Math.Max(30,Math.Min(90,CurrentTemperature.Value))),Plot.Top,X(Math.Max(30,Math.Min(90,CurrentTemperature.Value))),Plot.Bottom);
                }
                return;
            }
            if(CurrentTemperature.HasValue && CurrentTemperature>=0 && CurrentTemperature<120) using(var current=new Pen(Color.FromArgb(100,137,189),1)) {
                current.DashStyle=DashStyle.Dot;float marker=X(Math.Max(30,Math.Min(80,CurrentTemperature.Value)));g.DrawLine(current,marker,Plot.Top,marker,Plot.Bottom);
            }
            using(var grid=new Pen(Color.FromArgb(51,61,76))) using(var gray=new SolidBrush(Color.LightSlateGray)) {
                for(int t=30;t<=80;t+=10) {g.DrawLine(grid,X(t),Plot.Top,X(t),Plot.Bottom);g.DrawString(t+"°",Font,gray,X(t)-12,Plot.Bottom+9);}
                for(int rpm=minimum-200;rpm<=maximum+100;rpm+=200) {g.DrawLine(grid,Plot.Left,Y(rpm),Plot.Right,Y(rpm));g.DrawString(rpm.ToString(),Font,gray,6,Y(rpm)-8);}
                g.DrawString("RPM",Font,gray,8,3);
            }
            using(var expected=new Pen(Color.FromArgb(129,148,183),2)) {
                expected.DashStyle=DashStyle.Dash; PointF last=new PointF();
                for(int t=30;t<=79;t++) {int step=profile.SelectStep(Curve.At(t)); var entry=profile.Entries[step-1];var pt=new PointF(X(t),Y(Math.Max(entry.Fan1Peak,entry.Fan2Peak)));if(t>30) g.DrawLine(expected,last,pt);last=pt;}
            }
            var points=new PointF[6];for(int i=0;i<6;i++)points[i]=new PointF(X(Curve.Temperatures[i]),Y(Curve.Rpms[i]));
            using(var line=new Pen(Color.FromArgb(109,218,193),3)) {g.DrawLines(line,points);}
            for(int i=0;i<6;i++) using(var brush=new SolidBrush(i==Selected?Color.White:Color.FromArgb(109,218,193))) {g.FillEllipse(brush,points[i].X-6,points[i].Y-6,12,12);}
            using(var red=new Pen(Color.IndianRed,2)) {g.DrawLine(red,X(80),Plot.Top,X(80),Plot.Bottom);}
        }
        protected override void OnMouseDown(MouseEventArgs e)
        {
            base.OnMouseDown(e); if(e.Button!=MouseButtons.Left)return; Focus();
            if(Curve.IsZeroHold) {if(Plot.Contains(e.Location)){dragging=true;Capture=true;SetZeroTemperature((int)Math.Round(30+(e.X-Plot.Left)*60/Plot.Width));}return;}
            for(int i=0;i<6;i++) if(Math.Abs(e.X-X(Curve.Temperatures[i]))<16 && Math.Abs(e.Y-Y(Curve.Rpms[i]))<16) {Selected=i;dragging=true;Capture=true;Invalidate();if(SelectionChanged!=null)SelectionChanged();break;}
        }
        protected override void OnMouseMove(MouseEventArgs e)
        {base.OnMouseMove(e);if(dragging){if(Curve.IsZeroHold)SetZeroTemperature((int)Math.Round(30+(e.X-Plot.Left)*60/Plot.Width));else SetRpm((int)(Math.Round((minimum-300+(Plot.Bottom-e.Y)*(maximum-minimum+500)/Plot.Height)/50.0)*50));}}
        protected override void OnMouseUp(MouseEventArgs e) {base.OnMouseUp(e);dragging=false;Capture=false;}
        protected override bool IsInputKey(Keys keyData) {return (keyData&Keys.KeyCode)==Keys.Left || (keyData&Keys.KeyCode)==Keys.Right || (keyData&Keys.KeyCode)==Keys.Up || (keyData&Keys.KeyCode)==Keys.Down || base.IsInputKey(keyData);}
        protected override void OnKeyDown(KeyEventArgs e)
        {base.OnKeyDown(e);if(Curve.IsZeroHold){if(e.KeyCode==Keys.Left || e.KeyCode==Keys.Down)SetZeroTemperature(Curve.ZeroStopTemperature-1);if(e.KeyCode==Keys.Right || e.KeyCode==Keys.Up)SetZeroTemperature(Curve.ZeroStopTemperature+1);return;}if(e.KeyCode==Keys.Left)Selected=Math.Max(0,Selected-1);if(e.KeyCode==Keys.Right)Selected=Math.Min(5,Selected+1);if(e.KeyCode==Keys.Up)SetRpm(Curve.Rpms[Selected]+50);if(e.KeyCode==Keys.Down)SetRpm(Curve.Rpms[Selected]-50);Invalidate();if(SelectionChanged!=null)SelectionChanged();}
    }
    sealed class FanCurveEditor : Form
    {
        internal bool ApplyRequested;
        internal readonly CurveGraph Graph;
        internal FanCurveEditor(FanCalibration profile)
        {
            Text="온도별 팬 커브"; Font=new Font("맑은 고딕",10); BackColor=Color.FromArgb(18,22,30);ForeColor=Color.White;
            ClientSize=new Size(650,530);FormBorderStyle=FormBorderStyle.FixedDialog;MaximizeBox=false;MinimizeBox=false;StartPosition=FormStartPosition.CenterParent;AutoScaleMode=AutoScaleMode.Dpi;
            var curve=FanCurve.Load(profile);
            var title=new Label {Text="온도에 맞춰 팬 속도 조절",Font=new Font(Font.FontFamily,17,FontStyle.Bold),Bounds=new Rectangle(22,18,600,35)}; Controls.Add(title);
            Controls.Add(new Label {Text="점을 위아래로 드래그하세요. 온도 사이 값은 직선으로 연결됩니다.",Bounds=new Rectangle(22,60,610,28),ForeColor=Color.LightSteelBlue});
            Graph=new CurveGraph(curve,profile){Bounds=new Rectangle(18,94,610,276)};Controls.Add(Graph);
            var selected=new Label {Bounds=new Rectangle(24,384,170,30)};Controls.Add(selected);
            var number=new NumericUpDown {Bounds=new Rectangle(200,382,100,30),Minimum=FanCurve.Round(profile.Entries[0].ConservativeRpm),Maximum=FanCurve.Round(profile.Entries[2].ConservativeRpm),Increment=50};Controls.Add(number);
            bool updating=false; Action refresh=delegate {updating=true;number.Minimum=0;number.Maximum=10000;number.Minimum=curve.IsZeroHold?45:FanCurve.Round(profile.Entries[0].ConservativeRpm);number.Maximum=curve.IsZeroHold?90:FanCurve.Round(profile.Entries[2].ConservativeRpm);number.Increment=curve.IsZeroHold?1:50;selected.Text=curve.IsZeroHold?"0 RPM 해제 온도 °C":curve.Temperatures[Graph.Selected]+"°C 목표 RPM";number.Value=curve.IsZeroHold?curve.ZeroStopTemperature:curve.Rpms[Graph.Selected];updating=false;};
            Graph.SelectionChanged+=refresh;number.ValueChanged+=delegate {if(!updating){if(curve.IsZeroHold)Graph.SetZeroTemperature((int)number.Value);else Graph.SetRpm((int)number.Value);}};refresh();
            Controls.Add(new Label {Text="초록: 설정 목표  ·  점선: 보정값 기반 예상 RPM\n상승은 즉시 반영 · 하강은 3°C / 10초 지연 · 80°C 자동 복귀",Bounds=new Rectangle(22,425,610,46),ForeColor=Color.LightSteelBlue});
            var save=new Button {Text="저장",Bounds=new Rectangle(288,480,100,32)};
            var apply=new Button {Text="저장하고 적용",Bounds=new Rectangle(396,480,132,32)};
            var cancel=new Button {Text="취소",Bounds=new Rectangle(536,480,90,32),DialogResult=DialogResult.Cancel};
            foreach(var button in new[]{save,apply,cancel}) {button.FlatStyle=FlatStyle.Flat;button.BackColor=Color.FromArgb(44,64,87);Controls.Add(button);}
            save.Click+=delegate {try {curve.Save(profile);DialogResult=DialogResult.OK;}catch(Exception ex){MessageBox.Show(this,ex.Message);}};
            apply.Click+=delegate {try {curve.Save(profile);ApplyRequested=true;DialogResult=DialogResult.OK;}catch(Exception ex){MessageBox.Show(this,ex.Message);}};
            CancelButton=cancel;
        }
    }
}
