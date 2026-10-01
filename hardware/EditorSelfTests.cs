using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Windows.Forms;
using System.Web.Script.Serialization;

namespace GalaxyHardware
{
    static class EditorSelfTests
    {
        internal static void Assert(bool value,string message){if(!value)throw new InvalidOperationException(message);}
        internal static object Run()
        {
            var profile=new FanCalibration {Model="Galaxy Book6 Pro - PAMB",Bios="PAMB.1.5.74.371",CreatedUtc=DateTime.UtcNow,MaxStep=3,Verified=true,Entries=new List<FanCalibrationEntry> {
                new FanCalibrationEntry {Step=1,Fan1Peak=1800,Fan2Peak=1900},new FanCalibrationEntry {Step=2,Fan1Peak=2700,Fan2Peak=2800},new FanCalibrationEntry {Step=3,Fan1Peak=3500,Fan2Peak=3600}}};
            using(var form=new ControlForm(true,profile)) {form.Show();Application.DoEvents();var report=form.CheckEditorInteractions(profile);form.Close();return report;}
        }
    }
    sealed partial class ControlForm
    {
        internal object CheckEditorInteractions(FanCalibration profile)
        {
            var passed=new List<string>();
            powerPicker.SelectedIndex=1;EditorSelfTests.Assert(pl1.Value==5 && pl2.Value==10,"Power preset did not update sliders.");
            pl1.Value=6;EditorSelfTests.Assert(powerPicker.SelectedIndex==0,"Edited power preset remained selected.");
            string powerTest=Path.Combine(Path.GetTempPath(),"GalaxyPower-"+Guid.NewGuid().ToString("N")+".json");
            try {var store=PowerPresetStore.Load(powerTest);store.Put("내 무소음",5,10);store.Save(powerTest);store=PowerPresetStore.Load(powerTest);EditorSelfTests.Assert(store.Items.Count==1 && store.Items[0].Burst==10,"Power preset reload failed.");store.Put("내 무소음",7.5m,12);store.Save(powerTest);EditorSelfTests.Assert(PowerPresetStore.Load(powerTest).Items[0].Sustained==7.5m && File.Exists(powerTest+".bak"),"Power preset overwrite failed.");bool rejected=false;try{store.Put("잘못된 값",20,10);}catch{rejected=true;}EditorSelfTests.Assert(rejected,"Invalid power pair accepted.");store.Items.Clear();store.Save(powerTest);EditorSelfTests.Assert(PowerPresetStore.Load(powerTest).Items.Count==0,"Power preset delete failed.");File.WriteAllText(powerTest,"{invalid");rejected=false;try{PowerPresetStore.Load(powerTest);}catch{rejected=true;}EditorSelfTests.Assert(rejected && File.ReadAllText(powerTest)=="{invalid","Corrupt power preset silently overwritten.");}
            finally {if(File.Exists(powerTest))File.Delete(powerTest);if(File.Exists(powerTest+".bak"))File.Delete(powerTest+".bak");}
            passed.Add("power preset selection, disk reload, overwrite backup, deletion and invalid-file preservation");
            using(var slider=new WattSlider("시험",pl1)){slider.ExerciseDrag(0);EditorSelfTests.Assert(pl1.Value==5,"Slider minimum failed.");slider.ExerciseDrag(340);EditorSelfTests.Assert(pl1.Value==80,"Slider maximum failed.");slider.ExerciseDrag(170);EditorSelfTests.Assert(pl1.Value==42.5m,"Slider half-watt mapping failed.");pl1.Value=15;}
            EditorSelfTests.Assert(curveDetails.Visible,"Curve must be visible by default.");passed.Add("power slider drag maps endpoints and half-watt increments; curve starts expanded");
            EditorSelfTests.Assert(PanelPosition(new System.Drawing.Rectangle(-1920,0,1920,1040),new System.Drawing.Size(560,740))==new System.Drawing.Point(-572,288),"Tray panel escaped secondary monitor bounds.");
            EditorSelfTests.Assert(!ShowInTaskbar && FormBorderStyle==FormBorderStyle.None,"Panel uses standard window chrome.");
            passed.Add("tray panel anchors inside negative-coordinate monitor and hides taskbar window");
            if(inlineGraph==null)throw new IOException("Curve editor failed to initialize.");
            var curve=inlineGraph.Curve;
            EditorSelfTests.Assert(curve.Version==3 && curve.Rpms.Length==8,"Legacy curve migration failed.");
            inlineGraph.ExerciseDrag(2,45,1000);
            EditorSelfTests.Assert(Math.Abs(curve.Temperatures[2]-45)<=1 && Math.Abs(curve.Rpms[2]-1000)<=50,"Mouse drag did not change both axes.");
            EditorSelfTests.Assert(curveTemperature.Value==curve.Temperatures[2] && curveRpm.Value==curve.Rpms[2],"Numeric inputs did not track mouse drag.");
            passed.Add("mouse hit-test and drag update both temperature and RPM, including below prior minimum");
            curveTemperature.Value=47;curveRpm.Value=1250;
            EditorSelfTests.Assert(curve.Temperatures[2]==47 && curve.Rpms[2]==1250,"Numeric edits not applied.");
            curveRpm.Value=0;EditorSelfTests.Assert(curve.HasZero && curve.Rpms[2]==0 && curve.Rpms[3]>0 && curveCutoff.Enabled,"Mixed zero and rotation curve failed.");
            curveCutoff.Value=85;EditorSelfTests.Assert(curve.ZeroStopTemperature==85,"Cutoff edit failed.");
            passed.Add("numeric temperature/RPM edits and mixed zero curve with independent cutoff");
            inlineGraph.ExerciseKey(Keys.Right);EditorSelfTests.Assert(curve.Temperatures[2]==48,"Keyboard temperature edit failed.");
            inlineGraph.ExerciseKey(Keys.Up);EditorSelfTests.Assert(curve.Rpms[2]==50,"Keyboard RPM edit failed.");
            inlineGraph.ExerciseKey(Keys.Control|Keys.Z);EditorSelfTests.Assert(curve.Rpms[2]==0,"Undo failed.");
            passed.Add("keyboard temperature/RPM edit and undo");
            string draft=curve.Signature;
            inlineGraph.ExerciseKey(Keys.Shift|Keys.Up);EditorSelfTests.Assert(curve.Rpms[0]==50,"Whole curve shift failed.");
            inlineGraph.Undo();EditorSelfTests.Assert(curve.Signature==draft,"Whole curve undo failed.");
            passed.Add("whole curve shift preserves shape and can be undone");
            string folder=Path.Combine(Path.GetTempPath(),"GalaxyHelperEditor-"+Guid.NewGuid().ToString("N"));string path=Path.Combine(folder,"presets.json");
            try {
                editorPresetTestPath=path;
                using(var modalInput=new System.Windows.Forms.Timer {Interval=100}) {
                    modalInput.Tick+=delegate {
                        modalInput.Stop();
                        var dialog=Application.OpenForms.Cast<Form>().First(f=>f.Text=="팬 커브 프리셋 저장");
                        dialog.Controls.OfType<TextBox>().Single().Text="UI 저장 시험";
                        dialog.Controls.OfType<Button>().Single(b=>b.Text=="저장").PerformClick();
                    };
                    modalInput.Start();savePresetButton.PerformClick();
                }
                EditorSelfTests.Assert(File.Exists(path) && (string)presetPicker.SelectedItem=="사용자 · UI 저장 시험","Save button/dialog did not persist preset.");
                presetPicker.SelectedItem="냉각 우선";presetPicker.SelectedItem="사용자 · UI 저장 시험";
                EditorSelfTests.Assert(curve.Signature==draft,"Custom preset picker did not restore saved curve.");
                passed.Add("real Save button and name dialog persist custom preset; picker reloads it");
                File.Delete(path);
                var store=FanPresetStore.Load(path,profile);store.Put("내 커브",curve,profile);store.Save(path);
                var loaded=FanPresetStore.Load(path,profile);EditorSelfTests.Assert(loaded.Items.Count==1 && loaded.Items[0].Curve.Signature==draft,"Named preset roundtrip failed.");
                inlineGraph.SetPreset("냉각 우선");inlineGraph.SetCurve(loaded.Items[0].Curve);EditorSelfTests.Assert(curve.Signature==draft,"Preset reload failed.");
                curveRpm.Value=500;EditorSelfTests.Assert(loaded.Items[0].Curve.Signature==draft,"Draft edit mutated stored preset.");
                store.Put("내 커브",curve,profile);store.Save(path);EditorSelfTests.Assert(FanPresetStore.Load(path,profile).Items.Count==1 && File.Exists(path+".bak"),"Overwrite duplicated preset or lost backup.");
                store.Items.RemoveAll(p=>p.Name=="내 커브");store.Save(path);EditorSelfTests.Assert(FanPresetStore.Load(path,profile).Items.Count==0,"Delete did not persist.");
                passed.Add("named preset save, disk reload, overwrite backup and delete; draft isolation");
                File.WriteAllText(path,"{bad json");bool rejected=false;try{FanPresetStore.Load(path,profile);}catch{rejected=true;}EditorSelfTests.Assert(rejected && File.ReadAllText(path)=="{bad json","Corrupt preset file was silently overwritten.");
                passed.Add("invalid preset file is preserved and reported");
                bool reserved=false;try{FanPresetStore.CheckName("평균");}catch(ArgumentException){reserved=true;}EditorSelfTests.Assert(reserved,"Built-in preset overwrite accepted.");
                passed.Add("built-in preset names cannot be overwritten");
                SaveLayoutReport(Path.Combine(folder,"layout.json"));var layout=new JavaScriptSerializer().Deserialize<Dictionary<string,object>>(File.ReadAllText(Path.Combine(folder,"layout.json")));EditorSelfTests.Assert((bool)layout["Success"],"Editor controls clipped or scrollbars visible: "+File.ReadAllText(Path.Combine(folder,"layout.json")));
                passed.Add("complete editor fits without scrolling or clipped controls");
            }finally {editorPresetTestPath=null;foreach(string file in new[]{path,path+".bak",Path.Combine(folder,"layout.json")})if(File.Exists(file))File.Delete(file);if(Directory.Exists(folder))Directory.Delete(folder,false);}
            return new {Success=true,Passed=passed.Count,Cases=passed,HardwareAccess=false,UserFilesModified=false};
        }
    }
}
