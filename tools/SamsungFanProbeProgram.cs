using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Security.Principal;
using System.Web.Script.Serialization;

static class SamsungFanProbeProgram
{
    static int Main()
    {
        string exe = Process.GetCurrentProcess().MainModule.FileName;
        if (!new WindowsPrincipal(WindowsIdentity.GetCurrent()).IsInRole(WindowsBuiltInRole.Administrator))
        {
            try {
                using (var child = Process.Start(new ProcessStartInfo(exe) { UseShellExecute = true, Verb = "runas", WindowStyle = ProcessWindowStyle.Hidden })) {
                    child.WaitForExit(); return child.ExitCode;
                }
            } catch (Exception ex) { Console.Error.WriteLine(ex.Message); return 1; }
        }
        string output = Path.Combine(Path.GetDirectoryName(exe), "samsung-fan-standalone.json");
        var results = new List<object>();
        int exitCode = 0;
        try {
            string path = SamsungFanReadProbe.FindInterface();
            foreach (string query in new[] {"Support", "Rpm", "MaxStep"}) {
                string result = SamsungFanReadProbe.Read(path, query);
                results.Add(new { Query = query, Result = result });
                if (result.Contains("Error=") || result.StartsWith("InvalidResponse") || result.Contains("Supported=False")) { exitCode = 2; break; }
            }
        } catch (Exception ex) { results.Add(new { Error = ex.ToString() }); exitCode = 1; }
        File.WriteAllText(output, new JavaScriptSerializer().Serialize(new { Time = DateTimeOffset.Now.ToString("o"), SettingsWrites = false, StandaloneProcess = true, Results = results }));
        return exitCode;
    }
}
