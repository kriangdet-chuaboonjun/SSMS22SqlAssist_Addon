using System;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace SqlAssist.Setup
{
    /// <summary>The small window /installonexit shows once every SSMS has closed: it runs the install while saying so
    /// (so the user doesn't reopen SSMS halfway), then says it is done, or why it failed.</summary>
    internal sealed class InstallOnExitForm : Form
    {
        private const int ClientW = 460, ClientH = 200, Pad = 24;

        private readonly string _version;
        private readonly string _ide;
        private readonly Func<Task<string>> _install;
        private readonly Label _title = new Label { AutoSize = true, Font = Theme.Ui(12f, FontStyle.Bold), ForeColor = Theme.Text };
        private readonly Label _text = new Label { AutoSize = true, Font = Theme.Ui(9.5f), ForeColor = Theme.Muted };
        private readonly ProgressBar _bar = new ProgressBar { Style = ProgressBarStyle.Marquee, MarqueeAnimationSpeed = 30 };
        private readonly FlatButton _primary = new FlatButton { Primary = true };
        private readonly FlatButton _secondary = new FlatButton();
        private bool _busy = true;
        private Action _primaryAction;

        /// <summary>Null when the install worked.</summary>
        public string Error { get; private set; }

        /// <param name="install">Starts the install on a worker thread; the task's result is null on success, otherwise the error text.
        /// The caller keeps the task, so it can wait for it if this window is closed before it ends.</param>
        public InstallOnExitForm(string version, string ide, Func<Task<string>> install)
        {
            _version = version;
            _ide = ide;
            _install = install;

            Text = "SqlAssist Update";
            Font = Theme.Ui(9f);
            BackColor = Color.White;
            FormBorderStyle = FormBorderStyle.FixedDialog;
            MaximizeBox = false;
            MinimizeBox = false;
            TopMost = true; // the user has just closed SSMS: this is the only thing that says what happens next
            StartPosition = FormStartPosition.CenterScreen;
            AutoScaleMode = AutoScaleMode.None;
            try { Icon = Icon.ExtractAssociatedIcon(Application.ExecutablePath); } catch { /* default icon */ }

            ClientSize = new Size(D(ClientW), D(ClientH));
            _title.Location = new Point(D(Pad), D(22));
            _text.Location = new Point(D(Pad), D(58));
            _text.MaximumSize = new Size(D(ClientW - 2 * Pad), 0);
            _bar.Bounds = new Rectangle(D(Pad), D(130), D(ClientW - 2 * Pad), D(14));
            foreach (var b in new[] { _primary, _secondary }) { b.Font = Theme.Ui(9f); b.Visible = false; }
            _primary.Click += (s, e) => _primaryAction?.Invoke();
            _secondary.Click += (s, e) => Close();
            Controls.AddRange(new Control[] { _title, _text, _bar, _primary, _secondary });

            _title.Text = "กำลังอัปเดต SqlAssist เป็น " + version + "...";
            _text.Text = "ปิด SSMS ครบแล้ว กำลังติดตั้งให้ ใช้เวลาไม่นาน\nอย่าเปิด SSMS จนกว่าหน้าต่างนี้จะบอกว่าเสร็จ";

            // The user can't close it mid-install; if something else does (End task, sign-out) the install still finishes (Program).
            FormClosing += (s, e) => { if (_busy && e.CloseReason == CloseReason.UserClosing) e.Cancel = true; };
            Shown += async (s, e) => await RunAsync();
        }

        private static int D(int v) => (int)Math.Round(v * Theme.Scale);

        private async Task RunAsync()
        {
            string error;
            // Not back on the UI context: if the window was closed meanwhile its message loop is gone and a continuation
            // posted to it would throw. The outcome is handed to the UI thread by hand, and dropped if there is no window.
            try { error = await _install().ConfigureAwait(false); }
            catch (Exception ex) { error = ex.Message; }

            try { BeginInvoke(new Action(() => ShowOutcome(error))); }
            catch (Exception ex) when (ex is InvalidOperationException || ex is ObjectDisposedException) { /* window already closed */ }
        }

        private void ShowOutcome(string error)
        {
            _busy = false;
            Error = error;
            _bar.Visible = false;
            if (error == null)
            {
                _title.Text = "อัปเดตเรียบร้อยแล้ว";
                _text.Text = "ตอนนี้ SqlAssist เป็นเวอร์ชัน " + _version + " แล้ว เปิด SSMS ได้เลย";
                _primary.Text = "เปิด SSMS";
                _primaryAction = () => { LaunchSsms(); Close(); };
                _secondary.Text = "ปิด";
                _secondary.Visible = true;
            }
            else
            {
                _title.Text = "อัปเดตไม่สำเร็จ";
                _text.Text = error + "\n\nถ้า SqlAssist หายไปจาก SSMS ให้ดับเบิลคลิกไฟล์นี้เพื่อติดตั้งใหม่:\n" + Application.ExecutablePath;
                _primary.Text = "ปิด";
                _primaryAction = Close;
            }
            _primary.Visible = true;
            LayoutButtons();
            _primary.Focus();
        }

        private void LayoutButtons()
        {
            int top = Math.Max(D(ClientH) - D(Pad) - D(34), _text.Bottom + D(16));
            int right = D(ClientW - Pad);
            foreach (var b in new[] { _primary, _secondary })
            {
                b.Size = new Size(b.FitWidth(b == _primary ? 110 : 80), D(34));
                b.Location = new Point(right - b.Width, top);
                right = b.Left - D(10);
            }
            ClientSize = new Size(D(ClientW), Math.Max(D(ClientH), top + D(34) + D(Pad)));
        }

        private void LaunchSsms()
        {
            try { Process.Start(new ProcessStartInfo(Path.Combine(_ide, "Ssms.exe")) { UseShellExecute = true }); }
            catch { /* SSMS can be opened by hand */ }
        }
    }
}
