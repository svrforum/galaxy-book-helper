using System;
using System.Collections.Generic;
using System.IO;
using System.Management;
using Microsoft.Win32;
using System.Web.Script.Serialization;

namespace GalaxyHelper
{
    public static class Diagnostics
    {
        static object Query(string query, params string[] fields)
        {
            try
            {
                var rows = new List<Dictionary<string, object>>();
                using (var searcher = new ManagementObjectSearcher(query))
                using (var results = searcher.Get())
                    foreach (ManagementObject item in results)
                    using (item)
                    {
                        var row = new Dictionary<string, object>();
                        foreach (string field in fields) row[field] = item[field];
                        rows.Add(row);
                    }
                return rows;
            }
            catch (Exception ex) { return new { Error = ex.Message }; }
        }
        public static string SamsungStatus()
        {
            using (RegistryKey key = Registry.LocalMachine.OpenSubKey(@"SOFTWARE\Samsung\SamsungSettings\ModulePerformance"))
            {
                if (key == null) return "삼성 성능 모드 정보 없음";
                return "삼성 보고값: " + Convert.ToString(key.GetValue("Value", "미확인")) + "  |  모드 목록: " + Convert.ToString(key.GetValue("ModeList", "미확인"));
            }
        }
        public static string Model()
        {
            try
            {
                using (var searcher = new ManagementObjectSearcher("SELECT Model FROM Win32_ComputerSystem"))
                using (var results = searcher.Get())
                    foreach (ManagementObject item in results)
                    using (item) { return Convert.ToString(item["Model"]); }
            }
            catch { }
            return "기기 정보 조회 불가";
        }
        public static Dictionary<string, object> Collect(IPower power, PowerController controller)
        {
            var result = new Dictionary<string, object>();
            result["AppVersion"] = "0.1.0";
            result["CollectedUtc"] = DateTime.UtcNow.ToString("o");
            result["Computer"] = Query("SELECT Manufacturer, Model FROM Win32_ComputerSystem", "Manufacturer", "Model");
            result["Cpu"] = Query("SELECT Name, NumberOfCores, NumberOfLogicalProcessors FROM Win32_Processor", "Name", "NumberOfCores", "NumberOfLogicalProcessors");
            result["Bios"] = Query("SELECT SMBIOSBIOSVersion FROM Win32_BIOS", "SMBIOSBIOSVersion");
            result["Acpi"] = Query("SELECT Name, DeviceID FROM Win32_PnPEntity WHERE DeviceID LIKE 'ACPI%SAM%' OR DeviceID LIKE 'ACPI%PNP0C0B%'", "Name", "DeviceID");
            result["FanWmi"] = Query("SELECT Name, DesiredSpeed, Status FROM Win32_Fan", "Name", "DesiredSpeed", "Status");
            result["SamsungServices"] = Query("SELECT Name, State FROM Win32_Service WHERE Name LIKE 'Samsung%Support%' OR Name = 'SamsungPlatformEngine'", "Name", "State");
            try { result["SamsungRegistry"] = SamsungStatus(); } catch (Exception ex) { result["SamsungRegistry"] = ex.Message; }
            try { result["WindowsPowerPolicy"] = controller.Capture(power.Active()); } catch (Exception ex) { result["PowerError"] = ex.Message; }
            result["Capabilities"] = new { WindowsCpuPolicy = "Read/write with readback; not watt control", WattLimit = "Unavailable: no verified Panther Lake backend", FanRpmLimit = "Unavailable: no verified Samsung firmware interface", FanRpmSensor = "Unavailable", SamsungModeWrite = "Unavailable: use Samsung Settings" };
            return result;
        }
        public static void Save(string path, object value)
        {
            string parent = Path.GetDirectoryName(Path.GetFullPath(path));
            Directory.CreateDirectory(parent);
            File.WriteAllText(path, new JavaScriptSerializer().Serialize(value), new System.Text.UTF8Encoding(false));
        }
    }
}
