using System;
using System.Drawing;
using System.Linq;
using System.Windows.Forms;

namespace GalaxyHardware
{
    sealed partial class ControlForm
    {
        ComboBox presetPicker=new ComboBox();
        Button savePresetButton=new SoftButton(),deletePresetButton=new SoftButton();
        FanPresetStore userPresets;
        string editorPresetTestPath;
        string PresetStoragePath {get{return editorPresetTestPath??FanPresetStore.FilePath;}}
        bool pickingPreset;
        void RefreshPresetPicker(string selected)
        {
            pickingPreset=true;presetPicker.Items.Clear();
            presetPicker.Items.Add("사용자 커브 · 직접 편집");
            foreach(string name in FanPresetStore.Builtins)presetPicker.Items.Add(name);
            if(userPresets!=null)foreach(var item in userPresets.Items)presetPicker.Items.Add("사용자 · "+item.Name);
            if(selected==null)presetPicker.SelectedIndex=0;else presetPicker.SelectedItem=selected;deletePresetButton.Enabled=selected!=null && selected.StartsWith("사용자 · ",StringComparison.Ordinal);pickingPreset=false;
        }
        void BuildPresetRow(FlowLayoutPanel parent)
        {
            var row=Row();
            presetPicker.DropDownStyle=ComboBoxStyle.DropDownList;presetPicker.Width=218;presetPicker.Margin=new Padding(0,3,8,0);presetPicker.AccessibleName="팬 커브 프리셋";
            presetPicker.BackColor=Color.FromArgb(242,244,247);presetPicker.ForeColor=ForeColor;presetPicker.FlatStyle=FlatStyle.Flat;
            presetPicker.DrawMode=DrawMode.OwnerDrawFixed;
presetPicker.DrawItem+=delegate(object sender,DrawItemEventArgs e){if(e.Index<0)return;using(var brush=new SolidBrush((e.State & DrawItemState.Selected)!=0?Color.FromArgb(221,235,255):presetPicker.BackColor))e.Graphics.FillRectangle(brush,e.Bounds);TextRenderer.DrawText(e.Graphics,presetPicker.Items[e.Index].ToString(),Font,e.Bounds,ForeColor,TextFormatFlags.VerticalCenter|TextFormatFlags.EndEllipsis);};
            row.Controls.Add(presetPicker);
            Style(savePresetButton,"이름 저장",96);row.Controls.Add(savePresetButton);
            detailsTip.SetToolTip(savePresetButton,"현재 곡선을 사용자 프리셋 이름으로 저장합니다.");
            Style(deletePresetButton,"삭제",64);row.Controls.Add(deletePresetButton);
            try {userPresets=previewCalibration==null?FanPresetStore.Load(PresetStoragePath,inlineProfile):new FanPresetStore {Version=1,Items=new System.Collections.Generic.List<NamedFanPreset>()};}
            catch(Exception ex){userPresets=null;savePresetButton.Enabled=false;fanControlStatus.Text="프리셋 읽기 실패: "+ex.Message;}
            RefreshPresetPicker(null);
            presetPicker.SelectedIndexChanged+=delegate {
                if(pickingPreset || presetPicker.SelectedIndex<=0)return;
                string choice=(string)presetPicker.SelectedItem;
                if(choice.StartsWith("사용자 · ",StringComparison.Ordinal))inlineGraph.SetCurve(userPresets.Items.First(p=>p.Name==choice.Substring(6)).Curve);
                else inlineGraph.SetPreset(choice);
                deletePresetButton.Enabled=choice.StartsWith("사용자 · ",StringComparison.Ordinal);
                fanControlStatus.Text=fanClient!=null&&fanCurvePolicy!=null?"프리셋 불러옴 · 변경값 자동 반영 대기":"프리셋 불러옴 · 커브 시작을 눌러 적용하세요.";
            };
            savePresetButton.Click+=delegate {SaveNamedPreset();};
            deletePresetButton.Click+=delegate {
                if(userPresets==null || presetPicker.SelectedItem==null)return;string choice=(string)presetPicker.SelectedItem;
                if(!choice.StartsWith("사용자 · ",StringComparison.Ordinal))return;string name=choice.Substring(6);
                if(MessageBox.Show(this,"‘"+name+"’ 프리셋을 삭제할까요?","프리셋 삭제",MessageBoxButtons.YesNo,MessageBoxIcon.Question)!=DialogResult.Yes)return;
                try {var store=FanPresetStore.Load(PresetStoragePath,inlineProfile);store.Items.RemoveAll(p=>p.Name==name);store.Save(PresetStoragePath);userPresets=store;RefreshPresetPicker(null);fanControlStatus.Text="프리셋 삭제 완료 · 현재 커브는 유지됩니다.";}catch(Exception ex){NotifyError(ex.Message);}
            };
            parent.Controls.Add(row);
        }
        void SaveNamedPreset()
        {
            if(inlineGraph==null || userPresets==null)return;
            using(var dialog=new Form {Text="팬 커브 프리셋 저장",ClientSize=new Size(340,128),Font=Font,StartPosition=FormStartPosition.CenterParent,FormBorderStyle=FormBorderStyle.FixedDialog,MaximizeBox=false,MinimizeBox=false}) {
                var name=new TextBox {Bounds=new Rectangle(18,28,302,27),MaxLength=40,AccessibleName="프리셋 이름"};
                string selected=presetPicker.SelectedItem as string;name.Text=selected!=null && selected.StartsWith("사용자 · ",StringComparison.Ordinal)?selected.Substring(6):"내 커브";
                dialog.Controls.Add(new Label {Text="프리셋 이름",Bounds=new Rectangle(18,5,300,22)});dialog.Controls.Add(name);
                var save=new Button {Text="저장",Bounds=new Rectangle(142,78,85,32)};var cancel=new Button {Text="취소",Bounds=new Rectangle(235,78,85,32),DialogResult=DialogResult.Cancel};dialog.Controls.Add(save);dialog.Controls.Add(cancel);dialog.AcceptButton=save;dialog.CancelButton=cancel;
                save.Click+=delegate {
                    try {
                        string value=FanPresetStore.CheckName(name.Text);var store=FanPresetStore.Load(PresetStoragePath,inlineProfile);
                        if(store.Items.Any(p=>String.Equals(p.Name,value,StringComparison.OrdinalIgnoreCase)) && MessageBox.Show(dialog,"같은 이름의 프리셋을 덮어쓸까요?","프리셋 저장",MessageBoxButtons.YesNo,MessageBoxIcon.Question)!=DialogResult.Yes)return;
                        store.Put(value,inlineGraph.Curve,inlineProfile);store.Save(PresetStoragePath);userPresets=store;RefreshPresetPicker("사용자 · "+value);fanControlStatus.Text="프리셋 저장됨 · 목록에서 다시 불러올 수 있습니다.";dialog.DialogResult=DialogResult.OK;
                    }catch(Exception ex){MessageBox.Show(dialog,ex.Message,"저장 실패",MessageBoxButtons.OK,MessageBoxIcon.Error);}
                };
                dialog.Shown+=delegate {name.SelectAll();name.Focus();};dialog.ShowDialog(this);
            }
        }
    }
}
