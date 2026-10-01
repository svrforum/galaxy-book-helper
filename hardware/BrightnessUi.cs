using System;
using System.ComponentModel;
using System.Drawing;
using System.Management;
using System.Windows.Forms;
namespace GalaxyHardware
{
    sealed partial class ControlForm
    {
        readonly NumericUpDown brightnessValue=new NumericUpDown{Minimum=0,Maximum=100,Increment=1,Value=50};
        readonly System.Windows.Forms.Timer brightnessDelay=new System.Windows.Forms.Timer{Interval=250};
        WattSlider brightnessSlider;
        bool brightnessSync,brightnessBusy,brightnessPending;
        void BuildBrightnessRow(FlowLayoutPanel stack)
        {
            var row=new RoundedCard{Width=424,Height=54,BackColor=surface,Padding=new Padding(8,0,8,0),Margin=new Padding(0,0,0,6)};
            brightnessSlider=new WattSlider("내장 화면 밝기",brightnessValue,"%"){Width=408,Enabled=false};row.Controls.Add(brightnessSlider);stack.Controls.Add(row);
            brightnessValue.ValueChanged+=delegate{if(brightnessSync||smokeMode)return;brightnessDelay.Stop();brightnessDelay.Start();};
            brightnessDelay.Tick+=delegate{brightnessDelay.Stop();if(brightnessBusy){brightnessPending=true;return;}RunBrightness(true);};
            Shown+=delegate{if(!smokeMode)RunBrightness(false);};
            detailsTip.SetToolTip(brightnessSlider,"내장 화면 밝기 · 드래그하면 적용");
        }
        void RunBrightness(bool write)
        {
            if(brightnessBusy||IsDisposed)return;brightnessBusy=true;byte requested=(byte)brightnessValue.Value;
            var worker=new BackgroundWorker();worker.DoWork+=delegate(object sender,DoWorkEventArgs e){
                var options=new EnumerationOptions{Timeout=TimeSpan.FromSeconds(3)};
                using(var search=new ManagementObjectSearcher("root\\wmi","SELECT * FROM WmiMonitorBrightness WHERE Active=TRUE",options))using(var results=search.Get()) {
                    string instance=null;byte current=0;foreach(ManagementObject item in results)using(item){if(instance!=null)throw new InvalidOperationException("밝기 대상 화면이 여러 개라 변경하지 않았습니다.");instance=(string)item["InstanceName"];current=Convert.ToByte(item["CurrentBrightness"]);}
                    if(instance==null)throw new InvalidOperationException("Windows 밝기 제어를 지원하는 내장 화면이 없습니다.");
                    if(write)using(var methods=new ManagementObjectSearcher("root\\wmi","SELECT * FROM WmiMonitorBrightnessMethods",options))using(var entries=methods.Get()) {
                        bool found=false;foreach(ManagementObject entry in entries)using(entry){if((string)entry["InstanceName"]!=instance)continue;var output=entry.InvokeMethod("WmiSetBrightness",new object[]{(uint)0,requested});if(output!=null&&Convert.ToUInt32(output)!=0)throw new InvalidOperationException("밝기 변경을 Windows가 거부했습니다.");found=true;}
                        if(!found)throw new InvalidOperationException("밝기 변경 인터페이스가 없습니다.");
                    }
                    e.Result=write?requested:current;
                }
            };
            worker.RunWorkerCompleted+=delegate(object sender,RunWorkerCompletedEventArgs e){brightnessBusy=false;if(!IsDisposed){if(e.Error!=null){detailsTip.SetToolTip(brightnessSlider,e.Error.Message);if(write)NotifyError(e.Error.Message);}else{brightnessSlider.Enabled=true;if(!write){brightnessSync=true;brightnessValue.Value=Convert.ToDecimal(e.Result);brightnessSync=false;}}if(brightnessPending){brightnessPending=false;RunBrightness(true);}}worker.Dispose();};worker.RunWorkerAsync();
        }
    }
}
