using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Windows.Forms;
namespace GalaxyHardware
{
    sealed class WattSlider : Control
    {
        readonly NumericUpDown input;
        readonly string caption;
        public WattSlider(string title,NumericUpDown source)
        {
            caption=title;input=source;Height=48;Width=340;TabStop=true;Cursor=Cursors.Hand;AccessibleName=title+" 전력 제한";
            SetStyle(ControlStyles.UserPaint|ControlStyles.AllPaintingInWmPaint|ControlStyles.OptimizedDoubleBuffer|ControlStyles.Selectable,true);
            input.ValueChanged+=delegate {AccessibleDescription=input.Value.ToString("0.#")+" W";Invalidate();};
        }
        internal void ExerciseDrag(int x){OnMouseDown(new MouseEventArgs(MouseButtons.Left,1,x,35,0));OnMouseUp(new MouseEventArgs(MouseButtons.Left,1,x,35,0));}
        void MoveValue(int x) {double ratio=Math.Max(0,Math.Min(1,(x-9.0)/(Width-18)));input.Value=Math.Round((input.Minimum+(decimal)ratio*(input.Maximum-input.Minimum))*2)/2;}
        protected override void OnMouseDown(MouseEventArgs e){base.OnMouseDown(e);if(e.Button==MouseButtons.Left){Focus();Capture=true;MoveValue(e.X);}}
        protected override void OnMouseMove(MouseEventArgs e){base.OnMouseMove(e);if(Capture)MoveValue(e.X);}
        protected override void OnMouseUp(MouseEventArgs e){base.OnMouseUp(e);Capture=false;}
        protected override bool IsInputKey(Keys keyData){return keyData==Keys.Left||keyData==Keys.Right||keyData==Keys.Home||keyData==Keys.End||base.IsInputKey(keyData);}
        protected override void OnKeyDown(KeyEventArgs e){base.OnKeyDown(e);decimal delta=e.Shift?5:0.5m;if(e.KeyCode==Keys.Left)input.Value=Math.Max(input.Minimum,input.Value-delta);else if(e.KeyCode==Keys.Right)input.Value=Math.Min(input.Maximum,input.Value+delta);else if(e.KeyCode==Keys.Home)input.Value=input.Minimum;else if(e.KeyCode==Keys.End)input.Value=input.Maximum;else return;e.Handled=true;}
        protected override void OnPaint(PaintEventArgs e)
        {
            var g=e.Graphics;g.SmoothingMode=SmoothingMode.AntiAlias;
            TextRenderer.DrawText(g,caption,Font,new Rectangle(0,0,Width/2,23),Color.FromArgb(92,104,120),TextFormatFlags.Left|TextFormatFlags.VerticalCenter);
            using(var bold=new Font(Font,FontStyle.Bold))TextRenderer.DrawText(g,input.Value.ToString("0.#")+" W",bold,new Rectangle(Width/2,0,Width/2,23),Color.FromArgb(35,43,55),TextFormatFlags.Right|TextFormatFlags.VerticalCenter);
            float x=9+(Width-18)*(float)((input.Value-input.Minimum)/(input.Maximum-input.Minimum));
            using(var rail=new Pen(Color.FromArgb(231,235,241),5))using(var active=new Pen(Color.FromArgb(49,130,246),5)) {
                rail.StartCap=rail.EndCap=active.StartCap=active.EndCap=LineCap.Round;g.DrawLine(rail,9,35,Width-9,35);if(x>9)g.DrawLine(active,9,35,x,35);
            }
            using(var shadow=new SolidBrush(Color.FromArgb(220,229,243)))g.FillEllipse(shadow,x-9,27,18,18);
            using(var fill=new SolidBrush(Color.FromArgb(49,130,246)))g.FillEllipse(fill,x-7,28,14,14);
            if(Focused && ShowFocusCues)ControlPaint.DrawFocusRectangle(g,new Rectangle(0,24,Width,23));
        }
    }
}
