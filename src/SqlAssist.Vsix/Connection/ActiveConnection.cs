using System;
using System.Data.SqlClient;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Security;
using Microsoft.VisualStudio.Shell;

namespace SqlAssist.Connection
{
    internal sealed class ConnectionTarget
    {
        public string Server { get; set; }
        public string Database { get; set; }
        public string ConnectionString { get; set; }
        /// <summary>Identifies one schema: same server, database and login share a cache entry.</summary>
        public string Key { get; set; }
        /// <summary>Like <see cref="Key"/> without the database: the same server and login, whichever database is in use.</summary>
        public string ServerKey { get; set; }
        public bool IntegratedSecurity { get; set; }
        public string UserName { get; set; }
        /// <summary>SSMS's own UIConnectionInfo for this connection, for opening a new query window on it.</summary>
        public object UiConnectionInfo { get; set; }
        public override string ToString() => Server + "/" + (Database ?? "(default)");

        /// <summary>The same server and login in another database (a script's <c>USE</c>): its own schema cache entry.</summary>
        public ConnectionTarget ForDatabase(string database)
        {
            if (string.Equals(database, Database, StringComparison.OrdinalIgnoreCase)) return this;
            return new ConnectionTarget
            {
                Server = Server,
                Database = database,
                ConnectionString = new SqlConnectionStringBuilder(ConnectionString) { InitialCatalog = database }.ConnectionString,
                Key = (Server + "|" + database + "|" + (IntegratedSecurity ? "windows" : UserName)).ToLowerInvariant(),
                ServerKey = ServerKey,
                IntegratedSecurity = IntegratedSecurity,
                UserName = UserName,
                UiConnectionInfo = UiConnectionInfo,
            };
        }
    }

    /// <summary>Reads the active query window's connection from SSMS. There is no public API for this, so it goes
    /// through SQLEditors' ScriptFactory by reflection — the same route other SSMS 22 extensions use — and every
    /// step tolerates the member being gone in a later SSMS build.</summary>
    internal static class ActiveConnection
    {
        private const BindingFlags Any = BindingFlags.Instance | BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic;
        private static string _lastProblem;
        private static ConnectionTarget _lastKnown;
        /// <summary>One connection per server and login seen this session, most recent first (UI thread only). The Search
        /// window offers these as its server list; SSMS has no API to enumerate its open connections.</summary>
        private static readonly System.Collections.Generic.List<ConnectionTarget> Seen = new System.Collections.Generic.List<ConnectionTarget>();
        private static bool _activeUnsupported;
        private static bool _loggedFallback;

        /// <summary>Null when there is no connected query window (or it uses Entra ID, which v1 does not support).</summary>
        public static ConnectionTarget TryGet()
        {
            ThreadHelper.ThrowIfNotOnUIThread();
            _activeUnsupported = false;
            try
            {
                var asm = FindAssembly("SQLEditors");
                var factoryType = asm?.GetType("Microsoft.SqlServer.Management.UI.VSIntegration.Editors.ScriptFactory");
                if (factoryType == null) return Problem("ScriptFactory type not found in SQLEditors");
                object factory = factoryType.GetProperty("Instance", Any)?.GetValue(null);
                object wnd = factory == null ? null : factoryType.GetProperty("CurrentlyActiveWndConnectionInfo", Any)?.GetValue(factory);
                object info = Get(wnd, "UIConnectionInfo");
                if (info == null) return null; // not connected — normal, not worth logging

                string server = Get(info, "ServerName") as string;
                if (string.IsNullOrEmpty(server)) return null;
                if (Get(info, "RenewableToken") != null)
                    return Unsupported("Connection to " + server + " uses Entra ID authentication, which SqlAssist does not support yet");

                string database = GetAdvancedOption(info, "DATABASE");
                var builder = new SqlConnectionStringBuilder
                {
                    DataSource = server,
                    ApplicationName = "SqlAssist",
                    ConnectTimeout = 10,
                    Encrypt = true,
                    TrustServerCertificate = true,
                };
                if (!string.IsNullOrEmpty(database)) builder.InitialCatalog = database;

                string user = Get(info, "UserName") as string;
                string password = Get(info, "Password") as string;
                if (string.IsNullOrEmpty(password)) password = Read(Get(info, "InMemoryPassword") as SecureString);
                if (!string.IsNullOrEmpty(password))
                {
                    builder.UserID = user ?? "";
                    builder.Password = password;
                }
                else
                {
                    builder.IntegratedSecurity = true;
                }

                _lastProblem = null;
                _lastKnown = new ConnectionTarget
                {
                    Server = server,
                    Database = database,
                    ConnectionString = builder.ConnectionString,
                    Key = (server + "|" + database + "|" + (builder.IntegratedSecurity ? "windows" : user)).ToLowerInvariant(),
                    ServerKey = (server + "|" + (builder.IntegratedSecurity ? "windows" : user)).ToLowerInvariant(),
                    IntegratedSecurity = builder.IntegratedSecurity,
                    UserName = builder.IntegratedSecurity ? null : user,
                    UiConnectionInfo = info,
                };
                Seen.RemoveAll(t => t.ServerKey == _lastKnown.ServerKey);
                Seen.Insert(0, _lastKnown);
                return _lastKnown;
            }
            catch (Exception ex)
            {
                return Problem("Reading the active connection failed: " + ex.Message);
            }
        }

        /// <summary>For windows outside the query editor (the Search tool window): the active query window's connection,
        /// or the last one seen when focus has left the editor and ScriptFactory no longer reports one. Which of the two
        /// SSMS 22 does is not verified yet, so the first fallback is logged. An active window that is connected but
        /// unsupported (Entra ID) does not fall back, so a search never silently runs against a different server.</summary>
        public static ConnectionTarget TryGetForToolWindow()
        {
            ThreadHelper.ThrowIfNotOnUIThread();
            var target = TryGet();
            if (target != null || _activeUnsupported || _lastKnown == null) return target;
            if (!_loggedFallback)
            {
                Log.Info("No active query window connection; using the last one seen (" + _lastKnown + ")");
                _loggedFallback = true;
            }
            return _lastKnown;
        }

        public static System.Collections.Generic.IReadOnlyList<ConnectionTarget> SeenServers
        {
            get
            {
                ThreadHelper.ThrowIfNotOnUIThread();
                return Seen.ToArray();
            }
        }

        private static ConnectionTarget Unsupported(string message)
        {
            _activeUnsupported = true;
            return Problem(message);
        }

        private static ConnectionTarget Problem(string message)
        {
            if (message != _lastProblem) Log.Info(message);
            _lastProblem = message;
            return null;
        }

        private static Assembly FindAssembly(string name)
        {
            foreach (var asm in AppDomain.CurrentDomain.GetAssemblies())
                if (string.Equals(asm.GetName().Name, name, StringComparison.OrdinalIgnoreCase)) return asm;
            return null;
        }

        private static object Get(object target, string property)
        {
            if (target == null) return null;
            try { return target.GetType().GetProperty(property, Any)?.GetValue(target); }
            catch { return null; }
        }

        // UIConnectionInfo.AdvancedOptions is a NameValueCollection-like bag; "DATABASE" follows the editor's database.
        private static string GetAdvancedOption(object info, string key)
        {
            object options = Get(info, "AdvancedOptions");
            if (options == null) return null;
            try
            {
                var indexer = options.GetType().GetProperty("Item", BindingFlags.Instance | BindingFlags.Public, null, null, new[] { typeof(string) }, null);
                return indexer?.GetValue(options, new object[] { key }) as string;
            }
            catch { return null; }
        }

        private static string Read(SecureString secure)
        {
            if (secure == null || secure.Length == 0) return null;
            IntPtr ptr = IntPtr.Zero;
            try
            {
                ptr = Marshal.SecureStringToGlobalAllocUnicode(secure);
                return Marshal.PtrToStringUni(ptr);
            }
            finally
            {
                if (ptr != IntPtr.Zero) Marshal.ZeroFreeGlobalAllocUnicode(ptr);
            }
        }
    }
}
