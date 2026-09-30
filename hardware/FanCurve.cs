using System;
using System.IO;
using System.Linq;
using System.Web.Script.Serialization;

namespace GalaxyHardware
{
    sealed class FanCurve
    {
        public int Version {get;set;}
        public int[] Temperatures {get;set;}
        public int[] Rpms {get;set;}
        public int ZeroStopTemperature {get;set;}
        public bool IsZeroHold {get {return Version==2 || (Version==3 && Rpms!=null && Rpms.All(r=>r==0));}}
        public bool HasZero {get {return Version==2 || (Version==3 && Rpms!=null && Rpms.Any(r=>r==0));}}
        public string Signature {get {return Version+":"+ZeroStopTemperature+":"+String.Join(",",Temperatures)+":"+String.Join(",",Rpms);}}
        public static string FilePath {get {return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),"GalaxyHelper","fan-curve.json");}}
        public static FanCurve Default(FanCalibration profile)
        { return Preset(profile,"평균"); }
        public static FanCurve Preset(FanCalibration profile,string name)
        {
            if(name=="0 RPM")return new FanCurve {Version=2,Temperatures=new[]{30,40,50,60,70,79},Rpms=new int[6],ZeroStopTemperature=90};
            int low=Round(profile.Entries[0].ConservativeRpm),mid=Round(profile.Entries[1].ConservativeRpm),high=Round(profile.Entries[2].ConservativeRpm);
            int[] rpms;
            switch(name) {
                case "극저소음": rpms=new[]{low,low,low,low,mid,high};break;
                case "최적화": rpms=new[]{low,low,low,mid,high,high};break;
                case "평균": rpms=new[]{low,low,mid,mid,high,high};break;
                case "냉각 우선": rpms=new[]{low,mid,mid,high,high,high};break;
                default: throw new ArgumentException("알 수 없는 커브 프리셋입니다.");
            }
            var result=new FanCurve {Version=1,Temperatures=new[]{30,40,50,60,70,79},Rpms=rpms};result.Validate(profile);return result;
        }
        internal static int Round(int value) {return (int)Math.Ceiling(value/100.0)*100;}
        public void Validate(FanCalibration profile)
        {
            profile.Validate();
            if(Version==3) {
                if(Temperatures==null || Rpms==null || Temperatures.Length!=8 || Rpms.Length!=8 || ZeroStopTemperature<45 || ZeroStopTemperature>90)throw new InvalidDataException("커브 형식이 올바르지 않습니다.");
                for(int i=0;i<8;i++)if(Temperatures[i]<20 || Temperatures[i]>90 || Rpms[i]<0 || Rpms[i]>Round(profile.Entries[2].ConservativeRpm) || (i>0 && (Temperatures[i]<=Temperatures[i-1] || Rpms[i]<Rpms[i-1])))throw new InvalidDataException("온도는 20~90°C에서 증가하고, RPM은 0 이상에서 같거나 증가해야 합니다.");
                return;
            }
            if((Version!=1 && Version!=2) || Temperatures==null || Rpms==null || Temperatures.Length!=6 || Rpms.Length!=6 || !Temperatures.SequenceEqual(new[]{30,40,50,60,70,79})) throw new InvalidDataException("커브 온도 지점이 올바르지 않습니다.");
            if(IsZeroHold) {if(ZeroStopTemperature<45 || ZeroStopTemperature>90 || Rpms.Any(r=>r!=0))throw new InvalidDataException("0 RPM 해제 온도는 45~90°C입니다.");return;}
            int minimum=Round(profile.Entries[0].ConservativeRpm),maximum=Round(profile.Entries[2].ConservativeRpm);
            for(int i=0;i<6;i++) if(Rpms[i]<minimum || Rpms[i]>maximum || (i>0 && Rpms[i]<Rpms[i-1])) throw new InvalidDataException("커브는 실측 RPM 범위 안에서 온도가 높을수록 같거나 높아야 합니다.");
        }
        internal void EditPoint(int index,int rpm,FanCalibration profile)
        {
            if(Version==3){EditKnot(index,Temperatures[index],rpm,profile);return;}
            Validate(profile);
            if(IsZeroHold)throw new InvalidOperationException("0 RPM 모드에서는 해제 온도를 조정하세요.");
            if(index<0 || index>=Rpms.Length) throw new ArgumentOutOfRangeException("index");
            int value=Math.Max(Round(profile.Entries[0].ConservativeRpm),Math.Min(Round(profile.Entries[2].ConservativeRpm),rpm));
            Rpms[index]=value;
            for(int i=index-1;i>=0;i--) Rpms[i]=Math.Min(Rpms[i],value);
            for(int i=index+1;i<Rpms.Length;i++) Rpms[i]=Math.Max(Rpms[i],value);
            Validate(profile);
        }
        internal int Interpolate(int temperature)
        {
            if(temperature<=Temperatures[0])return Rpms[0];
            for(int i=1;i<Temperatures.Length;i++)if(temperature<=Temperatures[i])return (int)Math.Round(Rpms[i-1]+(Rpms[i]-Rpms[i-1])*(temperature-Temperatures[i-1])/(double)(Temperatures[i]-Temperatures[i-1]));
            return Rpms[Rpms.Length-1];
        }
        internal void UpgradeForEditing(FanCalibration profile)
        {
            Validate(profile);if(Version==3)return;
            var temperatures=new[]{20,30,40,50,60,70,79,90};
            var values=temperatures.Select(t=>Interpolate(t)).ToArray();
            if(Version==1)ZeroStopTemperature=90;
            Version=3;Temperatures=temperatures;Rpms=values;Validate(profile);
        }
        internal void EditKnot(int index,int temperature,int rpm,FanCalibration profile)
        {
            UpgradeForEditing(profile);
            if(index<0 || index>=Rpms.Length)throw new ArgumentOutOfRangeException("index");
            int t=Math.Max(20+index,Math.Min(90-(Rpms.Length-1-index),temperature));
            int value=Math.Max(0,Math.Min(Round(profile.Entries[2].ConservativeRpm),rpm));
            Temperatures[index]=t;Rpms[index]=value;
            for(int i=index-1;i>=0;i--){Temperatures[i]=Math.Min(Temperatures[i],Temperatures[i+1]-1);Rpms[i]=Math.Min(Rpms[i],value);}
            for(int i=index+1;i<Rpms.Length;i++){Temperatures[i]=Math.Max(Temperatures[i],Temperatures[i-1]+1);Rpms[i]=Math.Max(Rpms[i],value);}
            Validate(profile);
        }
        internal void ShiftRpm(int delta,FanCalibration profile)
        {UpgradeForEditing(profile);for(int i=0;i<Rpms.Length;i++)Rpms[i]=Math.Max(0,Math.Min(Round(profile.Entries[2].ConservativeRpm),Rpms[i]+delta));Validate(profile);}
        internal int SelectStep(int temperature,FanCalibration profile)
        {
            int rpm=At(temperature);if(rpm==0)return 0;
            return rpm<profile.Entries[0].ConservativeRpm?1:profile.SelectStep(rpm);
        }
        public int At(int temperature)
        {
            if(temperature<0)throw new InvalidDataException("온도 센서 범위 밖입니다.");
            int rpm=Interpolate(temperature);
            if(rpm==0 && temperature>=ZeroStopTemperature)throw new InvalidDataException("정지 해제 온도에 도달하여 삼성 자동 냉각으로 복귀합니다.");
            if(rpm>0 && temperature>=80)throw new InvalidDataException("회전 구간의 80°C 보호: 삼성 자동 냉각으로 복귀합니다.");
            return rpm;
        }
        public static FanCurve Load(FanCalibration profile)
        {
            var curve=File.Exists(FilePath)?new JavaScriptSerializer().Deserialize<FanCurve>(File.ReadAllText(FilePath)):Default(profile);
            curve.Validate(profile); return curve;
        }
        public void Save(FanCalibration profile)
        { Validate(profile); Directory.CreateDirectory(Path.GetDirectoryName(FilePath)); File.WriteAllText(FilePath,new JavaScriptSerializer().Serialize(this)); }
    }
    sealed class FanCurvePolicy
    {
        readonly FanCalibration profile; readonly FanCurve curve;
        DateTime lowerSince=DateTime.MinValue; int lowerCandidate; int peak;
        public int Step {get;private set;}
        public int Target {get;private set;}
        public int ZeroLimit {get {return curve.HasZero?curve.ZeroStopTemperature:0;}}
        public FanCurvePolicy(FanCalibration profile,FanCurve curve,int temperature)
        { curve.Validate(profile); this.profile=profile; this.curve=curve; Target=curve.At(temperature); Step=curve.SelectStep(temperature,profile); peak=temperature; }
        public int Observe(FanControlState state,DateTime now)
        {
            if(!state.Manual || state.MaxStep!=profile.MaxStep || !state.Fan1Rpm.HasValue || !state.Fan2Rpm.HasValue) throw new IOException("커브 제어 센서/성능 모드가 달라져 자동 복귀합니다.");
            Target=curve.At(state.TemperatureC);
            if(curve.HasZero && state.Step==0 && state.ZeroLimitC!=curve.ZeroStopTemperature)throw new IOException("0 RPM 설정 응답이 일치하지 않습니다.");
            int desired=curve.SelectStep(state.TemperatureC,profile);
            peak=Math.Max(peak,state.TemperatureC);
            if(desired>Step) {Step=desired; peak=state.TemperatureC; lowerSince=DateTime.MinValue;}
            else if(desired<Step && state.TemperatureC<=peak-3) {
                if(lowerSince==DateTime.MinValue || lowerCandidate!=desired) {lowerSince=now;lowerCandidate=desired;}
                if((now-lowerSince).TotalSeconds>=10) {Step=desired;peak=state.TemperatureC;lowerSince=DateTime.MinValue;}
            } else lowerSince=DateTime.MinValue;
            return Step;
        }
    }
}
