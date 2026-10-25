using System.Linq;

namespace SqlAssist.Core.Sql
{
    /// <summary>Which database a script has switched to with <c>USE</c> by the caret, so completion can read that
    /// database's tables instead of the query window's.</summary>
    public static class SqlUseStatement
    {
        /// <summary>The database of the last <c>USE x</c> before the caret, without brackets or quotes; null when there is
        /// none. GO does not reset it: the connection stays in that database. A name still being typed at the caret does
        /// not count, and neither do the query hints <c>OPTION (USE HINT(...))</c> / <c>USE PLAN</c>.</summary>
        public static string LastDatabaseBefore(string text, int caret)
        {
            if (caret <= 0 || caret > text.Length) return null;
            var sig = SqlTokenizer.Tokenize(text.Substring(0, caret)).Where(t => t.Kind != TokenKind.Comment).ToList();
            string database = null;
            for (int i = 0; i + 1 < sig.Count; i++)
            {
                if (!sig[i].IsWord("USE")) continue;
                if (i > 0 && (sig[i - 1].IsSymbol('(') || sig[i - 1].IsSymbol(','))) continue;
                var name = sig[i + 1];
                if (name.Kind != TokenKind.Word && name.Kind != TokenKind.QuotedIdentifier) continue;
                if (name.IsWord("PLAN") || name.IsWord("HINT") || name.End >= caret) continue;
                database = name.Value;
            }
            return database;
        }
    }
}
