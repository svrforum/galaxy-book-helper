using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Web.Script.Serialization;

namespace GalaxyHardware
{
    sealed class FanStepReport
    {
        public int Step { get; set; }
        public bool Success { get; set; }
        public bool LeaseExpiryVerified { get; set; }
        public List<FanControlState> Samples { get; set; }
        public FanControlState Restored { get; set; }
        public string Error { get; set; }
    }
    sealed class FanCalibrationEntry
    {
        public int Step { get; set; }
        public int Fan1Peak { get; set; }
        public int Fan2Peak { get; set; }
        public int ConservativeRpm { get { return Math.Max(Fan1Peak,Fan2Peak)+100; } }
    }
    sealed class FanCalibration
    {
        public string Model { get; set; }
        public string Bios { get; set; }
        public DateTime CreatedUtc { get; set; }
        public uint MaxStep { get; set; }
        public bool Verified { get; set; }
        public List<FanCalibrationEntry> Entries { get; set; }
        public static string FilePath { get { return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),"GalaxyHelper","fan-calibration.json"); } }
        public void Validate()
        {
            if (Model != "Galaxy Book6 Pro - PAMB" || Bios != "PAMB.1.5.74.371" || (MaxStep != 3 && MaxStep != 6) || Entries == null || Entries.Count != 3)
                throw new InvalidDataException("팬 보정 파일의 모델/BIOS/단계가 일치하지 않습니다.");
            int previous=0;
            for (int i=0;i<3;i++) {
                var e=Entries[i];
                if (e.Step != i+1 || e.Fan1Peak<500 || e.Fan2Peak<500 || e.Fan1Peak>6500 || e.Fan2Peak>6500 || e.ConservativeRpm<=previous)
                    throw new InvalidDataException("팬 단계별 RPM 보정값을 사용할 수 없습니다.");
                previous=e.ConservativeRpm;
            }
        }
        public int SelectStep(int targetRpm)
        {
            Validate();
            int selected=0;
            foreach (var entry in Entries) if (entry.ConservativeRpm<=targetRpm) selected=entry.Step;
            if (selected==0) throw new InvalidOperationException("목표 RPM이 실측 최소 단계보다 낮습니다. 팬 정지 명령은 사용하지 않습니다.");
            return selected;
        }
        public static FanCalibration Load(bool requireVerified = true)
        {
            if (!File.Exists(FilePath)) throw new IOException("먼저 단계별 RPM 보정 검증이 필요합니다.");
            var value=new JavaScriptSerializer().Deserialize<FanCalibration>(File.ReadAllText(FilePath));
            value.Validate();
            if (requireVerified && !value.Verified) throw new InvalidDataException("RPM 목표 유지와 자동 복귀 검증이 아직 완료되지 않았습니다.");
            if ((DateTime.UtcNow-value.CreatedUtc.ToUniversalTime()).TotalDays>7 || value.CreatedUtc.ToUniversalTime()>DateTime.UtcNow.AddMinutes(1))
                throw new InvalidDataException("RPM 보정 기록이 오래됐습니다. 다시 보정하세요.");
            return value;
        }
        public static FanCalibration FromReports(IList<FanStepReport> reports)
        {
            if (reports == null || reports.Count!=3) throw new InvalidDataException("세 단계 측정이 필요합니다.");
            var calibration=new FanCalibration { Model="Galaxy Book6 Pro - PAMB", Bios="PAMB.1.5.74.371",CreatedUtc=DateTime.UtcNow,Entries=new List<FanCalibrationEntry>() };
            foreach (var report in reports) {
                if (!report.Success) throw new InvalidDataException("실패한 시험을 보정값으로 저장할 수 없습니다.");
                var samples=report.Samples.Where(s=>s.Manual && s.Step==report.Step).ToList();
                if (samples.Count<30) throw new InvalidDataException("안정화 측정 수가 부족합니다.");
                var tail=samples.Skip(samples.Count-20).ToList();
                if (tail.Any(s=>!s.Fan1Rpm.HasValue || !s.Fan2Rpm.HasValue || s.TemperatureC>=75)) throw new InvalidDataException("RPM 보정 센서/온도 조건이 맞지 않습니다.");
                if (calibration.MaxStep==0) calibration.MaxStep=tail[0].MaxStep;
                if (samples.Any(s=>s.MaxStep!=calibration.MaxStep)) throw new InvalidDataException("보정 도중 성능 모드/최대 단계가 바뀌었습니다.");
                if (tail.Max(s=>s.Fan1Rpm.Value)-tail.Min(s=>s.Fan1Rpm.Value)>100 || tail.Max(s=>s.Fan2Rpm.Value)-tail.Min(s=>s.Fan2Rpm.Value)>100)
                    throw new InvalidDataException("팬 RPM이 충분히 안정화되지 않았습니다.");
                calibration.Entries.Add(new FanCalibrationEntry { Step=report.Step,Fan1Peak=tail.Max(s=>s.Fan1Rpm.Value),Fan2Peak=tail.Max(s=>s.Fan2Rpm.Value) });
            }
            calibration.Validate();
            return calibration;
        }
    }
    static class FanControlDiagnostics
    {
        internal static Action TestGuard;
        public static FanStepReport RunStep(int step,int seconds,bool dropRenewal)
        {
            if (step<1 || step>3 || seconds<5 || seconds>120) throw new ArgumentOutOfRangeException("step/seconds");
            var report=new FanStepReport { Step=step,Samples=new List<FanControlState>() };
            using (var client=new FanControlClient()) {
                try {
                    if (TestGuard!=null) TestGuard();
                    report.Samples.Add(client.Start(step));
                    for (int i=0;i<seconds;i++) {
                        Thread.Sleep(1000);
                        if (TestGuard!=null) TestGuard();
                        var observed=FanControlClient.Read();
                        if (!observed.Manual && !dropRenewal) { report.Samples.Add(observed); throw new IOException("Manual control stopped: reason="+observed.StopReason+", temperature="+observed.TemperatureC); }
                        var state=dropRenewal ? FanControlClient.Read() : client.Heartbeat(step);
                        report.Samples.Add(state);
                        if (dropRenewal && state.State==0 && state.StopReason==2 && state.Status==0) { report.LeaseExpiryVerified=true; break; }
                    }
                } catch (Exception ex) { report.Error=ex.Message; }
                finally { if (client.HasRequest) try { report.Restored=client.Restore(); } catch (Exception ex) { report.Error=(report.Error??"")+" Restore: "+ex.Message; } }
            }
            report.Success=report.Error==null && report.Restored!=null && report.Restored.State==0 && report.Restored.Status==0 && (!dropRenewal || report.LeaseExpiryVerified);
            return report;
        }
        public static object Calibrate()
        {
            var reports=new List<FanStepReport>();
            for (int step=1;step<=3;step++) {
                reports.Add(RunStep(step,90,false));
                string folder=Path.GetFullPath(Path.Combine(AppDomain.CurrentDomain.BaseDirectory,"..","artifacts"));
                Directory.CreateDirectory(folder);
                File.WriteAllText(Path.Combine(folder,"fan-calibration-progress.json"),new JavaScriptSerializer().Serialize(reports));
                if (!reports[reports.Count-1].Success) throw new IOException("Calibration step "+step+": "+reports[reports.Count-1].Error);
                Thread.Sleep(2000);
            }
            var calibration=FanCalibration.FromReports(reports);
            Directory.CreateDirectory(Path.GetDirectoryName(FanCalibration.FilePath));
            File.WriteAllText(FanCalibration.FilePath,new JavaScriptSerializer().Serialize(calibration));
            return new { Success=true, Calibration=calibration, Reports=reports, File=FanCalibration.FilePath };
        }
        public static object TestRpmTarget(int target)
        {
            var profile=FanCalibration.Load(false);
            var policy=new RpmCapPolicy(profile,target,DateTime.UtcNow);
            var samples=new List<FanControlState>();
            FanControlState restored=null;
            string error=null;
            profile.Verified=false;
            File.WriteAllText(FanCalibration.FilePath,new JavaScriptSerializer().Serialize(profile));
            using (var client=new FanControlClient()) {
                try {
                    if (TestGuard!=null) TestGuard();
                    samples.Add(client.Start(policy.Step)); policy.MarkStarted(DateTime.UtcNow);
                    for (int i=0;i<120;i++) {
                        Thread.Sleep(1000);
                        if (TestGuard!=null) TestGuard();
                        var state=client.Heartbeat(policy.Step);
                        samples.Add(state); policy.Observe(state,DateTime.UtcNow);
                    }
                } catch (Exception ex) { error=ex.Message; }
                finally { if (client.HasRequest) try { restored=client.Restore(); } catch (Exception ex) { error=(error??"")+" Restore: "+ex.Message; } }
            }
            var tail=samples.Skip(Math.Max(0,samples.Count-30)).ToList();
            bool verified=!policy.Settling(DateTime.UtcNow) && tail.Count==30 && tail.All(s=>s.Manual && s.Step==(uint)policy.Step && s.Fan1Rpm.HasValue && s.Fan2Rpm.HasValue && s.Fan1Rpm<=target+100 && s.Fan2Rpm<=target+100);
            if (verified) verified=tail.Max(s=>s.Fan1Rpm.Value)-tail.Min(s=>s.Fan1Rpm.Value)<=100 && tail.Max(s=>s.Fan2Rpm.Value)-tail.Min(s=>s.Fan2Rpm.Value)<=100;
            bool success=error==null && samples.Count>=121 && verified && restored!=null && restored.State==0 && restored.Status==0;
            profile.Verified=success;
            File.WriteAllText(FanCalibration.FilePath,new JavaScriptSerializer().Serialize(profile));
            return new { Success=success,Error=error,DurationSeconds=120,TargetRpm=target,ToleranceRpm=100,SettledRpmVerified=verified,Samples=samples,Restored=restored };
        }
        public static FanVerificationReport VerifyTargetOnly()
        {
            var report=new FanVerificationReport { Stage="rpm-target" };
            try {
                var profile=FanCalibration.Load(false);
                report.TargetRpm=(int)(Math.Ceiling(profile.Entries[1].ConservativeRpm/100.0)*100);
                SaveProgress(report);
                report.Target=TestRpmTarget(report.TargetRpm);
                var result=new JavaScriptSerializer().Deserialize<Dictionary<string,object>>(new JavaScriptSerializer().Serialize(report.Target));
                report.Success=(bool)result["Success"];
                if (!report.Success) report.Error="RPM target or restoration was not verified.";
                else report.Stage="complete";
            } catch (Exception ex) { report.Error=ex.Message; }
            SaveProgress(report); return report;
        }
        public static FanVerificationReport VerifyAll()
        {
            var report=new FanVerificationReport();
            try {
                report.Stage="step2"; SaveProgress(report);
                report.Step2=RunStep(2,15,false); SaveProgress(report);
                if (!report.Step2.Success) throw new IOException(report.Step2.Error??"Step 2 failed");
                report.Stage="lease"; SaveProgress(report);
                report.Lease=RunStep(2,10,true); SaveProgress(report);
                if (!report.Lease.Success) throw new IOException(report.Lease.Error??"Lease expiry was not verified");
                report.Stage="calibration"; SaveProgress(report);
                report.Calibration=Calibrate(); SaveProgress(report);
                var profile=FanCalibration.Load(false);
                report.TargetRpm=(int)(Math.Ceiling(profile.Entries[1].ConservativeRpm/100.0)*100);
                report.Stage="rpm-target"; SaveProgress(report);
                report.Target=TestRpmTarget(report.TargetRpm);
                var result=new JavaScriptSerializer().Deserialize<Dictionary<string,object>>(new JavaScriptSerializer().Serialize(report.Target));
                if (!(bool)result["Success"]) throw new IOException("RPM target was not verified.");
                report.Success=true; report.Stage="complete";
            } catch (Exception ex) { report.Error=ex.Message; }
            SaveProgress(report); return report;
        }
        static void SaveProgress(FanVerificationReport report)
        {
            string folder=Path.GetFullPath(Path.Combine(AppDomain.CurrentDomain.BaseDirectory,"..","artifacts"));
            Directory.CreateDirectory(folder);
            File.WriteAllText(Path.Combine(folder,"fan-control-live.json"),new JavaScriptSerializer().Serialize(report));
        }
    }
    sealed class FanVerificationReport
    {
        public string Stage { get; set; }
        public bool Success { get; set; }
        public string Error { get; set; }
        public FanStepReport Step2 { get; set; }
        public FanStepReport Lease { get; set; }
        public object Calibration { get; set; }
        public object Target { get; set; }
        public int TargetRpm { get; set; }
    }
}
