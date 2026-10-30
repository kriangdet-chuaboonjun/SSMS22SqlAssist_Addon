using System;
using System.Diagnostics;
using System.IO;
using SqlAssist.Core.Settings;

namespace SqlAssist.Settings
{
    /// <summary>The user's settings.xml under %APPDATA%\SqlAssist, re-read whenever the file changes (so editing it by hand works).</summary>
    internal static class SettingsProvider
    {
        public static readonly string FilePath = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "SqlAssist", SqlAssistSettings.FileName);

        private static readonly object Gate = new object();
        private static SqlAssistSettings _current;
        private static DateTime _loadedWriteTime;
        private static readonly Stopwatch Clock = Stopwatch.StartNew();
        private const long CheckInterval = 1000, RetryAfter = 5000;   // milliseconds
        private static long _checkedAt = -1, _retryAt;

        /// <summary>A copy: changing it does nothing until it is passed to <see cref="Save"/>.</summary>
        public static SqlAssistSettings Load()
        {
            lock (Gate)
            {
                Refresh();
                return SqlAssistSettings.Parse(_current.ToXml());
            }
        }

        /// <summary>Whether the INSERT column highlight is on. Asked on every caret move, so it does not copy the settings.</summary>
        public static bool HighlightInsertColumns
        {
            get
            {
                lock (Gate)
                {
                    Refresh(throttled: true);
                    return _current.Editor.HighlightInsertColumns;
                }
            }
        }

        /// <param name="throttled">For callers on a hot path (every caret move): look at the file at most once a second.</param>
        private static void Refresh(bool throttled = false)
        {
            long now = Clock.ElapsedMilliseconds;
            if (_current != null && (now < _retryAt || (throttled && _checkedAt >= 0 && now - _checkedAt < CheckInterval))) return;
            _checkedAt = now;
            try
            {
                DateTime writeTime = File.Exists(FilePath) ? File.GetLastWriteTimeUtc(FilePath) : DateTime.MinValue;
                if (_current == null || writeTime != _loadedWriteTime)
                {
                    _current = SqlAssistSettings.Parse(writeTime == DateTime.MinValue ? null : File.ReadAllText(FilePath));
                    _loadedWriteTime = writeTime;
                }
            }
            catch (Exception ex)
            {
                // A file that cannot be read (another SSMS is writing it) means what was loaded before, or the defaults, not a failing
                // command. The next try is a few seconds later, so it is neither lost for the session nor logged on every call.
                Log.Error("Reading " + FilePath, ex);
                if (_current == null) _current = new SqlAssistSettings();
                _retryAt = now + RetryAfter;
            }
        }

        public static void Save(SqlAssistSettings settings)
        {
            lock (Gate)
            {
                Directory.CreateDirectory(Path.GetDirectoryName(FilePath));
                string xml = settings.ToXml();
                File.WriteAllText(FilePath, xml, new System.Text.UTF8Encoding(false));
                _current = SqlAssistSettings.Parse(xml);
                _loadedWriteTime = File.GetLastWriteTimeUtc(FilePath);
            }
        }
    }
}
