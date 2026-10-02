using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Text;
using System.Web.Script.Serialization;
using System.Windows.Forms;
namespace GalaxyHardware
{
    sealed class PowerPreset
    {
        public string Name {get;set;}
        public decimal Sustained {get;set;}
        public decimal Burst {get;set;}
        public override string ToString(){return Name+"  ·  "+Sustained.ToString("0.#")+" / "+Burst.ToString("0.#")+" W";}
    }
    sealed class PowerPresetStore
    {
        public int Version {get;set;}
        public List<PowerPreset> Items {get;set;}
        internal static string PathName {get{return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),"GalaxyHelper","power-presets.json");}}
        internal static string NameFor(string name){name=(name??"").Trim().Normalize();if(name.Length<1||name.Length>40||name.Any(Char.IsControl)||new[]{"무소음","절전","균형","성능"}.Contains(name))throw new ArgumentException("기본 프리셋과 다른 이름을 1~40자로 입력하세요.");return name;}
        internal static void Validate(PowerPreset p){if(p==null||NameFor(p.Name)!=p.Name||p.Sustained<5||p.Burst>80||p.Burst<p.Sustained||p.Sustained*2!=Math.Truncate(p.Sustained*2)||p.Burst*2!=Math.Truncate(p.Burst*2))throw new InvalidDataException("전력은 5~80W, 0.5W 단위이며 단기는 지속 이상이어야 합니다.");}
        internal static PowerPresetStore Load(string path){if(!File.Exists(path))return new PowerPresetStore {Version=1,Items=new List<PowerPreset>()};var s=new JavaScriptSerializer().Deserialize<PowerPresetStore>(File.ReadAllText(path));if(s==null||s.Version!=1||s.Items==null||s.Items.Count>100)throw new InvalidDataException("전력 프리셋 형식 오류");var names=new HashSet<string>(StringComparer.OrdinalIgnoreCase);foreach(var p in s.Items){Validate(p);if(!names.Add(p.Name))throw new InvalidDataException("중복 프리셋 이름");}return s;}
        internal void Put(string name,decimal a,decimal b){var p=new PowerPreset{Name=NameFor(name),Sustained=a,Burst=b};Validate(p);int index=Items.FindIndex(x=>String.Equals(x.Name,p.Name,StringComparison.OrdinalIgnoreCase));if(index>=0)Items[index]=p;else{if(Items.Count>=100)throw new InvalidDataException("최대 100개까지 저장할 수 있습니다.");Items.Add(p);}}
        internal void Save(string path){Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(path)));string temp=path+"."+Guid.NewGuid().ToString("N")+".tmp";try{File.WriteAllText(temp,new JavaScriptSerializer().Serialize(this),new UTF8Encoding(false));if(File.Exists(path))File.Replace(temp,path,path+".bak");else File.Move(temp,path);}finally{if(File.Exists(temp))File.Delete(temp);}}
    }
    sealed partial class ControlForm
    {
        readonly ComboBox powerPicker=new ComboBox();
        readonly Button powerSave=new SoftButton(),powerDelete=new SoftButton();
        PowerPresetStore powerPresets;
        bool pickingPower;
        void RefreshPowerPresets(string selected=null)
        {
            pickingPower=true;powerPicker.Items.Clear();powerPicker.Items.Add("전력 프리셋 선택");
            foreach(var p in new[]{new PowerPreset{Name="무소음",Sustained=5,Burst=10},new PowerPreset{Name="절전",Sustained=10,Burst=10},new PowerPreset{Name="균형",Sustained=15,Burst=20},new PowerPreset{Name="성능",Sustained=25,Burst=35}})powerPicker.Items.Add(p);
            if(powerPresets!=null)foreach(var p in powerPresets.Items)powerPicker.Items.Add(p);
            powerPicker.SelectedIndex=0;for(int i=5;i<powerPicker.Items.Count;i++)if(((PowerPreset)powerPicker.Items[i]).Name==selected)powerPicker.SelectedIndex=i;
            powerDelete.Enabled=powerPicker.SelectedIndex>=5;pickingPower=false;
        }
        void BuildPowerPresets(FlowLayoutPanel parent)
        {
            var row=Row();powerPicker.DropDownStyle=ComboBoxStyle.DropDownList;powerPicker.FlatStyle=FlatStyle.Flat;powerPicker.BackColor=Color.FromArgb(242,244,247);powerPicker.Width=218;powerPicker.Margin=new Padding(0,3,8,0);powerPicker.AccessibleName="전력 프리셋";row.Controls.Add(powerPicker);
            Style(powerSave,"이름 저장",96);Style(powerDelete,"삭제",64);row.Controls.Add(powerSave);row.Controls.Add(powerDelete);parent.Controls.Add(row);
            detailsTip.SetToolTip(powerSave,"현재 전력값을 사용자 프리셋으로 저장합니다. 실제 전력은 ‘전력 적용’으로 변경하세요.");
            try {powerPresets=smokeMode?new PowerPresetStore{Version=1,Items=new List<PowerPreset>()}:PowerPresetStore.Load(PowerPresetStore.PathName);}catch(Exception ex){powerSave.Enabled=false;status.Text="프리셋 읽기 실패: "+ex.Message;}
            RefreshPowerPresets();
            powerPicker.SelectedIndexChanged+=delegate {if(pickingPower)return;var p=powerPicker.SelectedItem as PowerPreset;if(p==null)return;pickingPower=true;pl1.Value=p.Sustained;pl2.Value=p.Burst;pickingPower=false;powerDelete.Enabled=powerPicker.SelectedIndex>=5;status.Text="불러옴 · 적용하면 반영됩니다.";};
            EventHandler edited=delegate {if(pickingPower)return;pickingPower=true;powerPicker.SelectedIndex=0;powerDelete.Enabled=false;pickingPower=false;if(!busy)status.Text="변경 대기 · 전력 적용을 누르면 반영됩니다.";};pl1.ValueChanged+=edited;pl2.ValueChanged+=edited;
            powerSave.Click+=delegate {SavePowerPreset();};
            powerDelete.Click+=delegate {var p=powerPicker.SelectedItem as PowerPreset;if(p==null||powerPicker.SelectedIndex<5)return;if(MessageBox.Show(this,p.Name+" 프리셋을 삭제할까요?","전력 프리셋",MessageBoxButtons.YesNo)!=DialogResult.Yes)return;try{var store=PowerPresetStore.Load(PowerPresetStore.PathName);store.Items.RemoveAll(x=>x.Name==p.Name);store.Save(PowerPresetStore.PathName);powerPresets=store;RefreshPowerPresets();status.Text="전력 프리셋 삭제 완료";}catch(Exception ex){NotifyError(ex.Message);}};
        }
        void SavePowerPreset()
        {
            using(var dialog=new Form{Text="전력 프리셋 저장",ClientSize=new Size(340,145),Font=Font,StartPosition=FormStartPosition.CenterParent,FormBorderStyle=FormBorderStyle.FixedDialog,MaximizeBox=false,MinimizeBox=false}) {
                var selected=powerPicker.SelectedItem as PowerPreset;
                var name=new TextBox{Text=powerPicker.SelectedIndex>=5?selected.Name:"내 전력 설정",MaxLength=40,Bounds=new Rectangle(18,44,304,26)};
                dialog.Controls.Add(new Label{Text="지속 "+pl1.Value.ToString("0.#")+" W  /  단기 "+pl2.Value.ToString("0.#")+" W",Bounds=new Rectangle(18,12,304,24)});dialog.Controls.Add(name);
                var save=new SoftButton{Text="저장",Bounds=new Rectangle(142,94,85,32)};var cancel=new SoftButton{Text="취소",Bounds=new Rectangle(237,94,85,32),DialogResult=DialogResult.Cancel};dialog.Controls.Add(save);dialog.Controls.Add(cancel);dialog.AcceptButton=save;dialog.CancelButton=cancel;
                save.Click+=delegate {try{string value=PowerPresetStore.NameFor(name.Text);var store=PowerPresetStore.Load(PowerPresetStore.PathName);if(store.Items.Any(p=>String.Equals(p.Name,value,StringComparison.OrdinalIgnoreCase))&&MessageBox.Show(dialog,"같은 이름을 덮어쓸까요?","전력 프리셋",MessageBoxButtons.YesNo)!=DialogResult.Yes)return;store.Put(value,pl1.Value,pl2.Value);store.Save(PowerPresetStore.PathName);powerPresets=store;RefreshPowerPresets(value);status.Text="전력 프리셋 저장 완료";dialog.DialogResult=DialogResult.OK;}catch(Exception ex){MessageBox.Show(dialog,ex.Message,"저장 실패");}};
                dialog.Shown+=delegate{name.Focus();name.SelectAll();};dialog.ShowDialog(this);
            }
        }
    }
}
