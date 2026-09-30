using System;
using System.IO;
using System.Threading;
using Microsoft.Win32;

namespace GalaxyHardware
{
    sealed class FanSample
    {
        public bool Success { get; set; }
        public string NtStatus { get; set; }
        public int? Fan1Rpm { get; set; }
        public int? Fan2Rpm { get; set; }
        public DateTime SampleUtc { get; set; }
        public bool FreshRequestCompleted { get; set; }
    }
    static class FanReadBridge
    {
        const string KeyPath = @"SYSTEM\CurrentControlSet\Services\GalaxyFanRead\Parameters";
        internal static FanSample Decode(byte[] record)
        {
            if (record == null || record.Length != 24 || BitConverter.ToUInt32(record, 0) != 1)
                throw new InvalidDataException("팬 드라이버 측정 기록이 없거나 형식이 다릅니다.");
            uint status = BitConverter.ToUInt32(record, 4);
            return new FanSample {
                Success = status == 0, NtStatus = "0x" + status.ToString("X8"),
                Fan1Rpm = status == 0 ? (int?)checked((int)BitConverter.ToUInt32(record, 8)) : null,
                Fan2Rpm = status == 0 ? (int?)checked((int)BitConverter.ToUInt32(record, 12)) : null,
                SampleUtc = DateTime.FromFileTimeUtc(BitConverter.ToInt64(record, 16))
            };
        }
        public static FanSample Cached()
        {
            using (var key = Registry.LocalMachine.OpenSubKey(KeyPath))
            {
                if (key == null) throw new IOException("팬 읽기 드라이버가 설치되지 않았습니다.");
                return Decode(key.GetValue("LastProbe") as byte[]);
            }
        }
        public static FanSample Request()
        {
            // A non-persistent, fixed-read request. This never accepts a fan command.
            using (var key = Registry.LocalMachine.OpenSubKey(KeyPath, true))
            {
                if (key == null || key.GetValue("ProbeCompletedSequence") == null)
                    throw new IOException("팬 읽기 드라이버 초기화가 끝나지 않았습니다.");
                int sequence;
                do { sequence = BitConverter.ToInt32(Guid.NewGuid().ToByteArray(), 0) & Int32.MaxValue; }
                while (sequence == 0 || sequence == Convert.ToInt32(key.GetValue("ProbeRequestSequence", 0)) || sequence == Convert.ToInt32(key.GetValue("ProbeCompletedSequence", 0)));
                DateTime started = DateTime.UtcNow;
                key.SetValue("ProbeRequestSequence", sequence, RegistryValueKind.DWord);
                var clock = System.Diagnostics.Stopwatch.StartNew();
                while (clock.Elapsed.TotalSeconds < 15)
                {
                    Thread.Sleep(100);
                    if (Convert.ToInt32(key.GetValue("ProbeRequestSequence", 0)) != sequence)
                        throw new IOException("다른 조회 요청이 발생했습니다. 다시 조회하세요.");
                    if (Convert.ToInt32(key.GetValue("ProbeCompletedSequence", 0)) != sequence) continue;
                    var sample = Decode(key.GetValue("LastProbe") as byte[]);
                    if (sample.SampleUtc < started) throw new IOException("이전 측정값이므로 새 결과로 사용할 수 없습니다.");
                    sample.FreshRequestCompleted = true;
                    return sample;
                }
                throw new TimeoutException("새 팬 측정이 완료되지 않았습니다. 이전 값을 현재 RPM으로 표시하지 않습니다.");
            }
        }
    }
}
