using System;
using System.Diagnostics;
using System.IO;
using System.IO.Compression;
using System.Reflection;
using System.Security.Cryptography;
using System.Security.Principal;
using System.Windows.Forms;
using System.Collections.Generic;
using System.Web.Script.Serialization;
class Bootstrap
{
    [STAThread] static int Main(string[] args)
    {
        Application.EnableVisualStyles();
        try {
            byte[] payload;using(var source=Assembly.GetExecutingAssembly().GetManifestResourceStream("payload.zip"))using(var memory=new MemoryStream()){source.CopyTo(memory);payload=memory.ToArray();}
            string hash;using(var sha=SHA256.Create())hash=BitConverter.ToString(sha.ComputeHash(payload)).Replace("-","");
            using(var archive=new ZipArchive(new MemoryStream(payload),ZipArchiveMode.Read)) {
                var paths=new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                foreach(var entry in archive.Entries)if(!paths.Add(entry.FullName)||entry.FullName.Contains("..")||Path.IsPathRooted(entry.FullName)||entry.FullName.Contains(":"))throw new IOException("Invalid or duplicate package path.");
#if APP_ONLY
                string[] required={"bin/GalaxyHelper.exe","bin/GalaxyHardware.exe","bin/modules/IntelMSR.bin","bin/modules/IntelMCHBAR.bin"};
#else
                string[] required={"Start.ps1","bin/GalaxyHelper.exe","package/GalaxyFanRead.sys"};
#endif
                foreach(string name in required)if(archive.GetEntry(name)==null)throw new IOException("Incomplete package: "+name);
                if(archive.GetEntry("hashes.json")==null)throw new IOException("Missing package hashes.");
                Dictionary<string,string> hashes;using(var reader=new StreamReader(archive.GetEntry("hashes.json").Open()))hashes=new JavaScriptSerializer().Deserialize<Dictionary<string,string>>(reader.ReadToEnd());
                foreach(var entry in archive.Entries)if(!entry.FullName.EndsWith("/")&&entry.FullName!="hashes.json"&&!hashes.ContainsKey(entry.FullName))throw new IOException("Unverified payload: "+entry.FullName);
                foreach(var pair in hashes){var entry=archive.GetEntry(pair.Key);if(entry==null)throw new IOException("Missing payload: "+pair.Key);using(var source=entry.Open())using(var sha=SHA256.Create())if(BitConverter.ToString(sha.ComputeHash(source)).Replace("-","")!=pair.Value)throw new IOException("Payload hash mismatch: "+pair.Key);}
                if(args.Length==2 && args[0]=="--verify-package"){File.WriteAllText(args[1],"Verified embedded package: "+archive.Entries.Count+" entries\r\nSHA256 "+hash);return 0;}
                int currentId=Process.GetCurrentProcess().Id;
                foreach(var process in Process.GetProcessesByName("GalaxyHelper"))using(process)if(process.Id!=currentId){MessageBox.Show("이미 실행 중입니다. 트레이 아이콘을 눌러주세요. 업데이트하려면 기존 앱에서 종료 및 설정 복원을 선택한 뒤 다시 실행하세요.","Galaxy Helper");return 0;}
                if(!new WindowsPrincipal(WindowsIdentity.GetCurrent()).IsInRole(WindowsBuiltInRole.Administrator)){Process.Start(new ProcessStartInfo(Application.ExecutablePath){Verb="runas",UseShellExecute=true});return 0;}
                string root=Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles),"GalaxyHelper","packages",hash.Substring(0,16));
                Directory.CreateDirectory(root);
                foreach(var entry in archive.Entries){if(entry.FullName.EndsWith("/"))continue;string path=Path.Combine(root,entry.FullName.Replace('/',Path.DirectorySeparatorChar));Directory.CreateDirectory(Path.GetDirectoryName(path));using(var source=entry.Open())using(var target=new FileStream(path,FileMode.Create,FileAccess.Write,FileShare.None))source.CopyTo(target);}
#if APP_ONLY
                Process.Start(new ProcessStartInfo(Path.Combine(root,"bin","GalaxyHelper.exe")) {UseShellExecute=true,WorkingDirectory=Path.Combine(root,"bin")});
                return 0;
#else
                using(var p=Process.Start(new ProcessStartInfo(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.System),"WindowsPowerShell","v1.0","powershell.exe"),"-NoProfile -ExecutionPolicy Bypass -File \""+Path.Combine(root,"Start.ps1")+"\""){UseShellExecute=false,CreateNoWindow=true,WorkingDirectory=root})){p.WaitForExit();return p.ExitCode;}
#endif
            }
        } catch(System.ComponentModel.Win32Exception ex){if(ex.NativeErrorCode!=1223)MessageBox.Show(ex.Message,"Galaxy Helper");return 1;}
        catch(Exception ex){if(args.Length==2 && args[0]=="--verify-package")File.WriteAllText(args[1]+".error.txt",ex.Message);else MessageBox.Show(ex.Message,"Galaxy Helper 설치 / 실행");return 1;}
    }
}
