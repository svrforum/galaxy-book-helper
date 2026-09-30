using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Management;
using System.Threading;
using Microsoft.Win32;

namespace GalaxyHardware
{
    sealed class FanTrialSample
    {
        public uint Sequence { get; set; }
        public uint State { get; set; }
        public uint ApplyStatus { get; set; }
        public uint RestoreStatus { get; set; }
        public int TemperatureC { get; set; }
        public int? Fan1Rpm { get; set; }
        public int? Fan2Rpm { get; set; }
        public DateTime SampleUtc { get; set; }
        public uint StopReason { get; set; }
        public bool AutoRestored { get { return State == 4 && RestoreStatus == 0; } }
        public bool TrialCompleted { get { return AutoRestored && ApplyStatus == 0 && StopReason == 1; } }
    }
    sealed class FanTrialReport
    {
        public FanSample Before { get; set; }
        public FanSample After { get; set; }
        public List<FanTrialSample> Samples { get; set; }
        public bool Success { get; set; }
        public string Error { get; set; }
    }
    static class FanTrialBridge
    {
        const string KeyPath = @"SYSTEM\CurrentControlSet\Services\GalaxyFanRead\Parameters";
        internal static FanTrialSample Decode(byte[] bytes)
        {
            if (bytes == null || bytes.Length != 56 || BitConverter.ToUInt32(bytes, 0) != 1)
                throw new InvalidDataException("지원하지 않는 팬 시험 결과입니다.");
            uint state = BitConverter.ToUInt32(bytes, 8);
            uint reason = BitConverter.ToUInt32(bytes, 48);
            if (state < 1 || state > 6 || reason > 6) throw new InvalidDataException("팬 시험 상태가 올바르지 않습니다.");
            uint rpm1 = BitConverter.ToUInt32(bytes, 24), rpm2 = BitConverter.ToUInt32(bytes, 28);
            return new FanTrialSample {
                Sequence = BitConverter.ToUInt32(bytes, 4), State = state,
                ApplyStatus = BitConverter.ToUInt32(bytes, 12), RestoreStatus = BitConverter.ToUInt32(bytes, 16),
                TemperatureC = BitConverter.ToInt32(bytes, 20),
                Fan1Rpm = rpm1 == UInt32.MaxValue ? null : (int?)checked((int)rpm1),
                Fan2Rpm = rpm2 == UInt32.MaxValue ? null : (int?)checked((int)rpm2),
                SampleUtc = DateTime.FromFileTimeUtc(BitConverter.ToInt64(bytes, 32)), StopReason = reason
            };
        }
        public static bool IsReady()
        {
            using (var key = Registry.LocalMachine.OpenSubKey(KeyPath))
            {
                if (key == null || !(key.GetValue("FanTrialReadyTime") is long)) return false;
                DateTime boot = ManagementDateTimeConverter.ToDateTime(Program.Boot()).ToUniversalTime();
                return DateTime.FromFileTimeUtc((long)key.GetValue("FanTrialReadyTime")) >= boot;
            }
        }
        public static FanTrialReport Run(Action<FanTrialSample> progress)
        {
            var report = new FanTrialReport { Samples = new List<FanTrialSample>() };
            try
            {
                Program.CheckMachine();
                if (!IsReady()) throw new IOException("시험용 드라이버가 아직 적용되지 않았습니다. 재시작 후 30초 기다려 주세요.");
                report.Before = FanReadBridge.Request();
                if (!report.Before.Success) throw new IOException("팬 RPM 사전 조회에 실패했습니다.");
                using (var key = Registry.LocalMachine.OpenSubKey(KeyPath, true))
                {
                    var previous = key.GetValue("FanTrialResult") as byte[];
                    if (previous != null) {
                        var state = Decode(previous).State;
                        if (state == 1 || state == 2 || state == 3 || state == 5) throw new IOException("이전 팬 시험 또는 복귀가 완료되지 않았습니다.");
                    }
                    int sequence;
                    do { sequence = BitConverter.ToInt32(Guid.NewGuid().ToByteArray(), 0) & Int32.MaxValue; }
                    while (sequence == 0 || sequence == Convert.ToInt32(key.GetValue("FanTrialRequestSequence", 0)));
                    DateTime started = DateTime.UtcNow, seen = DateTime.MinValue;
                    key.SetValue("FanTrialRequestSequence", sequence, RegistryValueKind.DWord);
                    var clock = Stopwatch.StartNew();
                    while (clock.Elapsed.TotalSeconds < 45)
                    {
                        Thread.Sleep(200);
                        if (Convert.ToInt32(key.GetValue("FanTrialRequestSequence", 0)) != sequence) throw new IOException("다른 팬 시험 요청이 발생했습니다. 기존 시험의 자동 복귀는 드라이버가 수행합니다.");
                        var bytes = key.GetValue("FanTrialResult") as byte[];
                        if (bytes == null) continue;
                        var sample = Decode(bytes);
                        if (sample.Sequence != (uint)sequence || sample.SampleUtc < started || sample.SampleUtc == seen) continue;
                        seen = sample.SampleUtc;
                        report.Samples.Add(sample);
                        if (progress != null) progress(sample);
                        if (sample.State == 6) throw new IOException("온도 또는 장치 사전 조건 때문에 팬 시험을 시작하지 않았습니다.");
                        if (!sample.AutoRestored) continue;
                        report.After = FanReadBridge.Request();
                        report.Success = sample.TrialCompleted && report.After.Success;
                        if (!sample.TrialCompleted) report.Error = "시험이 조기 중단됐으며 자동 제어로 복귀했습니다. 중단 사유: " + sample.StopReason;
                        return report;
                    }
                    throw new IOException("자동 복귀 응답을 확인하지 못했습니다. 추가 시험을 중단하고 진단 기록을 확인하세요.");
                }
            }
            catch (Exception ex) { report.Error = ex.Message; return report; }
        }
    }
}
