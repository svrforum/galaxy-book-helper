using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.IO;
using System.Linq;

namespace GalaxyHelper
{
    static class Tests
    {
        sealed class FakePower : IPower
        {
            public Guid Plan = Guid.NewGuid();
            public Dictionary<string, uint> Values = new Dictionary<string, uint>();
            public int Writes;
            public int FailWrite = -1;
            public bool SwitchOnWrite;
            public bool IgnoreFirstWrite;
            public int Activations;
            public Guid Active() { return Plan; }
            public FakePower()
            {
                foreach (Guid setting in new[] { Settings.Maximum, Settings.Maximum1, Settings.Minimum, Settings.Minimum1, Settings.Boost })
                    foreach (bool ac in new[] { true, false })
                        Values[Key(setting, ac)] = setting == Settings.Boost ? 2u : setting == Settings.Minimum || setting == Settings.Minimum1 ? 5u : 100u;
            }
            public static string Key(Guid setting, bool ac) { return setting.ToString() + ac; }
            public uint Read(Guid plan, Guid setting, bool ac)
            { uint value; if (!Values.TryGetValue(Key(setting, ac), out value)) throw new Win32Exception(2); return value; }
            public void Write(Guid plan, Guid setting, bool ac, uint value)
            {
                Writes++;
                if (Writes == FailWrite) throw new IOException("Injected write failure");
                if (SwitchOnWrite && Writes == 1) Plan = Guid.NewGuid();
                if (IgnoreFirstWrite && Writes == 1) return;
                Values[Key(setting, ac)] = value;
            }
            public void Activate(Guid plan) { if (plan != Plan) throw new Exception("Unexpected plan switch"); Activations++; }
        }
        static void Assert(bool condition, string message) { if (!condition) throw new Exception(message); }
        static void Throws(Action action) { bool thrown = false; try { action(); } catch { thrown = true; } Assert(thrown, "Expected an exception"); }
        public static object Run()
        {
            var results = new List<string>();
            Action<string, Action> run = (name, action) => { action(); results.Add("PASS " + name); };
            string root = Path.Combine(Path.GetTempPath(), "GalaxyHelperTests-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(root);
            try
            {
                run("AC isolated; both CPU classes; persisted original survives repeated apply; exact restore", delegate
                {
                    var p = new FakePower(); var s = new RecoveryStore(Path.Combine(root, "ac")); var c = new PowerController(p, s);
                    c.Apply(p.Plan, true, 80, 0); Assert(p.Read(p.Plan, Settings.Maximum, true) == 80 && p.Read(p.Plan, Settings.Maximum1, true) == 80, "Both classes");
                    Assert(p.Read(p.Plan, Settings.Maximum, false) == 100 && p.Read(p.Plan, Settings.Boost, false) == 2, "DC was modified");
                    c.Apply(p.Plan, true, 60, null); Assert(p.Read(p.Plan, Settings.Boost, true) == 0, "Keep boost");
                    c = new PowerController(p, new RecoveryStore(Path.Combine(root, "ac"))); c.Restore(p.Plan);
                    Assert(p.Read(p.Plan, Settings.Maximum, true) == 100 && p.Read(p.Plan, Settings.Boost, true) == 2 && !s.Exists(p.Plan), "Original restore");
                });
                run("DC applies without altering AC", delegate
                {
                    var p = new FakePower(); var c = new PowerController(p, new RecoveryStore(Path.Combine(root, "dc")));
                    c.Apply(p.Plan, false, 75, 1); Assert(p.Read(p.Plan, Settings.Maximum, false) == 75 && p.Read(p.Plan, Settings.Maximum, true) == 100, "AC isolation");
                });
                run("Out of range / below current minimum / stale plan rejected before writing", delegate
                {
                    var p = new FakePower(); var c = new PowerController(p, new RecoveryStore(Path.Combine(root, "invalid")));
                    Throws(() => c.Apply(p.Plan, true, 0, 0)); Throws(() => c.Apply(p.Plan, true, 101, 0)); Throws(() => c.Apply(p.Plan, true, 4, 0));
                    Throws(() => c.Apply(p.Plan, true, 80, 7)); Throws(() => c.Apply(Guid.NewGuid(), true, 80, 0)); Assert(p.Writes == 0, "Invalid write");
                });
                run("Partial write failure rolls back; durable recovery preserved", delegate
                {
                    var p = new FakePower { FailWrite = 2 }; var s = new RecoveryStore(Path.Combine(root, "rollback")); var c = new PowerController(p, s);
                    Throws(() => c.Apply(p.Plan, true, 70, 0)); Assert(p.Read(p.Plan, Settings.Maximum, true) == 100 && p.Read(p.Plan, Settings.Boost, true) == 2 && s.Exists(p.Plan), "Rollback");
                });
                run("Readback mismatch detected and rolled back", delegate
                {
                    var p = new FakePower { IgnoreFirstWrite = true }; var c = new PowerController(p, new RecoveryStore(Path.Combine(root, "mismatch")));
                    Throws(() => c.Apply(p.Plan, true, 70, 0)); Assert(p.Read(p.Plan, Settings.Maximum1, true) == 100, "Readback rollback");
                });
                run("Concurrent scheme switch does not reactivate obsolete plan", delegate
                {
                    var p = new FakePower { SwitchOnWrite = true }; var c = new PowerController(p, new RecoveryStore(Path.Combine(root, "switch")));
                    Guid old = p.Plan; Throws(() => c.Apply(old, true, 70, 0)); Assert(p.Plan != old && p.Activations == 0 && p.Read(old, Settings.Maximum, true) == 100, "Scheme race");
                });
                run("Missing optional CPU class is tolerated", delegate
                {
                    var p = new FakePower(); p.Values.Remove(FakePower.Key(Settings.Maximum1, true)); p.Values.Remove(FakePower.Key(Settings.Maximum1, false));
                    var c = new PowerController(p, new RecoveryStore(Path.Combine(root, "optional"))); c.Apply(p.Plan, true, 80, 0);
                    Assert(p.Read(p.Plan, Settings.Maximum, true) == 80, "Optional class");
                });
                run("Failed backup write prevents hardware-policy writes", delegate
                {
                    string file = Path.Combine(root, "not-a-directory"); File.WriteAllText(file, "block");
                    var p = new FakePower(); var c = new PowerController(p, new RecoveryStore(file));
                    Throws(() => c.Apply(p.Plan, true, 80, 0)); Assert(p.Writes == 0, "No durable backup");
                });
                run("Malformed recovery rejected before writes", delegate
                {
                    var p = new FakePower(); string dir = Path.Combine(root, "corrupt"); Directory.CreateDirectory(dir);
                    File.WriteAllText(Path.Combine(dir, p.Plan.ToString() + ".json"), "{\"Version\":1,\"Plan\":\"" + p.Plan + "\",\"Values\":[]}");
                    var c = new PowerController(p, new RecoveryStore(dir)); Throws(() => c.Apply(p.Plan, true, 80, 0)); Throws(() => c.Restore(p.Plan)); Assert(p.Writes == 0, "Corrupt backup");
                });
                run("Failed restoration retains original recovery data", delegate
                {
                    var p = new FakePower(); var s = new RecoveryStore(Path.Combine(root, "retry")); var c = new PowerController(p, s); c.Apply(p.Plan, true, 80, 0);
                    p.FailWrite = p.Writes + 2; Throws(() => c.Restore(p.Plan)); Assert(s.Exists(p.Plan), "Deleted failed restore");
                    c.Restore(p.Plan); Assert(!s.Exists(p.Plan) && p.Read(p.Plan, Settings.Maximum, true) == 100, "Restore retry");
                });
                return new { Passed = results.Count, Results = results, TestType = "In-memory backend; no live power changes" };
            }
            finally
            {
                // This directory was uniquely created by this test invocation only.
                foreach (string file in Directory.GetFiles(root, "*", SearchOption.AllDirectories)) File.Delete(file);
                foreach (string dir in Directory.GetDirectories(root).OrderByDescending(s => s.Length)) Directory.Delete(dir);
                Directory.Delete(root);
            }
        }
        public static object Integration(WindowsPower power)
        {
            Guid original = power.Active(); Guid temporary = power.Duplicate(original);
            var results = new List<string>();
            try
            {
                foreach (Guid setting in new[] { Settings.Maximum, Settings.Maximum1, Settings.Boost })
                {
                    foreach (bool ac in new[] { true, false })
                    {
                        uint before = power.Read(temporary, setting, ac);
                        uint desired = setting == Settings.Boost ? 0u : 80u;
                        power.Write(temporary, setting, ac, desired);
                        Assert(power.Read(temporary, setting, ac) == desired, "Native readback failure");
                        power.Write(temporary, setting, ac, before);
                        Assert(power.Read(temporary, setting, ac) == before, "Native restore failure");
                        results.Add("PASS " + setting + (ac ? " AC" : " DC"));
                    }
                }
                Assert(power.Active() == original, "Active scheme changed externally during test");
            }
            finally
            {
                if (power.Active() == temporary) throw new InvalidOperationException("Test scheme unexpectedly became active; preserved: " + temporary);
                power.Delete(temporary);
            }
            return new { Passed = results.Count, Results = results, ActivePlanUnchanged = power.Active() == original, TemporarySchemeDeleted = temporary, Scope = "Native API writes on an inactive duplicate only; no thermal/physical validation" };
        }
    }
}
