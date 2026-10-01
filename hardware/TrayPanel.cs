using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Runtime.InteropServices;
using System.Windows.Forms;

namespace GalaxyHardware
{
    // Small, independently drawn glyphs: no font or external icon dependency.
    static class PanelIcons
    {
        [DllImport("user32.dll")] static extern bool DestroyIcon(IntPtr icon);
        internal static Bitmap Draw(string kind, Color color, int size=20)
        {
            var image=new Bitmap(size,size);
            using(var g=Graphics.FromImage(image)) using(var p=new Pen(color,1.6f)) {
                g.SmoothingMode=SmoothingMode.AntiAlias;g.ScaleTransform(size/24f,size/24f);
                p.StartCap=p.EndCap=LineCap.Round;p.LineJoin=LineJoin.Round;
                if(kind=="power")g.DrawPolygon(p,new[]{new Point(13,3),new Point(5,13),new Point(11,13),new Point(10,21),new Point(19,10),new Point(13,10)});
                else if(kind=="fan") {g.DrawEllipse(p,10,10,4,4);for(int i=0;i<3;i++){var s=g.Save();g.TranslateTransform(12,12);g.RotateTransform(i*120);g.DrawBezier(p,0,-3,-10,-15,10,-13,4,-3);g.Restore(s);}}
                else if(kind=="save") {g.DrawRectangle(p,4,4,16,16);g.DrawRectangle(p,8,4,8,6);g.DrawRectangle(p,8,14,8,6);}
                else if(kind=="close") {g.DrawLine(p,6,6,18,18);g.DrawLine(p,18,6,6,18);}
                else if(kind=="pin") {g.DrawPolygon(p,new[]{new Point(8,4),new Point(16,4),new Point(15,10),new Point(19,14),new Point(5,14),new Point(9,10)});g.DrawLine(p,12,14,12,21);}
                else if(kind=="apply") {g.DrawLines(p,new[]{new Point(5,12),new Point(10,17),new Point(19,6)});}
                else if(kind=="delete") {g.DrawLine(p,5,6,19,6);g.DrawLine(p,9,3,15,3);g.DrawRectangle(p,7,7,10,13);g.DrawLine(p,10,10,10,17);g.DrawLine(p,14,10,14,17);}
                else {g.DrawArc(p,4,4,16,16,210,290);g.DrawLines(p,new[]{new Point(3,4),new Point(4,10),new Point(10,9)});}
            }
            return image;
        }
        internal static Icon Tray()
        {
            using(var bitmap=Draw("fan",Color.FromArgb(49,130,246),32)) {
                IntPtr handle=bitmap.GetHicon();try {using(var borrowed=Icon.FromHandle(handle))return (Icon)borrowed.Clone();}finally {DestroyIcon(handle);}
            }
        }
    }
    sealed partial class ControlForm
    {
        FlowLayoutPanel curveDetails;
        Icon panelIcon;
        bool panelPinned;
        readonly System.Windows.Forms.Timer dismissTimer=new System.Windows.Forms.Timer {Interval=180};
        DateTime lastDismiss=DateTime.MinValue;
        internal static Point PanelPosition(Rectangle work,Size size)
        {return new Point(Math.Max(work.Left,work.Right-size.Width-12),Math.Max(work.Top,work.Bottom-size.Height-12));}
        internal void CollapseCurve() {curveDetails.Visible=false;}
        void PositionPanel() {Location=PanelPosition(Screen.FromPoint(Cursor.Position).WorkingArea,Size);}
        void SetupTrayPanel(Panel header)
        {
            FormBorderStyle=FormBorderStyle.None;ShowInTaskbar=false;StartPosition=FormStartPosition.Manual;KeyPreview=true;
            panelIcon=PanelIcons.Tray();Icon=panelIcon;tray.Icon=panelIcon;
            var pin=QuickButton("",30,delegate {});pin.Location=new Point(350,1);
            pin.AccessibleName="패널 고정";pin.Image=PanelIcons.Draw("pin",ForeColor);pin.BackColor=surface;
            pin.Click+=delegate {panelPinned=!panelPinned;TopMost=panelPinned;pin.BackColor=panelPinned?Color.FromArgb(210,228,255):surface;detailsTip.SetToolTip(pin,panelPinned?"고정 해제":"패널 고정");};
            var close=QuickButton("",30,delegate {Hide();});close.Location=new Point(388,1);close.Image=PanelIcons.Draw("close",ForeColor);close.BackColor=surface;close.AccessibleName="트레이로 접기";
            detailsTip.SetToolTip(pin,"패널 고정");detailsTip.SetToolTip(close,"트레이로 접기 · 제어는 계속됩니다");header.Controls.Add(pin);header.Controls.Add(close);
            Shown+=delegate {PositionPanel();};
            Deactivate+=delegate {if(!smokeMode)dismissTimer.Start();};
            dismissTimer.Tick+=delegate {
                dismissTimer.Stop();
                if(panelPinned || smokeMode || !Visible || ContainsFocus || !Enabled || OwnedForms.Length>0 || quickMenu.Visible || (presetPicker!=null && presetPicker.DroppedDown) || powerPicker.DroppedDown || displayPicker.DroppedDown || refreshPicker.DroppedDown || resolutionPicker.DroppedDown)return;
                lastDismiss=DateTime.UtcNow;Hide();
            };
            KeyDown+=delegate(object sender,KeyEventArgs e){if(e.KeyCode==Keys.Escape){Hide();e.Handled=true;}};
            Paint+=delegate(object sender,PaintEventArgs e){using(var p=new Pen(Color.FromArgb(221,226,233)))e.Graphics.DrawRectangle(p,0,0,ClientSize.Width-1,ClientSize.Height-1);};
        }
        void DecorateButtons(Control parent)
        {
            foreach(Control c in parent.Controls) {
                var b=c as Button;
                if(b!=null && b.Image==null) {
                    string kind=b.Text.Contains("저장")?"save":b.Text.Contains("삭제")?"delete":b.Text.Contains("조회")||b.Text.Contains("복원")||b.Text.Contains("복귀")?"refresh":b.Text.Contains("적용")?"apply":b.Text.Contains("W")?"power":null;
                    if(kind!=null) {b.Image=PanelIcons.Draw(kind,b.ForeColor,16);b.TextImageRelation=TextImageRelation.ImageBeforeText;b.ImageAlign=ContentAlignment.MiddleLeft;}
                    b.FlatStyle=FlatStyle.Flat;b.FlatAppearance.BorderSize=0;b.FlatAppearance.MouseOverBackColor=Color.FromArgb(207,224,249);
                }
                DecorateButtons(c);
            }
        }
    }
}
