using System.Drawing;
using System.Windows.Forms;
namespace GalaxyHardware
{
    sealed class SingleLineStatus : Label
    {
        protected override void OnPaint(PaintEventArgs e)
        {TextRenderer.DrawText(e.Graphics,Text.Replace('\r',' ').Replace('\n',' '),Font,ClientRectangle,ForeColor,TextFormatFlags.SingleLine|TextFormatFlags.EndEllipsis|TextFormatFlags.VerticalCenter|TextFormatFlags.NoPrefix);}
    }
}
