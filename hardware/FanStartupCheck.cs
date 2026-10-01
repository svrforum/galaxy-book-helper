using System;
using System.IO;
using System.Collections.Generic;

namespace GalaxyHardware
{
    sealed class FanStartupResult
    {
        internal bool Success,DriverReady,CalibrationUsable;
        internal FanSample Sample;
        internal string Error;
    }
    static class FanStartupCheck
    {
        // Readiness and fresh sensor reads only. This never runs calibration,
        // changes power, or sends a manual fan command.
        internal static FanStartupResult Run(Func<bool> ready,Func<FanSample> read,Func<bool> calibrated)
        {
            var result=new FanStartupResult();
            try {
                result.DriverReady=ready();
                if(!result.DriverReady)throw new IOException("팬 드라이버 준비 대기 · 설치 후 재시작했다면 30초 뒤 다시 확인하세요.");
                result.Sample=read();
                var s=result.Sample;
                if(s==null || !s.Success || !s.FreshRequestCompleted || !s.Fan1Rpm.HasValue || !s.Fan2Rpm.HasValue ||
                    s.Fan1Rpm<0 || s.Fan2Rpm<0 || s.Fan1Rpm>6500 || s.Fan2Rpm>6500)
                    throw new IOException("새 팬 RPM을 확인하지 못했습니다. 새로고침으로 다시 조회하세요.");
                result.CalibrationUsable=calibrated();result.Success=true;
            }catch(Exception ex){result.Error=ex.Message;}
            return result;
        }
        internal static List<string> Tests()
        {
            var cases=new List<string>();bool read=false,calibrated=false;
            var result=Run(delegate{return false;},delegate{read=true;return null;},delegate{calibrated=true;return true;});
            if(result.Success||read||calibrated)throw new InvalidOperationException("Not-ready startup touched sensor or calibration.");
            cases.Add("quick startup exits when driver is unavailable without calibration or fan writes");
            var sample=new FanSample {Success=true,FreshRequestCompleted=true,Fan1Rpm=0,Fan2Rpm=0,SampleUtc=DateTime.UtcNow};
            result=Run(delegate{return true;},delegate{return sample;},delegate{return false;});
            if(!result.Success||result.CalibrationUsable)throw new InvalidOperationException("Missing calibration was treated as verified.");
            cases.Add("quick sensor readiness does not manufacture a verified RPM calibration");
            result=Run(delegate{return true;},delegate{return sample;},delegate{return true;});
            if(!result.Success||!result.CalibrationUsable)throw new InvalidOperationException("Existing calibration was not reused.");
            cases.Add("quick startup reuses valid calibration without measuring fan stages again");
            sample.FreshRequestCompleted=false;result=Run(delegate{return true;},delegate{return sample;},delegate{return true;});
            if(result.Success)throw new InvalidOperationException("Stale RPM accepted during quick startup.");
            cases.Add("quick startup rejects cached RPM instead of reporting readiness success");
            return cases;
        }
    }
}
