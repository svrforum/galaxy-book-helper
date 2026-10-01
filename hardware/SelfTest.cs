using System;
using System.Collections.Generic;

namespace GalaxyHardware
{
    static class SelfTest
    {
        static void Assert(bool ok) { if (!ok) throw new Exception("Hardware protocol test failed"); }
        static void Reject(Action action) { bool threw = false; try { action(); } catch { threw = true; } Assert(threw); }
        public static object Run()
        {
            var passed = new List<string>();
            ulong units = 3 | (14UL << 8); // 1/8 W, 1/16384 J -- fixture, not a hardware assumption.
            ulong original = 0x0057800000578000UL | 200UL | (640UL << 32);
            Assert(Rapl.Pl1(original, units) == 25 && Rapl.Pl2(original, units) == 80); passed.Add("unit decoding");
            ulong reduced = Rapl.EncodeReduction(original, units, 15, 25);
            Assert(Rapl.Pl1(reduced, units) == 15 && Rapl.Pl2(reduced, units) == 25); passed.Add("requested watts encoded");
            Assert((reduced & ~Rapl.PowerMask) == (original & ~Rapl.PowerMask)); passed.Add("tau, enable, clamp, reserved bits preserved");
            Reject(() => Rapl.EncodeReduction(original | (1UL << 63), units, 15, 25)); passed.Add("firmware lock rejected");
            Reject(() => Rapl.EncodeReduction(original & ~(1UL << 15), units, 15, 25));
            Reject(() => Rapl.EncodeReduction(original & ~(1UL << 47), units, 15, 25)); passed.Add("disabled limits rejected");
            Reject(() => Rapl.EncodeReduction(original, units, 26, 80));
            Reject(() => Rapl.EncodeReduction(original, units, 25, 81)); passed.Add("increases rejected");
            Reject(() => Rapl.EncodeReduction(original, units, 4, 20));
            Reject(() => Rapl.EncodeReduction(original, units, 20, 15));
            Reject(() => Rapl.EncodeReduction(original, units, Double.NaN, 20));
            Reject(() => Rapl.EncodeReduction(original, units, 15, Double.PositiveInfinity)); passed.Add("invalid request rejected");
            ulong rounded = Rapl.EncodeReduction(original, units, 12.1, 20.1);
            Assert(Rapl.Pl1(rounded, units) <= 12.1 && Rapl.Pl2(rounded, units) <= 20.1); passed.Add("quantization rounds down");
            Assert(Math.Abs(Rapl.Watts(UInt32.MaxValue - 9, 10, units, 1) - 20.0 / 16384) < 1e-10); passed.Add("energy counter wrap handled");
            Reject(() => Rapl.Watts(0, 10, units, 0)); passed.Add("invalid sample time rejected");
            // Actual successful Samsung response captured during the official fan test.
            byte[] fan = {0x43,0x58,0x7A,0,0xAA,0,0,2,0x0A,0x8A,0x0A,0xDA,0x0B,0x54,0x0B,0xB8,0,0,0,0,0};
            var reading = SamsungFanProtocol.ParseRpm(fan);
            Assert(reading.FanCount == 2 && reading.Fan1Rpm == 2698 && reading.Fan2Rpm == 2778);
            passed.Add("Samsung captured RPM reply decoded without percent or x100 conversion");
            Reject(() => SamsungFanProtocol.ParseRpm(new byte[20]));
            Reject(() => SamsungFanProtocol.ParseRpm(new byte[22]));
            byte[] bad = (byte[])fan.Clone(); bad[4] = 0;
            Reject(() => SamsungFanProtocol.ParseRpm(bad));
            bad[4] = 0xAA; bad[2] = 0x2C;
            Reject(() => SamsungFanProtocol.ParseRpm(bad));
            passed.Add("Samsung short, long, incomplete and wrong-command replies rejected");
            bad = (byte[])fan.Clone(); bad[7] = 0;
            Reject(() => SamsungFanProtocol.ParseRpm(bad));
            bad[7] = 3; Reject(() => SamsungFanProtocol.ParseRpm(bad));
            passed.Add("Samsung invalid fan count rejected instead of reporting zero RPM");
            bad[7] = 1;
            Assert(SamsungFanProtocol.ParseRpm(bad).Fan2Rpm == null);
            passed.Add("absent second fan stays unavailable");
            bad = (byte[])fan.Clone(); bad[8] = bad[9] = bad[10] = bad[11] = 0;
            Assert(SamsungFanProtocol.ParseRpm(bad).Fan1Rpm == 0 && SamsungFanProtocol.ParseRpm(bad).Fan2Rpm == 0);
            passed.Add("valid stopped-fan response accepted");
            bad[7] = 6; Assert(SamsungFanProtocol.ParseMaxStep(bad) == 6);
            bad[7] = 3; Assert(SamsungFanProtocol.ParseMaxStep(bad) == 3);
            bad[7] = 7; Reject(() => SamsungFanProtocol.ParseMaxStep(bad));
            passed.Add("observed max-step 3 and 6 accepted; unobserved range rejected");
            var record = new byte[24];
            BitConverter.GetBytes(1U).CopyTo(record, 0);
            BitConverter.GetBytes(3216U).CopyTo(record, 8);
            BitConverter.GetBytes(3330U).CopyTo(record, 12);
            var timestamp = DateTime.UtcNow;
            BitConverter.GetBytes(timestamp.ToFileTimeUtc()).CopyTo(record, 16);
            var sample = FanReadBridge.Decode(record);
            Assert(sample.Success && sample.Fan1Rpm == 3216 && sample.Fan2Rpm == 3330 && sample.SampleUtc == timestamp && !sample.FreshRequestCompleted);
            passed.Add("driver record decoded with timestamp and cached status");
            BitConverter.GetBytes(0xC00000BBU).CopyTo(record, 4);
            sample = FanReadBridge.Decode(record);
            Assert(!sample.Success && sample.Fan1Rpm == null && sample.Fan2Rpm == null);
            Reject(() => FanReadBridge.Decode(new byte[23]));
            record[0] = 2; Reject(() => FanReadBridge.Decode(record));
            passed.Add("failed or malformed driver records never display zero RPM");
            var trial = new byte[56];
            BitConverter.GetBytes(1U).CopyTo(trial, 0);
            BitConverter.GetBytes(42U).CopyTo(trial, 4);
            BitConverter.GetBytes(4U).CopyTo(trial, 8);
            BitConverter.GetBytes(timestamp.ToFileTimeUtc()).CopyTo(trial, 32);
            BitConverter.GetBytes(1U).CopyTo(trial, 48);
            var trialSample = FanTrialBridge.Decode(trial);
            Assert(trialSample.Sequence == 42 && trialSample.AutoRestored && trialSample.TrialCompleted);
            BitConverter.GetBytes(3U).CopyTo(trial, 48);
            Assert(FanTrialBridge.Decode(trial).AutoRestored && !FanTrialBridge.Decode(trial).TrialCompleted);
            BitConverter.GetBytes(5U).CopyTo(trial, 8);
            Assert(!FanTrialBridge.Decode(trial).AutoRestored);
            passed.Add("trial success distinguished from thermal abort and failed restoration");
            BitConverter.GetBytes(0U).CopyTo(trial, 8);
            Reject(() => FanTrialBridge.Decode(trial));
            Reject(() => FanTrialBridge.Decode(new byte[48]));
            passed.Add("invalid or mismatched trial records rejected");
            var request=FanControlClient.Encode(7,42,1,2,timestamp);
            Assert(request.Length==32 && BitConverter.ToUInt64(request,8)==42 && BitConverter.ToUInt32(request,20)==2 && BitConverter.ToInt64(request,24)==timestamp.ToFileTimeUtc());
            Assert(BitConverter.ToUInt32(FanControlClient.Encode(7,42,1,0,timestamp),20)==0);
            Reject(()=>FanControlClient.Encode(7,42,2,4,timestamp));
            Reject(()=>FanControlClient.Encode(7,0,1,2,timestamp));
            passed.Add("control packet layout and step/owner bounds validated");
            var control=new byte[64];
            BitConverter.GetBytes(1U).CopyTo(control,0); BitConverter.GetBytes(1U).CopyTo(control,16); BitConverter.GetBytes(2U).CopyTo(control,20);
            BitConverter.GetBytes(UInt32.MaxValue).CopyTo(control,32); BitConverter.GetBytes(timestamp.ToFileTimeUtc()).CopyTo(control,48);
            Assert(FanControlClient.Decode(control).Manual && FanControlClient.Decode(control).Fan1Rpm==null);
            BitConverter.GetBytes(0xC00000BBU).CopyTo(control,24); Assert(!FanControlClient.Decode(control).Manual);
            Reject(()=>FanControlClient.Decode(new byte[63]));
            passed.Add("manual state requires successful status and valid control record");
            var profile=new FanCalibration { Model="Galaxy Book6 Pro - PAMB",Bios="PAMB.1.5.74.371",CreatedUtc=timestamp,MaxStep=3,Entries=new List<FanCalibrationEntry> {
                new FanCalibrationEntry {Step=1,Fan1Peak=1800,Fan2Peak=1900},
                new FanCalibrationEntry {Step=2,Fan1Peak=2700,Fan2Peak=2800},
                new FanCalibrationEntry {Step=3,Fan1Peak=3500,Fan2Peak=3600}
            } };
            Assert(profile.SelectStep(3000)==2 && profile.SelectStep(2800)==1 && profile.SelectStep(4000)==3);
            Reject(()=>profile.SelectStep(1900));
            profile.Bios="changed"; Reject(()=>profile.SelectStep(3000));
            passed.Add("RPM target selects a measured step with margin and rejects unsupported targets/BIOS");
            profile.Bios="PAMB.1.5.74.371";
            var capPolicy=new RpmCapPolicy(profile,3000,timestamp);
            var hotRpm=new FanControlState {State=1,Status=0,MaxStep=3,Fan1Rpm=3300,Fan2Rpm=3400};
            Assert(capPolicy.Observe(hotRpm,timestamp.AddSeconds(5))==2);
            Assert(capPolicy.Observe(hotRpm,timestamp.AddSeconds(16))==2);
            Assert(capPolicy.Observe(hotRpm,timestamp.AddSeconds(17))==2);
            Assert(capPolicy.Observe(hotRpm,timestamp.AddSeconds(18))==1);
            capPolicy.Observe(hotRpm,timestamp.AddSeconds(34)); capPolicy.Observe(hotRpm,timestamp.AddSeconds(35));
            Reject(()=>capPolicy.Observe(hotRpm,timestamp.AddSeconds(36)));
            passed.Add("RPM feedback waits for settling, lowers after repeated excess, and rejects an unattainable minimum");
            var reports=new List<FanStepReport>();
            for (int step=1;step<=3;step++) {
                var report=new FanStepReport { Step=step,Success=true,Samples=new List<FanControlState>() };
                for (int i=0;i<30;i++) report.Samples.Add(new FanControlState {State=1,Status=0,Step=(uint)step,Fan1Rpm=step*800+700,Fan2Rpm=step*800+800,MaxStep=3,TemperatureC=40});
                reports.Add(report);
            }
            Assert(FanCalibration.FromReports(reports).Entries.Count==3);
            reports[1].Samples[29].Fan1Rpm=6000; Reject(()=>FanCalibration.FromReports(reports));
            reports[1].Samples[29].Fan1Rpm=2300; reports[1].Samples[29].MaxStep=6; Reject(()=>FanCalibration.FromReports(reports));
            passed.Add("calibration rejects unstable RPM and capability changes");
            reports[1].Samples[29].MaxStep=3;
            for (int i=0;i<30;i++) reports[1].Samples[i].Fan1Rpm=2300+i*10;
            Reject(()=>FanCalibration.FromReports(reports));
            passed.Add("calibration rejects a continuing slow ramp across the longer settling window");
            var curve=FanCurve.Default(profile); curve.Validate(profile);
            Assert(curve.At(0)==2000 && curve.At(45)==2450 && curve.At(79)==3700);
            Reject(()=>curve.At(-1)); Reject(()=>curve.At(80));
            curve.Rpms[2]=1900;Reject(()=>curve.Validate(profile));curve=FanCurve.Default(profile);
            passed.Add("fan curve interpolates endpoints and rejects non-monotonic, unsupported and thermal-invalid targets");
            var curvePolicy=new FanCurvePolicy(profile,curve,30);
            var curveSample=new FanControlState {State=1,Status=0,MaxStep=3,Fan1Rpm=2800,Fan2Rpm=2900,TemperatureC=70};
            Assert(curvePolicy.Step==1 && curvePolicy.Observe(curveSample,timestamp)==3);
            curveSample.TemperatureC=69; Assert(curvePolicy.Observe(curveSample,timestamp.AddSeconds(1))==3);
            curveSample.TemperatureC=60; Assert(curvePolicy.Observe(curveSample,timestamp.AddSeconds(2))==3);
            Assert(curvePolicy.Observe(curveSample,timestamp.AddSeconds(11))==3);
            Assert(curvePolicy.Observe(curveSample,timestamp.AddSeconds(12))==2);
            curveSample.TemperatureC=80;Reject(()=>curvePolicy.Observe(curveSample,timestamp.AddSeconds(13)));
            curveSample.TemperatureC=50;curveSample.MaxStep=6;Reject(()=>curvePolicy.Observe(curveSample,timestamp.AddSeconds(14)));
            passed.Add("curve increases immediately, delays cooling by 3 C and 10 seconds, and rejects thermal/capability changes");
            foreach(string name in new[]{"극저소음","최적화","평균","냉각 우선"}) {var preset=FanCurve.Preset(profile,name);preset.Validate(profile);Assert(preset.At(79)==3700);}
            Assert(FanCurve.Preset(profile,"극저소음").At(60)==2000 && FanCurve.Preset(profile,"최적화").At(50)==2000 && FanCurve.Preset(profile,"평균").At(50)==2900 && FanCurve.Preset(profile,"냉각 우선").At(60)==3700);
            Reject(()=>FanCurve.Preset(profile,"unknown"));
            passed.Add("four curve presets stay monotonic, within calibration, and retain maximum cooling at 79 C");
            var editable=FanCurve.Preset(profile,"극저소음");
            editable.EditPoint(1,2900,profile);Assert(editable.Rpms[1]==2900 && editable.Rpms[2]==2900 && editable.Rpms[3]==2900);
            editable.EditPoint(3,2000,profile);Assert(editable.Rpms[1]==2000 && editable.Rpms[2]==2000 && editable.Rpms[3]==2000);
            editable.EditPoint(0,99999,profile);Assert(editable.Rpms[0]==3700 && editable.Rpms[5]==3700);
            editable.EditPoint(5,0,profile);Assert(editable.Rpms[0]==2000 && editable.Rpms[5]==2000);editable.Validate(profile);
            passed.Add("flat curve points can move both ways, adjusting neighbors and preserving supported limits");
            var zero=FanCurve.Preset(profile,"0 RPM");zero.Validate(profile);
            Assert(zero.At(89)==0);Reject(()=>zero.At(90));Reject(()=>zero.At(-1));
            zero.ZeroStopTemperature=45;Assert(zero.At(44)==0);Reject(()=>zero.At(45));
            zero.ZeroStopTemperature=91;Reject(()=>zero.Validate(profile));zero.ZeroStopTemperature=44;Reject(()=>zero.Validate(profile));
            zero.ZeroStopTemperature=90;zero.Rpms[1]=2000;Reject(()=>zero.Validate(profile));zero.Rpms[1]=0;
            var zeroPolicy=new FanCurvePolicy(profile,zero,40);Assert(zeroPolicy.Step==0 && zeroPolicy.ZeroLimit==90);
            var zeroState=new FanControlState {State=1,Status=0,Step=0,ZeroLimitC=90,TemperatureC=89,MaxStep=3,Fan1Rpm=0,Fan2Rpm=0};
            Assert(zeroPolicy.Observe(zeroState,timestamp)==0);zeroState.TemperatureC=90;Reject(()=>zeroPolicy.Observe(zeroState,timestamp));
            zeroState.TemperatureC=40;zeroState.ZeroLimitC=80;Reject(()=>zeroPolicy.Observe(zeroState,timestamp));
            passed.Add("zero preset holds below cutoff, rejects 90C boundary, malformed curves and mismatched driver cutoff");
            byte[] hold=FanControlClient.EncodeZeroHold(1,44,1,90,DateTime.UtcNow);
            Assert(BitConverter.ToUInt32(hold,0)==2 && BitConverter.ToUInt32(hold,20)==(90U<<16));
            Reject(()=>FanControlClient.EncodeZeroHold(1,44,1,91,DateTime.UtcNow));Reject(()=>FanControlClient.EncodeZeroHold(1,44,1,44,DateTime.UtcNow));Reject(()=>FanControlClient.EncodeZeroHold(1,44,0,90,DateTime.UtcNow));
            passed.Add("zero hold protocol is versioned and rejects out-of-range cutoff and Auto encoding");
            var free=FanCurve.Default(profile);int beforeMigration=free.At(45);free.UpgradeForEditing(profile);Assert(free.Rpms.Length==8 && free.At(45)==beforeMigration);
            string signature=free.Signature;free.EditKnot(2,43,1000,profile);Assert(free.Temperatures[2]==43 && free.Rpms[2]==1000 && free.Signature!=signature);
            free.EditKnot(2,89,0,profile);free.Validate(profile);Assert(free.Temperatures[2]==85 && free.Temperatures[7]==90 && free.Rpms[2]==0);
            passed.Add("free XY editor migrates legacy shape, accepts low RPM and adjusts neighbors without crossing temperature bounds");
            var mixed=FanCurve.Preset(profile,"0 RPM");mixed.UpgradeForEditing(profile);mixed.EditKnot(4,60,1000,profile);
            Assert(mixed.At(49)==0 && mixed.At(55)==500 && mixed.SelectStep(55,profile)==1);Reject(()=>mixed.At(80));
            var mixedPolicy=new FanCurvePolicy(profile,mixed,45);
            var mixedState=new FanControlState {State=1,Status=0,Step=0,ZeroLimitC=90,TemperatureC=55,MaxStep=3,Fan1Rpm=0,Fan2Rpm=0};
            Assert(mixedPolicy.Observe(mixedState,timestamp)==1);
            mixedState.Step=1;mixedState.ZeroLimitC=0;mixedState.TemperatureC=45;
            Assert(mixedPolicy.Observe(mixedState,timestamp.AddSeconds(1))==1);
            Assert(mixedPolicy.Observe(mixedState,timestamp.AddSeconds(10))==1);
            Assert(mixedPolicy.Observe(mixedState,timestamp.AddSeconds(11))==0);
            passed.Add("mixed curve starts rotation immediately and returns to zero only after cooling hysteresis; low targets map to supported minimum");
            var random=new Random(42);
            for(int i=0;i<500;i++){free.EditKnot(random.Next(8),random.Next(-50,140),random.Next(-1000,6000),profile);free.Validate(profile);free.ShiftRpm(random.Next(-800,800),profile);free.Validate(profile);}
            passed.Add("500 arbitrary XY edits and whole-curve shifts preserve ordering and ranges");
            passed.AddRange(FanSetupSelfTests.WorkflowCases());
            return new { Passed = passed.Count, Cases = passed, HardwareAccess = false, Scope = "Offline protocol and control-policy checks; no live hardware verification performed by this command" };
        }
    }
}
