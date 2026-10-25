using System;
using System.Data.SqlClient;
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
    /// <summary>F12 (Edit.GoToDefinition) on an object name: a table opens in Object Explorer; a view, procedure or function
    /// opens its definition in a new query window, never executed. Anything else goes on to SSMS. The decision uses only
    /// the already-loaded schema, so the key press never waits on the database; reading a definition runs in the background.</summary>
    internal static class GoToDefinition
    {
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
                bool routineName = hover.Kind == HoverKind.Routine;
                string schemaName = routineName ? hover.Schema : hover.Table.Schema;
                string name = routineName ? hover.Name : hover.Table.Name;
                string otherDatabase = routineName ? hover.Database : hover.Table.Database;

                var target = EditorConnection.Get(window);
                var schema = SchemaCache.Instance.TryGetLoaded(target);
                if (target == null || schema == null) return false;
                // OtherDb.dbo.Orders: the cache holds this database only, and a same-named object here is not that one.
                if (otherDatabase != null && !string.Equals(otherDatabase, target.Database, StringComparison.OrdinalIgnoreCase)) return false;

                var table = schema.Find(schemaName, name);
                var routine = table == null ? schema.FindRoutine(schemaName, name) : null;
                if (table == null && routine == null) return false; // built-ins, temp tables, system procedures: SSMS's

                if (!table?.IsView ?? false)
                {
                    if (!SsmsNavigator.CanLocate()) return false;
                    ThreadHelper.JoinableTaskFactory.RunAsync(() => LocateTableAsync(target, table.Schema, table.Name))
                        .FileAndForget("SqlAssist/GoToDefinition");
                    return true;
                }

                // The schema the cache resolved the name to: "vOrders" may live in Sales, not the login's default schema.
                string resolvedSchema = table?.Schema ?? routine.Schema;
                var type = table != null ? SearchObjectType.View : routine.IsProcedure ? SearchObjectType.Procedure : SearchObjectType.Function;
                ScriptDelivery.Status("SqlAssist: reading the definition of " + resolvedSchema + "." + name + "...");
                ThreadHelper.JoinableTaskFactory.RunAsync(() => OpenDefinitionAsync(target, resolvedSchema, name, type))
                    .FileAndForget("SqlAssist/GoToDefinition");
                return true;
            }
            catch (Exception ex)
            {
                Log.Error("Go to definition", ex);
                return false;
            }
        }

        private static async System.Threading.Tasks.Task LocateTableAsync(Connection.ConnectionTarget target, string schema, string name)
        {
            string error;
            try
            {
                string database;
                using (var cts = new CancellationTokenSource(DefinitionWait))
                    database = target.Database ?? await SearchIndexLoader.CurrentDatabaseAsync(target.ConnectionString, cts.Token);
                error = await SsmsNavigator.LocateAsync(target, new SearchItem(database, schema, name, SearchObjectType.Table), CancellationToken.None);
            }
            catch (Exception ex)
            {
                Log.Error("Go to definition: locating " + schema + "." + name, ex);
                error = "could not reach the server (see SqlAssist.log)";
            }
            await ThreadHelper.JoinableTaskFactory.SwitchToMainThreadAsync();
            if (error != null) ScriptDelivery.Status("SqlAssist: " + error);
        }

        private static async System.Threading.Tasks.Task OpenDefinitionAsync(Connection.ConnectionTarget target, string schema, string name, SearchObjectType type)
        {
            string status;
            using (var cts = new CancellationTokenSource(DefinitionWait))
            {
                try
                {
                    // OpenScript writes "USE [database]" first, so a window without a database needs the login's default.
                    string database = target.Database ?? await SearchIndexLoader.CurrentDatabaseAsync(target.ConnectionString, cts.Token);
                    string definition = await DefinitionLoader.LoadAsync(target.ConnectionString, schema, name, cts.Token);
                    await ThreadHelper.JoinableTaskFactory.SwitchToMainThreadAsync();
                    if (definition == null)
                    {
                        status = "SqlAssist: no readable definition for " + schema + "." + name;
                    }
                    else
                    {
                        string error = SsmsNavigator.OpenScript(target, new SearchItem(database, schema, name, type, definition: definition), -1, 0);
                        status = error == null ? "Ready" : "SqlAssist: " + error;
                    }
                }
                // SqlClient reports its own cancellation as a SqlException.
                catch (Exception ex) when (cts.IsCancellationRequested && (ex is OperationCanceledException || ex is SqlException))
                {
                    status = "SqlAssist: Go to Definition timed out";
                }
                catch (Exception ex)
                {
                    Log.Error("Go to definition: reading " + schema + "." + name, ex);
                    status = "SqlAssist: could not read the definition (see SqlAssist.log)";
                }
            }
            await ThreadHelper.JoinableTaskFactory.SwitchToMainThreadAsync();
            ScriptDelivery.Status(status);
        }
    }
}
