using System;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using Microsoft.VisualStudio.Shell;
using SqlAssist.Core.Formatting;
using SqlAssist.Core.Risk;
using SqlAssist.Core.Settings;
using SqlAssist.Risk;

namespace SqlAssist.Settings
{
    /// <summary>Tools › SqlAssist › Options…: how Format SQL lays text out (with a sample that changes as the choices do) and which
    /// risks Check SQL Risks looks for. A plain WPF Window (see <see cref="DialogHost"/>), like the Snippets Manager.</summary>
    internal sealed class OptionsWindow : Window
    {
        private const string Sample =
            "select a.id,a.name,count(*) as cnt from dbo.Orders a inner join dbo.Customers c on c.id=a.cid and c.active=1 " +
            "where a.status in (1,2,3) and (a.x is null or a.y>5) -- active only\n" +
            "group by a.id,a.name order by a.id desc";

        private readonly SqlAssistSettings _settings;
        private readonly ComboBox _keywordCase = new ComboBox { Width = 260, Height = 26 };
        private readonly ComboBox _indent = new ComboBox { Width = 80, Height = 26 };
        private readonly RadioButton _leading = new RadioButton { Content = "นำหน้าบรรทัด (  , b)", GroupName = "comma", Margin = new Thickness(0, 0, 18, 0) };
        private readonly RadioButton _trailing = new RadioButton { Content = "ท้ายบรรทัด (a,  )", GroupName = "comma" };
        private readonly TextBox _preview = new TextBox
        {
            IsReadOnly = true, FontFamily = new FontFamily("Consolas"), FontSize = 13,
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto, HorizontalScrollBarVisibility = ScrollBarVisibility.Auto,
            TextWrapping = TextWrapping.NoWrap,
        };
        private readonly CheckBox _update = new CheckBox { Content = "UPDATE ที่ไม่มี WHERE", Margin = new Thickness(0, 0, 0, 6) };
        private readonly CheckBox _delete = new CheckBox { Content = "DELETE ที่ไม่มี WHERE", Margin = new Thickness(0, 0, 0, 6) };
        private readonly CheckBox _truncate = new CheckBox { Content = "TRUNCATE TABLE", Margin = new Thickness(0, 0, 0, 6) };
        private readonly CheckBox _drop = new CheckBox { Content = "DROP TABLE", Margin = new Thickness(0, 0, 0, 6) };
        private readonly CheckBox _skipTemp = new CheckBox { Content = "ข้ามตาราง temp (#...) และ table variable (@...)", Margin = new Thickness(0, 6, 0, 0) };
        private readonly CheckBox _highlightInsert = new CheckBox { Content = "ไฮไลต์คอลัมน์และค่าที่ตรงกันใน INSERT … VALUES", FontWeight = FontWeights.SemiBold };
        private readonly CheckBox _warn = new CheckBox { Content = "เตือนก่อนกด Execute (ทดลอง)", FontWeight = FontWeights.SemiBold };
        private readonly TextBlock _guardStatus = new TextBlock { TextWrapping = TextWrapping.Wrap, Foreground = Brushes.Gray, FontSize = 11, Margin = new Thickness(18, 6, 0, 0) };
        private bool _loading = true;

        public OptionsWindow()
        {
            _settings = SettingsProvider.Load();
            Title = "SqlAssist: Options";
            Width = 700;
            Height = 640;
            MinWidth = 560;
            MinHeight = 520;
            WindowStartupLocation = WindowStartupLocation.CenterOwner;
            ResizeMode = ResizeMode.CanResizeWithGrip;
            ShowInTaskbar = false;
            FontFamily = new FontFamily("Segoe UI");
            FontSize = 13;

            _keywordCase.Items.Add("ตัวพิมพ์ใหญ่ทั้งหมด (SELECT)");
            _keywordCase.Items.Add("ตัวพิมพ์เล็กทั้งหมด (select)");
            _keywordCase.Items.Add("ตามที่พิมพ์ไว้ (ไม่เปลี่ยน)");
            for (int size = FormatOptions.MinIndentSize; size <= FormatOptions.MaxIndentSize; size++) _indent.Items.Add(size);

            _keywordCase.SelectedIndex = (int)_settings.Format.KeywordCase;
            _indent.SelectedItem = _settings.Format.IndentSize;
            _leading.IsChecked = _settings.Format.CommaStyle == CommaStyle.Leading;
            _trailing.IsChecked = _settings.Format.CommaStyle == CommaStyle.Trailing;
            _update.IsChecked = _settings.Risk.UpdateWithoutWhere;
            _delete.IsChecked = _settings.Risk.DeleteWithoutWhere;
            _truncate.IsChecked = _settings.Risk.TruncateTable;
            _drop.IsChecked = _settings.Risk.DropTable;
            _skipTemp.IsChecked = _settings.Risk.SkipTempTables;
            _warn.IsChecked = _settings.Risk.WarnBeforeExecute;
            _highlightInsert.IsChecked = _settings.Editor.HighlightInsertColumns;
            _guardStatus.Text = "สถานะตอนนี้: " + ExecuteGuard.Status;

            // Building the layout needs no UI-thread check: it only creates WPF controls, which is also what lets this window be
            // opened outside SSMS to look at it. Everything that touches SSMS (Save) checks for itself.
#pragma warning disable VSTHRD010
            Content = BuildLayout();
#pragma warning restore VSTHRD010
            _keywordCase.SelectionChanged += (s, e) => Changed();
            _indent.SelectionChanged += (s, e) => Changed();
            _leading.Click += (s, e) => Changed();
            _trailing.Click += (s, e) => Changed();
            _loading = false;
            Changed();
        }

        private UIElement BuildLayout()
        {
            var root = new DockPanel { Margin = new Thickness(14) };

            var buttons = new DockPanel { LastChildFill = false, Margin = new Thickness(0, 12, 0, 0) };
            // Opens the log in the default editor for .log files, not inside SSMS: this window is modal, so a document opened in
            // SSMS would land behind it.
            var openLog = new Button
            {
                Content = "เปิดไฟล์ Log", Height = 28, Padding = new Thickness(14, 0, 14, 0), ToolTip = Log.FilePath,
            };
            openLog.Click += (s, e) => OpenLog();
            DockPanel.SetDock(openLog, Dock.Left);
            buttons.Children.Add(openLog);
            var cancel = new Button { Content = "ยกเลิก", Width = 90, Height = 28, IsCancel = true };
            cancel.Click += (s, e) => DialogResult = false;
            DockPanel.SetDock(cancel, Dock.Right);
            buttons.Children.Add(cancel);
            var save = new Button { Content = "บันทึก", Width = 90, Height = 28, IsDefault = true, Margin = new Thickness(0, 0, 8, 0) };
            save.Click += (s, e) => { ThreadHelper.ThrowIfNotOnUIThread(); Save(); };
            DockPanel.SetDock(save, Dock.Right);
            buttons.Children.Add(save);
            DockPanel.SetDock(buttons, Dock.Bottom);
            root.Children.Add(buttons);

            var location = new TextBlock
            {
                Text = "เก็บไว้ที่ " + SettingsProvider.FilePath, Foreground = Brushes.Gray, FontSize = 11,
                TextTrimming = TextTrimming.CharacterEllipsis, Margin = new Thickness(0, 8, 0, 0),
            };
            DockPanel.SetDock(location, Dock.Bottom);
            root.Children.Add(location);

            var tabs = new TabControl();
            tabs.Items.Add(new TabItem { Header = "จัดรูปแบบ SQL", Content = FormatTab() });
            tabs.Items.Add(new TabItem { Header = "ตรวจจุดเสี่ยง", Content = RiskTab() });
            tabs.Items.Add(new TabItem { Header = "Editor", Content = EditorTab() });
            root.Children.Add(tabs);
            return root;
        }

        private UIElement FormatTab()
        {
            var root = new DockPanel { Margin = new Thickness(12) };
            var form = new StackPanel();
            form.Children.Add(new TextBlock { Text = "Tools › SqlAssist › Format SQL, คลิกขวาใน query editor › Format SQL (SqlAssist) หรือ Ctrl+K, Ctrl+Shift+F", FontWeight = FontWeights.SemiBold, Margin = new Thickness(0, 0, 0, 10) });
            form.Children.Add(Row("ตัวพิมพ์ของ keyword", _keywordCase));
            form.Children.Add(Row("ย่อหน้า (จำนวนช่องว่างต่อระดับ)", _indent));
            var comma = new StackPanel { Orientation = Orientation.Horizontal };
            comma.Children.Add(_leading);
            comma.Children.Add(_trailing);
            form.Children.Add(Row("ตำแหน่งเครื่องหมาย , ในรายการ", comma));
            form.Children.Add(new TextBlock
            {
                Text = "Keyword ที่เปลี่ยนตัวพิมพ์ได้คือคำสงวนของ T-SQL กับชื่อฟังก์ชัน/ชนิดข้อมูลที่ตำแหน่งบอกชัดว่าไม่ใช่ชื่อ — ชื่อตาราง คอลัมน์ alias, ข้อความใน '...' และชื่อใน [...] ไม่ถูกเปลี่ยนเด็ดขาด",
                TextWrapping = TextWrapping.Wrap, Foreground = Brushes.Gray, FontSize = 11, Margin = new Thickness(0, 6, 0, 10),
            });
            form.Children.Add(new TextBlock { Text = "ตัวอย่าง", Margin = new Thickness(0, 0, 0, 3) });
            DockPanel.SetDock(form, Dock.Top);
            root.Children.Add(form);
            root.Children.Add(_preview);
            return root;
        }

        private UIElement RiskTab()
        {
            var panel = new StackPanel { Margin = new Thickness(12) };
            panel.Children.Add(new TextBlock { Text = "Tools › SqlAssist › Check SQL Risks ตรวจเรื่องเหล่านี้ในส่วนที่เลือก (หรือทั้งหน้า query)", FontWeight = FontWeights.SemiBold, Margin = new Thickness(0, 0, 0, 10), TextWrapping = TextWrapping.Wrap });
            panel.Children.Add(_update);
            panel.Children.Add(_delete);
            panel.Children.Add(_truncate);
            panel.Children.Add(_drop);
            panel.Children.Add(_skipTemp);
            panel.Children.Add(new TextBlock
            {
                Text = RiskAnalyzer.Disclaimer, TextWrapping = TextWrapping.Wrap, Foreground = Brushes.Gray, FontSize = 11, Margin = new Thickness(0, 10, 0, 0),
            });
            panel.Children.Add(new Separator { Margin = new Thickness(0, 14, 0, 14) });
            panel.Children.Add(_warn);
            panel.Children.Add(new TextBlock
            {
                Text = "ถ้าเปิด เมื่อกด Execute แล้ว script ที่จะรัน (ส่วนที่เลือก หรือทั้งหน้า query) มีรายการเสี่ยงตามกฎด้านบน จะขึ้นหน้าต่างบอก Server, Database และคำสั่งที่เสี่ยง " +
                       "ปุ่มเริ่มต้นคือ \"ยกเลิก\" (กด Enter หรือ Esc ก็ยกเลิก) ต้องกด \"รันต่อ\" เองถึงจะรัน Cancel Query ไม่ถูกแตะต้อง\n\n" +
                       "ยังเป็นของทดลอง: ดักที่ระดับคำสั่งของ SSMS (F5, Ctrl+E, ปุ่ม Execute, เมนู Query) แต่ยังไม่ได้ยืนยันกับทุกช่องทางที่รัน query ใน SSMS จริง จึงไม่ใช่การรับประกันว่าจะไม่มีคำสั่งเสี่ยงหลุดไปรัน " +
                       "ผลของตัวเลือกนี้เริ่มหลังกดบันทึก",
                TextWrapping = TextWrapping.Wrap, Foreground = Brushes.Gray, FontSize = 11, Margin = new Thickness(18, 4, 0, 0),
            });
            panel.Children.Add(_guardStatus);
            return new ScrollViewer { Content = panel, VerticalScrollBarVisibility = ScrollBarVisibility.Auto };
        }

        private UIElement EditorTab()
        {
            var panel = new StackPanel { Margin = new Thickness(12) };
            panel.Children.Add(_highlightInsert);
            panel.Children.Add(new TextBlock
            {
                Text = "เมื่อ caret อยู่ที่ค่าใน VALUES (…) จะแรเงาชื่อคอลัมน์ในรายการ INSERT (…) ที่อยู่ตำแหน่งเดียวกัน พร้อมกรอบสีฟ้ารอบค่านั้น " +
                       "และเมื่อ caret อยู่ที่ชื่อคอลัมน์ จะแรเงาค่าตำแหน่งเดียวกันในทุกแถวของ VALUES\n\n" +
                       "จับคู่ตามลำดับตำแหน่งจากข้อความ SQL (ไม่ต่อฐานข้อมูล) ใช้กับ INSERT ที่ระบุรายชื่อคอลัมน์และใช้ VALUES เท่านั้น ไม่รวม INSERT … SELECT / EXEC " +
                       "ถ้าจำนวนคอลัมน์กับค่าไม่เท่ากัน หรือ SQL ยังพิมพ์ไม่ครบ จะแสดงเฉพาะคู่ที่ชัดเจน\n\n" +
                       "ผลของตัวเลือกนี้เริ่มหลังกดบันทึกและขยับ caret",
                TextWrapping = TextWrapping.Wrap, Foreground = Brushes.Gray, FontSize = 11, Margin = new Thickness(18, 6, 0, 0),
            });
            return new ScrollViewer { Content = panel, VerticalScrollBarVisibility = ScrollBarVisibility.Auto };
        }

        private static UIElement Row(string label, UIElement control)
        {
            var row = new DockPanel { Margin = new Thickness(0, 0, 0, 8) };
            var text = new TextBlock { Text = label, Width = 230, VerticalAlignment = VerticalAlignment.Center };
            DockPanel.SetDock(text, Dock.Left);
            row.Children.Add(text);
            var holder = new StackPanel { Orientation = Orientation.Horizontal };
            holder.Children.Add(control);
            row.Children.Add(holder);
            return row;
        }

        private void OpenLog()
        {
            try
            {
                if (!File.Exists(Log.FilePath))
                {
                    Directory.CreateDirectory(Log.Directory);
                    File.WriteAllText(Log.FilePath, "");
                }
                try
                {
                    Process.Start(new ProcessStartInfo(Log.FilePath) { UseShellExecute = true });
                }
                catch (Win32Exception)
                {
                    // No program is associated with .log files.
                    Process.Start("notepad.exe", "\"" + Log.FilePath + "\"");
                }
            }
            catch (Exception ex)
            {
                Log.Error("Open log", ex);
                MessageBox.Show(this, "เปิดไฟล์ log ไม่สำเร็จ:\n" + ex.Message + "\n\nไฟล์อยู่ที่ " + Log.FilePath, Title, MessageBoxButton.OK, MessageBoxImage.Warning);
            }
        }

        private FormatOptions Chosen() => new FormatOptions
        {
            KeywordCase = (KeywordCase)Math.Max(0, _keywordCase.SelectedIndex),
            IndentSize = _indent.SelectedItem is int size ? size : 4,
            CommaStyle = _trailing.IsChecked == true ? CommaStyle.Trailing : CommaStyle.Leading,
            Newline = "\r\n",
        };

        private void Changed()
        {
            if (_loading) return;
            var result = SqlFormatter.Format(Sample, Chosen());
            _preview.Text = result.IsSuccess ? result.Text : result.Error;
        }

        private void Save()
        {
            ThreadHelper.ThrowIfNotOnUIThread();
            try
            {
                var chosen = Chosen();
                _settings.Format.KeywordCase = chosen.KeywordCase;
                _settings.Format.IndentSize = chosen.IndentSize;
                _settings.Format.CommaStyle = chosen.CommaStyle;
                _settings.Risk.UpdateWithoutWhere = _update.IsChecked == true;
                _settings.Risk.DeleteWithoutWhere = _delete.IsChecked == true;
                _settings.Risk.TruncateTable = _truncate.IsChecked == true;
                _settings.Risk.DropTable = _drop.IsChecked == true;
                _settings.Risk.SkipTempTables = _skipTemp.IsChecked == true;
                _settings.Risk.WarnBeforeExecute = _warn.IsChecked == true;
                _settings.Editor.HighlightInsertColumns = _highlightInsert.IsChecked == true;
                SettingsProvider.Save(_settings);

                ExecuteGuard.Apply(_settings.Risk);
                if (_settings.Risk.WarnBeforeExecute && !ExecuteGuard.IsHooked)
                    MessageBox.Show(this, "บันทึกแล้ว แต่ตัวเตือนก่อน Execute ยังไม่ทำงาน:\n\n" + ExecuteGuard.Status, Title, MessageBoxButton.OK, MessageBoxImage.Warning);
                DialogResult = true;
            }
            catch (Exception ex)
            {
                Log.Error("Saving settings", ex);
                MessageBox.Show(this, "บันทึกไม่สำเร็จ:\n" + ex.Message, Title, MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }
    }
}
