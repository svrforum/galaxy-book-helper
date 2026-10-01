using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using System.Runtime.InteropServices;
using System.Windows.Forms;
namespace GalaxyHardware
{
    sealed class Resolution
    {
        internal readonly uint Width,Height;
        internal Resolution(uint width,uint height){Width=width;Height=height;}
        public override string ToString(){return Width+" × "+Height;}
        public override bool Equals(object obj){var r=obj as Resolution;return r!=null&&Width==r.Width&&Height==r.Height;}
        public override int GetHashCode(){return (int)(Width*397^Height);}
    }
    static class DisplayRefresh
    {
        // Unicode DEVMODEW, retaining every byte returned by the display driver.
        [StructLayout(LayoutKind.Explicit,Size=220)]
        internal struct Mode {
            [FieldOffset(68)] public ushort Size;
            [FieldOffset(72)] public uint Fields;
            [FieldOffset(84)] public uint Orientation;
            [FieldOffset(88)] public uint FixedOutput;
            [FieldOffset(168)] public uint Bits;
            [FieldOffset(172)] public uint Width;
            [FieldOffset(176)] public uint Height;
            [FieldOffset(180)] public uint Flags;
            [FieldOffset(184)] public uint Hz;
        }
        [DllImport("user32.dll",CharSet=CharSet.Unicode)] static extern bool EnumDisplaySettings(string device,int index,ref Mode mode);
        [DllImport("user32.dll",CharSet=CharSet.Unicode)] static extern int ChangeDisplaySettingsEx(string device,ref Mode mode,IntPtr window,uint flags,IntPtr param);
        internal static Mode Current(string device){var m=new Mode{Size=220};if(!EnumDisplaySettings(device,-1,ref m))throw new InvalidOperationException("현재 화면 모드를 읽을 수 없습니다.");return m;}
        internal static Mode Saved(string device){var m=new Mode{Size=220};if(!EnumDisplaySettings(device,-2,ref m))throw new InvalidOperationException("저장된 화면 설정을 읽을 수 없습니다.");return m;}
        internal static void Persist(string device,Mode mode)
        {
            var previous=Saved(device);mode.Fields=0x580000;
            if(TestMode(device,mode)!=0)throw new InvalidOperationException("저장할 화면 설정을 Windows가 허용하지 않았습니다.");
            // Persist only after the live change was verified/accepted.
            int result=ChangeDisplaySettingsEx(device,ref mode,IntPtr.Zero,1,IntPtr.Zero);
            if(result==0){var saved=Saved(device);if(saved.Width==mode.Width&&saved.Height==mode.Height&&saved.Hz==mode.Hz)return;}
            previous.Fields=0x580000;int rollback=ChangeDisplaySettingsEx(device,ref previous,IntPtr.Zero,0x10000001,IntPtr.Zero);
            throw new InvalidOperationException("화면 설정 저장 실패 ("+result+")"+(rollback==0?" · 이전 저장값 복원":" · 이전 저장값 복원 실패"));
        }
        internal static object SaveCurrent(){return Screen.AllScreens.Select(s=>{var mode=Current(s.DeviceName);Persist(s.DeviceName,mode);var saved=Saved(s.DeviceName);return new {Device=s.DeviceName,Width=saved.Width,Height=saved.Height,Hz=saved.Hz,Saved=true};}).ToArray();}
        internal static bool Compatible(Mode a,Mode b){return a.Width==b.Width&&a.Height==b.Height&&a.Bits==b.Bits&&a.Orientation==b.Orientation&&a.Flags==b.Flags&&a.FixedOutput==b.FixedOutput&&b.Hz>1;}
        internal static uint[] Rates(string device){var current=Current(device);var rates=new SortedSet<uint>();for(int i=0;i<4096;i++){var m=new Mode{Size=220};if(!EnumDisplaySettings(device,i,ref m))break;if(Compatible(current,m))rates.Add(m.Hz);}return rates.ToArray();}
        internal static int Test(string device,uint hz){var m=Current(device);if(!Rates(device).Contains(hz))throw new InvalidOperationException("현재 해상도에서 지원하지 않는 주사율입니다.");m.Hz=hz;m.Fields=0x400000;return ChangeDisplaySettingsEx(device,ref m,IntPtr.Zero,2,IntPtr.Zero);}
        internal static bool SameRatio(Mode current,Mode candidate){return (ulong)current.Width*candidate.Height==(ulong)candidate.Width*current.Height;}
        static Mode[] ResolutionModes(string device){var current=Current(device);var result=new List<Mode>();for(int i=0;i<4096;i++){var m=new Mode{Size=220};if(!EnumDisplaySettings(device,i,ref m))break;if(m.Width>0&&m.Height>0&&SameRatio(current,m)&&m.Bits==current.Bits&&m.Orientation==current.Orientation&&m.Flags==current.Flags&&(m.Hz==60||m.Hz==120))result.Add(m);}return result.ToArray();}
        internal static Resolution[] Resolutions(string device){return ResolutionModes(device).Select(m=>new Resolution(m.Width,m.Height)).Distinct().OrderByDescending(r=>r.Width).ToArray();}
        internal static Mode ForResolution(string device,Resolution resolution){var current=Current(device);var matches=ResolutionModes(device).Where(m=>m.Width==resolution.Width&&m.Height==resolution.Height).OrderBy(m=>m.Hz==current.Hz?0:1).ThenBy(m=>m.Hz).ToArray();if(matches.Length==0)throw new InvalidOperationException("지원되지 않는 해상도입니다.");current.Width=matches[0].Width;current.Height=matches[0].Height;current.Hz=matches[0].Hz;current.Fields=0x580000;return current;}
        internal static int TestMode(string device,Mode mode){return ChangeDisplaySettingsEx(device,ref mode,IntPtr.Zero,2,IntPtr.Zero);}
        internal static void SetMode(string device,Mode mode){mode.Fields=0x580000;if(TestMode(device,mode)!=0)throw new InvalidOperationException("Windows가 화면 변경을 허용하지 않았습니다.");int result=ChangeDisplaySettingsEx(device,ref mode,IntPtr.Zero,0,IntPtr.Zero);if(result!=0)throw new InvalidOperationException("화면 변경 실패 ("+result+")");var current=Current(device);if(current.Width!=mode.Width||current.Height!=mode.Height||current.Hz!=mode.Hz)throw new InvalidOperationException("화면 변경 결과가 요청과 다릅니다.");}
        internal static void Apply(string device,uint hz){if(Test(device,hz)!=0)throw new InvalidOperationException("Windows가 주사율 변경을 허용하지 않았습니다.");var original=Current(device);var m=original;m.Hz=hz;m.Fields=0x400000;int result=ChangeDisplaySettingsEx(device,ref m,IntPtr.Zero,0,IntPtr.Zero);if(result!=0)throw new InvalidOperationException("주사율 변경 실패 ("+result+")");var actual=Current(device);if(actual.Hz!=hz||actual.Width!=original.Width||actual.Height!=original.Height){original.Fields=0x400000;ChangeDisplaySettingsEx(device,ref original,IntPtr.Zero,0,IntPtr.Zero);throw new InvalidOperationException("변경 확인 실패 · 이전 주사율 복원을 요청했습니다.");}try{Persist(device,actual);}catch{original.Fields=0x400000;ChangeDisplaySettingsEx(device,ref original,IntPtr.Zero,0,IntPtr.Zero);throw;}}
        internal static object Inspect(){return Screen.AllScreens.Select(s=>new {Device=s.DeviceName,Primary=s.Primary,CurrentHz=Current(s.DeviceName).Hz,Resolution=Current(s.DeviceName).Width+"x"+Current(s.DeviceName).Height,Resolutions=Resolutions(s.DeviceName).Select(r=>new {Size=r.ToString(),Hz=ForResolution(s.DeviceName,r).Hz,Test=TestMode(s.DeviceName,ForResolution(s.DeviceName,r))}).ToArray(),Rates=Rates(s.DeviceName).Where(h=>h==60||h==120).ToArray(),Tests=Rates(s.DeviceName).Where(h=>h==60||h==120).Select(h=>new {Hz=h,Result=Test(s.DeviceName,h)}).ToArray()}).ToArray();}
    }
    sealed partial class ControlForm
    {
        void ChangeResolution()
        {
            if(pickingRefresh||smokeMode||resolutionPicker.SelectedItem==null)return;
            string device=(string)displayPicker.SelectedItem;DisplayRefresh.Mode original;
            try {original=DisplayRefresh.Current(device);}catch(Exception ex){MessageBox.Show(this,ex.Message,"해상도");return;}
            bool keep=false;
            try {
                var target=DisplayRefresh.ForResolution(device,(Resolution)resolutionPicker.SelectedItem);
                using(var dialog=new Form {Text="화면 설정 유지",ClientSize=new Size(350,122),Font=Font,StartPosition=FormStartPosition.CenterScreen,FormBorderStyle=FormBorderStyle.FixedDialog,MaximizeBox=false,MinimizeBox=false,TopMost=true})
                using(var countdown=new System.Windows.Forms.Timer{Interval=1000}) {
                    var label=new Label {Bounds=new Rectangle(18,14,315,48)};dialog.Controls.Add(label);
                    var yes=new SoftButton{Text="유지",Bounds=new Rectangle(140,77,90,30),DialogResult=DialogResult.OK};var no=new SoftButton{Text="되돌리기",Bounds=new Rectangle(240,77,90,30),DialogResult=DialogResult.Cancel};dialog.Controls.Add(yes);dialog.Controls.Add(no);dialog.CancelButton=no;
                    DateTime deadline=DateTime.UtcNow.AddSeconds(15);Action tick=delegate{int remaining=(int)Math.Ceiling((deadline-DateTime.UtcNow).TotalSeconds);label.Text=target.Width+" × "+target.Height+" · "+target.Hz+" Hz\n"+Math.Max(0,remaining)+"초 후 이전 설정으로 복원합니다.";if(remaining<=0)dialog.DialogResult=DialogResult.Cancel;};
                    countdown.Tick+=delegate{tick();};
                    DisplayRefresh.SetMode(device,target);tick();countdown.Start();bool accepted=dialog.ShowDialog(this)==DialogResult.OK;countdown.Stop();if(accepted){DisplayRefresh.Persist(device,target);keep=true;}
                }
            }catch(Exception ex){MessageBox.Show(this,ex.Message,"해상도 변경");}
            finally {
                if(!keep)try{DisplayRefresh.SetMode(device,original);}catch(Exception ex){MessageBox.Show(this,"이전 화면 복원 실패: "+ex.Message,"화면 복원");}
                LoadRefreshRates();FitWithoutScroll((FlowLayoutPanel)Controls[0]);PositionPanel();
            }
        }
        readonly ComboBox displayPicker=new ComboBox(),refreshPicker=new ComboBox(),resolutionPicker=new ComboBox();
        bool pickingRefresh;
        void LoadRefreshRates()
        {
            pickingRefresh=true;try{refreshPicker.Items.Clear();string device=displayPicker.SelectedItem as string;if(device==null)return;var mode=DisplayRefresh.Current(device);uint current=mode.Hz;resolutionPicker.Items.Clear();foreach(var size in DisplayRefresh.Resolutions(device))resolutionPicker.Items.Add(size);resolutionPicker.SelectedItem=new Resolution(mode.Width,mode.Height);resolutionPicker.Enabled=resolutionPicker.Items.Count>1;foreach(uint hz in DisplayRefresh.Rates(device).Where(h=>h==60||h==120))refreshPicker.Items.Add(hz);refreshPicker.SelectedItem=current;refreshPicker.Enabled=refreshPicker.Items.Count>1;detailsTip.SetToolTip(refreshPicker,"현재 해상도 유지 · 재부팅 후에도 유지");}catch(Exception ex){refreshPicker.Enabled=false;detailsTip.SetToolTip(refreshPicker,ex.Message);}finally{pickingRefresh=false;}
        }
        void BuildRefreshRow(FlowLayoutPanel stack)
        {
            var row=new RoundedCard{Width=520,Height=52,Padding=new Padding(14,10,14,10),BackColor=surface,Margin=new Padding(0,0,0,6)};
            resolutionPicker.DropDownStyle=ComboBoxStyle.DropDownList;resolutionPicker.FlatStyle=FlatStyle.Flat;resolutionPicker.Width=205;resolutionPicker.AccessibleName="같은 화면 비율 해상도";
            displayPicker.DropDownStyle=refreshPicker.DropDownStyle=ComboBoxStyle.DropDownList;displayPicker.FlatStyle=refreshPicker.FlatStyle=FlatStyle.Flat;displayPicker.Width=130;refreshPicker.Width=130;displayPicker.AccessibleName="주사율 변경 대상 화면";refreshPicker.AccessibleName="화면 주사율";
            displayPicker.FormattingEnabled=true;displayPicker.Format+=delegate(object sender,ListControlConvertEventArgs e){string name=e.ListItem as string;var screens=Screen.AllScreens;int index=Array.FindIndex(screens,x=>x.DeviceName==name);e.Value="화면 "+(index+1)+(index>=0 && screens[index].Primary?" · 기본":"");};
            refreshPicker.Format+=delegate(object sender,ListControlConvertEventArgs e){e.Value=e.ListItem+" Hz";};refreshPicker.FormattingEnabled=true;
            row.Controls.Add(displayPicker);row.Controls.Add(resolutionPicker);row.Controls.Add(refreshPicker);stack.Controls.Add(row);
            foreach(var screen in Screen.AllScreens)displayPicker.Items.Add(screen.DeviceName);displayPicker.SelectedItem=Screen.PrimaryScreen.DeviceName;LoadRefreshRates();
            displayPicker.SelectedIndexChanged+=delegate{LoadRefreshRates();};
            resolutionPicker.SelectionChangeCommitted+=delegate{ChangeResolution();};
            refreshPicker.SelectionChangeCommitted+=delegate{if(pickingRefresh||smokeMode||refreshPicker.SelectedItem==null)return;try{DisplayRefresh.Apply((string)displayPicker.SelectedItem,(uint)refreshPicker.SelectedItem);}catch(Exception ex){MessageBox.Show(this,ex.Message,"화면 주사율");}finally{LoadRefreshRates();}};
            Activated+=delegate{string old=displayPicker.SelectedItem as string;displayPicker.Items.Clear();foreach(var screen in Screen.AllScreens)displayPicker.Items.Add(screen.DeviceName);displayPicker.SelectedItem=displayPicker.Items.Contains(old??"")?old:Screen.PrimaryScreen.DeviceName;LoadRefreshRates();};
        }
    }
}
