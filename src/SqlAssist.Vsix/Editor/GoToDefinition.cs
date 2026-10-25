using System;
using System.Threading;
using Microsoft.VisualStudio.Shell;
using Microsoft.VisualStudio.Text.Editor;
using SqlAssist.Core.Search;
using SqlAssist.Core.Sql;
using SqlAssist.Results;
using SqlAssist.Schema;
using SqlAssist.Search;

namespace SqlAssist.Editor
{
    /// <summary>F12 (Edit.GoToDefinition) on an object name: a table opens in Object Explorer; a view, procedure, function
    /// or trigger opens its definition in a new query window, never executed. Anything else goes on to SSMS.</summary>
    internal static class GoToDefinition
    {
        private static readonly TimeSpan SchemaWait = TimeSpan.FromSeconds(5);
        private static readonly TimeSpan DefinitionWait = TimeSpan.FromSeconds(10);

        /// <summary>False hands the command on to the rest of the chain.</summary>
        public static bool TryRun(IWpfTextView view)
        {
            ThreadHelper.ThrowIfNotOnUIThread();
            try
            {
                var caret = view.Caret.Position.BufferPosition;
                var window = TextWindow.Around(caret.Snapshot, caret.Position);
                // The caret right after a name ("Orders|") counts as on it.
                var hover = SqlContextAnalyzer.FindHoverTarget(window.Text, window.Caret)
                    ?? (window.Caret > 0 ? SqlContextAnalyzer.FindHoverTarget(window.Text, window.Caret - 1) : null);
                if (hover == null || hover.Kind == HoverKind.Column || hover.Table?.Derived != null) return false;
                string schemaName = hover.Kind == HoverKind.Routine ? hover.Schema : hover.Table.Schema;
                string name = hover.Kind == HoverKind.Routine ? hover.Name : hover.Table.Name;

                var target = EditorConnection.Get(window);
                if (target == null)
                {
                    ScriptDelivery.Status("SqlAssist: no connected query window");
                    return false;
                }

                var jtf = ThreadHelper.JoinableTaskFactory;
                var schema = jtf.Run(() => SchemaCache.Instance.GetAsync(target, SchemaWait, CancellationToken.None));
                var table = schema.Find(schemaName, name);
                string database, definition = null;
                using (var cts = new CancellationTokenSource(DefinitionWait))
                {
                    // OpenScript writes "USE [database]" first, so a window without a database needs the login's default.
                    database = target.Database ?? jtf.Run(() => SearchIndexLoader.CurrentDatabaseAsync(target.ConnectionString, cts.Token));
                    if (table == null || table.IsView)
                        definition = jtf.Run(() => DefinitionLoader.LoadAsync(target.ConnectionString, schemaName, name, cts.Token));
                }

                if (table != null && !table.IsView)
                {
                    var item = new SearchItem(database, table.Schema, table.Name, SearchObjectType.Table);
                    jtf.RunAsync(async () =>
                    {
                        string error = await SsmsNavigator.LocateAsync(target, item, CancellationToken.None);
                        await jtf.SwitchToMainThreadAsync();
                        if (error != null) ScriptDelivery.Status("SqlAssist: " + error);
                    }).FileAndForget("SqlAssist/GoToDefinition");
                    return true;
                }

                if (definition == null)
                {
                    ScriptDelivery.Status("SqlAssist: no definition found for " + name);
                    return false;
                }
                var routine = schema.FindRoutine(schemaName, name);
                var type = table != null ? SearchObjectType.View
                    : routine != null && !routine.IsProcedure ? SearchObjectType.Function
                    : SearchObjectType.Procedure;
                var script = new SearchItem(database, table?.Schema ?? routine?.Schema ?? schemaName, name, type, definition: definition);
                string openError = SsmsNavigator.OpenScript(target, script, -1, 0);
                if (openError != null) ScriptDelivery.Status("SqlAssist: " + openError);
                return true;
            }
            catch (OperationCanceledException)
            {
                ScriptDelivery.Status("SqlAssist: Go to Definition timed out");
                return false;
            }
            catch (Exception ex)
            {
                Log.Error("Go to definition", ex);
                return false;
            }
        }
    }
}
