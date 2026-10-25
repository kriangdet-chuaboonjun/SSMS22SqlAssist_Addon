using System;
using System.ComponentModel.Composition;
using System.Runtime.InteropServices;
using System.Threading;
using Microsoft.VisualStudio;
using Microsoft.VisualStudio.Editor;
using Microsoft.VisualStudio.Language.Intellisense.AsyncCompletion;
using Microsoft.VisualStudio.Language.Intellisense.AsyncCompletion.Data;
using Microsoft.VisualStudio.OLE.Interop;
using Microsoft.VisualStudio.Shell;
using Microsoft.VisualStudio.Text;
using Microsoft.VisualStudio.Text.Editor;
using Microsoft.VisualStudio.Utilities;
using SqlAssist.Connection;
using SqlAssist.Core.Completion;
using SqlAssist.Core.Sql;
using SqlAssist.Schema;
using SqlAssist.Snippets;

namespace SqlAssist.Editor
{
    [Export(typeof(IWpfTextViewCreationListener))]
    [Name("SqlAssist command filter")]
    [ContentType(SqlContentType.Name)]
    [TextViewRole(PredefinedTextViewRoles.Editable)]
    internal sealed class TabCommandFilterProvider : IWpfTextViewCreationListener
    {
        [Import] internal IVsEditorAdaptersFactoryService Adapters { get; set; }
        [Import] internal IAsyncCompletionBroker Broker { get; set; }

        public void TextViewCreated(IWpfTextView textView)
        {
            ThreadHelper.ThrowIfNotOnUIThread();
            var adapter = Adapters.GetViewAdapter(textView);
            if (adapter == null) return;
            var filter = new TabCommandFilter(textView, Broker);
            if (ErrorHandler.Succeeded(adapter.AddCommandFilter(filter, out var next)))
            {
                filter.Next = next;
                Log.Info($"SqlAssist {SqlAssistPackage.Version} attached to a SQL editor");
            }
        }
    }

    /// <summary>SSMS routes keys through the IOleCommandTarget chain (WPF key processors never see them),
    /// so this sits at the front of each SQL editor's chain. Tab is decided in this order:
    /// snippet shortcut → commit the open completion list → expand SELECT * → plain Tab.</summary>
    internal sealed class TabCommandFilter : IOleCommandTarget
    {
        private static readonly TimeSpan StarSchemaWait = TimeSpan.FromSeconds(5);
        private static readonly TimeSpan DefinitionWait = TimeSpan.FromSeconds(10);

        private readonly IWpfTextView _view;
        private readonly IAsyncCompletionBroker _broker;

        public TabCommandFilter(IWpfTextView view, IAsyncCompletionBroker broker)
        {
            _view = view;
            _broker = broker;
        }

        public IOleCommandTarget Next { get; set; }

        public int QueryStatus(ref Guid pguidCmdGroup, uint cCmds, OLECMD[] prgCmds, IntPtr pCmdText)
        {
            ThreadHelper.ThrowIfNotOnUIThread();
            if (cCmds == 1 && RefreshCommand.Matches(pguidCmdGroup, prgCmds[0].cmdID))
            {
                // Keep Ctrl+Shift+R alive even with SSMS's own IntelliSense switched off.
                prgCmds[0].cmdf = (uint)(OLECMDF.OLECMDF_SUPPORTED | OLECMDF.OLECMDF_ENABLED);
                return VSConstants.S_OK;
            }
            return Next.QueryStatus(ref pguidCmdGroup, cCmds, prgCmds, pCmdText);
        }

        public int Exec(ref Guid pguidCmdGroup, uint nCmdID, uint nCmdexecopt, IntPtr pvaIn, IntPtr pvaOut)
        {
            ThreadHelper.ThrowIfNotOnUIThread();
            try
            {
                if (RefreshCommand.Matches(pguidCmdGroup, nCmdID))
                {
                    SchemaCache.Instance.Clear();
                    Search.SearchIndexCache.Instance.Clear();
                    Next.Exec(ref pguidCmdGroup, nCmdID, nCmdexecopt, pvaIn, pvaOut);
                    return VSConstants.S_OK;
                }

                if (pguidCmdGroup == VSConstants.VSStd2K)
                {
                    switch ((VSConstants.VSStd2KCmdID)nCmdID)
                    {
                        case VSConstants.VSStd2KCmdID.TAB:
                            if (HandleTab()) return VSConstants.S_OK;
                            break;

                        case VSConstants.VSStd2KCmdID.COMPLETEWORD:
                        case VSConstants.VSStd2KCmdID.SHOWMEMBERLIST:
                        case VSConstants.VSStd2KCmdID.AUTOCOMPLETE:
                            // Ctrl+Space: open our list, and keep SSMS's from opening on top of it.
                            Trigger(CompletionTriggerReason.Invoke, '\0');
                            return VSConstants.S_OK;

                        case VSConstants.VSStd2KCmdID.RIGHT:
                        case VSConstants.VSStd2KCmdID.LEFT:
                            if (HandleTreeArrow((VSConstants.VSStd2KCmdID)nCmdID == VSConstants.VSStd2KCmdID.RIGHT)) return VSConstants.S_OK;
                            break;

                        case VSConstants.VSStd2KCmdID.TYPECHAR:
                            return TypeChar(ref pguidCmdGroup, nCmdID, nCmdexecopt, pvaIn, pvaOut);
                    }
                }
            }
            catch (Exception ex)
            {
                Log.Error("Command filter", ex);
            }
            return Next.Exec(ref pguidCmdGroup, nCmdID, nCmdexecopt, pvaIn, pvaOut);
        }

        private int TypeChar(ref Guid group, uint id, uint opt, IntPtr pvaIn, IntPtr pvaOut)
        {
            ThreadHelper.ThrowIfNotOnUIThread();
            char ch = (char)(ushort)Marshal.GetObjectForNativeVariant(pvaIn);

            // Shift+Space in the table tree ticks the selected table/column instead of typing a space.
            if (ch == ' ' && (System.Windows.Input.Keyboard.Modifiers & System.Windows.Input.ModifierKeys.Shift) != 0 &&
                TrySelectedTreeNode(out var treeSession, out var treeItem, out var treeNode))
            {
                TreeState.Toggle(treeSession, _view, treeItem, treeNode);
                return VSConstants.S_OK;
            }
            // Nothing commits on a typed character (see SqlCommitManager), so a list still open when the word ends
            // is closed here instead of following the caret into the next word.
            if (!SqlIdentifier.IsWordChar(ch) && ch != '[')
                _broker.GetSession(_view)?.Dismiss();

            int hr = Next.Exec(ref group, id, opt, pvaIn, pvaOut);

            // The editor's own completion handler normally does both of these; repeating them is harmless and
            // covers an SSMS build where it is not in the chain.
            // The character is in the buffer now: a failure here must not reach Exec's catch, which sends it down the chain again.
            try
            {
                var session = _broker.GetSession(_view);
                if (session != null && !session.IsDismissed)
                {
                    var caret = _view.Caret.Position.BufferPosition;
                    session.OpenOrUpdate(new CompletionTrigger(CompletionTriggerReason.Insertion, caret.Snapshot, ch), caret, CancellationToken.None);
                }
                else if (char.IsLetter(ch) || ch == '_' || ch == '.' || ch == ' ' || ch == '@')
                {
                    Trigger(CompletionTriggerReason.Insertion, ch);
                }
            }
            catch (Exception ex)
            {
                Log.Error("Completion after typing", ex);
            }
            return hr;
        }

        // → expands the selected table, ← collapses it (or, on a column, collapses its table and selects it).
        private bool HandleTreeArrow(bool right)
        {
            ThreadHelper.ThrowIfNotOnUIThread();
            if (!TrySelectedTreeNode(out var session, out var item, out var node)) return false;
            var tree = TreeState.For(session);
            if (node.IsTable)
            {
                bool expanded = tree.Expanded.Contains(node.Key);
                if (right != expanded) tree.ToggleExpanded(node);
                TreeState.Refresh(session, _view, item);
                return true;
            }
            if (right) return true; // a column has nothing to expand; keep the caret where it is
            var parent = tree.ParentItem(node);
            tree.Expanded.Remove(node.TableNodeKey);
            TreeState.Refresh(session, _view, parent);
            return true;
        }

        private bool TrySelectedTreeNode(out IAsyncCompletionSession session, out CompletionItem item, out TreeNode node)
        {
            session = _broker.GetSession(_view);
            item = null;
            node = null;
            if (session == null || session.IsDismissed) return false;
            item = session.GetComputedItems(CancellationToken.None)?.SelectedItem;
            node = TreeState.NodeOf(item);
            return node != null;
        }

        private bool HandleTab()
        {
            ThreadHelper.ThrowIfNotOnUIThread();
            if (!_view.Selection.IsEmpty || _view.Selection.Mode == TextSelectionMode.Box) return false;
            var caret = _view.Caret.Position.BufferPosition;
            var window = TextWindow.Around(caret.Snapshot, caret.Position);

            // 1. Snippet shortcut — checked first so "ssf" expands even while the list is open on it.
            if (SqlContextAnalyzer.TryGetSnippetWord(window.Text, window.Caret, out var word, out int start) &&
                SnippetProvider.Current.TryGet(word, out var snippet))
            {
                _broker.GetSession(_view)?.Dismiss();
                SnippetInserter.Insert(_view, _broker, new SnapshotSpan(caret.Snapshot, window.Offset + start, word.Length), snippet);
                return true;
            }

            // 2. An open list with a selection: let the editor commit it.
            var session = _broker.GetSession(_view);
            if (session != null && !session.IsDismissed)
            {
                if (session.GetComputedItems(CancellationToken.None)?.SelectedItem != null) return false;
                session.Dismiss();
            }

            // 3. SELECT *| → column list.
            if (ExpandStar(caret, window)) return true;

            // 4. INSERT INTO t| / UPDATE t| / EXEC p| / ALTER PROCEDURE p| → the full statement.
            return ExpandStatement(caret, window);
        }

        private bool ExpandStatement(SnapshotPoint caret, TextWindow window)
        {
            ThreadHelper.ThrowIfNotOnUIThread();
            var found = StatementExpander.Find(window.Text, window.Caret);
            if (found == null) return false;

            var target = ActiveConnection.TryGet();
            if (target == null)
            {
                Log.Info("Statement expansion skipped: no connected query window");
                return false;
            }

            var snapshot = caret.Snapshot;
            int statementStart = window.Offset + found.StatementStart;
            var line = snapshot.GetLineFromPosition(statementStart);
            string indent = LeadingWhitespace(snapshot.GetText(line.Start, statementStart - line.Start));
            string newLine = line.GetLineBreakText();
            if (string.IsNullOrEmpty(newLine)) newLine = _view.Options.GetOptionValue(DefaultOptions.NewLineCharacterOptionId);

            if (found.Kind == ExpansionKind.Alter)
            {
                string definition;
                try
                {
                    using (var cts = new CancellationTokenSource(DefinitionWait))
                        definition = ThreadHelper.JoinableTaskFactory.Run(() => DefinitionLoader.LoadAsync(target.ConnectionString, found.Schema, found.Name, cts.Token));
                }
                catch (Exception ex)
                {
                    Log.Error("ALTER expansion: reading the definition of " + found.Name, ex);
                    return false;
                }
                string script = StatementExpander.AlterScript(definition);
                if (script == null)
                {
                    Log.Info("ALTER expansion skipped: no readable definition for " + found.Name);
                    return false;
                }
                var replace = new SnapshotSpan(snapshot, statementStart, caret.Position - statementStart);
                snapshot.TextBuffer.Replace(replace, script);
                _view.Caret.MoveTo(new SnapshotPoint(_view.TextBuffer.CurrentSnapshot, statementStart));
                return true;
            }

            var schema = ThreadHelper.JoinableTaskFactory.Run(() => SchemaCache.Instance.GetAsync(target, StarSchemaWait, CancellationToken.None));
            Expansion expansion = null;
            if (found.Kind == ExpansionKind.Exec)
            {
                var routine = schema.FindRoutine(found.Schema, found.Name);
                if (StatementExpander.HasExecTemplate(routine))
                    expansion = StatementExpander.BuildExec(routine, snapshot.GetText(line.Start, caret.Position - line.Start), indent, newLine);
                else if (routine == null) SchemaCache.Instance.RefreshIfOlderThanThrottle(target);
            }
            else
            {
                var table = schema.Find(found.Schema, found.Name);
                if (table == null) SchemaCache.Instance.RefreshIfOlderThanThrottle(target);
                else if (found.Kind == ExpansionKind.Insert) expansion = StatementExpander.BuildInsert(table, indent, newLine);
                else expansion = StatementExpander.BuildUpdate(table, indent, newLine);
            }
            if (expansion == null)
            {
                Log.Info($"{found.Kind} expansion skipped: {found.Name} not found (or nothing to fill in) in {target}");
                return false;
            }

            // One edit, so a single Undo takes the whole template back.
            using (var edit = snapshot.TextBuffer.CreateEdit())
            {
                edit.Insert(caret.Position, expansion.Text);
                if (expansion.Prefix.Length > 0) edit.Insert(statementStart, expansion.Prefix);
                edit.Apply();
            }
            _view.Caret.MoveTo(new SnapshotPoint(_view.TextBuffer.CurrentSnapshot, caret.Position + expansion.Prefix.Length + expansion.CaretOffset));
            return true;
        }

        private static string LeadingWhitespace(string s) => s.Substring(0, s.Length - s.TrimStart().Length);

        private bool ExpandStar(SnapshotPoint caret, TextWindow window)
        {
            ThreadHelper.ThrowIfNotOnUIThread();
            var star = SqlContextAnalyzer.FindStar(window.Text, window.Caret);
            if (star == null) return false;

            var target = ActiveConnection.TryGet();
            if (target == null)
            {
                Log.Info("SELECT * expansion skipped: no connected query window");
                return false;
            }
            var schema = ThreadHelper.JoinableTaskFactory.Run(() => SchemaCache.Instance.GetAsync(target, StarSchemaWait, CancellationToken.None));
            var span = new SnapshotSpan(caret.Snapshot, window.Offset + star.ReplaceStart, star.ReplaceEnd - star.ReplaceStart);

            // One column per line with leading commas, aligned under the first column.
            var line = span.Start.GetContainingLine();
            string linePrefix = caret.Snapshot.GetText(line.Start, span.Start - line.Start);
            string newLine = line.GetLineBreakText();
            if (string.IsNullOrEmpty(newLine)) newLine = _view.Options.GetOptionValue(DefaultOptions.NewLineCharacterOptionId);

            string columns = StarExpander.Expand(star, schema, linePrefix, newLine);
            if (columns == null)
            {
                SchemaCache.Instance.RefreshIfOlderThanThrottle(target);
                Log.Info("SELECT * expansion skipped: table not found in schema of " + target);
                return false;
            }

            var after = caret.Snapshot.TextBuffer.Replace(span, columns);
            _view.Caret.MoveTo(new SnapshotPoint(after, span.Start.Position + columns.Length));
            return true;
        }

        private void Trigger(CompletionTriggerReason reason, char ch)
        {
            var caret = _view.Caret.Position.BufferPosition;
            var trigger = new CompletionTrigger(reason, caret.Snapshot, ch);
            _broker.TriggerCompletion(_view, trigger, caret, CancellationToken.None)?.OpenOrUpdate(trigger, caret, CancellationToken.None);
        }
    }
}
