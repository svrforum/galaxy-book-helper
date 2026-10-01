using System.Drawing;
using System.Drawing.Drawing2D;
using System.Windows.Forms;
namespace GalaxyHardware
{
    sealed class RoundedCard : FlowLayoutPanel
    {
        protected override void OnSizeChanged(System.EventArgs e)
        {
            base.OnSizeChanged(e);if(Width<24 || Height<24)return;
            using(var path=new GraphicsPath()) {
                path.AddArc(0,0,24,24,180,90);path.AddArc(Width-24,0,24,24,270,90);
                path.AddArc(Width-24,Height-24,24,24,0,90);path.AddArc(0,Height-24,24,24,90,90);path.CloseFigure();
                var old=Region;Region=new Region(path);if(old!=null)old.Dispose();
            }
        }
    }
}
