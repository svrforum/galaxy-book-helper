using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Management;
using System.Security.Principal;
using System.Text;
using System.Threading;
using System.Web.Script.Serialization;
using Microsoft.Win32;

namespace GalaxyHardware
{
    static class Program
    {
        static string ReadWmi(string type, string field)
        {
            using (var searcher = new ManagementObjectSearcher("SELECT " + field + " FROM " + type))
            using (var values = searcher.Get())
                foreach (ManagementObject value in values) using (value) return Convert.ToString(value[field]);
            throw new InvalidOperationException("Missing hardware identity");
        }
        internal static string Boot() { return ReadWmi("Win32_OperatingSystem", "LastBootUpTime"); }
        internal static void CheckMachine()
        {
            using (var key = Registry.LocalMachine.OpenSubKey(@"HARDWARE\DESCRIPTION\System\CentralProcessor\0"))
            {
                string id = Convert.ToString(key.GetValue("Identifier"));
                string cpu = Convert.ToString(key.GetValue("ProcessorNameString"));
                if (!id.Contains("Family 6 Model 204 ") || !cpu.Contains("358H")) throw new InvalidOperationException("Only verified family 6 model 204 / 358H is enabled.");
            }
            if (ReadWmi("Win32_ComputerSystem", "Model") != "Galaxy Book6 Pro - PAMB" || ReadWmi("Win32_BIOS", "SMBIOSBIOSVersion") != "PAMB.1.5.74.371")
                throw new InvalidOperationException("Model / BIOS changed; firmware offsets must be revalidated.");
        }
        static Dictionary<string, object> Limits(ulong raw, ulong units)
        {
            return new Dictionary<string, object> {
                {"Raw", raw.ToString("X16")}, {"UnitRaw", units.ToString("X16")}, {"PowerUnitWatts", Rapl.PowerUnit(units)},
                {"PL1Watts", Rapl.Pl1(raw, units)}, {"PL2Watts", Rapl.Pl2(raw, units)}, {"Locked", Rapl.Locked(raw)},
                {"PL1Enabled", (raw & (1UL << 15)) != 0}, {"PL2Enabled", (raw & (1UL << 47)) != 0}
            };
        }
        static object Probe()
        {
            CheckMachine();
            var result = new Dictionary<string, object> { { "TimestampUtc", DateTime.UtcNow.ToString("o") }, { "Model", "Galaxy Book6 Pro - PAMB" }, { "WritesPerformed", false } };
            using (var msr = new PawnDevice("IntelMSR"))
            {
                ulong units = msr.ReadMsr(0x606); result["MSR"] = Limits(msr.ReadMsr(0x610), units);
                uint before = (uint)msr.ReadMsr(0x611); var clock = Stopwatch.StartNew(); Thread.Sleep(1000);
                uint after = (uint)msr.ReadMsr(0x611); clock.Stop(); result["PackageWattsSample"] = Rapl.Watts(before, after, units, clock.Elapsed.TotalSeconds);
                try
                {
                    ulong target = msr.ReadMsr(0x1A2), thermal = msr.ReadMsr(0x1B1);
                    result["PackageTemperatureC"] = (thermal & (1UL << 31)) != 0 ? (object)((int)((target >> 16) & 255) - (int)((thermal >> 16) & 127)) : "Sensor not valid";
                }
                catch (Exception ex) { result["TemperatureError"] = ex.Message; }
            }
            try
            {
                using (var mmio = new PawnDevice("IntelMCHBAR"))
                {
                    result["MCHBARBase"] = "0x" + mmio.MchbarBase().ToString("X");
                    result["MMIO"] = Limits(mmio.ReadMchbar(0x59A0), mmio.ReadMchbar(0x5938));
                }
            }
            catch (Exception ex) { result["MMIOError"] = ex.Message; }
            result["FanControl"] = "Samsung ACPI step control with heartbeat lease; RPM targets use measured discrete steps.";
            return result;
        }
        public sealed class Recovery
        {
            public string Boot { get; set; }
            public string Original { get; set; }
            public string Intended { get; set; }
            public string Units { get; set; }
        }
        internal static readonly string Journal = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "GalaxyHelper", "rapl-recovery.json");
        internal static void SaveNewJournal(Recovery value)
        {
            Directory.CreateDirectory(Path.GetDirectoryName(Journal));
            byte[] bytes = Encoding.UTF8.GetBytes(new JavaScriptSerializer().Serialize(value));
            using (var file = new FileStream(Journal, FileMode.CreateNew, FileAccess.Write, FileShare.None)) { file.Write(bytes, 0, bytes.Length); file.Flush(true); }
        }
        internal static void Restore(PawnDevice device)
        {
            var saved = new JavaScriptSerializer().Deserialize<Recovery>(File.ReadAllText(Journal));
            if (saved.Boot != Boot()) throw new InvalidOperationException("Recovery is from another boot. Preserved for review; not applied.");
            ulong original = UInt64.Parse(saved.Original, NumberStyles.HexNumber), intended = UInt64.Parse(saved.Intended, NumberStyles.HexNumber);
            ulong units = UInt64.Parse(saved.Units, NumberStyles.HexNumber);
            if (units != device.ReadMsr(0x606) || (original & ~Rapl.PowerMask) != (intended & ~Rapl.PowerMask)) throw new InvalidDataException("Recovery unit or invariant mismatch");
            ulong current = device.ReadMsr(0x610);
            if ((current & Rapl.PowerMask) == (original & Rapl.PowerMask)) { File.Delete(Journal); return; }
            if ((current & Rapl.PowerMask) != (intended & Rapl.PowerMask)) throw new InvalidOperationException("OEM or another utility changed power fields; automatic overwrite refused. Recovery retained.");
            if (Rapl.Locked(current)) throw new InvalidOperationException("Firmware locked the power register; recovery retained.");
            ulong restored = (current & ~Rapl.PowerMask) | (original & Rapl.PowerMask);
            device.WritePackageLimit(restored);
            if ((device.ReadMsr(0x610) & Rapl.PowerMask) != (original & Rapl.PowerMask)) throw new IOException("Restore readback mismatch; recovery retained.");
            File.Delete(Journal);
        }
        internal static int Temperature(PawnDevice device)
        {
            ulong thermal = device.ReadMsr(0x1B1);
            if ((thermal & (1UL << 31)) == 0) throw new IOException("Package temperature sensor is invalid.");
            int temperature = (int)((device.ReadMsr(0x1A2) >> 16) & 255) - (int)((thermal >> 16) & 127);
            if (temperature < 0 || temperature > 110) throw new IOException("Package temperature outside plausible range.");
            return temperature;
        }
        static object Trial(double pl1, double pl2, int seconds, bool withLoad = false)
        {
            CheckMachine(); if (seconds < 5 || seconds > 300) throw new ArgumentOutOfRangeException("seconds", "Use 5..300 seconds");
            if (File.Exists(Journal)) throw new InvalidOperationException("Previous recovery exists. Run restore first: " + Journal);
            using (var device = new PawnDevice("IntelMSR"))
            {
                ulong units = device.ReadMsr(0x606), original = device.ReadMsr(0x610);
                ulong target = Rapl.EncodeReduction(original, units, pl1, pl2);
                var rows = new List<object>(); bool cancel = false;
                var baseline = new List<object>();
                BoundedLoad load = null;
                ConsoleCancelEventHandler handler = delegate(object s, ConsoleCancelEventArgs e) { e.Cancel = true; cancel = true; };
                Console.CancelKeyPress += handler;
                try
                {
                    if (withLoad)
                    {
                        if (Temperature(device) >= 80) throw new IOException("CPU already warm; load test not started.");
                        load = new BoundedLoad();
                        uint energy = (uint)device.ReadMsr(0x611); var clock = Stopwatch.StartNew();
                        for (int i = 0; i < 5 && !cancel; i++)
                        {
                            Thread.Sleep(1000); uint next = (uint)device.ReadMsr(0x611); double elapsed = clock.Elapsed.TotalSeconds; clock.Restart();
                            int temperature = Temperature(device); if (temperature >= 85) throw new IOException("85 C load cutoff reached.");
                            double watts = Rapl.Watts(energy, next, units, elapsed); energy = next;
                            baseline.Add(new { Second = i + 1, PackageWatts = watts, TemperatureC = temperature });
                            Console.WriteLine("baseline {0}s  package={1:F2} W  temperature={2} C", i + 1, watts, temperature);
                        }
                        if (cancel) throw new OperationCanceledException();
                    }
                    SaveNewJournal(new Recovery { Boot = Boot(), Original = original.ToString("X16"), Intended = target.ToString("X16"), Units = units.ToString("X16") });
                    try
                    {
                        if (device.ReadMsr(0x610) != original) throw new IOException("OEM changed register before apply.");
                        device.WritePackageLimit(target);
                        if (device.ReadMsr(0x610) != target) throw new IOException("Write readback mismatch; an unlocked MSR does not guarantee effective control.");
                        uint energy = (uint)device.ReadMsr(0x611); var clock = Stopwatch.StartNew();
                        for (int i = 0; i < seconds && !cancel; i++)
                        {
                            Thread.Sleep(1000); uint next = (uint)device.ReadMsr(0x611); double elapsed = clock.Elapsed.TotalSeconds; clock.Restart();
                            ulong now = device.ReadMsr(0x610); double watts = Rapl.Watts(energy, next, units, elapsed); energy = next;
                            int temperature = Temperature(device);
                            rows.Add(new { Second = i + 1, PackageWatts = watts, TemperatureC = temperature, PowerRaw = now.ToString("X16") });
                            Console.WriteLine("{0}s  package={1:F2} W  PL1={2:F2} W  PL2={3:F2} W", i + 1, watts, Rapl.Pl1(now, units), Rapl.Pl2(now, units));
                            if ((now & Rapl.PowerMask) != (target & Rapl.PowerMask)) throw new IOException("OEM rewrote power limits during trial.");
                            if (withLoad && temperature >= 85) throw new IOException("85 C load cutoff reached.");
                        }
                    }
                    finally { if (load != null) { load.Dispose(); load = null; } Restore(device); }
                }
                finally { if (load != null) load.Dispose(); Console.CancelKeyPress -= handler; }
                return new { Original = Limits(original, units), Requested = Limits(target, units), Baseline = baseline, Samples = rows, Restored = true, WithCpuLoad = withLoad, Scope = "MSR-only experiment; does not write MMIO or fan controls." };
            }
        }
        internal static FanSetupResult VerifyFanWithReducedPower(bool targetOnly=false,bool zeroTrial=false,FanDiagnosticSession session=null)
        {
            CheckMachine();
            if(File.Exists(Journal))throw new IOException("먼저 이전 전력 설정을 복원하세요.");
            if(!FanControlClient.IsReady())throw new IOException("팬 드라이버 적용 대기입니다. Windows 재시작 후 30초 기다려 주세요.");
            if(zeroTrial && !FanControlClient.IsZeroReady())throw new IOException("0 RPM 검증 드라이버가 준비되지 않았습니다.");
            session=session??new FanDiagnosticSession(CancellationToken.None);
            session.DeferVerification=!zeroTrial;
            session.Check();
            using(var device=new PawnDevice("IntelMSR")) {
                ulong units=device.ReadMsr(0x606),original=device.ReadMsr(0x610);
                double pl1=Math.Min(zeroTrial?5:10,Rapl.Pl1(original,units)),pl2=Math.Min(zeroTrial?5:10,Rapl.Pl2(original,units));
                ulong target=Rapl.EncodeReduction(original,units,pl1,pl2);
                var power=new List<object>();bool journalCreated=false;
                var result=FanSetupWorkflow.Run(session,delegate {
                    session.Report("임시 전력 제한 적용",0);
                    SaveNewJournal(new Recovery {Boot=Boot(),Original=original.ToString("X16"),Intended=target.ToString("X16"),Units=units.ToString("X16")});
                    journalCreated=true;
                    if(device.ReadMsr(0x610)!=original)throw new IOException("적용 직전 전력 설정이 변경됐습니다.");
                    device.WritePackageLimit(target);
                    if(device.ReadMsr(0x610)!=target)throw new IOException("전력 설정 재조회 불일치");
                },delegate {
                    uint energy=(uint)device.ReadMsr(0x611);var clock=Stopwatch.StartNew();
                    FanControlDiagnostics.TestGuard=delegate {
                        session.Check();
                        if((device.ReadMsr(0x610)&Rapl.PowerMask)!=(target&Rapl.PowerMask))throw new IOException("검증 중 전력 제한이 변경됐습니다.");
                        if(clock.Elapsed.TotalSeconds>=0.25) {
                            uint next=(uint)device.ReadMsr(0x611);double elapsed=clock.Elapsed.TotalSeconds;
                            power.Add(new {TimestampUtc=DateTime.UtcNow,PackageWatts=Rapl.Watts(energy,next,units,elapsed),TemperatureC=Temperature(device)});
                            energy=next;clock.Restart();
                        }
                    };
                    try {
                        if(!zeroTrial) {
                            session.Begin("CPU가 60°C 이하로 식기를 기다리는 중",0,15);
                            var cooling=Stopwatch.StartNew();
                            while(Temperature(device)>60 && cooling.Elapsed.TotalSeconds<90) {
                                session.Wait(1000);FanControlDiagnostics.TestGuard();session.Advance((int)cooling.Elapsed.TotalSeconds,90);
                            }
                            if(Temperature(device)>60)throw new IOException("CPU가 60°C 이하로 식지 않았습니다. 부하 작업을 닫고 다시 시작하세요.");
                        }
                        return zeroTrial?ZeroFanDiagnostics.Verify():(targetOnly?FanControlDiagnostics.VerifyTargetOnly(session):FanControlDiagnostics.VerifyAll(session));
                    } finally { FanControlDiagnostics.TestGuard=null; }
                },delegate {
                    if(journalCreated)Restore(device);
                });
                result.OriginalPower=Limits(original,units);result.VerificationPower=Limits(target,units);result.PowerSamples=power;
                if(result.Success && !zeroTrial) {
                    try {
                        var profile=FanCalibration.Load(false);profile.Verified=true;
                        File.WriteAllText(FanCalibration.FilePath,new JavaScriptSerializer().Serialize(profile));
                    } catch(Exception ex) { result.Success=false;result.Error="보정 결과 저장 실패: "+ex.Message; }
                }
                return result;
            }
        }
        static int Main(string[] args)
        {
            Console.OutputEncoding = Encoding.UTF8;
            // Offline tests do not contend with an active hardware UI session.
            if (args.Length == 1 && args[0] == "self-test") {
                try { Console.WriteLine(new JavaScriptSerializer().Serialize(SelfTest.Run())); return 0; }
                catch (Exception ex) { Console.Error.WriteLine(ex.Message); return 1; }
            }
            bool acquired;
            using (var gate = new Mutex(true, "Global\\GalaxyHelper-Rapl", out acquired))
            {
                if (!acquired) { Console.Error.WriteLine("Another hardware session is active."); return 2; }
                try
                {
                    object report;
                    if (args.Length == 1 && args[0] == "self-test") { report = SelfTest.Run(); }
                    else
                    {
                        if (!(new WindowsPrincipal(WindowsIdentity.GetCurrent())).IsInRole(WindowsBuiltInRole.Administrator)) throw new UnauthorizedAccessException("Administrator rights required for hardware access. No hardware values changed.");
                        if (args.Length == 1 && args[0] == "fan-verify-cool") report = VerifyFanWithReducedPower();
                        else if (args.Length == 1 && args[0] == "fan-verify-target-cool") report = VerifyFanWithReducedPower(true);
                        else if (args.Length == 1 && args[0] == "fan-zero-verify") report = VerifyFanWithReducedPower(false,true);
                        else if (args.Length == 2 && args[0] == "fan-rpm-trial") report = FanControlDiagnostics.TestRpmTarget(Int32.Parse(args[1]));
                        else if (args.Length == 1 && args[0] == "fan-calibrate") report = FanControlDiagnostics.Calibrate();
                        else if (args.Length == 1 && args[0] == "fan-lease-test") report = FanControlDiagnostics.RunStep(2,10,true);
                        else if (args.Length == 3 && args[0] == "fan-control-trial") report = FanControlDiagnostics.RunStep(Int32.Parse(args[1]),Int32.Parse(args[2]),false);
                        else if (args.Length == 1 && args[0] == "fan-read") report = FanReadBridge.Request();
                        else if (args.Length == 1 && args[0] == "probe") report = Probe();
                        else if (args.Length == 1 && args[0] == "load-trial") report = Trial(15, 20, 15, true);
                        else if (args.Length == 3 && args[0] == "load-trial") report = Trial(Double.Parse(args[1], CultureInfo.InvariantCulture), Double.Parse(args[2], CultureInfo.InvariantCulture), 15, true);
                        else if (args.Length == 4 && args[0] == "trial-power") report = Trial(Double.Parse(args[1], CultureInfo.InvariantCulture), Double.Parse(args[2], CultureInfo.InvariantCulture), Int32.Parse(args[3], CultureInfo.InvariantCulture));
                        else if (args.Length == 1 && args[0] == "restore") { CheckMachine(); using (var device = new PawnDevice("IntelMSR")) Restore(device); report = new { Restored = true }; }
                        else throw new ArgumentException("Commands: probe | fan-read | trial-power <PL1 W> <PL2 W> <5..300 seconds> | restore | self-test");
                    }
                    Console.WriteLine(new JavaScriptSerializer().Serialize(report)); return 0;
                }
                catch (Exception ex) { Console.Error.WriteLine(ex.ToString()); return 1; }
                finally { gate.ReleaseMutex(); }
            }
        }
    }
}
