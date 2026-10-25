using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using Microsoft.VisualStudio.Imaging;
using Microsoft.VisualStudio.Imaging.Interop;
using Microsoft.VisualStudio.PlatformUI;
using SqlAssist.Core.Snippets;

namespace SqlAssist.Snippets
{
    /// <summary>Tools › SqlAssist › Edit Snippets… — list on the left, Shortcut / Description / Text on the right,
    /// literals to insert, and Save / Cancel that write or reload snippets.xml. Edits stay in memory until Save.</summary>
    /// A plain WPF Window (the caller sets SSMS as its owner) rather than VS's DialogWindow, which needs shell
    /// services at construction and so cannot even be opened outside SSMS to check its layout.
    internal sealed class SnippetManagerWindow : Window
    {
        private sealed class Entry
        {
            public string Key = "";
            public string Description = "";
            public string Body = "";
            public override string ToString() => string.IsNullOrEmpty(Description) ? Key : Key + "  —  " + Description;
        }

        private readonly List<Entry> _entries = new List<Entry>();
        private readonly ListBox _list = new ListBox { BorderThickness = new Thickness(1) };
        private readonly TextBox _filter = new TextBox();
        private readonly ComboBox _sort = new ComboBox { Width = 130 };
        private readonly TextBox _key = new TextBox();
        private readonly TextBox _description = new TextBox();
        private readonly TextBox _body = new TextBox
        {
            AcceptsReturn = true, AcceptsTab = true, FontFamily = new FontFamily("Consolas"), FontSize = 14,
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto, HorizontalScrollBarVisibility = ScrollBarVisibility.Auto,
            TextWrapping = TextWrapping.NoWrap,
        };
        private readonly ListBox _literals = new ListBox();
        private readonly Button _save;
        private readonly Button _cancel;
        private readonly Button _delete;
        private readonly TextBlock _status = new TextBlock { VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(8, 0, 0, 0) };
        private Entry _current;
        private bool _loadingFields;
        private bool _dirty;

        public SnippetManagerWindow()
        {
            Title = "SqlAssist: Snippets Manager";
            Width = 980;
            Height = 640;
            MinWidth = 720;
            MinHeight = 420;
            WindowStartupLocation = WindowStartupLocation.CenterOwner;
            ResizeMode = ResizeMode.CanResizeWithGrip;
            ShowInTaskbar = false;
            FontFamily = new FontFamily("Segoe UI");
            FontSize = 13;

            _save = ToolButton(KnownMonikers.Save, "บันทึก (Ctrl+S)", (s, e) => Save());
            _cancel = ToolButton(KnownMonikers.Undo, "ยกเลิกการแก้ไข", (s, e) => Revert());
            _delete = ToolButton(KnownMonikers.DeleteListItem, "ลบ", (s, e) => DeleteCurrent());

            Content = BuildLayout();
            Load();

            _list.SelectionChanged += (s, e) => Select(_list.SelectedItem as Entry);
            _filter.TextChanged += (s, e) => RefreshList(_current);
            _sort.SelectionChanged += (s, e) => RefreshList(_current);
            _key.TextChanged += (s, e) => FieldChanged(en => en.Key = _key.Text.Trim(), refreshList: true);
            _description.TextChanged += (s, e) => FieldChanged(en => en.Description = _description.Text, refreshList: true);
            _body.TextChanged += (s, e) => FieldChanged(en => en.Body = _body.Text, refreshList: false);
            _literals.MouseDoubleClick += (s, e) => InsertLiteral();
            PreviewKeyDown += OnKeyDown;
            Closing += OnClosing;
        }

        private UIElement BuildLayout()
        {
            var root = new DockPanel { Margin = new Thickness(10) };

            // Toolbar
            var toolbar = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 0, 0, 8) };
            toolbar.Children.Add(ToolButton(KnownMonikers.NewItem, "Snippet ใหม่", (s, e) => AddNew()));
            toolbar.Children.Add(_delete);
            toolbar.Children.Add(new Separator { Margin = new Thickness(6, 2, 6, 2) });
            toolbar.Children.Add(_save);
            toolbar.Children.Add(_cancel);
            toolbar.Children.Add(new Separator { Margin = new Thickness(6, 2, 6, 2) });
            toolbar.Children.Add(ToolButton(KnownMonikers.OpenFolder, "เปิดโฟลเดอร์", (s, e) => OpenFolder()));
            toolbar.Children.Add(_status);
            DockPanel.SetDock(toolbar, Dock.Top);
            root.Children.Add(toolbar);

            // Bottom: location + close
            var bottom = new DockPanel { Margin = new Thickness(0, 8, 0, 0) };
            var close = new Button { Content = "ปิด", Width = 90, Height = 28, IsCancel = true };
            close.Click += (s, e) => Close();
            DockPanel.SetDock(close, Dock.Right);
            bottom.Children.Add(close);
            var location = new TextBlock { VerticalAlignment = VerticalAlignment.Center, TextTrimming = TextTrimming.CharacterEllipsis };
            location.Inlines.Add(new System.Windows.Documents.Run("ที่เก็บไฟล์: ") { Foreground = Brushes.Gray });
            location.Inlines.Add(new System.Windows.Documents.Run(SnippetProvider.FilePath));
            bottom.Children.Add(location);
            DockPanel.SetDock(bottom, Dock.Bottom);
            root.Children.Add(bottom);

            // Body: list | splitter | editor
            var body = new Grid();
            body.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(300), MinWidth = 180 });
            body.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(6) });
            body.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

            var left = new DockPanel();
            var sortRow = new DockPanel { Margin = new Thickness(0, 0, 0, 4) };
            _sort.Items.Add("เรียงตาม Shortcut");
            _sort.Items.Add("เรียงตามคำอธิบาย");
            _sort.SelectedIndex = 0;
            DockPanel.SetDock(_sort, Dock.Right);
            sortRow.Children.Add(_sort);
            sortRow.Children.Add(Labeled("ค้นหา", _filter, new Thickness(0, 0, 6, 0)));
            DockPanel.SetDock(sortRow, Dock.Top);
            left.Children.Add(sortRow);
            left.Children.Add(_list);
            body.Children.Add(left);

            var splitter = new GridSplitter { Width = 6, HorizontalAlignment = HorizontalAlignment.Stretch, Background = Brushes.Transparent };
            Grid.SetColumn(splitter, 1);
            body.Children.Add(splitter);

            var editor = new Grid();
            editor.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            editor.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(10) });
            editor.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(220) });
            editor.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            editor.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            editor.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            editor.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });

            var keyRow = new Grid { Margin = new Thickness(0, 0, 0, 8) };
            keyRow.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(200) });
            keyRow.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(10) });
            keyRow.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            keyRow.Children.Add(Stacked("Shortcut (พิมพ์แล้วกด Tab)", _key));
            var desc = Stacked("คำอธิบาย", _description);
            Grid.SetColumn(desc, 2);
            keyRow.Children.Add(desc);
            Grid.SetColumnSpan(keyRow, 3);
            editor.Children.Add(keyRow);

            var textLabel = new TextBlock { Text = "ข้อความ (Text)", Margin = new Thickness(0, 0, 0, 3) };
            Grid.SetRow(textLabel, 1);
            editor.Children.Add(textLabel);
            Grid.SetRow(_body, 2);
            Grid.SetRowSpan(_body, 2);
            editor.Children.Add(_body);

            var literalsLabel = new TextBlock { Text = "Literals (ดับเบิลคลิกเพื่อแทรก)", Margin = new Thickness(0, 0, 0, 3) };
            Grid.SetRow(literalsLabel, 1);
            Grid.SetColumn(literalsLabel, 2);
            editor.Children.Add(literalsLabel);
            foreach (var l in SnippetLiterals.All)
            {
                var item = new StackPanel { Margin = new Thickness(2) , ToolTip = l.Description };
                item.Children.Add(new TextBlock { Text = l.Marker, FontWeight = FontWeights.SemiBold });
                item.Children.Add(new TextBlock { Text = l.Description, FontSize = 11, Foreground = Brushes.Gray, TextWrapping = TextWrapping.Wrap });
                item.Tag = l;
                _literals.Items.Add(item);
            }
            var insert = new Button { Content = "แทรก", Height = 26, Margin = new Thickness(0, 6, 0, 0) };
            insert.Click += (s, e) => InsertLiteral();
            var literalsPanel = new DockPanel();
            DockPanel.SetDock(insert, Dock.Bottom);
            literalsPanel.Children.Add(insert);
            literalsPanel.Children.Add(_literals);
            Grid.SetRow(literalsPanel, 2);
            Grid.SetRowSpan(literalsPanel, 2);
            Grid.SetColumn(literalsPanel, 2);
            editor.Children.Add(literalsPanel);

            Grid.SetColumn(editor, 2);
            body.Children.Add(editor);
            root.Children.Add(body);
            return root;
        }

        private void Load()
        {
            _entries.Clear();
            try
            {
                SnippetProvider.EnsureFileExists();
                var store = SnippetStore.Parse(File.ReadAllText(SnippetProvider.FilePath));
                _entries.AddRange(store.All.Select(s => new Entry { Key = s.Key, Description = s.Description ?? "", Body = s.Body.Replace("\n", "\r\n") }));
            }
            catch (Exception ex)
            {
                Log.Error("Snippets Manager: reading " + SnippetProvider.FilePath, ex);
                string copy = SnippetProvider.BackupUnreadable();
                MessageBox.Show(this, "อ่านไฟล์ snippets ไม่ได้:\n" + ex.Message
                    + (copy == null ? "" : "\n\nถ้าบันทึกจากหน้านี้ snippets.xml จะถูกแทนที่ด้วยรายการในหน้านี้ สำเนาของไฟล์เดิมเก็บไว้ที่:\n" + copy),
                    Title, MessageBoxButton.OK, MessageBoxImage.Warning);
            }
            SetDirty(false);
            RefreshList(null);
            if (_list.Items.Count > 0) _list.SelectedIndex = 0;
            // Explicitly: on first load the SelectionChanged handler is not attached yet.
            Select(_list.SelectedItem as Entry);
        }

        private void RefreshList(Entry keep)
        {
            string filter = _filter.Text.Trim();
            IEnumerable<Entry> view = _entries.Where(e => filter.Length == 0 ||
                e.Key.IndexOf(filter, StringComparison.OrdinalIgnoreCase) >= 0 ||
                e.Description.IndexOf(filter, StringComparison.OrdinalIgnoreCase) >= 0 ||
                e.Body.IndexOf(filter, StringComparison.OrdinalIgnoreCase) >= 0);
            view = _sort.SelectedIndex == 1
                ? view.OrderBy(e => e.Description, StringComparer.CurrentCultureIgnoreCase).ThenBy(e => e.Key, StringComparer.OrdinalIgnoreCase)
                : view.OrderBy(e => e.Key, StringComparer.OrdinalIgnoreCase);

            _loadingFields = true; // re-filling the list must not count as the user picking an item
            _list.Items.Clear();
            foreach (var e in view) _list.Items.Add(e);
            _list.SelectedItem = keep != null && _list.Items.Contains(keep) ? keep : null;
            _loadingFields = false;
        }

        private void Select(Entry entry)
        {
            if (_loadingFields) return;
            _current = entry;
            _loadingFields = true;
            _key.Text = entry?.Key ?? "";
            _description.Text = entry?.Description ?? "";
            _body.Text = entry?.Body ?? "";
            _loadingFields = false;
            _key.IsEnabled = _description.IsEnabled = _body.IsEnabled = _delete.IsEnabled = entry != null;
        }

        private void FieldChanged(Action<Entry> apply, bool refreshList)
        {
            if (_loadingFields || _current == null) return;
            apply(_current);
            SetDirty(true);
            if (refreshList) _list.Items.Refresh();
        }

        private void AddNew()
        {
            int n = 1;
            while (_entries.Any(e => e.Key.Equals("new" + n, StringComparison.OrdinalIgnoreCase))) n++;
            var entry = new Entry { Key = "new" + n, Description = "", Body = "$CARET$" };
            _entries.Add(entry);
            _filter.Text = "";
            RefreshList(entry);
            _list.SelectedItem = entry;
            Select(entry);
            SetDirty(true);
            _key.Focus();
            _key.SelectAll();
        }

        private void DeleteCurrent()
        {
            if (_current == null) return;
            if (MessageBox.Show(this, $"ลบ snippet \"{_current.Key}\" ใช่ไหม?", Title, MessageBoxButton.YesNo, MessageBoxImage.Question) != MessageBoxResult.Yes) return;
            _entries.Remove(_current);
            SetDirty(true);
            RefreshList(null);
            if (_list.Items.Count > 0) _list.SelectedIndex = 0; else Select(null);
        }

        private bool Save()
        {
            var snippets = _entries.Select(e => new Snippet(e.Key.Trim(), e.Body.Replace("\r\n", "\n"), e.Description.Trim())).ToList();
            var errors = SnippetStore.Validate(snippets);
            if (errors.Count > 0)
            {
                MessageBox.Show(this, "ยังบันทึกไม่ได้:\n\n• " + string.Join("\n• ", errors), Title, MessageBoxButton.OK, MessageBoxImage.Warning);
                return false;
            }
            try
            {
                SnippetProvider.Save(snippets);
                SetDirty(false);
                _status.Text = $"บันทึกแล้ว {DateTime.Now:HH:mm:ss}";
                return true;
            }
            catch (Exception ex)
            {
                Log.Error("Snippets Manager: saving", ex);
                MessageBox.Show(this, "บันทึกไม่สำเร็จ:\n" + ex.Message, Title, MessageBoxButton.OK, MessageBoxImage.Error);
                return false;
            }
        }

        private void Revert()
        {
            if (_dirty && MessageBox.Show(this, "ยกเลิกการแก้ไขทั้งหมดที่ยังไม่บันทึก แล้วโหลดจากไฟล์ใหม่?", Title,
                    MessageBoxButton.YesNo, MessageBoxImage.Question) != MessageBoxResult.Yes) return;
            _filter.Text = "";
            Load();
            _status.Text = "";
        }

        private void InsertLiteral()
        {
            if (_current == null || !(_literals.SelectedItem is FrameworkElement fe) || !(fe.Tag is SnippetLiteral l)) return;
            int at = _body.CaretIndex;
            _body.SelectedText = l.Marker;
            _body.CaretIndex = at + l.Marker.Length;
            _body.SelectionLength = 0;
            _body.Focus();
        }

        private static void OpenFolder()
        {
            try { Process.Start("explorer.exe", "/select,\"" + SnippetProvider.FilePath + "\""); }
            catch (Exception ex) { Log.Error("Snippets Manager: open folder", ex); }
        }

        private void SetDirty(bool dirty)
        {
            _dirty = dirty;
            _save.IsEnabled = _cancel.IsEnabled = dirty;
            if (dirty) _status.Text = "มีการแก้ไขที่ยังไม่บันทึก";
        }

        private void OnKeyDown(object sender, KeyEventArgs e)
        {
            if (e.Key == Key.S && Keyboard.Modifiers == ModifierKeys.Control)
            {
                e.Handled = true;
                if (_dirty) Save();
            }
        }

        private void OnClosing(object sender, System.ComponentModel.CancelEventArgs e)
        {
            if (!_dirty) return;
            var answer = MessageBox.Show(this, "บันทึกการแก้ไขก่อนปิดไหม?", Title, MessageBoxButton.YesNoCancel, MessageBoxImage.Question);
            if (answer == MessageBoxResult.Cancel || (answer == MessageBoxResult.Yes && !Save())) e.Cancel = true;
        }

        private static Button ToolButton(ImageMoniker icon, string text, RoutedEventHandler click)
        {
            var content = new StackPanel { Orientation = Orientation.Horizontal };
            content.Children.Add(new CrispImage { Moniker = icon, Width = 16, Height = 16, Margin = new Thickness(0, 0, 5, 0) });
            content.Children.Add(new TextBlock { Text = text, VerticalAlignment = VerticalAlignment.Center });
            var button = new Button { Content = content, Padding = new Thickness(8, 3, 8, 3), Margin = new Thickness(0, 0, 4, 0) };
            button.Click += click;
            return button;
        }

        private static UIElement Stacked(string label, Control control)
        {
            var panel = new StackPanel();
            panel.Children.Add(new TextBlock { Text = label, Margin = new Thickness(0, 0, 0, 3) });
            control.Height = 26;
            control.VerticalContentAlignment = VerticalAlignment.Center;
            panel.Children.Add(control);
            return panel;
        }

        private static UIElement Labeled(string label, Control control, Thickness margin)
        {
            var panel = new DockPanel { Margin = margin };
            var text = new TextBlock { Text = label, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 6, 0) };
            DockPanel.SetDock(text, Dock.Left);
            panel.Children.Add(text);
            control.Height = 24;
            control.VerticalContentAlignment = VerticalAlignment.Center;
            panel.Children.Add(control);
            return panel;
        }
    }
}
