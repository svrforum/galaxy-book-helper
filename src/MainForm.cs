using System;
using System.Drawing;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace GalaxyHelper
{
    public sealed class MainForm : Form
    {
        readonly IPower power;
        readonly PowerController controller;
        readonly RecoveryStore store;
        readonly Label summary = new Label();
        readonly Label policy = new Label();
        readonly Label status = new Label();
        readonly Label samsung = new Label();
        readonly Label battery = new Label();
        readonly ComboBox source = new ComboBox();
        readonly ComboBox boost = new ComboBox();
        readonly NumericUpDown maximum = new NumericUpDown();
        readonly Button apply = new Button();
        readonly Button restore = new Button();
        readonly NotifyIcon tray = new NotifyIcon();
        readonly Timer timer = new Timer();
        readonly FlowLayoutPanel stack = new FlowLayoutPanel();
        Guid displayedPlan;
        bool busy;
        bool ready;
        public MainForm(IPower power, PowerController controller, RecoveryStore store)
        {
            this.power = power; this.controller = controller; this.store = store;
            Text = "Galaxy Helper · 연구 프리뷰 0.1";
            Font = new Font("맑은 고딕", 10F);
            BackColor = Color.FromArgb(18, 23, 34); ForeColor = Color.FromArgb(232, 238, 247);
            ClientSize = new Size(730, 920); MinimumSize = new Size(650, 650);
            StartPosition = FormStartPosition.CenterScreen; AutoScaleMode = AutoScaleMode.Dpi;
            stack.Dock = DockStyle.Fill; stack.FlowDirection = FlowDirection.TopDown;
            stack.WrapContents = false; stack.AutoScroll = true; stack.Padding = new Padding(24);
            Controls.Add(stack);
            AddLabel("GALAXY HELPER", 22, 50, Color.White);
            AddLabel("전력과 소음 관리를 위한 Windows 도구", 11, 32, Color.LightSteelBlue);
            summary.Text = "기기 확인 중…"; Add(summary, 34);
            Add(battery, 28);
            AddLabel("WINDOWS CPU 정책", 12, 40, Color.FromArgb(108, 186, 255));
            AddLabel("성능 상한은 CPU 성능 비율(%)입니다. 소비전력(W)이나 팬 RPM 상한이 아닙니다.", 9, 34, Color.LightSteelBlue);
            Add(policy, 50);
            status.ForeColor = Color.FromArgb(165, 213, 172); Add(status, 50);
            var row = new FlowLayoutPanel { Height = 42, Width = 650 };
            source.DropDownStyle = ComboBoxStyle.DropDownList; source.Width = 170; source.ForeColor = Color.Black;
            source.Items.AddRange(new object[] { "충전기 연결 · AC", "배터리 사용 · DC" }); source.SelectedIndex = 0;
            maximum.Minimum = 1; maximum.Maximum = 100; maximum.Value = 100; maximum.Width = 80;
            row.Controls.Add(source); row.Controls.Add(new Label { Text = "CPU 상한 (%)", AutoSize = true, Padding = new Padding(12, 6, 0, 0) }); row.Controls.Add(maximum);
            stack.Controls.Add(row);
            boost.DropDownStyle = ComboBoxStyle.DropDownList; boost.Width = 300; boost.ForeColor = Color.Black;
            boost.Items.AddRange(new object[] { "부스트: 현재 설정 유지", "부스트: 사용 안 함", "부스트: 사용", "부스트: 적극적" }); boost.SelectedIndex = 0;
            ConfigureCombo(source); ConfigureCombo(boost);
            stack.Controls.Add(boost);
            AddLabel("지원되는 클래스 0 / 1에 같은 상한을 적용합니다. 선택한 AC/DC만 변경합니다.", 9, 33, Color.LightSteelBlue);
            var actions = new FlowLayoutPanel { Width = 650, Height = 46 };
            Style(apply, "정책 적용", 130); Style(restore, "이 계획 원래 값 복원", 200);
            var refresh = Button("새로고침", 130, delegate { Reload(); });
            actions.Controls.Add(apply); actions.Controls.Add(restore); actions.Controls.Add(refresh); stack.Controls.Add(actions);
            AddLabel("직접 하드웨어 제어 · 검증 대기", 12, 40, Color.FromArgb(108, 186, 255));
            AddLabel("PL1 / PL2 와트 제한     —   지원 인터페이스 미확인\n팬 RPM 상한 / 팬 곡선  —   지원 인터페이스 미확인\n온도 / 팬 RPM 센서      —   아직 연결되지 않음", 10, 80, Color.Silver);
            Add(samsung, 48);
            var extra = new FlowLayoutPanel { Width = 650, Height = 46 };
            extra.Controls.Add(Button("Samsung Settings 열기", 240, delegate { OpenSamsung(); }));
            extra.Controls.Add(Button("진단 JSON 저장", 160, delegate { Export(); })); stack.Controls.Add(extra);
            AddLabel("삼성 앱에서 저소음 모드를 선택할 수 있습니다. 위 숫자는 레지스트리 보고값이며\n펌웨어 실측값이 아닙니다. 이 앱은 삼성 모드를 직접 변경하지 않습니다.", 9, 50, Color.LightSteelBlue);
            AddLabel("적용한 정책은 앱 종료 후에도 유지됩니다. 복원 버튼으로 되돌리세요.\n절전·재부팅·삼성 앱 전환 후에는 새로고침으로 값을 확인하세요.", 9, 48, Color.LightSteelBlue);
            source.SelectedIndexChanged += delegate { Reload(); };
            apply.Click += delegate { Apply(); }; restore.Click += delegate { Restore(); };
            tray.Icon = SystemIcons.Application; tray.Text = "Galaxy Helper"; tray.Visible = true;
            var menu = new ContextMenuStrip();
            menu.Items.Add("열기", null, delegate { Show(); WindowState = FormWindowState.Normal; Activate(); });
            menu.Items.Add("종료 (정책 유지)", null, delegate { Close(); }); tray.ContextMenuStrip = menu;
            tray.DoubleClick += delegate { Show(); WindowState = FormWindowState.Normal; Activate(); };
            Resize += delegate { if (WindowState == FormWindowState.Minimized) Hide(); };
            FormClosed += delegate { timer.Stop(); timer.Dispose(); tray.Dispose(); };
            Shown += async delegate { Reload(); summary.Text = await Task.Run(() => Diagnostics.Model()); };
            timer.Interval = 5000; timer.Tick += delegate { UpdateReadout(); }; timer.Start();
        }
        void Add(Control control, int height) { control.Width = 650; control.Height = height; control.Margin = new Padding(0, 2, 0, 3); stack.Controls.Add(control); }
        void AddLabel(string text, float size, int height, Color color)
        { Add(new Label { Text = text, Font = new Font(Font.FontFamily, size), ForeColor = color }, height); }
        void Style(Button button, string text, int width)
        { button.Text = text; button.Width = width; button.Height = 36; button.FlatStyle = FlatStyle.Flat; button.BackColor = Color.FromArgb(35, 52, 76); button.ForeColor = Color.White; }
        Button Button(string text, int width, Action action)
        { var button = new Button(); Style(button, text, width); button.Click += delegate { if (!busy) action(); }; return button; }
        void ConfigureCombo(ComboBox combo)
        {
            combo.DrawMode = DrawMode.OwnerDrawFixed; combo.ItemHeight = 25;
            combo.DrawItem += delegate(object sender, DrawItemEventArgs e)
            {
                e.DrawBackground();
                if (e.Index >= 0)
                {
                    bool selected = (e.State & DrawItemState.Selected) != 0;
                    TextRenderer.DrawText(e.Graphics, Convert.ToString(combo.Items[e.Index]), combo.Font,
                        e.Bounds, selected ? SystemColors.HighlightText : Color.Black,
                        TextFormatFlags.Left | TextFormatFlags.VerticalCenter);
                }
                e.DrawFocusRectangle();
            };
        }
        bool AC { get { return source.SelectedIndex == 0; } }
        void SetBusy(bool value)
        {
            busy = value; apply.Enabled = !value && ready; restore.Enabled = !value && ready && store.Exists(displayedPlan);
            restore.BackColor = restore.Enabled ? Color.FromArgb(35, 52, 76) : Color.LightSlateGray;
        }
        void Reload()
        {
            if (busy) return;
            try
            {
                displayedPlan = power.Active(); Snapshot snapshot = controller.Capture(displayedPlan);
                uint value = snapshot.Values.First(e => e.AC == AC && new Guid(e.Setting) == Settings.Maximum).Value;
                maximum.Value = Math.Max(1, Math.Min(100, value)); boost.SelectedIndex = 0;
                uint currentBoost = snapshot.Values.First(e => e.AC == AC && new Guid(e.Setting) == Settings.Boost).Value;
                ValueEntry class1 = snapshot.Values.FirstOrDefault(e => e.AC == AC && new Guid(e.Setting) == Settings.Maximum1);
                policy.Text = "계획: " + displayedPlan + "\n저장된 상한: " + value + "%  /  클래스 1: " + (class1 == null ? "미지원" : class1.Value + "%") + "  /  부스트: " + currentBoost;
                ready = true; status.Text = store.Exists(displayedPlan) ? "이 계획의 변경 전 복원 파일이 있습니다." : "준비됨 · 적용 버튼을 누르기 전에는 설정을 변경하지 않습니다.";
                UpdateReadout();
            }
            catch (Exception ex) { ready = false; status.Text = "정책 조회 실패: " + ex.Message; }
            SetBusy(false);
        }
        void UpdateReadout()
        {
            try
            {
                var value = SystemInformation.PowerStatus;
                battery.Text = (value.PowerLineStatus == PowerLineStatus.Online ? "충전기 연결" : value.PowerLineStatus == PowerLineStatus.Offline ? "배터리 사용" : "전원 상태 미확인") + "  ·  " + (value.BatteryLifePercent >= 0 && value.BatteryLifePercent <= 1 ? Math.Round(value.BatteryLifePercent * 100) + "%" : "배터리 잔량 미확인");
                samsung.Text = Diagnostics.SamsungStatus();
                if (ready && !busy && power.Active() != displayedPlan) { ready = false; SetBusy(false); status.Text = "전원 계획이 변경되었습니다. 새로고침하세요."; }
            }
            catch (Exception ex) { samsung.Text = "상태 조회 실패: " + ex.Message; }
        }
        void Apply()
        {
            SetBusy(true);
            try
            {
                uint? mode = boost.SelectedIndex == 0 ? (uint?)null : (uint)(boost.SelectedIndex - 1);
                controller.Apply(displayedPlan, AC, (uint)maximum.Value, mode);
                SetBusy(false); Reload(); status.Text = "정책 저장 및 재조회 검증 완료. 실제 소비전력·소음 변화는 별도 측정이 필요합니다.";
            }
            catch (Exception ex) { ShowError(ex); }
            finally { SetBusy(false); }
        }
        void Restore()
        {
            SetBusy(true);
            try { controller.Restore(displayedPlan); SetBusy(false); Reload(); status.Text = "최초 변경 전 AC/DC 설정을 복원하고 재조회로 확인했습니다."; }
            catch (Exception ex) { ShowError(ex); }
            finally { SetBusy(false); }
        }
        void ShowError(Exception ex)
        { status.Text = ex.Message; MessageBox.Show(this, ex.Message + "\n\n액세스 거부인 경우 관리자 권한으로 실행하세요.", "Galaxy Helper", MessageBoxButtons.OK, MessageBoxIcon.Error); }
        void OpenSamsung()
        { try { Process.Start(new ProcessStartInfo("samsungsettings15:") { UseShellExecute = true }); } catch (Exception ex) { ShowError(ex); } }
        async void Export()
        {
            using (var dialog = new SaveFileDialog { Filter = "JSON 진단 파일|*.json", FileName = "galaxy-diagnostics-" + DateTime.Now.ToString("yyyyMMdd-HHmmss") + ".json" })
            {
                if (dialog.ShowDialog(this) != DialogResult.OK) return;
                SetBusy(true);
                try { await Task.Run(() => Diagnostics.Save(dialog.FileName, Diagnostics.Collect(power, controller))); status.Text = "진단 저장 완료. 일련번호·사용자 이름은 수집하지 않습니다."; }
                catch (Exception ex) { ShowError(ex); }
                finally { SetBusy(false); }
            }
        }
    }
}
