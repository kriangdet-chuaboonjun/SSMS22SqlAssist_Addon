using Microsoft.VisualStudio.Shell;
using SqlAssist.Connection;
using SqlAssist.Core.Sql;

namespace SqlAssist.Editor
{
    /// <summary>The connection the editor's features read the schema from: the query window's, switched to the database
    /// of the last <c>USE</c> above the caret.</summary>
    internal static class EditorConnection
    {
        public static ConnectionTarget Get(TextWindow window)
        {
            ThreadHelper.ThrowIfNotOnUIThread();
            var target = ActiveConnection.TryGet();
            // shortcut: a USE more than TextWindow's radius (20,000 characters) above the caret is not seen and the
            // window's database is used; scan the whole buffer if users hit it.
            string database = target == null ? null : SqlUseStatement.LastDatabaseBefore(window.Text, window.Caret);
            return database == null ? target : target.ForDatabase(database);
        }
    }
}
