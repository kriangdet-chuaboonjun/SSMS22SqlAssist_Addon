using System;
using System.ComponentModel.Composition;
using System.Linq;
using System.Windows.Threading;
using Microsoft.VisualStudio.Language.Intellisense.AsyncCompletion;
using Microsoft.VisualStudio.Language.StandardClassification;
using Microsoft.VisualStudio.Shell;
using Microsoft.VisualStudio.Text;
using Microsoft.VisualStudio.Text.Adornments;
using Microsoft.VisualStudio.Text.Editor;
using Microsoft.VisualStudio.Utilities;
using SqlAssist.Connection;
using SqlAssist.Core.Completion;
using SqlAssist.Core.Schema;
using SqlAssist.Core.Sql;
using SqlAssist.Schema;

namespace SqlAssist.Editor
{
    [Export(typeof(IWpfTextViewCreationListener))]
    [Name("SqlAssist expansion tip")]
    [ContentType(SqlContentType.Name)]
    [TextViewRole(PredefinedTextViewRoles.Editable)]
    internal sealed class ExpansionTipProvider : IWpfTextViewCreationListener
    {
        [Import] internal IToolTipService ToolTips { get; set; }
        [Import] internal IAsyncCompletionBroker Broker { get; set; }

        public void TextViewCreated(IWpfTextView textView) => new ExpansionTip(textView, ToolTips, Broker);
    }

    /// <summary>Shows "Press Tab to expand N columns" under a "*", "Press Tab to insert list of parameters" under the
    /// procedure name in "EXEC p", and the INSERT / UPDATE / ALTER hint under the object name — only where Tab would
    /// do it, so the hint never promises an expansion that Tab will not do. Re-checked a moment after the caret settles.</summary>
    internal sealed class ExpansionTip
    {
        private readonly IWpfTextView _view;
        private readonly IToolTipService _toolTips;
        private readonly IAsyncCompletionBroker _broker;
        private readonly DispatcherTimer _timer;
        private IToolTipPresenter _presenter;
        private int _shownAt = -1;
        private int _waitingForSchemaAt = -1;

        public ExpansionTip(IWpfTextView view, IToolTipService toolTips, IAsyncCompletionBroker broker)
        {
            _view = view;
            _toolTips = toolTips;
            _broker = broker;
            _timer = new DispatcherTimer(DispatcherPriority.Background) { Interval = TimeSpan.FromMilliseconds(250) };
            _timer.Tick += (s, e) => { _timer.Stop(); Evaluate(); };

            _view.Caret.PositionChanged += OnChanged;
            _view.TextBuffer.PostChanged += OnChanged;
            _view.LostAggregateFocus += OnLostFocus;
            _view.Closed += OnClosed;
        }

        private void OnChanged(object sender, EventArgs e)
        {
            _timer.Stop();
            _timer.Start();
        }

        private void OnLostFocus(object sender, EventArgs e) => Hide();

        private void OnClosed(object sender, EventArgs e)
        {
            _timer.Stop();
            Hide();
            _view.Caret.PositionChanged -= OnChanged;
            _view.TextBuffer.PostChanged -= OnChanged;
            _view.LostAggregateFocus -= OnLostFocus;
            _view.Closed -= OnClosed;
        }

        private void Evaluate()
        {
            ThreadHelper.ThrowIfNotOnUIThread();
            try
            {
                if (_view.IsClosed || !_view.HasAggregateFocus || _broker.IsCompletionActive(_view) || !_view.Selection.IsEmpty)
                {
                    Hide();
                    return;
                }

                var caret = _view.Caret.Position.BufferPosition;
                var window = TextWindow.Around(caret.Snapshot, caret.Position);
                var star = SqlContextAnalyzer.FindStar(window.Text, window.Caret);
                var stmt = star == null ? StatementExpander.Find(window.Text, window.Caret) : null;
                var target = star == null && stmt == null ? null : EditorConnection.Get(window);
                if (target == null)
                {
                    Hide();
                    return;
                }

                var schema = SchemaCache.Instance.TryGetLoaded(target);
                if (schema == null)
                {
                    Hide();
                    WaitForSchemaThenRetry(target, caret.Position);
                    return;
                }

                int start;
                SnapshotSpan span;
                string action;
                if (star != null)
                {
                    int count = CountColumns(star, schema);
                    if (count == 0)
                    {
                        Hide();
                        return;
                    }
                    start = window.Offset + star.ReplaceStart;
                    span = new SnapshotSpan(caret.Snapshot, start, star.ReplaceEnd - star.ReplaceStart);
                    action = $"expand {count} column{(count == 1 ? "" : "s")}";
                }
                else
                {
                    // Mirrors the templates: Tab writes nothing for an unknown table/procedure or one with nothing to fill in.
                    if (stmt.Kind == ExpansionKind.Exec)
                    {
                        if (!StatementExpander.HasExecTemplate(schema.FindRoutine(stmt.Schema, stmt.Name)))
                        {
                            Hide();
                            return;
                        }
                        action = "insert list of parameters";
                    }
                    else if (stmt.Kind == ExpansionKind.Alter)
                    {
                        // Tab reads the definition from the server, which a hint must not do on every caret move: it only checks the
                        // object is known (triggers aren't in the schema cache, so they get no hint). A definition Tab cannot read
                        // (an encrypted procedure) still shows the hint.
                        if (!IsKnownAlterTarget(stmt, schema))
                        {
                            Hide();
                            return;
                        }
                        action = "insert the current definition";
                    }
                    else
                    {
                        var table = schema.Find(stmt.Schema, stmt.Name);
                        bool insert = stmt.Kind == ExpansionKind.Insert;
                        var template = table == null ? null
                            : insert ? StatementExpander.BuildInsert(table, "", "\n")
                            : StatementExpander.BuildUpdate(table, "", "\n");
                        if (template == null)
                        {
                            Hide();
                            return;
                        }
                        action = insert ? "insert columns and VALUES" : "insert SET and WHERE";
                    }
                    start = window.Offset + stmt.NameStart;
                    span = new SnapshotSpan(caret.Snapshot, start, stmt.NameEnd - stmt.NameStart);
                }

                if (_presenter != null && _shownAt == start) return; // already showing here
                Show(span, action);
                _shownAt = start;
            }
            catch (Exception ex)
            {
                Log.Error("Expansion tip", ex);
                Hide();
            }
        }

        // First use of a database: the schema is still loading. Check again once it arrives, if the caret stayed put.
        private void WaitForSchemaThenRetry(ConnectionTarget target, int position)
        {
            if (_waitingForSchemaAt == position) return;
            _waitingForSchemaAt = position;
            ThreadHelper.JoinableTaskFactory.RunAsync(async () =>
            {
                await SchemaCache.Instance.GetAsync(target, TimeSpan.FromSeconds(15), System.Threading.CancellationToken.None);
                await ThreadHelper.JoinableTaskFactory.SwitchToMainThreadAsync();
                _waitingForSchemaAt = -1;
                // After a failed load GetAsync returns at once and the switch above doesn't yield on the UI thread, so
                // Evaluate -> here -> Evaluate would recurse until the stack overflows: only retry with a schema in hand.
                // shortcut: a load slower than the 15 s wait is picked up on the next caret move, not by a retry here.
                if (!_view.IsClosed && _view.Caret.Position.BufferPosition.Position == position &&
                    SchemaCache.Instance.TryGetLoaded(target) != null) Evaluate();
            }).FileAndForget("SqlAssist/ExpansionTipWaitForSchema");
        }

        private static bool IsKnownAlterTarget(ExpansionTarget stmt, SchemaModel schema)
        {
            switch (stmt.ObjectType)
            {
                case "PROCEDURE":
                case "PROC":
                    return schema.FindRoutine(stmt.Schema, stmt.Name)?.Kind == RoutineKind.Procedure;
                case "FUNCTION":
                    var function = schema.FindRoutine(stmt.Schema, stmt.Name);
                    return function != null && function.Kind != RoutineKind.Procedure;
                case "VIEW":
                    return schema.Find(stmt.Schema, stmt.Name)?.IsView == true;
                default:
                    return false;
            }
        }

        private static int CountColumns(StarContext star, SchemaModel schema)
        {
            // Mirrors StarExpander: Tab only expands when every table resolves.
            var tables = star.Tables.Select(schema.Find).ToList();
            return tables.Any(t => t == null) ? 0 : tables.Sum(t => t.Columns.Count);
        }

        private void Show(SnapshotSpan span, string action)
        {
            Hide();
            _presenter = _toolTips.CreatePresenter(_view, new ToolTipParameters(trackMouse: false, ignoreBufferChange: false));
            _presenter.Dismissed += (s, e) => { if (ReferenceEquals(s, _presenter)) { _presenter = null; _shownAt = -1; } };
            var text = new ClassifiedTextElement(
                new ClassifiedTextRun(PredefinedClassificationTypeNames.Text, "Press "),
                new ClassifiedTextRun(PredefinedClassificationTypeNames.Text, "Tab", ClassifiedTextRunStyle.Bold),
                new ClassifiedTextRun(PredefinedClassificationTypeNames.Text, " to " + action));
            _presenter.StartOrUpdate(span.Snapshot.CreateTrackingSpan(span, SpanTrackingMode.EdgeInclusive), new object[] { text });
        }

        private void Hide()
        {
            var presenter = _presenter;
            _presenter = null;
            _shownAt = -1;
            presenter?.Dismiss();
        }
    }
}
