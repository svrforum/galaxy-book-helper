using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Web.Script.Serialization;

namespace GalaxyHardware
{
    sealed class NamedFanPreset
    {
        public string Name {get;set;}
        public FanCurve Curve {get;set;}
    }
    sealed class FanPresetStore
    {
        public int Version {get;set;}
        public List<NamedFanPreset> Items {get;set;}
        internal static readonly string[] Builtins={"0 RPM","극저소음","최적화","평균","냉각 우선"};
        internal static string FilePath {get{return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),"GalaxyHelper","fan-presets.json");}}
        internal static FanCurve Copy(FanCurve curve)
        {return new FanCurve {Version=curve.Version,Temperatures=(int[])curve.Temperatures.Clone(),Rpms=(int[])curve.Rpms.Clone(),ZeroStopTemperature=curve.ZeroStopTemperature};}
        internal static FanPresetStore Load(string path,FanCalibration profile)
        {
            if(!File.Exists(path))return new FanPresetStore {Version=1,Items=new List<NamedFanPreset>()};
            var store=new JavaScriptSerializer().Deserialize<FanPresetStore>(File.ReadAllText(path));
            if(store==null || store.Version!=1 || store.Items==null || store.Items.Count>100)throw new InvalidDataException("사용자 프리셋 파일 형식이 올바르지 않습니다.");
            var names=new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach(var item in store.Items){if(item==null || item.Curve==null || CheckName(item.Name)!=item.Name || !names.Add(item.Name))throw new InvalidDataException("프리셋 이름 또는 커브가 올바르지 않습니다.");item.Curve.Validate(profile);}
            return store;
        }
        internal static string CheckName(string name)
        {
            name=(name??"").Trim().Normalize();
            if(name.Length==0 || name.Length>40 || name.Any(Char.IsControl) || Builtins.Contains(name,StringComparer.OrdinalIgnoreCase))throw new ArgumentException("기본 프리셋과 다른 이름을 1~40자로 입력하세요.");
            return name;
        }
        internal void Put(string name,FanCurve curve,FanCalibration profile)
        {
            name=CheckName(name);curve.Validate(profile);
            var found=Items.FirstOrDefault(p=>String.Equals(p.Name,name,StringComparison.OrdinalIgnoreCase));
            if(found!=null){found.Name=name;found.Curve=Copy(curve);}
            else {if(Items.Count>=100)throw new InvalidDataException("프리셋은 최대 100개입니다.");Items.Add(new NamedFanPreset {Name=name,Curve=Copy(curve)});}
        }
        internal void Save(string path)
        {
            Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(path)));
            string temp=path+"."+Guid.NewGuid().ToString("N")+".tmp";
            try {File.WriteAllText(temp,new JavaScriptSerializer().Serialize(this),new UTF8Encoding(false));if(File.Exists(path))File.Replace(temp,path,path+".bak");else File.Move(temp,path);}
            finally {if(File.Exists(temp))File.Delete(temp);}
        }
    }
}
