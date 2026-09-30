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
        public bool IsZeroHold {get {return Version==2;}}
        public string Signature {get {return Version+":"+ZeroStopTemperature+":"+String.Join(",",Rpms);}}
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
            if((Version!=1 && Version!=2) || Temperatures==null || Rpms==null || Temperatures.Length!=6 || Rpms.Length!=6 || !Temperatures.SequenceEqual(new[]{30,40,50,60,70,79})) throw new InvalidDataException("커브 온도 지점이 올바르지 않습니다.");
            if(IsZeroHold) {if(ZeroStopTemperature<45 || ZeroStopTemperature>90 || Rpms.Any(r=>r!=0))throw new InvalidDataException("0 RPM 해제 온도는 45~90°C입니다.");return;}
            int minimum=Round(profile.Entries[0].ConservativeRpm),maximum=Round(profile.Entries[2].ConservativeRpm);
            for(int i=0;i<6;i++) if(Rpms[i]<minimum || Rpms[i]>maximum || (i>0 && Rpms[i]<Rpms[i-1])) throw new InvalidDataException("커브는 실측 RPM 범위 안에서 온도가 높을수록 같거나 높아야 합니다.");
        }
        internal void EditPoint(int index,int rpm,FanCalibration profile)
        {
            Validate(profile);
            if(IsZeroHold)throw new InvalidOperationException("0 RPM 모드에서는 해제 온도를 조정하세요.");
            if(index<0 || index>=Rpms.Length) throw new ArgumentOutOfRangeException("index");
            int value=Math.Max(Round(profile.Entries[0].ConservativeRpm),Math.Min(Round(profile.Entries[2].ConservativeRpm),rpm));
            Rpms[index]=value;
            for(int i=index-1;i>=0;i--) Rpms[i]=Math.Min(Rpms[i],value);
            for(int i=index+1;i<Rpms.Length;i++) Rpms[i]=Math.Max(Rpms[i],value);
            Validate(profile);
        }
        public int At(int temperature)
        {
            if(temperature<0 || temperature>=(IsZeroHold?ZeroStopTemperature:80)) throw new InvalidDataException("설정 온도에 도달하여 삼성 자동 제어로 복귀합니다.");
            if(IsZeroHold)return 0;
            if(temperature<=Temperatures[0]) return Rpms[0];
            for(int i=1;i<Temperatures.Length;i++) if(temperature<=Temperatures[i]) return (int)Math.Round(Rpms[i-1]+(Rpms[i]-Rpms[i-1])*(temperature-Temperatures[i-1])/(double)(Temperatures[i]-Temperatures[i-1]));
            return Rpms[Rpms.Length-1];
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
        public int ZeroLimit {get {return curve.IsZeroHold?curve.ZeroStopTemperature:0;}}
        public FanCurvePolicy(FanCalibration profile,FanCurve curve,int temperature)
        { curve.Validate(profile); this.profile=profile; this.curve=curve; Target=curve.At(temperature); Step=curve.IsZeroHold?0:profile.SelectStep(Target); peak=temperature; }
        public int Observe(FanControlState state,DateTime now)
        {
            if(!state.Manual || state.MaxStep!=profile.MaxStep || !state.Fan1Rpm.HasValue || !state.Fan2Rpm.HasValue) throw new IOException("커브 제어 센서/성능 모드가 달라져 자동 복귀합니다.");
            Target=curve.At(state.TemperatureC);
            if(curve.IsZeroHold) {if(state.Step!=0 || state.ZeroLimitC!=curve.ZeroStopTemperature)throw new IOException("0 RPM 설정 응답이 일치하지 않습니다.");return 0;}
            int desired=profile.SelectStep(Target);
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
