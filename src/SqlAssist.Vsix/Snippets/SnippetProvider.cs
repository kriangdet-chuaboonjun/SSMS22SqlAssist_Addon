using System;
using System.IO;
using SqlAssist.Core.Snippets;

namespace SqlAssist.Snippets
{
    /// <summary>The user's snippets.xml under %APPDATA%\SqlAssist, re-read whenever the file changes.</summary>
    internal static class SnippetProvider
    {
        public static readonly string FilePath = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "SqlAssist", "snippets.xml");

        private static readonly object Gate = new object();
        private static SnippetStore _store;
        private static DateTime _loadedWriteTime;
        private static DateTime _lastCheckUtc;

        public static SnippetStore Current
        {
            get
            {
                lock (Gate)
                {
                    // A stat per keystroke is cheap, but there is no need for more than one every two seconds.
                    if (_store != null && DateTime.UtcNow - _lastCheckUtc < TimeSpan.FromSeconds(2)) return _store;
                    _lastCheckUtc = DateTime.UtcNow;
                    try
                    {
                        EnsureFileExists();
                        var writeTime = File.GetLastWriteTimeUtc(FilePath);
                        if (_store == null || writeTime != _loadedWriteTime)
                        {
                            _store = SnippetStore.Parse(File.ReadAllText(FilePath));
                            _loadedWriteTime = writeTime;
                            Log.Info($"Loaded {_store.All.Count} snippets from {FilePath}");
                        }
                    }
                    catch (Exception ex)
                    {
                        // A typo in the user's file keeps the previous snippets rather than losing them all.
                        Log.Error("Reading " + FilePath, ex);
                        if (_store == null) _store = SnippetStore.Parse(SnippetStore.DefaultXml);
                        _loadedWriteTime = SafeWriteTime();
                    }
                    return _store;
                }
            }
        }

        /// <summary>Writes the snippets (from the Snippets Manager) and makes them current straight away,
        /// without waiting for the two-second file check.</summary>
        public static void Save(System.Collections.Generic.IEnumerable<Snippet> snippets)
        {
            lock (Gate)
            {
                Directory.CreateDirectory(Path.GetDirectoryName(FilePath));
                string xml = SnippetStore.ToXml(snippets);
                WriteFile(xml);
                _store = SnippetStore.Parse(xml);
                _loadedWriteTime = File.GetLastWriteTimeUtc(FilePath);
                _lastCheckUtc = DateTime.UtcNow;
                Log.Info($"Saved {_store.All.Count} snippets to {FilePath}");
            }
        }

        /// <summary>Creates snippets.xml from the defaults, or (once per session) adds defaults newer than the
        /// existing file. Every reader goes through here — completion, Tab, the Snippets Manager, Edit Snippets.</summary>
        public static void EnsureFileExists()
        {
            lock (Gate)
            {
                if (!File.Exists(FilePath))
                {
                    Directory.CreateDirectory(Path.GetDirectoryName(FilePath));
                    WriteFile(SnippetStore.DefaultXml);
                }
                else if (!_defaultsUpgraded)
                {
                    UpgradeDefaults();
                }
                _defaultsUpgraded = true;
            }
        }

        private static bool _defaultsUpgraded;

        // New default snippets reach users whose snippets.xml predates them.
        private static void UpgradeDefaults()
        {
            try
            {
                string upgraded = SnippetStore.UpgradeDefaults(File.ReadAllText(FilePath));
                if (upgraded == null) return;
                WriteFile(upgraded);
                Log.Info("Added new default snippets to " + FilePath);
            }
            catch (Exception ex)
            {
                Log.Error("Upgrading default snippets in " + FilePath, ex);
            }
        }

        // Written beside the target and swapped in, so a full disk or a killed SSMS leaves the old file, not a truncated one.
        private static void WriteFile(string text)
        {
            string temp = FilePath + ".tmp";
            try
            {
                File.WriteAllText(temp, text, new System.Text.UTF8Encoding(false));
                if (File.Exists(FilePath)) File.Replace(temp, FilePath, null);
                else File.Move(temp, FilePath);
            }
            catch
            {
                try { File.Delete(temp); } catch { /* a stale .tmp is overwritten by the next write */ }
                throw;
            }
        }

        /// <summary>Keeps a copy of a snippets.xml that cannot be read, because saving from the Snippets Manager replaces
        /// it. Null when there is nothing to copy.</summary>
        public static string BackupUnreadable()
        {
            try
            {
                string copy = FilePath + ".unreadable";
                File.Copy(FilePath, copy, overwrite: true);
                return copy;
            }
            catch (Exception ex)
            {
                Log.Error("Copying the unreadable " + FilePath, ex);
                return null;
            }
        }

        private static DateTime SafeWriteTime()
        {
            try { return File.GetLastWriteTimeUtc(FilePath); } catch { return DateTime.MinValue; }
        }
    }
}
