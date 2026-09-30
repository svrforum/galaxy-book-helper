using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Web.Script.Serialization;

namespace GalaxyHelper
{
    public static class Settings
    {
        public static readonly Guid Processor = new Guid("54533251-82be-4824-96c1-47b60b740d00");
        public static readonly Guid Maximum = new Guid("bc5038f7-23e0-4960-96da-33abaf5935ec");
        public static readonly Guid Maximum1 = new Guid("bc5038f7-23e0-4960-96da-33abaf5935ed");
        public static readonly Guid Minimum = new Guid("893dee8e-2bef-41e0-89c6-b55d0929964c");
        public static readonly Guid Minimum1 = new Guid("893dee8e-2bef-41e0-89c6-b55d0929964d");
        public static readonly Guid Boost = new Guid("be337238-0d82-4146-a960-4f3749d470c7");
        public static bool Allowed(Guid id) { return id == Maximum || id == Maximum1 || id == Boost; }
    }

    public interface IPower
    {
        Guid Active();
        uint Read(Guid plan, Guid setting, bool ac);
        void Write(Guid plan, Guid setting, bool ac, uint value);
        void Activate(Guid plan);
    }

    public sealed class WindowsPower : IPower
    {
        [DllImport("powrprof.dll")] static extern uint PowerGetActiveScheme(IntPtr root, out IntPtr scheme);
        [DllImport("kernel32.dll")] static extern IntPtr LocalFree(IntPtr memory);
        [DllImport("powrprof.dll")] static extern uint PowerReadACValueIndex(IntPtr root, ref Guid plan, ref Guid group, ref Guid setting, out uint value);
        [DllImport("powrprof.dll")] static extern uint PowerReadDCValueIndex(IntPtr root, ref Guid plan, ref Guid group, ref Guid setting, out uint value);
        [DllImport("powrprof.dll")] static extern uint PowerWriteACValueIndex(IntPtr root, ref Guid plan, ref Guid group, ref Guid setting, uint value);
        [DllImport("powrprof.dll")] static extern uint PowerWriteDCValueIndex(IntPtr root, ref Guid plan, ref Guid group, ref Guid setting, uint value);
        [DllImport("powrprof.dll")] static extern uint PowerSetActiveScheme(IntPtr root, ref Guid plan);
        [DllImport("powrprof.dll")] static extern uint PowerDuplicateScheme(IntPtr root, ref Guid source, out IntPtr target);
        [DllImport("powrprof.dll")] static extern uint PowerDeleteScheme(IntPtr root, ref Guid plan);
        static void Check(uint code) { if (code != 0) throw new Win32Exception((int)code); }
        public Guid Active()
        {
            IntPtr ptr;
            Check(PowerGetActiveScheme(IntPtr.Zero, out ptr));
            try { return (Guid)Marshal.PtrToStructure(ptr, typeof(Guid)); } finally { LocalFree(ptr); }
        }
        public uint Read(Guid plan, Guid setting, bool ac)
        {
            Guid group = Settings.Processor; uint value;
            uint code = ac ? PowerReadACValueIndex(IntPtr.Zero, ref plan, ref group, ref setting, out value)
                : PowerReadDCValueIndex(IntPtr.Zero, ref plan, ref group, ref setting, out value);
            Check(code); return value;
        }
        public void Write(Guid plan, Guid setting, bool ac, uint value)
        {
            if (!Settings.Allowed(setting)) throw new InvalidOperationException("허용되지 않은 전원 설정입니다.");
            if ((setting == Settings.Boost && value > 6) || (setting != Settings.Boost && value > 100))
                throw new ArgumentOutOfRangeException("value");
            Guid group = Settings.Processor;
            Check(ac ? PowerWriteACValueIndex(IntPtr.Zero, ref plan, ref group, ref setting, value)
                : PowerWriteDCValueIndex(IntPtr.Zero, ref plan, ref group, ref setting, value));
        }
        public void Activate(Guid plan) { Check(PowerSetActiveScheme(IntPtr.Zero, ref plan)); }
        public Guid Duplicate(Guid source)
        {
            IntPtr ptr; Check(PowerDuplicateScheme(IntPtr.Zero, ref source, out ptr));
            try { return (Guid)Marshal.PtrToStructure(ptr, typeof(Guid)); } finally { LocalFree(ptr); }
        }
        public void Delete(Guid plan) { Check(PowerDeleteScheme(IntPtr.Zero, ref plan)); }
    }

    public sealed class ValueEntry
    {
        public string Setting { get; set; }
        public bool AC { get; set; }
        public uint Value { get; set; }
    }
    public sealed class Snapshot
    {
        public int Version { get; set; }
        public string Plan { get; set; }
        public string SavedUtc { get; set; }
        public List<ValueEntry> Values { get; set; }
    }
    public sealed class RecoveryStore
    {
        readonly string directory;
        public RecoveryStore(string directory) { this.directory = directory; }
        string PathFor(Guid plan) { return Path.Combine(directory, plan.ToString("D") + ".json"); }
        public bool Exists(Guid plan) { return File.Exists(PathFor(plan)); }
        public void SaveFirst(Snapshot snapshot)
        {
            Guid plan = new Guid(snapshot.Plan);
            if (Exists(plan)) { Load(plan); return; }
            Directory.CreateDirectory(directory);
            string temporary = PathFor(plan) + "." + Guid.NewGuid().ToString("N") + ".tmp";
            try
            {
                byte[] bytes = System.Text.Encoding.UTF8.GetBytes(new JavaScriptSerializer().Serialize(snapshot));
                using (FileStream stream = new FileStream(temporary, FileMode.CreateNew, FileAccess.Write, FileShare.None))
                { stream.Write(bytes, 0, bytes.Length); stream.Flush(true); }
                File.Move(temporary, PathFor(plan));
            }
            finally { if (File.Exists(temporary)) File.Delete(temporary); }
        }
        public Snapshot Load(Guid plan)
        {
            Snapshot value = new JavaScriptSerializer().Deserialize<Snapshot>(File.ReadAllText(PathFor(plan)));
            if (value == null || value.Version != 1 || value.Plan != plan.ToString("D") || value.Values == null || value.Values.Count == 0)
                throw new InvalidDataException("복원 파일 형식이 올바르지 않습니다.");
            HashSet<string> keys = new HashSet<string>();
            foreach (ValueEntry entry in value.Values)
            {
                Guid id;
                if (entry == null || !Guid.TryParse(entry.Setting, out id) || !Settings.Allowed(id) ||
                    !keys.Add(id.ToString() + entry.AC) || entry.Value > (id == Settings.Boost ? 6u : 100u))
                    throw new InvalidDataException("복원 파일에 잘못된 설정이 있습니다.");
            }
            foreach (Guid required in new[] { Settings.Maximum, Settings.Boost })
                foreach (bool ac in new[] { true, false })
                    if (!keys.Contains(required.ToString() + ac)) throw new InvalidDataException("복원 파일에 필수 설정이 없습니다.");
            if (keys.Contains(Settings.Maximum1.ToString() + true) != keys.Contains(Settings.Maximum1.ToString() + false))
                throw new InvalidDataException("클래스 1 복원값이 불완전합니다.");
            return value;
        }
        public void Remove(Guid plan) { File.Delete(PathFor(plan)); }
    }

    public sealed class PowerController
    {
        readonly IPower power;
        readonly RecoveryStore store;
        public PowerController(IPower power, RecoveryStore store) { this.power = power; this.store = store; }
        public Snapshot Capture(Guid plan)
        {
            var snapshot = new Snapshot { Version = 1, Plan = plan.ToString("D"), SavedUtc = DateTime.UtcNow.ToString("o"), Values = new List<ValueEntry>() };
            foreach (Guid setting in new[] { Settings.Maximum, Settings.Boost, Settings.Maximum1 })
            {
                uint ac, dc;
                try { ac = power.Read(plan, setting, true); dc = power.Read(plan, setting, false); }
                catch (Win32Exception ex)
                {
                    if (setting == Settings.Maximum1 && (ex.NativeErrorCode == 2 || ex.NativeErrorCode == 1168)) continue;
                    throw;
                }
                snapshot.Values.Add(new ValueEntry { Setting = setting.ToString("D"), AC = true, Value = ac });
                snapshot.Values.Add(new ValueEntry { Setting = setting.ToString("D"), AC = false, Value = dc });
            }
            return snapshot;
        }
        public void Apply(Guid expectedPlan, bool ac, uint maximum, uint? boost)
        {
            if (maximum < 1 || maximum > 100 || (boost.HasValue && boost.Value > 6)) throw new ArgumentOutOfRangeException("maximum / boost");
            if (power.Active() != expectedPlan) throw new InvalidOperationException("전원 계획이 변경되었습니다. 새로고침 후 다시 적용하세요.");
            Snapshot before = Capture(expectedPlan);
            List<ValueEntry> target = new List<ValueEntry>();
            foreach (ValueEntry entry in before.Values.Where(e => e.AC == ac))
            {
                Guid id = new Guid(entry.Setting);
                if (id == Settings.Boost && !boost.HasValue) continue;
                if (id != Settings.Boost)
                {
                    uint min = power.Read(expectedPlan, id == Settings.Maximum ? Settings.Minimum : Settings.Minimum1, ac);
                    if (maximum < min) throw new InvalidOperationException("상한이 현재 최소 프로세서 상태(" + min + "%)보다 낮습니다.");
                }
                target.Add(new ValueEntry { Setting = entry.Setting, AC = ac, Value = id == Settings.Boost ? boost.Value : maximum });
            }
            store.SaveFirst(before); // Durable original values MUST precede the first write.
            if (power.Active() != expectedPlan) throw new InvalidOperationException("적용 전 전원 계획 변경이 감지되었습니다.");
            try
            {
                WriteVerified(expectedPlan, target);
                if (power.Active() != expectedPlan) throw new InvalidOperationException("적용 중 전원 계획 변경이 감지되었습니다.");
                power.Activate(expectedPlan);
                Verify(expectedPlan, target);
            }
            catch (Exception original)
            {
                // Restore only touched settings; preserve unrelated AC/DC policy changes.
                var rollback = before.Values.Where(e => target.Any(t => t.Setting == e.Setting && t.AC == e.AC)).ToList();
                try { WriteVerified(expectedPlan, rollback); if (power.Active() == expectedPlan) power.Activate(expectedPlan); }
                catch (Exception recovery) { throw new InvalidOperationException("적용과 자동 복원 실패. 복원 파일을 보존했습니다. " + original.Message + " / " + recovery.Message, recovery); }
                throw new InvalidOperationException("적용 실패: 직전 값으로 복원했습니다. " + original.Message, original);
            }
        }
        public void Restore(Guid plan)
        {
            Snapshot original = store.Load(plan);
            WriteVerified(plan, original.Values);
            // Never switch the user to another plan just to restore stored values.
            if (power.Active() == plan) power.Activate(plan);
            Verify(plan, original.Values);
            store.Remove(plan); // Retain on any failure for a later retry.
        }
        void WriteVerified(Guid plan, List<ValueEntry> values)
        {
            foreach (ValueEntry entry in values) power.Write(plan, new Guid(entry.Setting), entry.AC, entry.Value);
            Verify(plan, values);
        }
        void Verify(Guid plan, List<ValueEntry> values)
        {
            foreach (ValueEntry entry in values)
                if (power.Read(plan, new Guid(entry.Setting), entry.AC) != entry.Value)
                    throw new InvalidOperationException("설정 재조회 값이 일치하지 않습니다. 다른 전원 관리 프로그램이 값을 변경했을 수 있습니다.");
        }
    }
}
