using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Windows.Forms;
namespace GalaxyHardware
{
    sealed class SoftButton : Button
    {
        bool hover;
        public SoftButton(){SetStyle(ControlStyles.UserPaint|ControlStyles.AllPaintingInWmPaint|ControlStyles.OptimizedDoubleBuffer,true);Cursor=Cursors.Hand;}
        protected override void OnMouseEnter(EventArgs e){hover=true;Invalidate();base.OnMouseEnter(e);}
        protected override void OnMouseLeave(EventArgs e){hover=false;Invalidate();base.OnMouseLeave(e);}
        protected override void OnPaint(PaintEventArgs e)
        {
            var g=e.Graphics;g.SmoothingMode=SmoothingMode.AntiAlias;g.Clear(Parent==null?Color.White:Parent.BackColor);
            bool primary=BackColor.B>200 && BackColor.R<100;
            Color fill=!Enabled?Color.FromArgb(245,246,248):primary?(hover?Color.FromArgb(28,108,225):Color.FromArgb(49,130,246)):(hover?Color.FromArgb(232,236,242):Color.FromArgb(242,244,247));
            using(var path=new GraphicsPath()) {
                float w=Width-1,h=Height-1,r=14;
                path.AddArc(0,0,r,r,180,90);path.AddArc(w-r,0,r,r,270,90);path.AddArc(w-r,h-r,r,r,0,90);path.AddArc(0,h-r,r,r,90,90);path.CloseFigure();
                using(var b=new SolidBrush(fill))g.FillPath(b,path);
            }
            var textRect=ClientRectangle;
            if(Image!=null && Text.Length==0)g.DrawImage(Image,(Width-Image.Width)/2,(Height-Image.Height)/2);
            TextRenderer.DrawText(g,Text,Font,textRect,!Enabled?Color.FromArgb(161,169,180):primary?Color.White:Color.FromArgb(65,76,91),TextFormatFlags.HorizontalCenter|TextFormatFlags.VerticalCenter|TextFormatFlags.EndEllipsis);
            if(Focused && ShowFocusCues)ControlPaint.DrawFocusRectangle(g,new Rectangle(4,4,Width-8,Height-8));
        }
    }
}
