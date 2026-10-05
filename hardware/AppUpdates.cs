using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Net;
using System.Security.Cryptography;
using System.Text.RegularExpressions;
using System.Web.Script.Serialization;
using System.Windows.Forms;
using Microsoft.Win32;

namespace GalaxyHardware
{
    static class AppUpdates
    {
        internal const string Current = "v0.2.4-experimental";
        const string DownloadRoot = "https://github.com/svrforum/galaxy-book-helper/releases/download/";
        internal sealed class Asset { public string name, browser_download_url, digest; public long size; }
        internal sealed class Release { public string tag_name; public bool draft; public Asset[] assets; }
        internal sealed class Candidate { public string Tag, Url, Hash; public long Size; }
        internal static Version VersionOf(string tag)
        {
            if(tag==null || !Regex.IsMatch(tag,@"\Av\d+\.\d+\.\d+-experimental\z"))return null;
            Version value;return Version.TryParse(tag.Substring(1,tag.Length-14),out value)?value:null;
        }
        internal static Candidate Select(string json,string current)
        {
            var releases=new JavaScriptSerializer().Deserialize<Release[]>(json);
            Version best=VersionOf(current);if(best==null)throw new IOException("현재 버전 정보 오류");
            Candidate selected=null;
            foreach(var release in releases) {
                var version=VersionOf(release.tag_name);
                if(release.draft || version==null || version<=best || release.assets==null)continue;
                string name="GalaxyHelper-"+release.tag_name+"-windows-x64.exe";
                foreach(var asset in release.assets) {
                    if(asset.name!=name || asset.browser_download_url!=DownloadRoot+release.tag_name+"/"+name || asset.size<1024 || asset.size>100*1024*1024 || asset.digest==null || !Regex.IsMatch(asset.digest,@"\Asha256:[a-fA-F0-9]{64}\z"))continue;
                    selected=new Candidate {Tag=release.tag_name,Url=asset.browser_download_url,Hash=asset.digest.Substring(7),Size=asset.size};best=version;break;
                }
            }
            return selected;
        }
        sealed class Client : WebClient
        {
            protected override WebRequest GetWebRequest(Uri address) {var r=base.GetWebRequest(address);r.Timeout=60000;return r;}
        }
        internal static Candidate Check()
        {
            ServicePointManager.SecurityProtocol|=SecurityProtocolType.Tls12;
            using(var client=new Client()) {client.Headers[HttpRequestHeader.UserAgent]="GalaxyHelper/"+Current;return Select(client.DownloadString("https://api.github.com/repos/svrforum/galaxy-book-helper/releases?per_page=100"),Current);}
        }
        internal static void VerifyFile(string path,Candidate candidate)
        {
            if(new FileInfo(path).Length!=candidate.Size)throw new IOException("업데이트 파일 크기가 일치하지 않습니다.");
            using(var file=File.OpenRead(path))using(var sha=SHA256.Create())if(!String.Equals(BitConverter.ToString(sha.ComputeHash(file)).Replace("-",""),candidate.Hash,StringComparison.OrdinalIgnoreCase))throw new IOException("업데이트 SHA256 검증 실패");
        }
        internal static string Download(Candidate candidate)
        {
            // Protected by inherited Program Files ACLs, including when the app runs elevated.
            string folder=Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles),"GalaxyHelper","updates",Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(folder);string path=Path.Combine(folder,"GalaxyHelperUpdate.exe");
            try {
                using(var client=new Client()){client.Headers[HttpRequestHeader.UserAgent]="GalaxyHelper/"+Current;client.DownloadFile(candidate.Url,path);}
                VerifyFile(path,candidate);
                string report=Path.Combine(folder,"package-check.txt");
                using(var process=Process.Start(new ProcessStartInfo(path,"--verify-package \""+report+"\""){UseShellExecute=false,CreateNoWindow=true})) {
                    if(!process.WaitForExit(30000))throw new IOException("업데이트 패키지 검사 시간 초과");
                    if(process.ExitCode!=0 || !File.Exists(report))throw new IOException("업데이트 패키지 검사 실패");
                }
                return path;
            } catch {try{Directory.Delete(folder,true);}catch{}throw;}
        }
        internal static bool Enabled
        {
            get {using(var key=Registry.CurrentUser.OpenSubKey(@"Software\GalaxyHelper"))return key==null || Convert.ToInt32(key.GetValue("AutoUpdate",1))!=0;}
            set {using(var key=Registry.CurrentUser.CreateSubKey(@"Software\GalaxyHelper"))key.SetValue("AutoUpdate",value?1:0,RegistryValueKind.DWord);}
        }
        internal static IEnumerable<string> Tests()
        {
            if(VersionOf(Current)!=new Version(0,2,4) || VersionOf("v0.2.4-experimental/junk")!=null)throw new Exception("Version parser failed");
            yield return "update version parser rejects malformed tags";
            string name="GalaxyHelper-v0.2.5-experimental-windows-x64.exe";
            var asset=new Asset {name=name,browser_download_url=DownloadRoot+"v0.2.5-experimental/"+name,size=2048,digest="sha256:"+new string('a',64)};
            var release=new Release {tag_name="v0.2.5-experimental",assets=new[]{asset}};var serializer=new JavaScriptSerializer();
            Func<string> json=()=>serializer.Serialize(new[]{release});
            if(Select(json(),Current)==null || Select(json(),"v0.2.5-experimental")!=null)throw new Exception("Update ordering failed");
            release.draft=true;if(Select(json(),Current)!=null)throw new Exception("Draft update accepted");release.draft=false;
            asset.browser_download_url="https://example.com/evil.exe";if(Select(json(),Current)!=null)throw new Exception("External update accepted");
            asset.browser_download_url=DownloadRoot+release.tag_name+"/"+name;asset.digest=null;if(Select(json(),Current)!=null)throw new Exception("Missing hash accepted");
            yield return "updates reject drafts, old versions, foreign URLs and missing hashes";
            string temp=Path.GetTempFileName();try {
                File.WriteAllBytes(temp,new byte[]{1,2,3});string hash;using(var sha=SHA256.Create())hash=BitConverter.ToString(sha.ComputeHash(File.ReadAllBytes(temp))).Replace("-","");
                var c=new Candidate {Size=3,Hash=hash};VerifyFile(temp,c);File.WriteAllBytes(temp,new byte[]{1,2,4});bool rejected=false;try{VerifyFile(temp,c);}catch(IOException){rejected=true;}if(!rejected)throw new Exception("Tampered update accepted");
            } finally{File.Delete(temp);}
            yield return "downloaded update verifies size and SHA256, rejects tampering";
        }
    }

    sealed partial class ControlForm
    {
        readonly System.Windows.Forms.Timer updateTimer=new System.Windows.Forms.Timer();
        DateTime nextUpdate=DateTime.UtcNow.AddSeconds(10);
        bool updateBusy;
        AppUpdates.Candidate availableUpdate;
        string preparedUpdate;
        void SetupUpdates()
        {
            updateTimer.Interval=10000;
            updateTimer.Tick+=delegate {
                if(smokeMode || updateBusy || setupRunning)return;
                if(preparedUpdate!=null){if(AppUpdates.Enabled && !Visible)InstallUpdate();return;}
                if(AppUpdates.Enabled && DateTime.UtcNow>=nextUpdate)CheckUpdate(false);
            };
            Shown+=delegate {if(!smokeMode)updateTimer.Start();};
        }
        void AddUpdateMenu()
        {
            var toggle=new ToolStripMenuItem("자동 업데이트") {Checked=AppUpdates.Enabled};
            toggle.Click+=delegate {AppUpdates.Enabled=!AppUpdates.Enabled;nextUpdate=DateTime.UtcNow;};quickMenu.Items.Add(toggle);
            var item=new ToolStripMenuItem(preparedUpdate!=null?availableUpdate.Tag+" 설치 및 재실행":updateBusy?"업데이트 확인 / 다운로드 중…":"업데이트 확인 · "+AppUpdates.Current);
            item.Enabled=!updateBusy;item.Click+=delegate {if(preparedUpdate!=null)InstallUpdate();else CheckUpdate(true);};quickMenu.Items.Add(item);
        }
        void CheckUpdate(bool manual)
        {
            if(updateBusy)return;updateBusy=true;nextUpdate=DateTime.UtcNow.AddHours(6);
            var worker=new BackgroundWorker();worker.DoWork+=delegate(object s,DoWorkEventArgs e) {
                var candidate=AppUpdates.Check();if(candidate!=null)e.Result=new object[]{candidate,AppUpdates.Download(candidate)};
            };
            worker.RunWorkerCompleted+=delegate(object s,RunWorkerCompletedEventArgs e) {
                updateBusy=false;worker.Dispose();if(IsDisposed || Disposing)return;
                if(e.Error!=null){if(manual)NotifyError("업데이트 실패 · 현재 버전 유지: "+e.Error.Message);return;}
                if(e.Result==null){if(manual)tray.ShowBalloonTip(3000,"Galaxy Helper","최신 버전입니다 · "+AppUpdates.Current,ToolTipIcon.Info);return;}
                var result=(object[])e.Result;availableUpdate=(AppUpdates.Candidate)result[0];preparedUpdate=(string)result[1];
                tray.ShowBalloonTip(5000,"Galaxy Helper",availableUpdate.Tag+" 다운로드 완료 · 패널을 닫으면 자동 설치합니다.",ToolTipIcon.Info);
                if(manual || (AppUpdates.Enabled && !Visible))InstallUpdate();
            };worker.RunWorkerAsync();
        }
        void InstallUpdate()
        {
            if(preparedUpdate==null || busy || fanBusy || setupRunning || inlineGraph!=null && inlineGraph.Capture)return;
            try {
                AppUpdates.VerifyFile(preparedUpdate,availableUpdate);
                // Do not clear persisted user settings. Only restore hardware ownership.
                curveEditDelay.Stop();afterFanRestore=null;afterFanRead=null;
                if(fanClient!=null){fanClient.Restore();StopFan();}
                if(ownsSetting)Restore();
                if(File.Exists(Program.Journal))throw new IOException("전력 복원 기록이 남아 있어 업데이트를 보류합니다.");
                Process.Start(new ProcessStartInfo(preparedUpdate,"--update-from "+Process.GetCurrentProcess().Id+" \""+Application.ExecutablePath+"\""){UseShellExecute=false,CreateNoWindow=true});
                exitRequested=true;Close();
            }catch(Exception ex){nextUpdate=DateTime.UtcNow.AddHours(6);preparedUpdate=null;NotifyError("업데이트 보류 · 현재 앱 유지: "+ex.Message);}
        }
    }
}
