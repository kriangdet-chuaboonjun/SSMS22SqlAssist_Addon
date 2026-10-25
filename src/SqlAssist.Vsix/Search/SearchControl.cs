using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Data;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;
using Microsoft.VisualStudio.PlatformUI;
using Microsoft.VisualStudio.Shell;
using SqlAssist.Connection;
using SqlAssist.Core.Search;

namespace SqlAssist.Search
{
    /// <summary>One row of the results grid. Plain strings so the grid can sort every column.</summary>
    internal sealed class ResultRow
    {
        public ResultRow(SearchMatch match)
        {
            Match = match;
            var item = match.Item;
            Name = item.Name;
            Schema = item.Schema ?? "";
            Type = SearchControl.TypeLabel(item);
            Parent = item.Parent ?? "";
            Database = item.Database;
            MatchedIn = match.Location == (MatchLocation.Name | MatchLocation.Definition) ? "Name, Definition" : match.Location.ToString();
        }

        public SearchMatch Match { get; }
        public string Name { get; }
        public string Schema { get; }
        public string Type { get; }
        public string Parent { get; }
        public string Database { get; }
        public string MatchedIn { get; }
    }

    /// <summary>The Search window's content, built in code like the completion presenter. Search as you type: every
    /// change restarts a short timer, and each search cancels the one before it. Indexes load per database through
    /// <see cref="SearchIndexCache"/>; matching runs off the UI thread.</summary>
    internal sealed class SearchControl : DockPanel
    {
        private const int ResultLimit = 5000;
        private static readonly TimeSpan Debounce = TimeSpan.FromMilliseconds(250);
        private static readonly Brush HitBrush = Freeze(new SolidColorBrush(Color.FromArgb(0x90, 0xFF, 0xC8, 0x00)));

        private static readonly (SearchObjectType type, string label)[] TypeChoices =
        {
            (SearchObjectType.Schema, "Schema"), (SearchObjectType.Table, "Table"), (SearchObjectType.View, "View"),
            (SearchObjectType.Procedure, "Procedure"), (SearchObjectType.Function, "Function"),
            (SearchObjectType.Trigger, "Trigger"), (SearchObjectType.Column, "Column"), (SearchObjectType.Index, "Index"),
            (SearchObjectType.Constraint, "Constraint"), (SearchObjectType.UserDefinedType, "User-defined type"),
        };

        /// <summary>The Type column's text: "User-defined type", "Constraint (Check)".</summary>
        internal static string TypeLabel(SearchItem item)
        {
            string label = TypeChoices.FirstOrDefault(c => c.type == item.Type).label ?? item.Type.ToString();
            return item.SubKind == null ? label : label + " (" + item.SubKind + ")";
        }

        private readonly SearchSettings _settings = new SearchSettings();
        private readonly ObservableCollection<ResultRow> _results = new ObservableCollection<ResultRow>();
        private readonly DispatcherTimer _timer;
        private readonly TextBox _searchBox;
        private readonly DropDownButton _typesButton;
        private readonly DropDownButton _databasesButton;
        private readonly DropDownButton _serverButton;
        private readonly TextBlock _status;
        private readonly DataGrid _grid;
        private readonly RichTextBox _preview;

        private ConnectionTarget _server;
        /// <summary>A server picked from the server dropdown; null follows the active query window. Cleared when the
        /// active query window moves to another server, so the newest connection wins.</summary>
        private string _pinnedServerKey;
        private string _lastActiveServerKey;
        private List<string> _databases;
        private readonly SortedSet<string> _selected = new SortedSet<string>(StringComparer.OrdinalIgnoreCase);
        private bool _allDatabases;
        /// <summary>Until the user picks databases, the selection follows the query window's database.</summary>
        private bool _selectionTouched;
        private CancellationTokenSource _cts;
        private SearchMatcher _matcher;

        public SearchControl()
        {
            SetResourceReference(BackgroundProperty, EnvironmentColors.ToolWindowBackgroundBrushKey);
            SetResourceReference(TextElement.ForegroundProperty, EnvironmentColors.ToolWindowTextBrushKey);
            TextElement.SetFontFamily(this, new FontFamily("Segoe UI"));
            LastChildFill = true;

            _timer = new DispatcherTimer { Interval = Debounce };
            _timer.Tick += (s, e) => { _timer.Stop(); StartSearch(); };

            // One toolbar, dbForge style: [options ▾] [search box] [object types ▾] [database ▾] [server ▾] [Refresh].
            _searchBox = new TextBox { MinWidth = 160, Margin = new Thickness(0, 0, 6, 0), VerticalContentAlignment = VerticalAlignment.Center };
            _searchBox.TextChanged += (s, e) => Schedule();
            _searchBox.PreviewKeyDown += OnSearchKey;

            var optionsButton = new DropDownButton(46, "Match options") { Text = "⚙", Margin = new Thickness(0, 0, 6, 0) };
            optionsButton.Click += (s, e) => ShowOptionsMenu(optionsButton);
            _typesButton = new DropDownButton(150, "Object types to search") { Margin = new Thickness(0, 0, 6, 0) };
            _typesButton.Click += (s, e) => ShowTypesMenu();
            _databasesButton = new DropDownButton(170, "Databases to search") { Margin = new Thickness(0, 0, 6, 0) };
            _databasesButton.Click += (s, e) => ThreadHelper.JoinableTaskFactory.RunAsync(ShowDatabaseMenuAsync).FileAndForget("SqlAssist/Search/Databases");
            _serverButton = new DropDownButton(170, "Server: the active query window's, or one used earlier this session") { Margin = new Thickness(0, 0, 6, 0) };
            _serverButton.Click += (s, e) => ShowServerMenu();
            var refresh = new Button { Content = "Refresh", Padding = new Thickness(8, 1, 8, 1), ToolTip = "Reload the search index from the server" };
            refresh.Click += (s, e) => { SearchIndexCache.Instance.Clear(); _databases = null; StartSearch(); };
            UpdateTypesButton();

            // Right-docked children fill from the right edge inward; the search box takes what is left.
            var searchRow = new DockPanel { Margin = new Thickness(6, 6, 6, 3) };
            SetDock(optionsButton, Dock.Left);
            searchRow.Children.Add(optionsButton);
            foreach (var e in new UIElement[] { refresh, _serverButton, _databasesButton, _typesButton })
            {
                SetDock(e, Dock.Right);
                searchRow.Children.Add(e);
            }
            searchRow.Children.Add(_searchBox);

            _status = new TextBlock { Margin = new Thickness(6, 0, 6, 4), Opacity = 0.8, TextTrimming = TextTrimming.CharacterEllipsis };

            _grid = BuildGrid();
            _preview = new RichTextBox
            {
                IsReadOnly = true,
                IsReadOnlyCaretVisible = true,
                FontFamily = new FontFamily("Consolas"),
                FontSize = 12,
                BorderThickness = new Thickness(0),
                HorizontalScrollBarVisibility = ScrollBarVisibility.Auto,
                VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
            };
            _preview.SetResourceReference(Control.BackgroundProperty, EnvironmentColors.ToolWindowBackgroundBrushKey);
            _preview.SetResourceReference(Control.ForegroundProperty, EnvironmentColors.ToolWindowTextBrushKey);
            ShowPreview(null);

            var split = new Grid();
            split.RowDefinitions.Add(new RowDefinition { Height = new GridLength(3, GridUnitType.Star), MinHeight = 60 });
            split.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            split.RowDefinitions.Add(new RowDefinition { Height = new GridLength(2, GridUnitType.Star), MinHeight = 40 });
            var splitter = new GridSplitter { Height = 4, HorizontalAlignment = HorizontalAlignment.Stretch, ResizeDirection = GridResizeDirection.Rows };
            splitter.SetResourceReference(Control.BackgroundProperty, EnvironmentColors.ToolWindowBorderBrushKey);
            Grid.SetRow(_grid, 0);
            Grid.SetRow(splitter, 1);
            Grid.SetRow(_preview, 2);
            split.Children.Add(_grid);
            split.Children.Add(splitter);
            split.Children.Add(_preview);

            SetDock(searchRow, Dock.Top);
            SetDock(_status, Dock.Top);
            Children.Add(searchRow);
            Children.Add(_status);
            Children.Add(split);

            SetStatus("Type at least " + SearchQuery.MinLength + " characters to search.");
            Loaded += (s, e) =>
            {
                ThreadHelper.ThrowIfNotOnUIThread();
                UpdateServer();
            };
        }

        public void FocusSearch()
        {
            ThreadHelper.ThrowIfNotOnUIThread();
            UpdateServer();
            Dispatcher.BeginInvoke(DispatcherPriority.Input, new Action(() =>
            {
                _searchBox.Focus();
                _searchBox.SelectAll();
            }));
        }

        private static ContextMenu MenuFor(FrameworkElement owner) =>
            new ContextMenu { PlacementTarget = owner, Placement = PlacementMode.Bottom, MinWidth = owner.ActualWidth };

        /// <summary>A check item whose text is shown as-is. A plain string Header treats "_" as an access-key marker,
        /// so AIP_Budget would show as AIPBudget. The raw text is kept in Tag.</summary>
        private static MenuItem Checkable(string header, bool isChecked) =>
            new MenuItem { Header = new TextBlock { Text = header }, Tag = header, IsCheckable = true, IsChecked = isChecked, StaysOpenOnClick = true };

        private void ShowOptionsMenu(FrameworkElement owner)
        {
            var menu = MenuFor(owner);
            void Add(string header, bool value, Action<bool> set)
            {
                var item = Checkable(header, value);
                item.Click += (s, e) =>
                {
                    set(item.IsChecked);
                    StartSearch();
                };
                menu.Items.Add(item);
            }
            Add("Match case", _settings.MatchCase, on => _settings.MatchCase = on);
            Add("Match whole word", _settings.WholeWord, on => _settings.WholeWord = on);
            Add("Use wildcards (* ?)", _settings.UseWildcards, on => _settings.UseWildcards = on);
            menu.IsOpen = true;
        }

        private void ShowTypesMenu()
        {
            var menu = MenuFor(_typesButton);
            var all = Checkable("All object types", _settings.Types == SearchObjectType.All);
            menu.Items.Add(all);
            menu.Items.Add(new Separator());
            var typeItems = new List<(MenuItem item, SearchObjectType type)>();
            foreach (var (type, label) in TypeChoices)
            {
                var item = Checkable(label, (_settings.Types & type) != 0);
                item.Click += (s, e) =>
                {
                    _settings.Types = item.IsChecked ? _settings.Types | type : _settings.Types & ~type;
                    all.IsChecked = _settings.Types == SearchObjectType.All;
                    TypesChanged();
                };
                typeItems.Add((item, type));
                menu.Items.Add(item);
            }
            all.Click += (s, e) =>
            {
                _settings.Types = all.IsChecked ? SearchObjectType.All : SearchObjectType.None;
                foreach (var (item, type) in typeItems) item.IsChecked = (_settings.Types & type) != 0;
                TypesChanged();
            };
            menu.IsOpen = true;
        }

        private void TypesChanged()
        {
            UpdateTypesButton();
            StartSearch();
        }

        private void UpdateTypesButton()
        {
            var chosen = TypeChoices.Where(c => (_settings.Types & c.type) != 0).Select(c => c.label).ToList();
            _typesButton.Text = chosen.Count == TypeChoices.Length ? "All object types"
                : chosen.Count == 0 ? "No object types"
                : chosen.Count <= 2 ? string.Join(", ", chosen)
                : chosen.Count + " object types";
        }

        private void ShowServerMenu()
        {
            ThreadHelper.ThrowIfNotOnUIThread();
            var menu = MenuFor(_serverButton);
            var follow = Checkable("Active query window", _pinnedServerKey == null);
            follow.StaysOpenOnClick = false;
            follow.Click += (s, e) => { _pinnedServerKey = null; StartSearch(); };
            menu.Items.Add(follow);
            menu.Items.Add(new Separator());
            var seen = ActiveConnection.SeenServers;
            if (seen.Count == 0) menu.Items.Add(new MenuItem { Header = "(no connection yet)", IsEnabled = false });
            foreach (var target in seen)
            {
                var item = Checkable(target.Server, _server != null && target.ServerKey == _server.ServerKey);
                item.StaysOpenOnClick = false;
                item.ToolTip = target.ServerKey.Contains("|windows") ? "Windows authentication" : "SQL login";
                string key = target.ServerKey;
                item.Click += (s, e) => { _pinnedServerKey = key; StartSearch(); };
                menu.Items.Add(item);
            }
            menu.IsOpen = true;
        }

        private DataGrid BuildGrid()
        {
            var grid = new DataGrid
            {
                ItemsSource = _results,
                AutoGenerateColumns = false,
                IsReadOnly = true,
                CanUserAddRows = false,
                SelectionMode = DataGridSelectionMode.Single,
                HeadersVisibility = DataGridHeadersVisibility.Column,
                GridLinesVisibility = DataGridGridLinesVisibility.None,
                BorderThickness = new Thickness(0),
                RowBackground = Brushes.Transparent,
            };
            grid.SetResourceReference(Control.BackgroundProperty, EnvironmentColors.ToolWindowBackgroundBrushKey);
            grid.SetResourceReference(Control.ForegroundProperty, EnvironmentColors.ToolWindowTextBrushKey);

            var header = new Style(typeof(DataGridColumnHeader));
            header.Setters.Add(new Setter(Control.BackgroundProperty, new DynamicResourceExtension(EnvironmentColors.ToolWindowBackgroundBrushKey)));
            header.Setters.Add(new Setter(Control.ForegroundProperty, new DynamicResourceExtension(EnvironmentColors.ToolWindowTextBrushKey)));
            header.Setters.Add(new Setter(Control.BorderBrushProperty, new DynamicResourceExtension(EnvironmentColors.ToolWindowBorderBrushKey)));
            header.Setters.Add(new Setter(Control.BorderThicknessProperty, new Thickness(0, 0, 1, 1)));
            header.Setters.Add(new Setter(Control.PaddingProperty, new Thickness(6, 2, 6, 2)));
            grid.ColumnHeaderStyle = header;

            foreach (var (title, path, width) in new[]
            {
                ("Name", nameof(ResultRow.Name), 220.0), ("Schema", nameof(ResultRow.Schema), 90.0), ("Type", nameof(ResultRow.Type), 140.0),
                ("Parent", nameof(ResultRow.Parent), 160.0), ("Database", nameof(ResultRow.Database), 130.0), ("Matched in", nameof(ResultRow.MatchedIn), 120.0),
            })
            {
                grid.Columns.Add(new DataGridTextColumn { Header = title, Binding = new Binding(path), Width = new DataGridLength(width), SortMemberPath = path });
            }

            grid.SelectionChanged += (s, e) => ShowPreview((grid.SelectedItem as ResultRow)?.Match);

            // Jump to a result: double-click / Enter selects it in Object Explorer, Ctrl+Enter opens its script.
            grid.MouseDoubleClick += (s, e) =>
            {
                if (e.OriginalSource is DependencyObject d && FindRow(d) != null) { Locate(); e.Handled = true; }
            };
            grid.PreviewKeyDown += (s, e) =>
            {
                if (e.Key != Key.Enter || grid.SelectedItem == null) return;
                if ((Keyboard.Modifiers & ModifierKeys.Control) != 0) OpenScript(); else Locate();
                e.Handled = true;
            };
            var rowMenu = new ContextMenu();
            var locate = new MenuItem { Header = "Locate in Object Explorer", InputGestureText = "Enter / Double-click" };
            locate.Click += (s, e) => Locate();
            var open = new MenuItem { Header = "Open in new query window", InputGestureText = "Ctrl+Enter" };
            open.Click += (s, e) => OpenScript();
            var copy = new MenuItem { Header = "Copy name", InputGestureText = "Ctrl+C" };
            copy.Click += (s, e) => CopyName();
            rowMenu.Items.Add(locate);
            rowMenu.Items.Add(open);
            rowMenu.Items.Add(new Separator());
            rowMenu.Items.Add(copy);
            var rowStyle = new Style(typeof(DataGridRow));
            rowStyle.Setters.Add(new Setter(FrameworkElement.ContextMenuProperty, rowMenu));
            // Right-click selects the row first, so the menu acts on what was clicked.
            rowStyle.Setters.Add(new EventSetter(UIElement.PreviewMouseRightButtonDownEvent, new MouseButtonEventHandler((s, e) => ((DataGridRow)s).IsSelected = true)));
            grid.RowStyle = rowStyle;
            grid.CommandBindings.Add(new CommandBinding(ApplicationCommands.Copy, (s, e) => CopyName()));
            return grid;
        }

        private static DataGridRow FindRow(DependencyObject d)
        {
            while (d != null && !(d is DataGridRow)) d = d is Visual || d is System.Windows.Media.Media3D.Visual3D ? VisualTreeHelper.GetParent(d) : LogicalTreeHelper.GetParent(d);
            return d as DataGridRow;
        }

        private SearchItem SelectedItem => (_grid.SelectedItem as ResultRow)?.Match.Item;

        private void Locate()
        {
            var item = SelectedItem;
            var server = _server;
            if (item == null || server == null) return;
            SetStatus("Locating " + item.Name + " in Object Explorer…");
            ThreadHelper.JoinableTaskFactory.RunAsync(async () =>
            {
                string problem = await SsmsNavigator.LocateAsync(server, item, CancellationToken.None);
                await ThreadHelper.JoinableTaskFactory.SwitchToMainThreadAsync();
                SetStatus(problem ?? "Selected " + item.Name + " in Object Explorer.");
            }).FileAndForget("SqlAssist/Search/Locate");
        }

        private void OpenScript()
        {
            ThreadHelper.ThrowIfNotOnUIThread();
            var item = SelectedItem;
            if (item == null || _server == null) return;
            var hits = _matcher?.FindAll(item.PreviewText);
            var first = hits != null && hits.Count > 0 ? hits[0] : new TextSpan(-1, 0);
            string problem = SsmsNavigator.OpenScript(_server, item, first.Start, first.Length);
            if (problem != null) SetStatus(problem);
        }

        private void CopyName()
        {
            var item = SelectedItem;
            if (item == null) return;
            string name = item.Schema == null ? TableScriptBuilder.Bracket(item.Name)
                : item.Type == SearchObjectType.Column || item.Type == SearchObjectType.Index || item.Type == SearchObjectType.Constraint
                    ? TableScriptBuilder.Bracket(item.Name)
                : TableScriptBuilder.Bracket(item.Schema) + "." + TableScriptBuilder.Bracket(item.Name);
            try { Clipboard.SetText(name); } catch (Exception ex) { Log.Error("Copy name", ex); }
        }

        private void OnSearchKey(object sender, KeyEventArgs e)
        {
            if (e.Key == Key.Enter)
            {
                _timer.Stop();
                StartSearch();
                e.Handled = true;
            }
            else if (e.Key == Key.Down && _results.Count > 0)
            {
                _grid.SelectedIndex = Math.Max(_grid.SelectedIndex, 0);
                _grid.Focus();
                e.Handled = true;
            }
        }

        private void Schedule()
        {
            _timer.Stop();
            _timer.Start();
        }

        private void StartSearch() => ThreadHelper.JoinableTaskFactory.RunAsync(SearchAsync).FileAndForget("SqlAssist/Search");

        /// <summary>Reads the query window's connection, or the server picked from the dropdown. A different server or
        /// login resets the database choice.</summary>
        private ConnectionTarget UpdateServer()
        {
            ThreadHelper.ThrowIfNotOnUIThread();
            var active = ActiveConnection.TryGetForToolWindow();
            if (active != null && active.ServerKey != _lastActiveServerKey)
            {
                _lastActiveServerKey = active.ServerKey;
                _pinnedServerKey = null;
            }
            var target = _pinnedServerKey == null ? active
                : ActiveConnection.SeenServers.FirstOrDefault(t => t.ServerKey == _pinnedServerKey) ?? active;
            if (target == null)
            {
                _serverButton.Text = "No connection";
                return null;
            }
            if (_server == null || _server.ServerKey != target.ServerKey)
            {
                _databases = null;
                _selectionTouched = false;
                _allDatabases = false;
                _selected.Clear();
            }
            _server = target;
            if (!_selectionTouched && !string.IsNullOrEmpty(target.Database))
            {
                _selected.Clear();
                _selected.Add(target.Database);
            }
            _serverButton.Text = target.Server;
            UpdateDatabasesButton();
            return target;
        }

        private void UpdateDatabasesButton()
        {
            _databasesButton.Text = _allDatabases ? "All databases"
                : _selected.Count == 1 ? _selected.First()
                : _selected.Count == 0 ? "Databases"
                : _selected.Count + " databases";
        }

        private async Task SearchAsync()
        {
            await ThreadHelper.JoinableTaskFactory.SwitchToMainThreadAsync();
            _cts?.Cancel();
            var cts = _cts = new CancellationTokenSource();
            var token = cts.Token;
            _results.Clear();
            ShowPreview(null);

            try
            {
                var server = UpdateServer();
                if (server == null)
                {
                    SetStatus("Open a query window connected to a server (Entra ID connections are not supported yet).");
                    return;
                }

                var matcher = SearchMatcher.Create(new SearchQuery
                {
                    Text = _searchBox.Text,
                    MatchCase = _settings.MatchCase,
                    WholeWord = _settings.WholeWord,
                    UseWildcards = _settings.UseWildcards,
                    Types = _settings.Types,
                });
                _matcher = matcher;
                if (_settings.Types == SearchObjectType.None) { SetStatus("Select at least one object type."); return; }
                if (matcher.IsEmpty) { SetStatus("Type at least " + SearchQuery.MinLength + " characters to search."); return; }

                if (_selected.Count == 0 && !_allDatabases)
                {
                    string current = await SearchIndexLoader.CurrentDatabaseAsync(server.ConnectionString, token);
                    if (token.IsCancellationRequested) return;
                    if (!string.IsNullOrEmpty(current)) _selected.Add(current);
                    UpdateDatabasesButton();
                }

                List<string> databases;
                if (_allDatabases)
                {
                    var loaded = _databases ?? await SearchIndexLoader.LoadDatabasesAsync(server.ConnectionString, token);
                    if (token.IsCancellationRequested) return;
                    // UpdateServer clears the list when the server changes: one that finished loading after a switch isn't kept.
                    if (_server?.ServerKey == server.ServerKey) _databases = loaded;
                    databases = loaded;
                }
                else databases = _selected.ToList();

                var skipped = new List<string>();
                int searched = 0;
                for (int i = 0; i < databases.Count && _results.Count < ResultLimit; i++)
                {
                    string db = databases[i];
                    SetStatus($"Searching {db} ({i + 1}/{databases.Count})… {_results.Count} found");
                    IReadOnlyList<SearchItem> items;
                    try
                    {
                        items = await SearchIndexCache.Instance.GetAsync(server, db, token);
                    }
                    catch (OperationCanceledException) { return; }
                    catch (Exception)
                    {
                        skipped.Add(db); // already logged by the cache
                        continue;
                    }
                    if (token.IsCancellationRequested) return;

                    int room = ResultLimit - _results.Count;
                    var found = await Task.Run(() => matcher.Search(items, room), token);
                    if (token.IsCancellationRequested) return;
                    foreach (var m in found) _results.Add(new ResultRow(m));
                    searched++;
                }

                string summary = $"{_results.Count} result{(_results.Count == 1 ? "" : "s")} in {searched} database{(searched == 1 ? "" : "s")}";
                if (_results.Count >= ResultLimit) summary += $" (first {ResultLimit} shown; narrow the search)";
                if (skipped.Count > 0) summary += " · skipped (see log): " + string.Join(", ", skipped);
                SetStatus(summary);
                if (_results.Count > 0) _grid.SelectedIndex = 0;
            }
            catch (OperationCanceledException)
            {
                // Superseded by a newer search.
            }
            catch (Exception ex)
            {
                if (token.IsCancellationRequested) return;
                Log.Error("Search", ex);
                SetStatus("Search failed: " + ex.Message);
            }
        }

        private async Task ShowDatabaseMenuAsync()
        {
            await ThreadHelper.JoinableTaskFactory.SwitchToMainThreadAsync();
            var server = UpdateServer();
            if (server == null) { SetStatus("Open a query window connected to a server first."); return; }
            var databases = _databases;
            if (databases == null)
            {
                try
                {
                    databases = await SearchIndexLoader.LoadDatabasesAsync(server.ConnectionString, CancellationToken.None);
                }
                catch (Exception ex)
                {
                    Log.Error("Listing databases of " + server.Server, ex);
                    databases = new List<string>();
                }
                // As in SearchAsync: a list for a server that is no longer the current one isn't kept.
                if (_server?.ServerKey == server.ServerKey) _databases = databases;
            }

            var menu = MenuFor(_databasesButton);
            var all = new MenuItem { Header = "All databases", IsCheckable = true, IsChecked = _allDatabases, StaysOpenOnClick = true };
            menu.Items.Add(all);
            menu.Items.Add(new Separator());

            // The query window's database first even when it is a system one (master) and not in the list.
            var names = databases.ToList();
            foreach (var extra in _selected.Where(d => !names.Contains(d, StringComparer.OrdinalIgnoreCase)).ToList()) names.Insert(0, extra);
            var dbItems = new List<MenuItem>();
            foreach (var name in names)
            {
                var item = Checkable(name, !_allDatabases && _selected.Contains(name));
                item.Click += (s, e) =>
                {
                    _selectionTouched = true;
                    _allDatabases = false;
                    all.IsChecked = false;
                    if (item.IsChecked) _selected.Add(name); else _selected.Remove(name);
                    UpdateDatabasesButton();
                    StartSearch();
                };
                dbItems.Add(item);
                menu.Items.Add(item);
            }
            all.Click += (s, e) =>
            {
                _selectionTouched = true;
                _allDatabases = all.IsChecked;
                foreach (var i in dbItems) i.IsChecked = !_allDatabases && _selected.Contains((string)i.Tag);
                UpdateDatabasesButton();
                StartSearch();
            };
            menu.IsOpen = true;
        }

        private void SetStatus(string text) => _status.Text = text;

        /// <summary>Shows the item's definition (or generated script) with every hit of the current query highlighted,
        /// scrolled to the first hit.</summary>
        private void ShowPreview(SearchMatch match)
        {
            var doc = new FlowDocument { PagePadding = new Thickness(6), PageWidth = 4000 };
            var para = new Paragraph { Margin = new Thickness(0) };
            doc.Blocks.Add(para);
            _preview.Document = doc;

            string text = match?.Item.PreviewText;
            if (text == null)
            {
                if (match != null) para.Inlines.Add(new Run(match.Item.Type == SearchObjectType.Schema
                    ? "Schema " + match.Item.Name
                    : "-- No definition available (encrypted or CLR object)") { FontStyle = FontStyles.Italic });
                return;
            }

            var hits = _matcher?.FindAll(text) ?? (IReadOnlyList<TextSpan>)new TextSpan[0];
            Run first = null;
            int hit = 0, pos = 0;
            while (pos <= text.Length)
            {
                int eol = text.IndexOf('\n', pos);
                int end = eol < 0 ? text.Length : eol;
                int lineEnd = end > pos && text[end - 1] == '\r' ? end - 1 : end;

                int cursor = pos;
                while (hit < hits.Count && hits[hit].Start < lineEnd)
                {
                    var h = hits[hit];
                    int start = Math.Max(h.Start, cursor), stop = Math.Min(h.End, lineEnd);
                    if (start > cursor) para.Inlines.Add(new Run(text.Substring(cursor, start - cursor)));
                    if (stop > start)
                    {
                        var run = new Run(text.Substring(start, stop - start)) { Background = HitBrush };
                        para.Inlines.Add(run);
                        if (first == null) first = run;
                        cursor = stop;
                    }
                    if (h.End <= lineEnd) hit++; else break;
                }
                if (lineEnd > cursor) para.Inlines.Add(new Run(text.Substring(cursor, lineEnd - cursor)));

                if (eol < 0) break;
                para.Inlines.Add(new LineBreak());
                pos = eol + 1;
            }

            if (first != null)
                Dispatcher.BeginInvoke(DispatcherPriority.Background, new Action(() => first.BringIntoView()));
        }

        private static Brush Freeze(Brush b)
        {
            b.Freeze();
            return b;
        }
    }

    /// <summary>A button drawn like a combo box (text, then ▾) that opens a menu. Real ComboBoxes cannot hold the
    /// multi-select check lists the toolbar needs.</summary>
    internal sealed class DropDownButton : Button
    {
        private readonly TextBlock _label = new TextBlock { TextTrimming = TextTrimming.CharacterEllipsis, VerticalAlignment = VerticalAlignment.Center };

        public DropDownButton(double width, string toolTip)
        {
            Width = width;
            ToolTip = toolTip;
            Padding = new Thickness(4, 1, 2, 1);
            HorizontalContentAlignment = HorizontalAlignment.Stretch;
            SetResourceReference(BackgroundProperty, EnvironmentColors.ComboBoxBackgroundBrushKey);
            SetResourceReference(ForegroundProperty, EnvironmentColors.ComboBoxTextBrushKey);
            SetResourceReference(BorderBrushProperty, EnvironmentColors.ComboBoxBorderBrushKey);

            var arrow = new TextBlock { Text = "▾", Margin = new Thickness(4, 0, 2, 0), VerticalAlignment = VerticalAlignment.Center };
            var panel = new DockPanel { LastChildFill = true };
            DockPanel.SetDock(arrow, Dock.Right);
            panel.Children.Add(arrow);
            panel.Children.Add(_label);
            Content = panel;
        }

        public string Text
        {
            get => _label.Text;
            set => _label.Text = value;
        }
    }
}
