using System;
using System.Collections;
using System.Linq;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using EnvDTE;
using Microsoft.VisualStudio.Shell;
using SqlAssist.Connection;
using SqlAssist.Core.Search;

namespace SqlAssist.Search
{
    /// <summary>Takes a Search result into SSMS: selects it in Object Explorer, or opens its definition in a new query
    /// window. Both go through SSMS internals by reflection (SqlWorkbench.Interfaces' Object Explorer navigation
    /// service, new in SSMS 22, and SQLEditors' ScriptFactory), so each step reports failure instead of throwing.</summary>
    internal static class SsmsNavigator
    {
        private const BindingFlags Any = BindingFlags.Instance | BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic;

        /// <summary>Null on success; otherwise why it did not work, for the status line.</summary>
        public static async Task<string> LocateAsync(ConnectionTarget server, SearchItem item, CancellationToken token)
        {
            await ThreadHelper.JoinableTaskFactory.SwitchToMainThreadAsync(token);
            try
            {
                var navType = NavigationServiceType();
                object nav = navType == null ? null : await AsyncServiceProvider.GlobalProvider.GetServiceAsync(navType);
                if (nav == null) return Fail("Object Explorer navigation service not available in this SSMS build");
                await ThreadHelper.JoinableTaskFactory.SwitchToMainThreadAsync(token);

                ShowObjectExplorer();

                string rootUrn = FindRootUrn(nav, server.Server);
                if (rootUrn == null)
                {
                    // Not in Object Explorer yet: connect it the same way the query window is connected.
                    Invoke(nav, "ConnectToServer", server.Server, item.Database, server.IntegratedSecurity, server.UserName, server.ConnectionString);
                    rootUrn = FindRootUrn(nav, server.Server) ?? ObjectUrn.Server(server.Server);
                }

                var urns = ObjectUrn.Candidates(rootUrn, item);
                foreach (var urn in urns)
                    if (await NavigateAsync(nav, urn, token)) return null;
                // A column, trigger, index or constraint node can be missing (folder not loaded, older SSMS); at least land on its owner.
                if (item.Parent != null)
                    foreach (var urn in urns)
                        if (await NavigateAsync(nav, ObjectUrn.Owner(urn), token)) return null;
                return Fail("Object Explorer could not find " + urns.FirstOrDefault());
            }
            catch (OperationCanceledException)
            {
                return null;
            }
            catch (Exception ex)
            {
                Log.Error("Locate in Object Explorer", ex);
                return "Locate failed: " + ex.Message;
            }
        }

        /// <summary>Opens a new query window on the server with <c>USE [db]</c> and the item's preview text, selecting
        /// <paramref name="selectStart"/>..+<paramref name="selectLength"/> of that text (the first hit).</summary>
        public static string OpenScript(ConnectionTarget server, SearchItem item, int selectStart, int selectLength)
        {
            ThreadHelper.ThrowIfNotOnUIThread();
            try
            {
                string body = item.PreviewText ?? (item.Type == SearchObjectType.Schema ? "-- Schema " + item.Name : "-- No definition available");
                string prefix = "USE " + TableScriptBuilder.Bracket(item.Database) + ";\r\nGO\r\n\r\n";
                string text = prefix + body.Replace("\r\n", "\n").Replace("\n", "\r\n");
                if (selectStart >= 0) selectStart = prefix.Length + CrLfOffset(body, selectStart);

                var doc = NewQueryWindow(server.UiConnectionInfo, text, out string error);
                if (doc == null) return error;

                var selection = doc.Selection;
                if (selectStart >= 0)
                {
                    var (line, col) = LineAndColumn(text, selectStart);
                    var (endLine, endCol) = LineAndColumn(text, selectStart + Math.Max(selectLength, 0));
                    selection.MoveToLineAndOffset(line, col);
                    selection.MoveToLineAndOffset(endLine, endCol, true);
                }
                else selection.StartOfDocument();
                return null;
            }
            catch (Exception ex)
            {
                Log.Error("Open script", ex);
                return "Open script failed: " + ex.Message;
            }
        }

        /// <summary>Opens a new query window holding <paramref name="text"/> (CRLF), connected through
        /// <paramref name="uiConnectionInfo"/> when there is one. Null on success; otherwise why not.</summary>
        public static string OpenQueryWindow(object uiConnectionInfo, string text)
        {
            ThreadHelper.ThrowIfNotOnUIThread();
            try
            {
                var doc = NewQueryWindow(uiConnectionInfo, text.Replace("\r\n", "\n").Replace("\n", "\r\n"), out string error);
                doc?.Selection.StartOfDocument();
                return error;
            }
            catch (Exception ex)
            {
                Log.Error("Open query window", ex);
                return "Open query window failed: " + ex.Message;
            }
        }

        private static TextDocument NewQueryWindow(object uiConnectionInfo, string text, out string error)
        {
            ThreadHelper.ThrowIfNotOnUIThread();
            error = null;
            var factoryType = FindAssembly("SQLEditors")?.GetType("Microsoft.SqlServer.Management.UI.VSIntegration.Editors.ScriptFactory");
            object factory = factoryType?.GetProperty("Instance", Any)?.GetValue(null);
            if (factory == null) { error = Fail("ScriptFactory not available"); return null; }

            var create = factoryType.GetMethods().FirstOrDefault(m => m.Name == "CreateNewBlankScript" && m.GetParameters().Length == 3
                && m.GetParameters()[1].ParameterType.Name == "UIConnectionInfo");
            var createBlank = factoryType.GetMethods().FirstOrDefault(m => m.Name == "CreateNewBlankScript" && m.GetParameters().Length == 1);
            var scriptType = (create ?? createBlank)?.GetParameters()[0].ParameterType;
            if (scriptType == null) { error = Fail("CreateNewBlankScript not found"); return null; }
            object sql = Enum.Parse(scriptType, "Sql");
            if (create != null && uiConnectionInfo != null) create.Invoke(factory, new[] { sql, uiConnectionInfo, null });
            else createBlank.Invoke(factory, new[] { sql });

            if (!(ServiceProvider.GlobalProvider.GetService(typeof(DTE)) is DTE dte) || !(dte.ActiveDocument?.Object("TextDocument") is TextDocument doc))
            {
                error = Fail("The new query window did not open");
                return null;
            }
            doc.StartPoint.CreateEditPoint().Insert(text);
            return doc;
        }

        private static void ShowObjectExplorer()
        {
            ThreadHelper.ThrowIfNotOnUIThread();
            try
            {
                if (ServiceProvider.GlobalProvider.GetService(typeof(DTE)) is DTE dte) dte.ExecuteCommand("View.ObjectExplorer");
            }
            catch
            {
                // Already visible or the command has another name; navigation still selects the node.
            }
        }

        private static string FindRootUrn(object nav, string serverName)
        {
            if (!(Invoke(nav, "GetConnectedServers") is IEnumerable servers)) return null;
            foreach (var s in servers)
            {
                string name = Get(s, "ServerName") as string;
                if (Get(s, "IsConnected") is bool connected && !connected) continue;
                if (string.Equals(name, serverName, StringComparison.OrdinalIgnoreCase)) return Get(s, "RootUrn") as string ?? ObjectUrn.Server(name);
            }
            return null;
        }

        private static async Task<bool> NavigateAsync(object nav, string urn, CancellationToken token)
        {
            if (!(Invoke(nav, "NavigateToUrnAsync", urn, token) is Task task)) return false;
            await task;
            return task.GetType().GetProperty("Result")?.GetValue(task) is bool ok && ok;
        }

        private static object Invoke(object target, string method, params object[] args)
        {
            var m = target.GetType().GetMethod(method, Any)
                ?? target.GetType().GetInterfaces().Select(i => i.GetMethod(method)).FirstOrDefault(x => x != null);
            return m?.Invoke(target, args);
        }

        private static object Get(object target, string property)
        {
            try { return target?.GetType().GetProperty(property, Any)?.GetValue(target); }
            catch { return null; }
        }

        /// <summary>Whether this SSMS build has the Object Explorer navigation <see cref="LocateAsync"/> relies on, so a
        /// caller can hand the job to SSMS instead when it is gone.</summary>
        public static bool CanLocate() => NavigationServiceType() != null;

        private static Type NavigationServiceType() =>
            FindAssembly("SqlWorkbench.Interfaces")?.GetType("Microsoft.SqlServer.Management.UI.VSIntegration.ObjectExplorer.SObjectExplorerNavigationService");

        private static Assembly FindAssembly(string name) =>
            AppDomain.CurrentDomain.GetAssemblies().FirstOrDefault(a => string.Equals(a.GetName().Name, name, StringComparison.OrdinalIgnoreCase));

        /// <summary>Where <paramref name="offset"/> in <paramref name="text"/> lands once every bare \n became \r\n.</summary>
        private static int CrLfOffset(string text, int offset)
        {
            int added = 0;
            for (int i = 0; i < offset && i < text.Length; i++)
                if (text[i] == '\n' && (i == 0 || text[i - 1] != '\r')) added++;
            return offset + added;
        }

        /// <summary>1-based line and column of an offset in CRLF text, as EnvDTE wants them.</summary>
        private static (int line, int column) LineAndColumn(string text, int offset)
        {
            int line = 1, lineStart = 0;
            for (int i = 0; i < offset && i < text.Length; i++)
                if (text[i] == '\n') { line++; lineStart = i + 1; }
            return (line, offset - lineStart + 1);
        }

        private static string Fail(string message)
        {
            Log.Info(message);
            return message;
        }
    }
}
