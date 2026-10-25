using System;
using System.ComponentModel.Composition;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.VisualStudio.Language.Intellisense;
using Microsoft.VisualStudio.PlatformUI;
using Microsoft.VisualStudio.Shell;
using Microsoft.VisualStudio.Text;
using Microsoft.VisualStudio.Text.Classification;
using Microsoft.VisualStudio.Text.Editor;
using Microsoft.VisualStudio.Utilities;
using SqlAssist.Connection;
using SqlAssist.Core.Schema;
using SqlAssist.Core.Sql;
using SqlAssist.Schema;

namespace SqlAssist.Editor
{
    [Export(typeof(IAsyncQuickInfoSourceProvider))]
    [Name("SqlAssist table quick info")]
    [ContentType(SqlContentType.Name)]
    [Order(Before = "default")]
    internal sealed class TableQuickInfoSourceProvider : IAsyncQuickInfoSourceProvider
    {
        [Import] internal IClassificationFormatMapService FormatMaps { get; set; }
        [Import] internal IClassificationTypeRegistryService Classifications { get; set; }

        public IAsyncQuickInfoSource TryCreateQuickInfoSource(ITextBuffer textBuffer) =>
            textBuffer.Properties.GetOrCreateSingletonProperty(() => new TableQuickInfoSource(this));
    }

    [Export(typeof(IWpfTextViewCreationListener))]
    [Name("SqlAssist quick info hover trigger")]
    [ContentType(SqlContentType.Name)]
    [TextViewRole(PredefinedTextViewRoles.Editable)]
    internal sealed class TableQuickInfoHoverTrigger : IWpfTextViewCreationListener
    {
        [Import] internal IAsyncQuickInfoBroker Broker { get; set; }

        /// <summary>Starts Quick Info on hover ourselves: SSMS's SQL editor may not, especially with its own
        /// IntelliSense switched off. Does nothing when a tooltip is already up, so it never doubles one.</summary>
        public void TextViewCreated(IWpfTextView textView)
        {
            textView.MouseHover += (s, e) =>
            {
                try
                {
                    if (Broker.IsQuickInfoActive(textView)) return;
                    var point = e.TextPosition.GetPoint(textView.TextBuffer, PositionAffinity.Predecessor);
                    if (point == null) return;
                    var tracking = point.Value.Snapshot.CreateTrackingPoint(point.Value.Position, PointTrackingMode.Positive);
                    Broker.TriggerQuickInfoAsync(textView, tracking, QuickInfoSessionOptions.TrackMouse)
                        .FileAndForget("SqlAssist/QuickInfoHover");
                }
                catch (Exception ex)
                {
                    Log.Error("Quick info hover", ex);
                }
            };
        }
    }

    /// <summary>Hovering a table name or alias in the query shows the same table panel as the completion list.
    /// Uses only an already-loaded schema: a tooltip must not wait on (or start) a database round trip.</summary>
    internal sealed class TableQuickInfoSource : IAsyncQuickInfoSource
    {
        private readonly TableQuickInfoSourceProvider _provider;

        public TableQuickInfoSource(TableQuickInfoSourceProvider provider) => _provider = provider;

        public async Task<QuickInfoItem> GetQuickInfoItemAsync(IAsyncQuickInfoSession session, CancellationToken cancellationToken)
        {
            try
            {
                var trigger = session.GetTriggerPoint(session.TextView.TextBuffer.CurrentSnapshot);
                if (trigger == null) return null;
                var point = trigger.Value;

                // Connection objects and WPF elements both belong to the UI thread.
                await ThreadHelper.JoinableTaskFactory.SwitchToMainThreadAsync(cancellationToken);

                var window = TextWindow.Around(point.Snapshot, point.Position);
                var hover = SqlContextAnalyzer.FindHoverTarget(window.Text, window.Caret);
                if (hover == null) return null;

                var schema = SchemaCache.Instance.TryGetLoaded(EditorConnection.Get(window));
                var table = schema?.Find(hover.Table);
                if (table == null) return null;

                var view = TableDetailsView.Create(
                    TableDetails.Build(schema, table),
                    SqlCompletionPresenterProvider.KeywordBrush(_provider.FormatMaps, _provider.Classifications, session.TextView),
                    EnvironmentColors.ToolTipTextBrushKey,
                    EnvironmentColors.ToolTipBorderBrushKey);
                var span = point.Snapshot.CreateTrackingSpan(window.Offset + hover.Start, hover.End - hover.Start, SpanTrackingMode.EdgeInclusive);
                return new QuickInfoItem(span, view);
            }
            catch (OperationCanceledException)
            {
                return null;
            }
            catch (Exception ex)
            {
                Log.Error("Table quick info", ex);
                return null;
            }
        }

        public void Dispose()
        {
        }
    }
}
