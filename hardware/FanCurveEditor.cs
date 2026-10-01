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
        internal event Action EditCompleted;
        readonly int maximum;
        bool dragging;
        int[] undoTemperatures,undoRpms;
        int undoLimit;
        internal CurveGraph(FanCurve curve,FanCalibration calibration)
        {
            Curve=curve;profile=calibration;Curve.UpgradeForEditing(profile);
            maximum=FanCurve.Round(profile.Entries[2].ConservativeRpm);
            DoubleBuffered=true;TabStop=true;BackColor=Color.FromArgb(248,250,252);ForeColor=Color.FromArgb(35,43,55);Cursor=Cursors.Hand;
            AccessibleName="팬 커브: 점을 좌우로 온도, 위아래로 RPM 조정. Shift 드래그는 전체 속도 이동. Ctrl Z 실행 취소.";
        }
        internal RectangleF Plot {get{return new RectangleF(48,27,Width-68,Height-60);}}
        float X(int t){return Plot.Left+(t-20)*Plot.Width/70;}
        float Y(int rpm){return Plot.Bottom-rpm*Plot.Height/(maximum+200);}
        internal Point PointPosition(int index){return Point.Round(new PointF(X(Curve.Temperatures[index]),Y(Curve.Rpms[index])));}
        void Changed(){Invalidate();if(SelectionChanged!=null)SelectionChanged();}
        void Remember(){undoTemperatures=(int[])Curve.Temperatures.Clone();undoRpms=(int[])Curve.Rpms.Clone();undoLimit=Curve.ZeroStopTemperature;}
        internal void Undo(){if(undoRpms==null)return;var t=Curve.Temperatures;var r=Curve.Rpms;int limit=Curve.ZeroStopTemperature;Curve.Temperatures=undoTemperatures;Curve.Rpms=undoRpms;Curve.ZeroStopTemperature=undoLimit;undoTemperatures=t;undoRpms=r;undoLimit=limit;Changed();}
        internal void SetRpm(int rpm){SetPoint(Curve.Temperatures[Selected],rpm);}
        internal void SetPoint(int temperature,int rpm){Remember();Curve.EditKnot(Selected,temperature,rpm,profile);Changed();}
        internal void SetZeroTemperature(int value){Remember();Curve.ZeroStopTemperature=Math.Max(45,Math.Min(90,value));Changed();}
        internal void SetPreset(string name)
        {
            Remember();var value=FanCurve.Preset(profile,name);value.UpgradeForEditing(profile);
            Curve.Version=value.Version;Curve.ZeroStopTemperature=value.ZeroStopTemperature;
            Curve.Temperatures=value.Temperatures;Curve.Rpms=value.Rpms;Selected=Math.Min(Selected,Curve.Rpms.Length-1);Changed();
        }
        internal void SetCurve(FanCurve source)
        {Remember();var value=FanPresetStore.Copy(source);value.UpgradeForEditing(profile);Curve.Version=value.Version;Curve.ZeroStopTemperature=value.ZeroStopTemperature;Curve.Temperatures=value.Temperatures;Curve.Rpms=value.Rpms;Selected=Math.Min(Selected,Curve.Rpms.Length-1);Changed();}
        internal void ExerciseDrag(int index,int temperature,int rpm)
        {
            var from=PointPosition(index);var to=Point.Round(new PointF(X(temperature),Y(rpm)));
            OnMouseDown(new MouseEventArgs(MouseButtons.Left,1,from.X,from.Y,0));
            OnMouseMove(new MouseEventArgs(MouseButtons.Left,0,to.X,to.Y,0));
            OnMouseUp(new MouseEventArgs(MouseButtons.Left,1,to.X,to.Y,0));
        }
        internal void ExerciseKey(Keys key){OnKeyDown(new KeyEventArgs(key));}
        protected override void OnPaint(PaintEventArgs e)
        {
            base.OnPaint(e);var g=e.Graphics;g.SmoothingMode=SmoothingMode.AntiAlias;
            using(var grid=new Pen(Color.FromArgb(224,230,238)))using(var gray=new SolidBrush(Color.LightSlateGray)) {
                for(int t=20;t<=90;t+=10){g.DrawLine(grid,X(t),Plot.Top,X(t),Plot.Bottom);g.DrawString(t+"°",Font,gray,X(t)-11,Plot.Bottom+8);}
                for(int rpm=0;rpm<=maximum;rpm+=1000){g.DrawLine(grid,Plot.Left,Y(rpm),Plot.Right,Y(rpm));g.DrawString(rpm.ToString(),Font,gray,3,Y(rpm)-8);}
                g.DrawString("RPM",Font,gray,3,4);
                g.DrawString("점 "+(Selected+1)+"   "+Curve.Temperatures[Selected]+"°C / "+Curve.Rpms[Selected]+" RPM",Font,gray,Plot.Left+5,4);
            }
            if(CurrentTemperature.HasValue)using(var marker=new Pen(Color.FromArgb(110,170,225))){marker.DashStyle=DashStyle.Dot;float x=X(Math.Max(20,Math.Min(90,CurrentTemperature.Value)));g.DrawLine(marker,x,Plot.Top,x,Plot.Bottom);}
            using(var expected=new Pen(Color.FromArgb(151,166,190),2)) {
                expected.DashStyle=DashStyle.Dash;PointF? last=null;
                for(int t=20;t<=90;t++) {
                    try {int step=Curve.SelectStep(t,profile);int rpm=step==0?0:Math.Max(profile.Entries[step-1].Fan1Peak,profile.Entries[step-1].Fan2Peak);var p=new PointF(X(t),Y(rpm));if(last.HasValue)g.DrawLine(expected,last.Value,p);last=p;}
                    catch(System.IO.InvalidDataException){last=null;}
                }
            }
            var points=new PointF[Curve.Rpms.Length];for(int i=0;i<points.Length;i++)points[i]=new PointF(X(Curve.Temperatures[i]),Y(Curve.Rpms[i]));
            using(var line=new Pen(Color.FromArgb(49,130,246),2.5f))g.DrawLines(line,points);
            for(int i=0;i<points.Length;i++)using(var brush=new SolidBrush(i==Selected?Color.FromArgb(20,69,140):Color.FromArgb(49,130,246)))g.FillEllipse(brush,points[i].X-5,points[i].Y-5,10,10);
            using(var red=new Pen(Color.IndianRed,1)){red.DashStyle=DashStyle.Dot;float x=X(Curve.HasZero?Curve.ZeroStopTemperature:80);g.DrawLine(red,x,Plot.Top,x,Plot.Bottom);}
        }
        protected override void OnMouseDown(MouseEventArgs e)
        {
            base.OnMouseDown(e);if(e.Button!=MouseButtons.Left)return;Focus();
            int nearest=-1;double distance=225;
            for(int i=0;i<Curve.Rpms.Length;i++){var p=PointPosition(i);double d=(e.X-p.X)*(e.X-p.X)+(e.Y-p.Y)*(e.Y-p.Y);if(d<distance){distance=d;nearest=i;}}
            if(nearest<0)return;Selected=nearest;Remember();dragging=true;Capture=true;Changed();
        }
        protected override void OnMouseMove(MouseEventArgs e)
        {
            base.OnMouseMove(e);if(!dragging)return;
            int temperature=(int)Math.Round(20+(e.X-Plot.Left)*70/Plot.Width);
            int rpm=(int)(Math.Round(((Plot.Bottom-e.Y)*(maximum+200)/Plot.Height)/50.0)*50);
            if((ModifierKeys&Keys.Shift)!=0)Curve.ShiftRpm(rpm-Curve.Rpms[Selected],profile);else Curve.EditKnot(Selected,temperature,rpm,profile);
            Changed();
        }
        protected override void OnMouseUp(MouseEventArgs e){base.OnMouseUp(e);bool edited=dragging;dragging=false;Capture=false;if(edited && EditCompleted!=null)EditCompleted();}
        protected override void OnMouseCaptureChanged(EventArgs e){base.OnMouseCaptureChanged(e);if(!Capture)dragging=false;}
        protected override bool IsInputKey(Keys keys){Keys k=keys&Keys.KeyCode;return k==Keys.Left || k==Keys.Right || k==Keys.Up || k==Keys.Down || base.IsInputKey(keys);}
        protected override void OnKeyDown(KeyEventArgs e)
        {
            base.OnKeyDown(e);
            if(e.Control && e.KeyCode==Keys.Z){Undo();e.Handled=true;return;}
            if(e.Control && (e.KeyCode==Keys.Left || e.KeyCode==Keys.Right)){Selected=Math.Max(0,Math.Min(Curve.Rpms.Length-1,Selected+(e.KeyCode==Keys.Left?-1:1)));Changed();e.Handled=true;return;}
            int dt=e.KeyCode==Keys.Left?-1:e.KeyCode==Keys.Right?1:0;
            int dr=e.KeyCode==Keys.Up?50:e.KeyCode==Keys.Down?-50:0;
            if(dt!=0 || dr!=0){Remember();if(e.Shift && dr!=0)Curve.ShiftRpm(dr,profile);else Curve.EditKnot(Selected,Curve.Temperatures[Selected]+dt,Curve.Rpms[Selected]+dr,profile);Changed();e.Handled=true;}
        }
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
            Controls.Add(new Label {Text="점을 좌우·상하로 드래그해 온도와 RPM을 바꾸세요.",Bounds=new Rectangle(22,60,610,28),ForeColor=Color.LightSteelBlue});
            Graph=new CurveGraph(curve,profile){Bounds=new Rectangle(18,94,610,276)};Controls.Add(Graph);
            var selected=new Label {Bounds=new Rectangle(24,384,55,30)};Controls.Add(selected);
            var temperature=new NumericUpDown {Bounds=new Rectangle(82,382,75,30),Minimum=20,Maximum=90,Increment=1};Controls.Add(temperature);
            Controls.Add(new Label {Text="°C",Bounds=new Rectangle(162,384,35,30)});
            var number=new NumericUpDown {Bounds=new Rectangle(200,382,100,30),Minimum=0,Maximum=FanCurve.Round(profile.Entries[2].ConservativeRpm),Increment=50};Controls.Add(number);
            Controls.Add(new Label {Text="RPM",Bounds=new Rectangle(305,384,50,30)});
            bool updating=false;Action refresh=delegate {updating=true;selected.Text="점 "+(Graph.Selected+1);temperature.Value=curve.Temperatures[Graph.Selected];number.Value=curve.Rpms[Graph.Selected];updating=false;};
            Graph.SelectionChanged+=refresh;number.ValueChanged+=delegate {if(!updating)Graph.SetRpm((int)number.Value);};temperature.ValueChanged+=delegate {if(!updating)Graph.SetPoint((int)temperature.Value,curve.Rpms[Graph.Selected]);};refresh();
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
