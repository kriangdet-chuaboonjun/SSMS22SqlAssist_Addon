using System;
using System.Collections.Generic;
using System.ComponentModel.Composition;
using System.Globalization;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Markup;
using System.Windows.Media;
using Microsoft.VisualStudio.Imaging;
using Microsoft.VisualStudio.Imaging.Interop;
using SqlAssist.Core.Schema;
using Microsoft.VisualStudio.Language.Intellisense.AsyncCompletion;
using Microsoft.VisualStudio.Language.Intellisense.AsyncCompletion.Data;
using Microsoft.VisualStudio.PlatformUI;
using Microsoft.VisualStudio.Shell;
using Microsoft.VisualStudio.Text.Classification;
using Microsoft.VisualStudio.Text.Editor;
using Microsoft.VisualStudio.Utilities;

namespace SqlAssist.Editor
{
    [Export(typeof(ICompletionPresenterProvider))]
    [Name("SqlAssist completion presenter")]
    [ContentType(SqlContentType.Name)]
    [Order(Before = PredefinedCompletionNames.DefaultCompletionPresenter)]
    internal sealed class SqlCompletionPresenterProvider : ICompletionPresenterProvider
    {
        [Import] internal IClassificationFormatMapService FormatMaps { get; set; }
        [Import] internal IClassificationTypeRegistryService Classifications { get; set; }

        public CompletionPresenterOptions Options { get; } = new CompletionPresenterOptions(SqlCompletionPresenter.VisibleRows);

        public ICompletionPresenter GetOrCreate(ITextView textView) =>
            textView.Properties.GetOrCreateSingletonProperty(() => new SqlCompletionPresenter((IWpfTextView)textView, this));

        /// <summary>The colour the editor gives keywords, so the list matches the code (and the theme).</summary>
        public Brush KeywordBrush(ITextView view) => KeywordBrush(FormatMaps, Classifications, view);

        public static Brush KeywordBrush(IClassificationFormatMapService formatMaps, IClassificationTypeRegistryService classifications, ITextView view)
        {
            var map = formatMaps.GetClassificationFormatMap(view);
            foreach (var name in new[] { "SQL Keyword", "keyword" })
            {
                var type = classifications.GetClassificationType(name);
                if (type == null) continue;
                var brush = map.GetTextProperties(type).ForegroundBrush;
                if (brush != null && !map.GetTextProperties(type).ForegroundBrushEmpty) return brush;
            }
            return Brushes.Blue;
        }
    }

    /// <summary>Our own completion list. The editor's built-in list draws every item in one colour; this one draws
    /// keywords in the keyword colour. Keyboard navigation still belongs to the session (it calls Update);
    /// the list only reports mouse picks back through the events.</summary>
    internal sealed class SqlCompletionPresenter : ICompletionPresenter
    {
        public const int VisibleRows = 9;
        private const double RowHeight = 20;
        /// <summary>How long a table must stay selected before its column panel appears.</summary>
        private static readonly TimeSpan TableDetailsDelay = TimeSpan.FromSeconds(1.5);

        private readonly IWpfTextView _view;
        private readonly SqlCompletionPresenterProvider _provider;
        private Popup _popup;
        private ListBox _list;
        private TextBlock _description;
        private Border _details;
        private IAsyncCompletionSession _session;
        private CompletionPresentationViewModel _model;
        private CompletionItem _describedItem;
        private CancellationTokenSource _descriptionCts;
        private bool _applying;

        public SqlCompletionPresenter(IWpfTextView view, SqlCompletionPresenterProvider provider)
        {
            _view = view;
            _provider = provider;
        }

        // No filter buttons in this list (the source defines no filters), so this never fires.
        public event EventHandler<CompletionFilterChangedEventArgs> FiltersChanged { add { } remove { } }
        public event EventHandler<CompletionItemSelectedEventArgs> CompletionItemSelected;
        public event EventHandler<CompletionItemEventArgs> CommitRequested;
        public event EventHandler<CompletionClosedEventArgs> CompletionClosed;

        public void Open(IAsyncCompletionSession session, CompletionPresentationViewModel presentation)
        {
            ThreadHelper.ThrowIfNotOnUIThread();
            EnsureUi();
            _view.LayoutChanged += OnLayoutChanged;
            _view.LostAggregateFocus += OnLostFocus;
            Apply(session, presentation);
            _popup.IsOpen = true;
        }

        public void Update(IAsyncCompletionSession session, CompletionPresentationViewModel presentation)
        {
            ThreadHelper.ThrowIfNotOnUIThread();
            if (_popup == null) return;
            Apply(session, presentation);
        }

        public void Close()
        {
            _view.LayoutChanged -= OnLayoutChanged;
            _view.LostAggregateFocus -= OnLostFocus;
            _descriptionCts?.Cancel();
            _describedItem = null;
            if (_popup != null) _popup.IsOpen = false;
            _session = null;
        }

        public void Dispose() => Close();

        private void Apply(IAsyncCompletionSession session, CompletionPresentationViewModel model)
        {
            _session = session;
            _model = model;
            _applying = true;
            try
            {
                var text = (Brush)_list.TryFindResource(EnvironmentColors.ToolWindowTextBrushKey) ?? SystemColors.WindowTextBrush;
                var keyword = _provider.KeywordBrush(_view);
                TreeState tree = null;
                session.Properties.TryGetProperty(typeof(TreeState), out tree);
                var rows = model.ItemList.Select(i => new Row(i.CompletionItem, i.CompletionItem.Properties.ContainsProperty(ItemProps.IsKeyword) ? keyword : text, model.UseSoftSelection, tree)).ToList();

                _list.ItemsSource = rows;
                _list.Width = MeasureWidth(rows);
                int selected = model.SelectedItemIndex;
                _list.SelectedIndex = selected >= 0 && selected < rows.Count ? selected : -1;
                if (_list.SelectedItem != null) _list.ScrollIntoView(_list.SelectedItem);
            }
            finally
            {
                _applying = false;
            }
            Position();
            ShowDescription(_list.SelectedItem is Row row ? row.Item : null);
        }

        private void Position()
        {
            try
            {
                var start = _model.ApplicableToSpan.GetStartPoint(_view.TextSnapshot);
                var line = _view.TextViewLines?.GetTextViewLineContainingBufferPosition(start);
                if (line == null) return;
                var bounds = line.GetCharacterBounds(start);
                _popup.PlacementTarget = _view.VisualElement;
                _popup.Placement = PlacementMode.Bottom; // flips above the line when there is no room below
                _popup.PlacementRectangle = new Rect(bounds.Left - _view.ViewportLeft - 22, line.TextTop - _view.ViewportTop, 1, line.TextHeight);
            }
            catch (Exception ex)
            {
                Log.Error("Positioning completion list", ex);
            }
        }

        private void ShowDescription(CompletionItem item)
        {
            if (item == _describedItem) return;
            _describedItem = item;
            _descriptionCts?.Cancel();
            _description.Visibility = Visibility.Collapsed;
            _details.Visibility = Visibility.Collapsed;
            if (item == null || _session == null) return;

            var cts = _descriptionCts = new CancellationTokenSource();
            var session = _session;

            // Tables and views get the column panel beside the list instead of the one-line description — after a
            // pause on the item, so arrowing through the list doesn't flash a large panel for every table passed.
            if (item.Properties.TryGetProperty(ItemProps.Table, out Lazy<TableDetails> table))
            {
                ThreadHelper.JoinableTaskFactory.RunAsync(async () =>
                {
                    await Task.Delay(TableDetailsDelay, cts.Token);
                    await ThreadHelper.JoinableTaskFactory.SwitchToMainThreadAsync(cts.Token);
                    if (cts.IsCancellationRequested || _describedItem != item) return;
                    try
                    {
                        _details.Child = TableDetailsView.Create(table.Value, _provider.KeywordBrush(_view),
                            EnvironmentColors.ToolWindowTextBrushKey, EnvironmentColors.ToolWindowBorderBrushKey);
                        _details.Visibility = Visibility.Visible;
                    }
                    catch (Exception ex)
                    {
                        Log.Error("Table details panel", ex);
                    }
                }).FileAndForget("SqlAssist/TableDetailsPanel");
                return;
            }

            ThreadHelper.JoinableTaskFactory.RunAsync(async () =>
            {
                var description = await item.Source.GetDescriptionAsync(session, item, cts.Token);
                await ThreadHelper.JoinableTaskFactory.SwitchToMainThreadAsync(cts.Token);
                if (cts.IsCancellationRequested || !(description is string s) || s.Length == 0) return;
                _description.Text = s;
                _description.Visibility = Visibility.Visible;
            }).FileAndForget("SqlAssist/CompletionDescription");
        }

        private void OnLayoutChanged(object sender, TextViewLayoutChangedEventArgs e)
        {
            if (_popup?.IsOpen == true) Position();
        }

        private void OnLostFocus(object sender, EventArgs e) =>
            CompletionClosed?.Invoke(this, new CompletionClosedEventArgs(_view));

        private void OnSelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (_applying || !(_list.SelectedItem is Row row)) return;
            CompletionItemSelected?.Invoke(this, new CompletionItemSelectedEventArgs(row.Item, suggestionItemSelected: false));
        }

        // ListBoxItem selects on click only if it can take focus, and these can't (the editor keeps it), so do it here.
        private void OnMouseDown(object sender, MouseButtonEventArgs e)
        {
            if (e.OriginalSource is DependencyObject source && ItemsControl.ContainerFromElement(_list, source) is ListBoxItem item)
                _list.SelectedItem = _list.ItemContainerGenerator.ItemFromContainer(item);
        }

        private void OnDoubleClick(object sender, MouseButtonEventArgs e)
        {
            if (_list.SelectedItem is Row row)
            {
                e.Handled = true;
                CommitRequested?.Invoke(this, new CompletionItemEventArgs(row.Item));
            }
        }

        // Fixed width from the longest entry: with virtualization, letting rows size the list makes it jump while scrolling.
        private double MeasureWidth(List<Row> rows)
        {
            if (rows.Count == 0) return 220;
            var longest = rows.OrderByDescending(r => r.Text.Length + r.Suffix.Length).First();
            var typeface = new Typeface(_list.FontFamily, FontStyles.Normal, FontWeights.Normal, FontStretches.Normal);
            double dpi = VisualTreeHelper.GetDpi(_view.VisualElement).PixelsPerDip;
            double Width(string s) => new FormattedText(s, CultureInfo.CurrentUICulture, FlowDirection.LeftToRight, typeface, _list.FontSize, Brushes.Black, dpi).WidthIncludingTrailingWhitespace;
            double tree = rows.Any(r => r.TreeVisibility == Visibility.Visible) ? 18 + 18 + 18 : 0; // indent, expander, checkbox
            double width = tree + 20 + 8 + Width(longest.Text) + 16 + Width(longest.Suffix) + 30; // icon, gaps, scrollbar
            return Math.Max(220, Math.Min(700, width));
        }

        private void EnsureUi()
        {
            if (_popup != null) return;
            var root = (Border)XamlReader.Parse(Xaml);
            _list = (ListBox)root.FindName("List");
            _description = (TextBlock)root.FindName("Description");
            _list.MaxHeight = VisibleRows * RowHeight + 4;
            _list.SelectionChanged += OnSelectionChanged;
            _list.PreviewMouseLeftButtonDown += OnMouseDown;
            _list.MouseDoubleClick += OnDoubleClick;
            _list.AddHandler(System.Windows.Controls.Primitives.ButtonBase.ClickEvent, new RoutedEventHandler(OnTreeClick));
            // Clicking the list must not take keyboard focus from the editor.
            _list.PreviewMouseUp += (s, e) => _view.VisualElement.Focus();

            // The column panel sits to the right of the list, top-aligned, in its own bordered box.
            _details = new Border { BorderThickness = new Thickness(1), Margin = new Thickness(2, 0, 0, 0), VerticalAlignment = VerticalAlignment.Top, Visibility = Visibility.Collapsed };
            _details.SetResourceReference(Border.BackgroundProperty, EnvironmentColors.ToolWindowBackgroundBrushKey);
            _details.SetResourceReference(Border.BorderBrushProperty, EnvironmentColors.ToolWindowBorderBrushKey);
            TextElement.SetFontFamily(_details, new FontFamily("Segoe UI"));
            TextElement.SetFontSize(_details, 13);
            root.VerticalAlignment = VerticalAlignment.Top;
            var container = new StackPanel { Orientation = Orientation.Horizontal };
            container.Children.Add(root);
            container.Children.Add(_details);

            _popup = new Popup
            {
                Child = container,
                // Transparent, so the space beside a list shorter than the panel is not painted black.
                AllowsTransparency = true,
                StaysOpen = true,
                Focusable = false,
            };
        }

        private sealed class Row
        {
            public Row(CompletionItem item, Brush foreground, bool soft, TreeState tree)
            {
                Item = item;
                Foreground = foreground;
                SoftSelection = soft;
                Text = item.DisplayText ?? "";
                Suffix = item.Suffix ?? "";
                Moniker = item.Icon == null ? default : new ImageMoniker { Guid = item.Icon.ImageId.Guid, Id = item.Icon.ImageId.Id };

                // Table tree ("SELECT |"): expander on tables, a checkbox on every node, columns indented.
                Node = TreeState.NodeOf(item);
                bool inTree = Node != null && tree != null;
                TreeVisibility = inTree ? Visibility.Visible : Visibility.Collapsed;
                ExpanderVisibility = inTree && Node.IsTable ? Visibility.Visible : Visibility.Hidden;
                Glyph = inTree && tree.Expanded.Contains(Node.Key) ? "▾" : "▸"; // ▾ expanded, ▸ collapsed
                Indent = inTree && !Node.IsTable ? 18 : 0;
                IsChecked = inTree && tree.IsChecked(Node);
            }

            public CompletionItem Item { get; }
            public string Text { get; }
            public string Suffix { get; }
            public Brush Foreground { get; }
            public ImageMoniker Moniker { get; }
            public bool SoftSelection { get; }
            public TreeNode Node { get; }
            public Visibility TreeVisibility { get; }
            public Visibility ExpanderVisibility { get; }
            public string Glyph { get; }
            public double Indent { get; }
            public bool IsChecked { get; }
        }

        // Expander and checkbox clicks in the table tree. Both re-run the item manager through the session, so the
        // list (and the ticks) are redrawn from TreeState rather than patched here.
        private void OnTreeClick(object sender, RoutedEventArgs e)
        {
            if (_session == null || !(e.OriginalSource is FrameworkElement fe) || !(fe.DataContext is Row row) || row.Node == null) return;
            e.Handled = true;
            try
            {
                if ((fe.Tag as string) == "check")
                {
                    TreeState.Toggle(_session, _view, row.Item, row.Node);
                    return;
                }
                if ((fe.Tag as string) != "expand") return;
                TreeState.For(_session).ToggleExpanded(row.Node);
                TreeState.Refresh(_session, _view, row.Item);
            }
            catch (Exception ex)
            {
                Log.Error("Completion tree click", ex);
            }
            finally
            {
                _view.VisualElement.Focus();
            }
        }

        // Theme colours come from the VS environment, so the list follows light/dark/blue themes like the rest of SSMS.
        private const string Xaml = @"
<Border xmlns='http://schemas.microsoft.com/winfx/2006/xaml/presentation'
        xmlns:x='http://schemas.microsoft.com/winfx/2006/xaml'
        xmlns:vsui='clr-namespace:Microsoft.VisualStudio.PlatformUI;assembly=Microsoft.VisualStudio.Shell.15.0'
        xmlns:imaging='clr-namespace:Microsoft.VisualStudio.Imaging;assembly=Microsoft.VisualStudio.Imaging'
        BorderThickness='1'
        BorderBrush='{DynamicResource {x:Static vsui:EnvironmentColors.ToolWindowBorderBrushKey}}'
        Background='{DynamicResource {x:Static vsui:EnvironmentColors.ToolWindowBackgroundBrushKey}}'
        TextElement.FontFamily='Segoe UI' TextElement.FontSize='13'>
  <StackPanel>
    <ListBox x:Name='List' Focusable='False' BorderThickness='0' Background='Transparent'
             ScrollViewer.HorizontalScrollBarVisibility='Disabled'
             VirtualizingPanel.IsVirtualizing='True' VirtualizingPanel.VirtualizationMode='Recycling'
             VirtualizingPanel.ScrollUnit='Item'>
      <ListBox.ItemContainerStyle>
        <Style TargetType='ListBoxItem'>
          <Setter Property='Focusable' Value='False' />
          <Setter Property='Template'>
            <Setter.Value>
              <ControlTemplate TargetType='ListBoxItem'>
                <Border x:Name='Bd' BorderThickness='1' BorderBrush='Transparent' Background='Transparent' Padding='2,0'>
                  <ContentPresenter />
                </Border>
                <ControlTemplate.Triggers>
                  <MultiDataTrigger>
                    <MultiDataTrigger.Conditions>
                      <Condition Binding='{Binding IsSelected, RelativeSource={RelativeSource Self}}' Value='True' />
                      <Condition Binding='{Binding SoftSelection}' Value='False' />
                    </MultiDataTrigger.Conditions>
                    <Setter TargetName='Bd' Property='Background' Value='{DynamicResource {x:Static vsui:TreeViewColors.SelectedItemActiveBrushKey}}' />
                  </MultiDataTrigger>
                  <MultiDataTrigger>
                    <MultiDataTrigger.Conditions>
                      <Condition Binding='{Binding IsSelected, RelativeSource={RelativeSource Self}}' Value='True' />
                      <Condition Binding='{Binding SoftSelection}' Value='True' />
                    </MultiDataTrigger.Conditions>
                    <Setter TargetName='Bd' Property='BorderBrush' Value='{DynamicResource {x:Static vsui:TreeViewColors.SelectedItemActiveBrushKey}}' />
                  </MultiDataTrigger>
                </ControlTemplate.Triggers>
              </ControlTemplate>
            </Setter.Value>
          </Setter>
        </Style>
      </ListBox.ItemContainerStyle>
      <ListBox.ItemTemplate>
        <DataTemplate>
          <Grid Height='20'>
            <Grid.ColumnDefinitions>
              <ColumnDefinition Width='Auto' />
              <ColumnDefinition Width='20' />
              <ColumnDefinition Width='*' />
              <ColumnDefinition Width='Auto' />
            </Grid.ColumnDefinitions>
            <StackPanel Orientation='Horizontal' Visibility='{Binding TreeVisibility}'>
              <Border Width='{Binding Indent}' />
              <Button Tag='expand' Content='{Binding Glyph}' Visibility='{Binding ExpanderVisibility}' Focusable='False'
                      Width='16' Height='18' Padding='0' Margin='0,0,2,0' Cursor='Hand' VerticalAlignment='Center'
                      Foreground='{DynamicResource {x:Static vsui:EnvironmentColors.ToolWindowTextBrushKey}}'>
                <Button.Template>
                  <ControlTemplate TargetType='Button'>
                    <Border Background='Transparent'><ContentPresenter HorizontalAlignment='Center' VerticalAlignment='Center' /></Border>
                  </ControlTemplate>
                </Button.Template>
              </Button>
              <CheckBox Tag='check' IsChecked='{Binding IsChecked, Mode=OneWay}' Focusable='False' VerticalAlignment='Center' Margin='0,0,2,0' />
            </StackPanel>
            <imaging:CrispImage Grid.Column='1' Width='16' Height='16' Moniker='{Binding Moniker}' />
            <TextBlock Grid.Column='2' Text='{Binding Text}' Foreground='{Binding Foreground}' Margin='6,0,12,0'
                       VerticalAlignment='Center' TextTrimming='CharacterEllipsis' />
            <TextBlock Grid.Column='3' Text='{Binding Suffix}' Opacity='0.6' Margin='0,0,4,0' VerticalAlignment='Center'
                       Foreground='{DynamicResource {x:Static vsui:EnvironmentColors.ToolWindowTextBrushKey}}' />
          </Grid>
        </DataTemplate>
      </ListBox.ItemTemplate>
    </ListBox>
    <TextBlock x:Name='Description' Visibility='Collapsed' TextWrapping='Wrap' MaxWidth='700' Padding='6,4'
               FontSize='12' Opacity='0.85'
               Foreground='{DynamicResource {x:Static vsui:EnvironmentColors.ToolWindowTextBrushKey}}'
               Background='{DynamicResource {x:Static vsui:EnvironmentColors.ToolWindowBackgroundBrushKey}}' />
  </StackPanel>
</Border>";
    }
}
