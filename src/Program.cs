using System;
using System.IO;
using System.Threading;
using System.Windows.Forms;

namespace GalaxyHelper
{
    static class Program
    {
        public static readonly string DataDirectory = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "GalaxyHelper");
        [STAThread]
        static int Main(string[] args)
        {
            bool owner;
            using (var mutex = new Mutex(true, "Local\\GalaxyHelper-" + Environment.UserName, out owner))
            {
                if (!owner) { if (args.Length == 0) MessageBox.Show("Galaxy Helper가 이미 실행 중입니다. 트레이 아이콘을 확인하세요."); return 2; }
                try
                {
                    var power = new WindowsPower();
                    var store = new RecoveryStore(Path.Combine(DataDirectory, "recovery"));
                    var controller = new PowerController(power, store);
                    if (args.Length == 2 && args[0] == "--diagnose") { Diagnostics.Save(args[1], Diagnostics.Collect(power, controller)); return 0; }
                    if (args.Length == 2 && args[0] == "--self-test") { Diagnostics.Save(args[1], Tests.Run()); return 0; }
                    if (args.Length == 2 && args[0] == "--integration-test") { Diagnostics.Save(args[1], Tests.Integration(power)); return 0; }
                    if (args.Length == 2 && args[0] == "--ui-smoke")
                    {
                        Application.EnableVisualStyles();
                        using (var form = new MainForm(power, controller, store))
                        {
                            var timer = new System.Windows.Forms.Timer { Interval = 2500 };
                            timer.Tick += delegate { timer.Stop(); using (var bitmap = new System.Drawing.Bitmap(form.Width, form.Height)) { form.DrawToBitmap(bitmap, form.ClientRectangle); bitmap.Save(args[1]); } form.Close(); };
                            form.Shown += delegate { timer.Start(); }; Application.Run(form); timer.Dispose();
                        }
                        return 0;
                    }
                    if (args.Length != 0) throw new ArgumentException("지원 명령: --diagnose <file>, --self-test <file>, --integration-test <file>, --ui-smoke <png>");
                    Application.EnableVisualStyles(); Application.SetCompatibleTextRenderingDefault(false);
                    Application.Run(new MainForm(power, controller, store)); return 0;
                }
                catch (Exception ex)
                {
                    if (args.Length == 2) { Diagnostics.Save(args[1] + ".error.json", new { Error = ex.ToString() }); }
                    else MessageBox.Show(ex.Message, "Galaxy Helper 오류", MessageBoxButtons.OK, MessageBoxIcon.Error);
                    return 1;
                }
                finally { mutex.ReleaseMutex(); }
            }
        }
    }
}
