using System;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace SqlAssist.Setup
{
    /// <summary>The setup window, in three pages: what is installed and what to do (Install / Update / Uninstall),
    /// progress while VSIXInstaller runs, and the result. The sidebar on the left tracks the steps.
    /// The action button stays disabled while SSMS is open (checked every second, so it unlocks by itself).</summary>
    internal sealed class SetupForm : Form
    {
        private const string ChangesUrl = "https://github.com/kriangdet-chuaboonjun/SSMS22SqlAssist_Addon/commits/main";

        // Layout in 96-dpi pixels; D() scales to the screen.
        private const int SidebarWidth = 230, ContentWidth = 630, WindowHeight = 580, Pad = 36, ButtonHeight = 36;
        private const int Inner = ContentWidth - 2 * Pad;
        private const int FooterTop = WindowHeight - Pad - ButtonHeight;

        private static readonly string[] InstallSteps = { "ตรวจสอบระบบ", "ถอนเวอร์ชันเดิม", "ติดตั้งไฟล์ใหม่", "ลงทะเบียนกับ SSMS" };
        private static readonly string[] InstallTitles = { "กำลังเตรียมไฟล์ติดตั้ง...", "กำลังถอนเวอร์ชันเดิมออก...", "กำลังติดตั้งไฟล์ใหม่...", "กำลังลงทะเบียนกับ SSMS..." };
        private static readonly string[] UninstallSteps = { "ตรวจสอบระบบ", "ถอน SqlAssist ออก", "ล้าง cache ของ SSMS" };
        private static readonly string[] UninstallTitles = { "", "กำลังถอน SqlAssist ออก...", "กำลังล้าง cache ของ SSMS..." };

        private readonly string _ide;
        private readonly string _ssmsVersion;
        private string _installed;
        private int? _compare;
        private bool _busy, _done, _uninstalling;

        private readonly Sidebar _sidebar;
        private readonly Panel _content = new Panel { BackColor = Color.White };
        private readonly Panel _welcome = Page(), _working = Page(), _finish = Page();
        private Panel _page;

        // Page 1: welcome.
        private readonly Badge _badge = new Badge();
        private readonly Label _welcomeTitle = Title();
        private readonly Label _welcomeText = Body();
        private readonly VersionCard _versions = new VersionCard();
        private readonly Label _choicesCaption = Caption("ต้องการทำอะไร");
        private readonly OptionCard _updateOption = new OptionCard();
        private readonly OptionCard _uninstallOption = new OptionCard
        {
            Title = "ถอนการติดตั้ง SqlAssist",
            Description = "ลบ SqlAssist ออกจาก SSMS 22 (ไฟล์ snippets ของคุณยังเก็บไว้)",
        };
        private readonly NoteBox _features = new NoteBox();

        // Page 2: working.
        private readonly Label _stepCaption = Caption("");
        private readonly Label _workingTitle = Title();
        private readonly Label _workingText = Body();
        private readonly Label _percent = new Label { AutoSize = true, Font = new Font("Segoe UI Semibold", 26f), ForeColor = Theme.Accent };
        private readonly Label _elapsedText = new Label { AutoSize = true, Font = Theme.Ui(8.5f), ForeColor = Theme.Muted };
        private readonly ProgressLine _bar = new ProgressLine();
        private readonly Label _detailsCaption = Caption("รายละเอียด");
        private readonly LogBox _log = new LogBox();

        // Page 3: finished or failed.
        private readonly StatusMark _mark = new StatusMark();
        private readonly Label _finishTitle = Title();
        private readonly RunsLabel _finishLine = new RunsLabel();
        private readonly Label _finishText = Body();
        private readonly NoteBox _finishNote = new NoteBox();
        private readonly NoteBox _finishWarning = new NoteBox();
        private readonly CheckBox _openSsms = new CheckBox { Text = "เปิด SSMS 22 หลังปิดหน้าต่างนี้", AutoSize = true, Checked = true, Font = Theme.Ui(9f), ForeColor = Theme.Text };

        // Footer, shared by the pages.
        private readonly Label _warning = new Label { AutoSize = true, Font = Theme.Ui(8.5f), ForeColor = Theme.Warning };
        private readonly LinkLabel _changes = new LinkLabel
        {
            Text = "ดูสิ่งที่เปลี่ยนในเวอร์ชันนี้", AutoSize = true, Font = Theme.Ui(8.75f),
            LinkColor = Theme.Accent, ActiveLinkColor = Theme.AccentHover, VisitedLinkColor = Theme.Accent,
        };
        private readonly FlatButton _primary = new FlatButton { Primary = true };
        private readonly FlatButton _secondary = new FlatButton();
        private readonly ToolTip _tips = new ToolTip();
        private Action _primaryAction, _secondaryAction;

        private readonly Timer _ssmsTimer = new Timer { Interval = 1000 };
        private readonly Timer _animTimer = new Timer { Interval = 80 };
        private readonly Stopwatch _elapsed = new Stopwatch();
        private readonly Stopwatch _stepClock = new Stopwatch();
        private SetupEngine.Step _step;
        private double _shownProgress;

        public SetupForm(bool preferUninstall = false)
        {
            Text = "SqlAssist Setup";
            Font = Theme.Ui(9f);
            FormBorderStyle = FormBorderStyle.FixedSingle;
            MaximizeBox = false;
            StartPosition = FormStartPosition.CenterScreen;
            AutoScaleMode = AutoScaleMode.None;
            BackColor = Color.White;
            KeyPreview = true;
            try { Icon = Icon.ExtractAssociatedIcon(Application.ExecutablePath); } catch { /* default icon */ }

            _ide = SetupEngine.FindSsmsIde();
            _ssmsVersion = SsmsVersion(_ide);
            _sidebar = new Sidebar(LoadLogo());
            BuildLayout();

            _primary.Click += (s, e) => _primaryAction?.Invoke();
            _secondary.Click += (s, e) => _secondaryAction?.Invoke();
            _updateOption.CheckedChanged += (s, e) => RefreshWelcome();
            _uninstallOption.CheckedChanged += (s, e) => RefreshWelcome();
            _changes.LinkClicked += (s, e) => OpenUrl(ChangesUrl);
            _ssmsTimer.Tick += (s, e) => RefreshWelcome();
            _animTimer.Tick += (s, e) => ShowProgress();
            KeyDown += (s, e) => { if (e.KeyCode == Keys.Escape && !_busy) Close(); };
            FormClosing += (s, e) => { if (_busy) e.Cancel = true; };
            FormClosed += (s, e) => LaunchSsmsIfAsked();

            LoadInstalledState();
            if (preferUninstall && _installed != null) _uninstallOption.Checked = true;
        }

        private static int D(int v) => (int)Math.Round(v * Theme.Scale);

        private void BuildLayout()
        {
            SuspendLayout();
            ClientSize = new Size(D(SidebarWidth + ContentWidth), D(WindowHeight));
            _sidebar.Bounds = new Rectangle(0, 0, D(SidebarWidth), D(WindowHeight));
            _content.Bounds = new Rectangle(D(SidebarWidth), 0, D(ContentWidth), D(WindowHeight));
            Controls.Add(_content);
            Controls.Add(_sidebar);

            foreach (var page in new[] { _welcome, _working, _finish })
            {
                page.Bounds = new Rectangle(0, 0, D(ContentWidth), D(FooterTop - 8));
                _content.Controls.Add(page);
            }

            Place(_welcome, _badge, 34);
            Place(_welcome, _welcomeTitle, 62);
            Place(_welcome, _welcomeText, 100);
            Place(_welcome, _versions, 134, 84);
            Place(_welcome, _choicesCaption, 238);
            Place(_welcome, _updateOption, 262, 66);
            Place(_welcome, _uninstallOption, 338, 66);
            Place(_welcome, _features, 238, 0);

            Place(_working, _stepCaption, 34);
            Place(_working, _workingTitle, 56);
            Place(_working, _workingText, 94);
            Place(_working, _percent, 126);
            Place(_working, _elapsedText, 150);
            Place(_working, _bar, 182, 6);
            Place(_working, _detailsCaption, 208);
            Place(_working, _log, 232, 150);

            Place(_finish, _mark, 34, 60);
            _mark.Width = D(60);
            Place(_finish, _finishTitle, 112);
            Place(_finish, _finishLine, 152);
            Place(_finish, _finishText, 176);
            Place(_finish, _finishNote, 214, 0);
            Place(_finish, _finishWarning, 280, 0);
            Place(_finish, _openSsms, 300);

            foreach (var b in new[] { _primary, _secondary }) b.Font = Theme.Ui(9f);
            _content.Controls.AddRange(new Control[] { _warning, _changes, _primary, _secondary });
            foreach (Control c in new Control[] { _warning, _changes, _primary, _secondary }) c.BringToFront();

            AcceptButton = _primary;
            ResumeLayout(false);
        }

        /// <summary>Adds a control at the page's left margin; a height of 0 leaves it to the control.</summary>
        private void Place(Panel page, Control control, int top, int height = -1)
        {
            control.Location = new Point(D(Pad), D(top));
            if (height >= 0) control.Size = new Size(D(Inner), height == 0 ? control.Height : D(height));
            if (control is Label label && label.AutoSize) label.MaximumSize = new Size(D(Inner), 0);
            page.Controls.Add(control);
        }

        // ---- Page 1: welcome ----------------------------------------------------------------------------------

        private void LoadInstalledState()
        {
            _installed = _ide == null ? null : SetupEngine.FindInstalledVersion(_ide);
            _compare = SetupEngine.CompareInstalled(_installed);
            ShowWelcome();
        }

        private void ShowWelcome()
        {
            string v = SetupEngine.SetupVersion;
            _done = false;
            _sidebar.SetSteps(InstallSteps);
            _sidebar.SetCurrent(0);
            _sidebar.Footer = "IntelliSense + Snippets\nสำหรับ SQL Server Management Studio";

            _versions.FromValue = _installed;
            _versions.ToValue = v;
            _versions.ToCaption = _compare == -1 || _installed == null ? "เวอร์ชันใหม่" : "เวอร์ชันนี้";
            _versions.Status = _ide == null ? "ไม่พบ SSMS 22" : "พบ " + _ssmsVersion;
            _versions.StatusOk = _ide != null;
            _versions.Invalidate();

            bool hasChoices = _ide != null && _installed != null;
            if (_ide == null)
            {
                _badge.Set("ไม่พบ SSMS 22", Theme.ErrorSoft, Theme.Error);
                _welcomeTitle.Text = "ติดตั้ง SqlAssist ไม่ได้";
                _welcomeText.Text = "ไม่พบ SQL Server Management Studio 22 ในเครื่องนี้ ติดตั้ง SSMS 22 ก่อน แล้วเปิดตัวติดตั้งนี้อีกครั้ง";
            }
            else if (_installed == null)
            {
                _badge.Set("ติดตั้งใหม่", Theme.AccentSoft, Theme.Accent);
                _welcomeTitle.Text = "พร้อมติดตั้ง SqlAssist";
                _welcomeText.Text = "ติดตั้งให้ผู้ใช้นี้ใน SSMS 22 ไม่ต้องใช้สิทธิ์ admin";
            }
            else
            {
                switch (_compare)
                {
                    case -1:
                        _badge.Set("มีเวอร์ชันใหม่", Theme.SuccessSoft, Theme.SuccessText);
                        _welcomeTitle.Text = "พร้อมอัปเดต SqlAssist";
                        _welcomeText.Text = "ตัวติดตั้งตรวจพบเวอร์ชันเดิมในเครื่อง และจะแทนที่ให้อัตโนมัติ";
                        SetUpdateOption($"อัปเดตเป็น {v}", "แนะนำ", "เก็บการตั้งค่าและ snippets ของคุณไว้เหมือนเดิม");
                        break;
                    case 0:
                        _badge.Set("ติดตั้งแล้ว", Theme.AccentSoft, Theme.Accent);
                        _welcomeTitle.Text = "SqlAssist เป็นเวอร์ชันล่าสุดแล้ว";
                        _welcomeText.Text = "ติดตั้งซ้ำเพื่อซ่อมแซม หรือถอน SqlAssist ออกจาก SSMS 22";
                        SetUpdateOption($"ติดตั้ง {v} ซ้ำ", "ซ่อมแซม", "ใช้เมื่อเมนูหรือ IntelliSense ของ SqlAssist ไม่ทำงาน");
                        break;
                    default:
                        _badge.Set("ตัวติดตั้งเก่ากว่า", Theme.WarningSoft, Theme.Warning);
                        _welcomeTitle.Text = "ในเครื่องมีเวอร์ชันที่ใหม่กว่า";
                        _welcomeText.Text = $"SqlAssist {_installed} ที่ติดตั้งอยู่ใหม่กว่าตัวติดตั้งนี้ ({v})";
                        SetUpdateOption($"ย้อนกลับเป็น {v}", null, "ติดตั้งเวอร์ชันนี้ทับของเดิม");
                        break;
                }
                _updateOption.Checked = true;
            }

            _choicesCaption.Visible = _updateOption.Visible = _uninstallOption.Visible = hasChoices;
            _features.Visible = _ide != null && _installed == null;
            _features.Set(NoteBox.Kind.Plain,
                "•  กด Tab ขยาย * เป็นรายชื่อคอลัมน์ และตั้ง alias ตารางให้อัตโนมัติ",
                "•  แนะนำ JOIN จาก foreign key และ completion แบบ fuzzy",
                "•  Snippets และหน้าต่างค้นหา object ในฐานข้อมูล");

            _changes.Visible = false;
            _secondary.Visible = true;
            _secondary.Text = "ยกเลิก";
            _secondary.Enabled = true;
            _secondaryAction = Close;
            _primaryAction = () => { var _ = RunAsync(); };
            ShowPage(_welcome);
            RefreshWelcome();
            _ssmsTimer.Start();
        }

        private void SetUpdateOption(string title, string hint, string description)
        {
            _updateOption.Title = title;
            _updateOption.Hint = hint;
            _updateOption.Description = description;
            _updateOption.Invalidate();
        }

        private bool UninstallSelected => _installed != null && _uninstallOption.Checked;

        /// <summary>Runs every second on the first page: SSMS state, button text and whether it can be pressed.</summary>
        private void RefreshWelcome()
        {
            if (_busy || _done || _page != _welcome) return;
            int ssms = SetupEngine.RunningSsmsCount();
            _warning.Visible = ssms > 0 && _ide != null;
            _warning.Text = $"⚠  ปิด SSMS ก่อนเริ่ม (เปิดอยู่ {ssms} หน้าต่าง)\n     ปุ่มจะกดได้เองเมื่อปิดครบแล้ว";

            _primary.Text = _installed == null ? "ติดตั้งเลย"
                : UninstallSelected ? "ถอนการติดตั้ง"
                : _compare == -1 ? "อัปเดตเลย"
                : _compare == 0 ? "ติดตั้งซ้ำ"
                : "ติดตั้งทับ";
            _primary.Enabled = _ide != null && ssms == 0;
            LayoutFooter();
        }

        // ---- Page 2: working ----------------------------------------------------------------------------------

        private async Task RunAsync()
        {
            // Checked again here: the timer may be up to a second behind.
            if (SetupEngine.RunningSsmsCount() > 0) { RefreshWelcome(); return; }

            bool uninstall = UninstallSelected;
            if (uninstall && MessageBox.Show(this, "ต้องการถอนการติดตั้ง SqlAssist ใช่ไหม?\n(ไฟล์ snippets ของคุณจะยังเก็บไว้)",
                    "ถอนการติดตั้ง", MessageBoxButtons.YesNo, MessageBoxIcon.Question) != DialogResult.Yes)
                return;

            BeginRun(uninstall);
            // Created on the UI thread, so each report lands back here.
            var progress = new Progress<SetupEngine.Step>(OnStep);

            string error;
            try
            {
                error = await Task.Run(() => uninstall ? SetupEngine.Uninstall(_ide, progress) : SetupEngine.Install(_ide, progress));
            }
            catch (Exception ex)
            {
                error = ex.Message;
            }

            if (error != null) ShowFailed(error);
            else ShowFinished();
        }

        private void BeginRun(bool uninstall)
        {
            _busy = true;
            _uninstalling = uninstall;
            _ssmsTimer.Stop();
            _sidebar.SetSteps(uninstall ? UninstallSteps : InstallSteps);
            _sidebar.SetCurrent(uninstall ? 1 : 0);
            _sidebar.Footer = uninstall ? $"ถอนการติดตั้ง {_installed}"
                : _installed == null ? $"ติดตั้ง {SetupEngine.SetupVersion}"
                : $"อัปเดต {_installed} → {SetupEngine.SetupVersion}";

            _log.Clear();
            _log.Add("ตรวจพบ " + _ssmsVersion);
            if (_installed != null) _log.Add("ตรวจพบ SqlAssist " + _installed);

            _workingText.Text = "อย่าปิดหน้าต่างนี้หรือเปิด SSMS ระหว่างติดตั้ง";
            _step = new SetupEngine.Step(0, uninstall ? 2 : 4, "");
            _shownProgress = 0;
            ShowStepHeader();

            _warning.Visible = false;
            _secondary.Text = "ยกเลิก";
            _secondary.Enabled = false;
            _primary.Text = uninstall ? "กำลังถอน..." : _installed == null ? "กำลังติดตั้ง..." : "กำลังอัปเดต...";
            _primary.Enabled = false;
            ShowPage(_working);

            _elapsed.Restart();
            _stepClock.Restart();
            ShowProgress();
            _animTimer.Start();
        }

        private void OnStep(SetupEngine.Step step)
        {
            _step = step;
            _stepClock.Restart();
            _log.Add(step.Text + "...", active: true);
            ShowStepHeader();
            ShowProgress();
        }

        /// <summary>Install steps line up with the sidebar one to one; uninstall's sidebar has "ตรวจสอบระบบ" in front.</summary>
        private int SidebarIndex => Math.Max(_step.Number - 1, 0) + (_uninstalling ? 1 : 0);

        private void ShowStepHeader()
        {
            var steps = _uninstalling ? UninstallSteps : InstallSteps;
            var titles = _uninstalling ? UninstallTitles : InstallTitles;
            int i = Math.Min(SidebarIndex, steps.Length - 1);
            _sidebar.SetCurrent(i);
            _stepCaption.Text = $"ขั้นตอน {i + 1} จาก {steps.Length}";
            _workingTitle.Text = i == 1 && !_uninstalling && _installed == null ? "กำลังตรวจหาเวอร์ชันเดิม..." : titles[i];
        }

        /// <summary>The engine only reports step boundaries, so within a step the bar eases toward the next one
        /// (never reaching it) to show that work is still going on.</summary>
        private void ShowProgress()
        {
            int total = Math.Max(_step.Total, 1);
            double within = 0.9 * (1 - Math.Exp(-_stepClock.Elapsed.TotalSeconds / 4.0));
            double value = (Math.Max(_step.Number - 1, 0) + (_step.Number > 0 ? within : 0)) / total;
            _shownProgress = Math.Max(_shownProgress, value);
            SetProgress(_shownProgress);
        }

        private void SetProgress(double value)
        {
            _bar.Value = value;
            _percent.Text = $"{(int)Math.Round(value * 100)}%";
            _elapsedText.Text = $"ผ่านไป {(int)_elapsed.Elapsed.TotalSeconds} วินาที · ทั้งหมดประมาณ 10–30 วินาที";
            _elapsedText.Location = new Point(D(Pad + Inner) - _elapsedText.Width, _percent.Bottom - _elapsedText.Height - D(8));
        }

        // ---- Page 3: finished or failed -----------------------------------------------------------------------

        private void EndRun()
        {
            _animTimer.Stop();
            _elapsed.Stop();
            _busy = false;
        }

        private void ShowFinished()
        {
            EndRun();
            _done = true;
            string v = SetupEngine.SetupVersion;
            bool updated = !_uninstalling && _installed != null;
            _sidebar.SetCurrent(int.MaxValue);
            _sidebar.Footer = $"ใช้เวลาทั้งหมด {(int)_elapsed.Elapsed.TotalSeconds} วินาที";
            _log.CompleteActive();
            SetProgress(1);

            _mark.Ok = true;
            _mark.Invalidate();
            var body = Theme.Ui(9.5f);
            _finishLine.Clear();
            if (_uninstalling)
            {
                _finishTitle.Text = "ถอนการติดตั้งเรียบร้อยแล้ว";
                _finishLine.Add("ถอน SqlAssist ออกจาก SSMS 22 แล้ว", body, Theme.Text);
                _finishText.Text = @"ไฟล์ snippets ยังเก็บไว้ที่ %APPDATA%\SqlAssist ถ้าติดตั้งใหม่ภายหลังจะใช้ต่อได้ทันที";
            }
            else
            {
                _finishTitle.Text = updated && _compare == -1 ? "อัปเดตเรียบร้อยแล้ว" : "ติดตั้งเรียบร้อยแล้ว";
                _finishLine.Add("ตอนนี้ SqlAssist เป็นเวอร์ชัน ", body, Theme.Text)
                           .Add(v, Theme.Mono(10f, FontStyle.Bold), Theme.Text)
                           .Add(" แล้ว", body, Theme.Text);
                _finishText.Text = updated ? "การตั้งค่าและ snippets เดิมของคุณยังอยู่ครบ" : "เมนูอยู่ที่ Tools › SqlAssist ใน SSMS";
            }

            int y = D(214);
            bool note = !_uninstalling;
            _finishNote.Visible = note;
            if (note)
            {
                _finishNote.Top = y;
                _finishNote.Set(NoteBox.Kind.Info,
                    "เปิด SSMS 22 ใหม่ แล้วเริ่มพิมพ์ใน Query Editor เพื่อใช้ IntelliSense",
                    "ปิด IntelliSense เดิมของ SSMS ได้ที่ Tools › Options › Text Editor › Transact-SQL › IntelliSense");
                y = _finishNote.Bottom + D(10);
            }
            // Not "if (_finishWarning.Visible)": Visible reads false while the page itself is still hidden.
            bool dbForge = !_uninstalling && SetupEngine.DbForgeInstalled(_ide);
            _finishWarning.Visible = dbForge;
            if (dbForge)
            {
                _finishWarning.Top = y;
                _finishWarning.Set(NoteBox.Kind.Warning, "พบ dbForge SQL Complete — ควรถอนหรือปิดก่อน เพราะแย่งปุ่ม Tab กัน");
                y = _finishWarning.Bottom + D(10);
            }
            _openSsms.Visible = !_uninstalling;
            _openSsms.Top = y + D(8);

            _changes.Visible = !_uninstalling;
            _warning.Visible = false;
            _secondary.Visible = false;
            _primary.Text = "เสร็จสิ้น";
            _primary.Enabled = true;
            _primaryAction = Close;
            ShowPage(_finish);
            _primary.Focus();
        }

        private void ShowFailed(string error)
        {
            EndRun();
            _sidebar.SetCurrent(Math.Min(SidebarIndex, (_uninstalling ? UninstallSteps : InstallSteps).Length - 1), failed: true);
            _sidebar.Footer = $"หยุดหลังจาก {(int)_elapsed.Elapsed.TotalSeconds} วินาที";

            _mark.Ok = false;
            _mark.Invalidate();
            _finishTitle.Text = _uninstalling ? "ถอนการติดตั้งไม่สำเร็จ" : "ติดตั้งไม่สำเร็จ";
            _finishLine.Clear().Add("ขั้นตอนที่ล้มเหลว: " + _step.Text, Theme.Ui(9.5f), Theme.Text);
            _finishText.Text = error;
            _finishNote.Visible = true;
            _finishNote.Top = _finishText.Bottom + D(16);
            _finishNote.Set(NoteBox.Kind.Warning,
                "ปิด SSMS ให้ครบทุกหน้าต่าง (รวม Ssms.exe ที่อาจค้างอยู่ใน Task Manager) แล้วกด ลองอีกครั้ง");
            _finishWarning.Visible = false;
            _openSsms.Visible = false;

            _changes.Visible = false;
            _secondary.Visible = true;
            _secondary.Text = "ปิด";
            _secondary.Enabled = true;
            _secondaryAction = Close;
            _primary.Text = "ลองอีกครั้ง";
            _primary.Enabled = true;
            _primaryAction = LoadInstalledState;
            ShowPage(_finish);
        }

        // ---- Shared -------------------------------------------------------------------------------------------

        private void ShowPage(Panel page)
        {
            _page = page;
            foreach (var p in new[] { _welcome, _working, _finish }) p.Visible = p == page;
            LayoutFooter();
        }

        private void LayoutFooter()
        {
            int y = D(FooterTop), right = D(ContentWidth - Pad);
            foreach (var b in new[] { _primary, _secondary })
            {
                b.Size = new Size(b.FitWidth(b == _primary ? 120 : 96), D(ButtonHeight));
                b.Location = new Point(right - b.Width, y);
                right = b.Left - D(10);
            }
            _warning.Location = new Point(D(Pad), y + (D(ButtonHeight) - _warning.Height) / 2);
            _tips.SetToolTip(_warning, "บันทึกงานแล้วปิด SSMS ทุกหน้าต่าง (รวม Ssms.exe ที่อาจค้างอยู่ใน Task Manager)");
            _changes.Location = new Point(D(Pad), y + (D(ButtonHeight) - _changes.Height) / 2);
        }

        private void LaunchSsmsIfAsked()
        {
            if (!_done || _uninstalling || !_openSsms.Checked || _ide == null) return;
            try { Process.Start(new ProcessStartInfo(Path.Combine(_ide, "Ssms.exe")) { UseShellExecute = true }); }
            catch { /* SSMS can be opened by hand */ }
        }

        private static void OpenUrl(string url)
        {
            try { Process.Start(new ProcessStartInfo(url) { UseShellExecute = true }); }
            catch { /* no browser registered */ }
        }

        // "SSMS 22.11" rather than the install path (the path is in the version card's tooltip).
        private static string SsmsVersion(string ide)
        {
            if (ide == null) return "SSMS 22";
            try
            {
                var v = FileVersionInfo.GetVersionInfo(Path.Combine(ide, "Ssms.exe"));
                return $"SSMS {v.FileMajorPart}.{v.FileMinorPart}";
            }
            catch
            {
                return "SSMS 22";
            }
        }

        private static Image LoadLogo()
        {
            try
            {
                using (var s = typeof(SetupForm).Assembly.GetManifestResourceStream("icon.png"))
                    return s == null ? null : new Bitmap(Image.FromStream(s));
            }
            catch
            {
                return null; // no logo is fine
            }
        }

        private static Panel Page() => new Panel { BackColor = Color.White, Visible = false };
        private static Label Title() => new Label { AutoSize = true, Font = Theme.Ui(15f, FontStyle.Bold), ForeColor = Theme.Text };
        private static Label Body() => new Label { AutoSize = true, Font = Theme.Ui(9.5f), ForeColor = Theme.Muted };
        private static Label Caption(string text) => new Label { AutoSize = true, Text = text, Font = Theme.Ui(8.75f), ForeColor = Theme.Muted };

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                _ssmsTimer.Dispose();
                _animTimer.Dispose();
                _tips.Dispose();
            }
            base.Dispose(disposing);
        }
    }
}
