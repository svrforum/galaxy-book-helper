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
    static string previousApp;
    static void WaitForParent(string pidText)
    {
        int pid;if(!Int32.TryParse(pidText,out pid)||pid<=0||pid==Process.GetCurrentProcess().Id)throw new IOException("Invalid update process ID.");
        try {using(var parent=Process.GetProcessById(pid))if(!parent.WaitForExit(30000))throw new IOException("기존 앱이 종료되지 않아 업데이트를 중단했습니다.");}
        catch(ArgumentException) { /* The original process already exited. */ }
    }
    static ProcessStartInfo Installer(string script,string arguments,string directory)
    {
        var info=new ProcessStartInfo(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.System),"WindowsPowerShell","v1.0","powershell.exe"),"-NoProfile -ExecutionPolicy Bypass -File \""+script+"\""+arguments){UseShellExecute=false,CreateNoWindow=true,WorkingDirectory=directory};
        // PowerShell 7's inherited module path can hide Windows PowerShell 5.1 modules.
        info.EnvironmentVariables.Remove("PSModulePath");return info;
    }
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
                string[] required={"Start.ps1","bin/GalaxyHelper.exe","bin/GalaxyHardware.exe","bin/modules/IntelMSR.bin","bin/modules/IntelMCHBAR.bin","package/GalaxyFanRead.sys","package/GalaxyFanRead.inf","package/GalaxyFanRead.cat","test-certificate.cer","FIRST-START.ko.md"};
#endif
                foreach(string name in required)if(archive.GetEntry(name)==null)throw new IOException("Incomplete package: "+name);
                if(archive.GetEntry("hashes.json")==null)throw new IOException("Missing package hashes.");
                Dictionary<string,string> hashes;using(var reader=new StreamReader(archive.GetEntry("hashes.json").Open()))hashes=new JavaScriptSerializer().Deserialize<Dictionary<string,string>>(reader.ReadToEnd());
                foreach(var entry in archive.Entries)if(!entry.FullName.EndsWith("/")&&entry.FullName!="hashes.json"&&!hashes.ContainsKey(entry.FullName))throw new IOException("Unverified payload: "+entry.FullName);
                foreach(var pair in hashes){var entry=archive.GetEntry(pair.Key);if(entry==null)throw new IOException("Missing payload: "+pair.Key);using(var source=entry.Open())using(var sha=SHA256.Create())if(BitConverter.ToString(sha.ComputeHash(source)).Replace("-","")!=pair.Value)throw new IOException("Payload hash mismatch: "+pair.Key);}
                if(args.Length==2 && args[0]=="--verify-package"){File.WriteAllText(args[1],"Verified embedded package: "+archive.Entries.Count+" entries\r\nSHA256 "+hash);return 0;}
                if(args.Length==3 && args[0]=="--verify-update-wait"){WaitForParent(args[1]);File.WriteAllText(args[2],"Verified graceful parent exit; no installation or hardware writes.");return 0;}
                if(args.Length==3 && args[0]=="--update-from") {
                    WaitForParent(args[1]);
                    string old=Path.GetFullPath(args[2]);string protectedRoot=Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles),"GalaxyHelper")+Path.DirectorySeparatorChar;
                    if(old.StartsWith(protectedRoot,StringComparison.OrdinalIgnoreCase) && Path.GetFileName(old)=="GalaxyHelper.exe" && File.Exists(old))previousApp=old;
                }
#if !APP_ONLY
                if(args.Length==2 && args[0]=="--verify-installation") {
                    string temporary=Path.Combine(Path.GetTempPath(),"GalaxyHelperInstallCheck-"+Guid.NewGuid().ToString("N"));
                    try {
                        Directory.CreateDirectory(temporary);
                        foreach(var entry in archive.Entries){if(entry.FullName.EndsWith("/"))continue;string path=Path.Combine(temporary,entry.FullName.Replace('/',Path.DirectorySeparatorChar));Directory.CreateDirectory(Path.GetDirectoryName(path));using(var source=entry.Open())using(var target=File.Create(path))source.CopyTo(target);}
                        using(var p=Process.Start(Installer(Path.Combine(temporary,"Start.ps1")," -VerifyReport \""+Path.GetFullPath(args[1])+"\"",temporary))){p.WaitForExit();return p.ExitCode;}
                    } finally {if(Directory.Exists(temporary) && String.Equals(Path.GetDirectoryName(Path.GetFullPath(temporary)),Path.GetFullPath(Path.GetTempPath()).TrimEnd(Path.DirectorySeparatorChar),StringComparison.OrdinalIgnoreCase))Directory.Delete(temporary,true);}
                }
#endif
                if(args.Length!=0 && !(args.Length==3 && args[0]=="--update-from"))throw new IOException("Unknown launch arguments.");
                int currentId=Process.GetCurrentProcess().Id;
                foreach(var process in Process.GetProcessesByName("GalaxyHelper"))using(process)if(process.Id!=currentId){MessageBox.Show("이미 실행 중입니다. 트레이 아이콘을 눌러주세요. 업데이트하려면 기존 앱에서 종료 및 설정 복원을 선택한 뒤 다시 실행하세요.","Galaxy Helper");return 0;}
                if(!new WindowsPrincipal(WindowsIdentity.GetCurrent()).IsInRole(WindowsBuiltInRole.Administrator)){Process.Start(new ProcessStartInfo(Application.ExecutablePath){Verb="runas",UseShellExecute=true});return 0;}
                string root=Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles),"GalaxyHelper","packages",hash.Substring(0,16));
                Directory.CreateDirectory(root);
                foreach(var entry in archive.Entries){if(entry.FullName.EndsWith("/"))continue;string path=Path.Combine(root,entry.FullName.Replace('/',Path.DirectorySeparatorChar));Directory.CreateDirectory(Path.GetDirectoryName(path));using(var source=entry.Open())using(var target=new FileStream(path,FileMode.Create,FileAccess.Write,FileShare.None))source.CopyTo(target);}
#if APP_ONLY
                Process.Start(new ProcessStartInfo(Path.Combine(root,"bin","GalaxyHelper.exe"),args.Length==3 && args[0]=="--update-from"?"--updated":"") {UseShellExecute=true,WorkingDirectory=Path.Combine(root,"bin")});
                return 0;
#else
                using(var p=Process.Start(Installer(Path.Combine(root,"Start.ps1"),"",root))){p.WaitForExit();return p.ExitCode;}
#endif
            }
        } catch(System.ComponentModel.Win32Exception ex){if(ex.NativeErrorCode!=1223)MessageBox.Show(ex.Message,"Galaxy Helper");return 1;}
        catch(Exception ex){if(args.Length==3 && args[0]=="--verify-update-wait")File.WriteAllText(args[2]+".error.txt",ex.Message);else if(args.Length==2 && (args[0]=="--verify-package"||args[0]=="--verify-installation"))File.WriteAllText(args[1]+".error.txt",ex.Message);else {MessageBox.Show(ex.Message,"Galaxy Helper 설치 / 실행");if(previousApp!=null)try{Process.Start(previousApp);}catch{}}return 1;}
    }
}
