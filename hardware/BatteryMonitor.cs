using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.InteropServices;
using System.Windows.Forms;
namespace GalaxyHardware
{
    sealed class BatteryReading
    {
        public bool Present {get;set;}
        public bool Online {get;set;}
        public bool Charging {get;set;}
        public int? Percent {get;set;}
        public double? DischargeWatts {get;set;}
        public double? RemainingWh {get;set;}
        public double? EstimatedMinutes {get;set;}
    }
    sealed class BatteryMonitor
    {
        [StructLayout(LayoutKind.Sequential)] struct PowerStatus {public byte Online,Flags,Percent,Reserved;public uint LifeTime,FullLifeTime;}
        [DllImport("kernel32.dll")] static extern bool GetSystemPowerStatus(out PowerStatus status);
        [DllImport("powrprof.dll")] static extern uint CallNtPowerInformation(int level,IntPtr input,uint inputSize,[Out]byte[] output,uint outputSize);
        readonly Queue<Tuple<DateTime,double>> rates=new Queue<Tuple<DateTime,double>>();
        DateTime last=DateTime.MinValue;
        internal BatteryReading Observe(byte[] state,int? percent,DateTime now)
        {
            if(state==null || state.Length!=32)throw new ArgumentException("Invalid Windows battery state.");
            var r=new BatteryReading{Online=state[0]!=0,Present=state[1]!=0,Charging=state[2]!=0,Percent=percent>=0&&percent<=100?percent:null};
            uint capacity=BitConverter.ToUInt32(state,12),rawRate=BitConverter.ToUInt32(state,16);
            int rate=unchecked((int)rawRate);
            if(r.Present && capacity!=UInt32.MaxValue)r.RemainingWh=capacity/1000.0;
            if(now<last || (now-last).TotalSeconds>15)rates.Clear();last=now;
            if(!r.Present || r.Online || state[3]==0 || rawRate==UInt32.MaxValue || rate>=0 || rate==Int32.MinValue){rates.Clear();return r;}
            r.DischargeWatts=-(double)rate/1000.0;
            rates.Enqueue(Tuple.Create(now,r.DischargeWatts.Value));
            while(rates.Count>0 && (now-rates.Peek().Item1).TotalSeconds>60)rates.Dequeue();
            if(r.RemainingWh.HasValue){double minutes=r.RemainingWh.Value/rates.Average(x=>x.Item2)*60;if(minutes>=0 && minutes<=7*24*60)r.EstimatedMinutes=minutes;}
            return r;
        }
        internal BatteryReading Read()
        {
            var bytes=new byte[32];uint status=CallNtPowerInformation(5,IntPtr.Zero,0,bytes,32);
            if(status!=0)throw new InvalidOperationException("Windows 배터리 조회 실패: 0x"+status.ToString("X8"));
            PowerStatus power;int? percent=GetSystemPowerStatus(out power)&&power.Percent<=100?(int?)power.Percent:null;
            return Observe(bytes,percent,DateTime.UtcNow);
        }
        internal static string Text(BatteryReading r)
        {
            if(!r.Present)return "배터리 없음 · PC 전체 전력 조회 불가";
            string battery="배터리 "+(r.Percent.HasValue?r.Percent+"%":"—");
            if(r.Online)return battery+" · "+(r.Charging?"충전 중":"전원 연결");
            string watts=r.DischargeWatts.HasValue?"PC 전체 "+r.DischargeWatts.Value.ToString("0.0")+"W":"PC 전체 — W";
            string time="남은 시간 계산 중";
            if(r.EstimatedMinutes.HasValue){int m=(int)Math.Floor(r.EstimatedMinutes.Value);time="예상 약 "+(m>=60?m/60+"시간 ":"")+m%60+"분";}
            return watts+" · "+battery+" · "+time;
        }
        internal static string[] Tests()
        {
            Action<bool> check=delegate(bool ok){if(!ok)throw new Exception("Battery calculation test failed.");};
            var b=new byte[32];b[1]=1;b[3]=1;BitConverter.GetBytes(30000U).CopyTo(b,12);BitConverter.GetBytes(-10000).CopyTo(b,16);
            var monitor=new BatteryMonitor();var now=DateTime.UtcNow;var r=monitor.Observe(b,50,now);check(r.DischargeWatts==10&&r.RemainingWh==30&&r.EstimatedMinutes==180);
            BitConverter.GetBytes(-20000).CopyTo(b,16);r=monitor.Observe(b,50,now.AddSeconds(5));check(r.EstimatedMinutes==120&&r.DischargeWatts==20);
            b[0]=1;b[2]=1;r=monitor.Observe(b,50,now.AddSeconds(10));check(!r.DischargeWatts.HasValue&&!r.EstimatedMinutes.HasValue);
            b[0]=0;b[2]=0;r=monitor.Observe(b,50,now.AddSeconds(15));check(r.EstimatedMinutes==90);
            BitConverter.GetBytes(UInt32.MaxValue).CopyTo(b,16);r=monitor.Observe(b,255,now.AddSeconds(20));check(!r.Percent.HasValue&&!r.DischargeWatts.HasValue&&!r.EstimatedMinutes.HasValue);
            BitConverter.GetBytes(-10000).CopyTo(b,16);BitConverter.GetBytes(UInt32.MaxValue).CopyTo(b,12);r=monitor.Observe(b,50,now.AddSeconds(25));check(r.DischargeWatts==10&&!r.EstimatedMinutes.HasValue);
            b[1]=0;r=monitor.Observe(b,50,now.AddSeconds(30));check(!r.Present&&!r.DischargeWatts.HasValue);
            b[1]=1;BitConverter.GetBytes(30000U).CopyTo(b,12);BitConverter.GetBytes(-20000).CopyTo(b,16);r=monitor.Observe(b,50,now.AddSeconds(100));check(r.EstimatedMinutes==90);
            return new[]{"battery signed mW and mWh convert to watts and runtime","battery runtime uses recent rates and clears on AC, unknown sensors and sleep gaps"};
        }
    }
    sealed partial class ControlForm
    {
        readonly Label batterySummary=new SingleLineStatus();
        readonly System.Windows.Forms.Timer batteryTimer=new System.Windows.Forms.Timer{Interval=5000};
        readonly BatteryMonitor batteryMonitor=new BatteryMonitor();
        void BuildBatterySummary(Panel header)
        {
            batterySummary.Bounds=new System.Drawing.Rectangle(0,31,424,18);batterySummary.Font=new System.Drawing.Font(Font.FontFamily,8.5f);batterySummary.ForeColor=System.Drawing.Color.FromArgb(85,104,127);
            batterySummary.Text="배터리 조회 중…";header.Controls.Add(batterySummary);header.Height=50;
            Action refresh=delegate {
                try {var r=batteryMonitor.Read();batterySummary.Text=BatteryMonitor.Text(r);detailsTip.SetToolTip(batterySummary,"배터리 방전 기준 전체 소비 전력입니다. 콘센트 입력 전력은 아닙니다.\n남은 시간은 최근 1분 평균 방전량으로 계산한 추정이며 작업·밝기에 따라 달라집니다.\n남은 용량: "+(r.RemainingWh.HasValue?r.RemainingWh.Value.ToString("0.0")+" Wh":"조회 불가"));}
                catch(Exception ex){batterySummary.Text="배터리 정보 조회 불가";detailsTip.SetToolTip(batterySummary,ex.Message);}
            };
            batteryTimer.Tick+=delegate {refresh();};Shown+=delegate{if(!smokeMode){refresh();batteryTimer.Start();}else batterySummary.Text="PC 전체 — W · 배터리 — · 예상 시간 —";};
        }
    }
}
