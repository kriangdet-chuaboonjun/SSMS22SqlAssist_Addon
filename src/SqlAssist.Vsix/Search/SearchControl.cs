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
using SqlAssist.Core.Completion;
using SqlAssist.Core.Search;
using SqlAssist.Results;
using SqlAssist.Schema;
using SqlAssist.Settings;

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

        /// <summary>A row of the Usages view: the referrer, and in "Matched in" (shown as "How") the evidence for it.</summary>
        public ResultRow(Usage usage)
        {
            var item = usage.Referrer;
            Match = new SearchMatch(item, new TextSpan[0], usage.Hits);
            Name = item.Name;
            Schema = item.Schema ?? "";
            Type = item.Type == SearchObjectType.None && item.SubKind == "Synonym" ? "Synonym" : SearchControl.TypeLabel(item);
            Parent = item.Parent ?? "";
            Database = item.Database;
            MatchedIn = Describe(usage.Kind);
        }

        private static string Describe(UsageKind kind)
        {
            switch (kind)
            {
                case UsageKind.Dependency: return "Dependency";
                case UsageKind.ForeignKey: return "Foreign key";
                case UsageKind.Trigger: return "Trigger on it";
                case UsageKind.Synonym: return "Synonym of it";
                case UsageKind.NameOnly: return "Name only (unresolved)";
                default: return "Text match";
            }
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
        /// <summary>The database-list load in flight for <see cref="_databasesLoadKey"/>, shared by the prefetch, the
        /// Databases dropdown and an all-databases search so the list is queried once.</summary>
        private Task<List<string>> _databasesLoad;
        private string _databasesLoadKey;
        private string _prefetchedKey;
        private static readonly TimeSpan DatabaseListTimeout = TimeSpan.FromSeconds(30);
        private readonly SortedSet<string> _selected = new SortedSet<string>(StringComparer.OrdinalIgnoreCase);
        private bool _allDatabases;
        /// <summary>Until the user picks databases, the selection follows the query window's database.</summary>
        private bool _selectionTouched;
        private CancellationTokenSource _cts;
        private SearchMatcher _matcher;

        // Usages view: the grid shows who refers to one object instead of the search results, which are kept aside and
        // come back with Back / Esc. Null target = the normal search results.
        private UsageTarget _usagesTarget;
        private CancellationTokenSource _usagesCts;
        private List<ResultRow> _stashedResults;
        private int _stashedSelection;
        private string _stashedStatus;
        private SearchMatcher _stashedMatcher;
        private readonly DockPanel _usagesBar;
        private readonly TextBlock _usagesTitle;
        private readonly DataGridColumn _howColumn;

        public SearchControl()
        {
            // Share only the button style with Options; preserve Search's original layout and other controls.
            var appearance = new ResourceDictionary
            { Source = new Uri("/SqlAssist;component/Settings/SettingsTheme.xaml", UriKind.Relative) };
            Resources[typeof(Button)] = appearance[typeof(Button)];
            Resources["SearchDatabaseFilterStyle"] = appearance[typeof(TextBox)];
            SetResourceReference(BackgroundProperty, EnvironmentColors.ToolWindowBackgroundBrushKey);
            SetResourceReference(TextElement.ForegroundProperty, EnvironmentColors.ToolWindowTextBrushKey);
            TextElement.SetFontFamily(this, new FontFamily("Segoe UI"));
            LastChildFill = true;

            _timer = new DispatcherTimer { Interval = Debounce };
            _timer.Tick += (s, e) => { _timer.Stop(); StartSearch(); };

            // One toolbar, dbForge style: [options ▾] [search box] [object types ▾] [database ▾] [server ▾] [Refresh].
            _searchBox = new TextBox { MinWidth = 160, Margin = new Thickness(0, 0, 6, 0),
                Padding = new Thickness(10, 0, 10, 0), VerticalContentAlignment = VerticalAlignment.Center };
            _searchBox.SetResourceReference(StyleProperty, "SearchDatabaseFilterStyle");
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
            var refresh = new Button { Content = "Refresh", Padding = new Thickness(12, 0, 12, 0), ToolTip = "Reload the search index from the server" };
            refresh.Click += (s, e) => { SearchIndexCache.Instance.Clear(); _databases = null; _databasesLoad = null; StartSearch(); };
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
            // Fixed metrics prevent the host's native text field and themed buttons from sizing differently.
            foreach (Control control in searchRow.Children)
            {
                control.MinHeight = 24;
                control.Height = 24;
                control.FontSize = 13;
                control.VerticalAlignment = VerticalAlignment.Center;
            }

            _status = new TextBlock { Margin = new Thickness(6, 0, 6, 4), Opacity = 0.8, TextTrimming = TextTrimming.CharacterEllipsis };

            _grid = BuildGrid();
            _howColumn = _grid.Columns[5];   // "Matched in", called "How" in the Usages view

            var back = new Button { Content = "← Back to results", Padding = new Thickness(8, 1, 8, 1) };
            back.Click += (s, e) => ExitUsagesMode(restore: true);
            _usagesTitle = new TextBlock { Margin = new Thickness(8, 0, 0, 0), VerticalAlignment = VerticalAlignment.Center, FontWeight = FontWeights.SemiBold, TextTrimming = TextTrimming.CharacterEllipsis };
            _usagesBar = new DockPanel { Margin = new Thickness(6, 0, 6, 4), Visibility = Visibility.Collapsed };
            SetDock(back, Dock.Left);
            _usagesBar.Children.Add(back);
            _usagesBar.Children.Add(_usagesTitle);
            PreviewKeyDown += OnControlKey;

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
            SetDock(_usagesBar, Dock.Top);
            Children.Add(searchRow);
            Children.Add(_status);
            Children.Add(_usagesBar);
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

        /// <summary>Rounded dropdown menus share the reference appearance without restyling SSMS menus.</summary>
        private static ContextMenu MenuFor(FrameworkElement owner)
        {
            var menu = new ContextMenu { PlacementTarget = owner, Placement = PlacementMode.Bottom,
                MinWidth = owner.ActualWidth + 16, HorizontalOffset = -8, VerticalOffset = -6 };
            menu.Resources.MergedDictionaries.Add(new ResourceDictionary
            { Source = new Uri("/SqlAssist;component/Search/SearchDropdownTheme.xaml", UriKind.Relative) });
            menu.SetResourceReference(StyleProperty, typeof(ContextMenu));
            return menu;
        }

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
            // Find Usages needs an object other objects can refer to; CRUD needs a table, which has a primary key to build on.
            var toolsSeparator = new Separator();
            var usages = new MenuItem { Header = "Find Usages", InputGestureText = "Shift+F12" };
            usages.Click += (s, e) => FindUsages();
            var crud = new MenuItem { Header = "Generate CRUD procedures…" };
            crud.Click += (s, e) => GenerateCrud();
            rowMenu.Opened += (s, e) =>
            {
                var selected = SelectedItem;
                bool canUsages = UsageTarget.CanFind(selected), canCrud = selected?.Type == SearchObjectType.Table;
                usages.Visibility = canUsages ? Visibility.Visible : Visibility.Collapsed;
                crud.Visibility = canCrud ? Visibility.Visible : Visibility.Collapsed;
                toolsSeparator.Visibility = canUsages || canCrud ? Visibility.Visible : Visibility.Collapsed;
            };
            rowMenu.Items.Add(locate);
            rowMenu.Items.Add(open);
            rowMenu.Items.Add(toolsSeparator);
            rowMenu.Items.Add(usages);
            rowMenu.Items.Add(crud);
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
            // A synonym (only listed by Find Usages) has no Search type, so there is no Object Explorer path to build for it.
            if (item.Type == SearchObjectType.None) { SetStatus("Locating a " + (item.SubKind ?? "this object") + " in Object Explorer is not supported."); return; }
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
            if (item.Type == SearchObjectType.None) { SetStatus("A " + (item.SubKind ?? "this object") + " has no script to open."); return; }
            var hits = HitsFor((_grid.SelectedItem as ResultRow)?.Match);
            var first = hits.Count > 0 ? hits[0] : new TextSpan(-1, 0);
            string problem = SsmsNavigator.OpenScript(_server, item, first.Start, first.Length);
            if (problem != null) SetStatus(problem);
        }

        /// <summary>Right-click a table › Generate CRUD procedures: loads that table from the schema cache of its database, asks
        /// what to generate and hands the script over. The script is never executed.</summary>
        private void GenerateCrud()
        {
            ThreadHelper.ThrowIfNotOnUIThread();
            var item = SelectedItem;
            var server = _server;
            if (item == null || server == null || item.Type != SearchObjectType.Table) return;
            SetStatus("Loading " + item.Name + " for CRUD procedures…");
            ThreadHelper.JoinableTaskFactory.RunAsync(async () =>
            {
                try
                {
                    var target = server.ForDatabase(item.Database);
                    var schema = await SchemaCache.Instance.GetAsync(target, TimeSpan.FromSeconds(15), CancellationToken.None);
                    var table = schema.Find(item.Schema, item.Name);
                    await ThreadHelper.JoinableTaskFactory.SwitchToMainThreadAsync();
                    if (table == null || table.IsView)
                    {
                        SetStatus("Table " + item.Schema + "." + item.Name + " is not in the loaded schema yet. Press Refresh and try again.");
                        return;
                    }
                    SetStatus("");

                    var saved = SettingsProvider.Load();
                    var window = new CrudWindow(table, item.Database, saved.Crud, ScriptTarget.NewWindow);
                    if (DialogHost.ShowDialog(window) != true) return;

                    saved.Crud.NamePattern = string.IsNullOrWhiteSpace(window.Options.NamePattern) ? CrudOptions.DefaultNamePattern : window.Options.NamePattern.Trim();
                    saved.Crud.CreateOrAlter = window.Options.CreateOrAlter;
                    try { SettingsProvider.Save(saved); } catch (Exception ex) { Log.Error("Saving CRUD settings", ex); }

                    // The procedures belong to this table's database, whichever one the query window happens to be on.
                    string text = "USE " + TableScriptBuilder.Bracket(item.Database) + ";\r\nGO\r\n\r\n" + CrudScriptBuilder.Build(table, window.Options).Text;
                    if (window.Target == ScriptTarget.NewWindow)
                    {
                        string problem = SsmsNavigator.OpenQueryWindow(server.UiConnectionInfo, text);
                        SetStatus(problem ?? "CRUD procedures for " + item.Name + " opened in a new query window (not executed).");
                    }
                    else ScriptDelivery.Send(text, window.Target, "CRUD procedures for " + item.Name, "SqlAssist");
                }
                catch (Exception ex)
                {
                    Log.Error("Generate CRUD procedures", ex);
                    await ThreadHelper.JoinableTaskFactory.SwitchToMainThreadAsync();
                    SetStatus("Generate CRUD procedures failed: " + ex.Message);
                }
            }).FileAndForget("SqlAssist/Search/GenerateCrud");
        }

        /// <summary>Find Usages of the selected result: asks its database which objects refer to it, joins that with the Search
        /// Index (for their definitions) and shows them in place of the search results. Read-only.</summary>
        private void FindUsages()
        {
            ThreadHelper.ThrowIfNotOnUIThread();
            var item = SelectedItem;
            var server = _server;
            if (server == null || !UsageTarget.CanFind(item)) return;
            var target = new UsageTarget(item);
            _usagesCts?.Cancel();
            var cts = _usagesCts = new CancellationTokenSource();
            var token = cts.Token;
            SetStatus("Finding usages of " + target.DisplayName + "…");
            ThreadHelper.JoinableTaskFactory.RunAsync(async () =>
            {
                try
                {
                    var rows = await UsageLoader.LoadAsync(server, target, token);
                    IReadOnlyList<SearchItem> index;
                    try { index = await SearchIndexCache.Instance.GetAsync(server, target.Database, token); }
                    catch (OperationCanceledException) { throw; }
                    catch (Exception) { index = new SearchItem[0]; }   // already logged by the cache: list the referrers without their definitions
                    var usages = await Task.Run(() =>
                    {
                        var all = UsageFinder.Merge(target, rows, index);
                        return target.IsColumn ? UsageFinder.ForColumn(target, all) : all;
                    }, token);

                    await ThreadHelper.JoinableTaskFactory.SwitchToMainThreadAsync();
                    if (token.IsCancellationRequested) return;
                    ShowUsages(target, usages);
                }
                catch (OperationCanceledException)
                {
                    // Superseded by a newer search or Find Usages.
                }
                catch (Exception ex)
                {
                    Log.Error("Find usages", ex);
                    await ThreadHelper.JoinableTaskFactory.SwitchToMainThreadAsync();
                    if (!token.IsCancellationRequested) SetStatus("Find usages failed: " + ex.Message);
                }
            }).FileAndForget("SqlAssist/Search/FindUsages");
        }

        private void ShowUsages(UsageTarget target, IReadOnlyList<Usage> usages)
        {
            ThreadHelper.ThrowIfNotOnUIThread();
            if (_usagesTarget == null)
            {
                // Entering from the search results: keep them for Back. Asking again from inside the view keeps the first set.
                // A search still streaming in from further databases is stopped, or its rows would be added to the usages.
                bool interrupted = _status.Text != null && _status.Text.StartsWith("Searching ", StringComparison.Ordinal);
                _cts?.Cancel();
                _stashedResults = _results.ToList();
                _stashedSelection = _grid.SelectedIndex;
                _stashedStatus = interrupted ? _results.Count + " results so far (the search was stopped by Find Usages)" : _status.Text;
                _stashedMatcher = _matcher;
            }
            _usagesTarget = target;
            _results.Clear();
            foreach (var usage in usages) _results.Add(new ResultRow(usage));
            _howColumn.Header = "How";
            _usagesBar.Visibility = Visibility.Visible;
            _usagesTitle.Text = "Usages of " + target.DisplayName + " (" + usages.Count + ")";

            if (usages.Count == 0)
                SetStatus("Nothing in " + target.Database + " refers to " + target.DisplayName + ". Dynamic SQL, other databases and applications are not covered.");
            else
                SetStatus(target.IsColumn
                    ? "Found by searching definitions for the column name: a column of the same name in another table can match too. Esc returns to the search results."
                    : usages.Count + " in " + target.Database + ". Esc returns to the search results.");
            if (usages.Count > 0) _grid.SelectedIndex = 0;
            _grid.Focus();
        }

        /// <summary>Leaves the Usages view: Back / Esc restore the search results; a new search just drops them.</summary>
        private void ExitUsagesMode(bool restore)
        {
            if (_usagesTarget == null) return;
            _usagesCts?.Cancel();
            _usagesTarget = null;
            _usagesBar.Visibility = Visibility.Collapsed;
            _howColumn.Header = "Matched in";
            _results.Clear();
            var stashed = _stashedResults;
            _stashedResults = null;
            if (!restore || stashed == null) return;

            _matcher = _stashedMatcher;
            foreach (var row in stashed) _results.Add(row);
            SetStatus(_stashedStatus);
            if (_stashedSelection >= 0 && _stashedSelection < _results.Count) _grid.SelectedIndex = _stashedSelection;
            _grid.Focus();
        }

        private void OnControlKey(object sender, KeyEventArgs e)
        {
            // Only while focus is in the grid or the search box: Esc closing a context menu must not also leave the view.
            bool inside = _grid.IsKeyboardFocusWithin || _searchBox.IsKeyboardFocusWithin;
            if (_usagesTarget != null && inside && Keyboard.Modifiers == ModifierKeys.None && e.Key == Key.Escape)
            {
                ExitUsagesMode(restore: true);
                e.Handled = true;
            }
            else if (e.Key == Key.F12 && Keyboard.Modifiers == ModifierKeys.Shift && _grid.IsKeyboardFocusWithin)
            {
                FindUsages();
                e.Handled = true;
            }
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
                _databasesLoad = null;
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
            // Once per server switch: a server that fails to answer isn't asked again on every keystroke; the dropdown retries.
            if (_databases == null && _databasesLoad == null && _prefetchedKey != target.ServerKey)
            {
                _prefetchedKey = target.ServerKey;
                PrefetchDatabases(target);
            }
            return target;
        }

        /// <summary>Starts listing the server's databases in the background, so the Databases dropdown usually opens at once.
        /// A failure is logged and the dropdown retries when it is opened.</summary>
        private void PrefetchDatabases(ConnectionTarget server) =>
            ThreadHelper.JoinableTaskFactory.RunAsync(async () =>
            {
                try { await GetDatabasesAsync(server, CancellationToken.None); }
                catch (Exception) { /* logged by LoadDatabaseListAsync */ }
            }).FileAndForget("SqlAssist/Search/PrefetchDatabases");

        /// <summary>The server's databases: the kept list, the load already in flight, or a new load. Cancelling
        /// <paramref name="token"/> stops only this wait, never the shared load.</summary>
        private async Task<List<string>> GetDatabasesAsync(ConnectionTarget server, CancellationToken token)
        {
            await ThreadHelper.JoinableTaskFactory.SwitchToMainThreadAsync();
            if (_databases != null && _server?.ServerKey == server.ServerKey) return _databases;
            // A failed load left behind by callers that all stopped waiting is retried, not joined.
            if (_databasesLoad == null || _databasesLoad.IsFaulted || _databasesLoad.IsCanceled || _databasesLoadKey != server.ServerKey)
            {
                _databasesLoadKey = server.ServerKey;
                _databasesLoad = LoadDatabaseListAsync(server);
            }
            var load = _databasesLoad;
            try
            {
                var cancelled = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
                using (token.Register(() => cancelled.TrySetResult(true)))
                    await Task.WhenAny(load, cancelled.Task);
                await ThreadHelper.JoinableTaskFactory.SwitchToMainThreadAsync();
                token.ThrowIfCancellationRequested();
                var names = await load;
                await ThreadHelper.JoinableTaskFactory.SwitchToMainThreadAsync();
                // UpdateServer clears the list when the server changes: one that finished loading after a switch isn't kept.
                if (_databasesLoad == load && _server?.ServerKey == server.ServerKey) _databases = names;
                return names;
            }
            finally
            {
                await ThreadHelper.JoinableTaskFactory.SwitchToMainThreadAsync();
                if (_databasesLoad == load && load.IsCompleted) _databasesLoad = null;
            }
        }

        private static async Task<List<string>> LoadDatabaseListAsync(ConnectionTarget server)
        {
            using (var cts = new CancellationTokenSource(DatabaseListTimeout))
            {
                try
                {
                    return await SearchIndexLoader.LoadDatabasesAsync(server, cts.Token).ConfigureAwait(false);
                }
                // SqlClient often reports a cancelled read as a SqlException ("Operation cancelled by user"), not an OCE.
                catch (Exception ex) when (cts.IsCancellationRequested)
                {
                    var timeout = new TimeoutException("No answer from " + server.Server + " within " + (int)DatabaseListTimeout.TotalSeconds + " s", ex);
                    Log.Error("Listing databases of " + server.Server, timeout);
                    throw timeout;
                }
                catch (Exception ex)
                {
                    Log.Error("Listing databases of " + server.Server, ex);
                    throw;
                }
            }
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
            _usagesCts?.Cancel();
            ExitUsagesMode(restore: false);   // a new search replaces the Usages view and the results kept behind it
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
                    string current = await SearchIndexLoader.CurrentDatabaseAsync(server, token);
                    if (token.IsCancellationRequested) return;
                    if (!string.IsNullOrEmpty(current)) _selected.Add(current);
                    UpdateDatabasesButton();
                }

                List<string> databases;
                if (_allDatabases)
                {
                    if (_databases == null) SetStatus("Loading the database list of " + server.Server + "…");
                    databases = await GetDatabasesAsync(server, token);
                    if (token.IsCancellationRequested) return;
                }
                else databases = _selected.ToList();

                var skipped = new List<string>();
                int searched = 0;
                for (int i = 0; i < databases.Count && _results.Count < ResultLimit; i++)
                {
                    string db = databases[i];
                    SetStatus($"Searching {db} ({i + 1}/{databases.Count})… {_results.Count} found");
                    try
                    {
                        var items = await SearchIndexCache.Instance.GetAsync(server, db, token);
                        token.ThrowIfCancellationRequested();
                        int room = ResultLimit - _results.Count;
                        var found = items == null
                            ? await SearchIndexLoader.SearchPagesAsync(server, db, matcher, room, token)
                            : await Task.Run(() => matcher.Search(items, room), token);
                        if (token.IsCancellationRequested) return;
                        foreach (var m in found) _results.Add(new ResultRow(m));
                        searched++;
                    }
                    catch (OperationCanceledException) { return; }
                    catch (Exception)
                    {
                        skipped.Add(db); // already logged by the cache
                        continue;
                    }
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
                // The first open may wait on a login over the network and a HAS_DBACCESS scan of every database: say so,
                // and keep a second click from starting the wait again.
                string loading = "Loading the database list of " + server.Server + "…";
                string before = _status.Text;
                _databasesButton.IsEnabled = false;
                _databasesButton.Text = "Loading…";
                SetStatus(loading);
                try
                {
                    databases = await GetDatabasesAsync(server, CancellationToken.None);
                    if (_status.Text == loading) SetStatus(before);
                }
                catch (Exception ex)
                {
                    // Already logged by LoadDatabaseListAsync.
                    databases = new List<string>();
                    SetStatus("Could not list the databases of " + server.Server + ": " + ex.Message + " (see log). Open the list again to retry.");
                }
                finally
                {
                    _databasesButton.IsEnabled = true;
                    UpdateDatabasesButton();
                }
            }

            if (_server?.ServerKey != server.ServerKey) return;
            BuildDatabaseMenu(databases).IsOpen = true;
        }

        /// <summary>Filters database suggestions without changing the selected database set.</summary>
        private ContextMenu BuildDatabaseMenu(IEnumerable<string> databases)
        {
            var menu = MenuFor(_databasesButton);
            menu.MinWidth = Math.Max(280, menu.MinWidth);
            menu.MaxHeight = Math.Max(200, SystemParameters.WorkArea.Height * 0.7);
            var filter = new TextBox { MinWidth = 230, MinHeight = 24, Height = 24, FontSize = 13, Padding = new Thickness(6, 0, 6, 0), Margin = new Thickness(0, 3, 0, 6),   // same 24 as the toolbar row
                ToolTip = "พิมพ์ชื่อ Database เพื่อกรองรายการ" };
            filter.SetResourceReference(StyleProperty, "SearchDatabaseFilterStyle");
            System.Windows.Automation.AutomationProperties.SetName(filter, "ค้นหา Database");

            // Clears every check (and All databases) in one click; the next pick starts a search.
            var clearIcon = new System.Windows.Shapes.Path { Data = Geometry.Parse("M1,1 H13 V13 H1 Z M4.5,4.5 L9.5,9.5 M9.5,4.5 L4.5,9.5"),
                StrokeThickness = 1.3, StrokeLineJoin = PenLineJoin.Round, StrokeStartLineCap = PenLineCap.Round, StrokeEndLineCap = PenLineCap.Round,
                Width = 14, Height = 14 };
            clearIcon.SetResourceReference(System.Windows.Shapes.Shape.StrokeProperty, EnvironmentColors.ToolWindowTextBrushKey);
            var clear = new Button { Content = clearIcon, Width = 24, MinHeight = 24, Height = 24, Margin = new Thickness(6, 3, 0, 6), Padding = new Thickness(0),
                Background = Brushes.Transparent, BorderThickness = new Thickness(0), Cursor = Cursors.Hand,
                ToolTip = "ล้าง Database ที่เลือกทั้งหมด" };
            clear.SetResourceReference(StyleProperty, typeof(Button));   // the toolbar's themed button look, as DropDownButton
            System.Windows.Automation.AutomationProperties.SetName(clear, "ล้าง Database ที่เลือกทั้งหมด");
            var editorRow = new DockPanel { LastChildFill = true };
            DockPanel.SetDock(clear, Dock.Right);
            editorRow.Children.Add(clear);
            editorRow.Children.Add(filter);
            menu.Items.Add(new MenuItem { Header = editorRow, StaysOpenOnClick = true, Focusable = false,
                Style = (Style)menu.FindResource("SearchDropdownEditor") });
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
            clear.Click += (s, e) =>
            {
                e.Handled = true;
                try
                {
                    _selectionTouched = true;
                    _allDatabases = false;
                    _selected.Clear();
                    all.IsChecked = false;
                    foreach (var i in dbItems) i.IsChecked = false;
                    UpdateDatabasesButton();
                    // No search here: with nothing picked, a search would fall back to the login's default database.
                    filter.Focus();
                }
                catch (Exception ex) { Log.Error("Clearing the database selection", ex); }
            };
            var empty = new MenuItem { Header = "ไม่พบฐานข้อมูล", IsEnabled = false, Visibility = dbItems.Count == 0 ? Visibility.Visible : Visibility.Collapsed };
            menu.Items.Add(empty);
            filter.TextChanged += (s, e) =>
            {
                bool any = false;
                foreach (var item in dbItems)
                {
                    bool matches = ((string)item.Tag).IndexOf(filter.Text.Trim(), StringComparison.OrdinalIgnoreCase) >= 0;
                    item.Visibility = matches ? Visibility.Visible : Visibility.Collapsed;
                    any |= matches;
                }
                empty.Visibility = any ? Visibility.Collapsed : Visibility.Visible;
            };
            filter.PreviewKeyDown += (s, e) =>
            {
                if (e.Key != Key.Down && e.Key != Key.Enter) return;
                var first = dbItems.FirstOrDefault(item => item.Visibility == Visibility.Visible);
                if (first != null)
                {
                    if (e.Key == Key.Down) first.Focus();
                    else
                    {
                        first.IsChecked = !first.IsChecked;
                        first.RaiseEvent(new RoutedEventArgs(MenuItem.ClickEvent));
                    }
                }
                e.Handled = true;
            };
            menu.Opened += (s, e) => { filter.Focus(); Keyboard.Focus(filter); };
            return menu;
        }

        private void SetStatus(string text) => _status.Text = text;

        /// <summary>What to highlight in the item's preview: the places the current query hits, or in the Usages view the
        /// places that name the object (only inside a Definition: a table's script is generated, not written by anyone).</summary>
        private IReadOnlyList<TextSpan> HitsFor(SearchMatch match)
        {
            var none = new TextSpan[0];
            if (match == null) return none;
            if (_usagesTarget != null) return match.Item.Definition != null ? match.DefinitionHits : none;
            return _matcher?.FindAll(match.Item.PreviewText) ?? none;
        }

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

            var hits = HitsFor(match);
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
            SetResourceReference(StyleProperty, typeof(Button));
            Padding = new Thickness(8, 0, 6, 0);
            _label.MaxWidth = Math.Max(16, width - 36);
            HorizontalContentAlignment = HorizontalAlignment.Stretch;
            SetResourceReference(BackgroundProperty, EnvironmentColors.ToolWindowBackgroundBrushKey);
            SetResourceReference(ForegroundProperty, EnvironmentColors.ToolWindowTextBrushKey);
            SetResourceReference(BorderBrushProperty, EnvironmentColors.ToolWindowBorderBrushKey);

            var arrow = new System.Windows.Shapes.Path { Data = Geometry.Parse("M 0,0 L 4,4 L 8,0"),
                StrokeThickness = 1.5, StrokeStartLineCap = PenLineCap.Round, StrokeEndLineCap = PenLineCap.Round,
                Width = 8, Height = 5, Margin = new Thickness(6, 0, 2, 0), VerticalAlignment = VerticalAlignment.Center };
            arrow.SetResourceReference(System.Windows.Shapes.Shape.StrokeProperty, EnvironmentColors.ToolWindowTextBrushKey);
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
