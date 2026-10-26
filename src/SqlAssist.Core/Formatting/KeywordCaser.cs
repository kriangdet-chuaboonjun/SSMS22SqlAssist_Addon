using System;
using System.Collections.Generic;
using SqlAssist.Core.Sql;

namespace SqlAssist.Core.Formatting
{
    /// <summary>Decides which words are keywords, i.e. may be re-cased. This has to be conservative: a database can be
    /// case-sensitive, so a table, column or alias must never change case. What qualifies: T-SQL's reserved words (they
    /// cannot be names unless bracketed), built-in function and type names where the position shows they are one (a
    /// function is followed by "(", a type follows a variable or sits in a column definition), and a few structural words
    /// that only mean something in one place (APPLY after CROSS, ROWS after OFFSET, hints in WITH (…) and OPTION (…)).
    /// Words after a dot are names, always.</summary>
    internal static class KeywordCaser
    {
        private static readonly HashSet<string> Functions = SqlKeywords.BuiltInFunctions;

        private static readonly HashSet<string> Types = new HashSet<string>
        {
            "INT", "BIGINT", "SMALLINT", "TINYINT", "BIT", "DECIMAL", "NUMERIC", "MONEY", "SMALLMONEY", "FLOAT", "REAL", "DATE", "TIME",
            "DATETIME", "DATETIME2", "SMALLDATETIME", "DATETIMEOFFSET", "CHAR", "VARCHAR", "NCHAR", "NVARCHAR", "TEXT", "NTEXT", "BINARY",
            "VARBINARY", "IMAGE", "UNIQUEIDENTIFIER", "XML", "SQL_VARIANT", "ROWVERSION", "TIMESTAMP", "HIERARCHYID", "GEOGRAPHY",
            "GEOMETRY", "SYSNAME",
        };

        private static readonly HashSet<string> TableHints = new HashSet<string>
        {
            "NOLOCK", "READUNCOMMITTED", "READCOMMITTED", "READCOMMITTEDLOCK", "REPEATABLEREAD", "SERIALIZABLE", "SNAPSHOT", "ROWLOCK",
            "PAGLOCK", "TABLOCK", "TABLOCKX", "UPDLOCK", "XLOCK", "READPAST", "NOWAIT", "NOEXPAND", "FORCESEEK", "FORCESCAN",
            "KEEPIDENTITY", "KEEPDEFAULTS", "IGNORE_CONSTRAINTS", "IGNORE_TRIGGERS",
        };

        private static readonly HashSet<string> IsolationWords = new HashSet<string>
            { "ISOLATION", "LEVEL", "COMMITTED", "UNCOMMITTED", "REPEATABLE", "SNAPSHOT", "SERIALIZABLE" };

        public static bool IsFunctionOrType(string upperWord) => Functions.Contains(upperWord) || Types.Contains(upperWord);

        /// <summary>True for every token index whose word is a keyword (the case of the others is never touched).</summary>
        public static bool[] Find(ScanResult scan)
        {
            var keyword = new bool[scan.Tokens.Count];
            foreach (var batch in scan.Batches)
            {
                if (batch.HasGo) keyword[batch.GoToken] = true;
                foreach (var statement in batch.Statements)
                    FindInStatement(scan.Tokens, statement, keyword);
            }
            return keyword;
        }

        private sealed class State
        {
            public bool SawOrderBy;
            public bool AfterOffset;
            /// <summary>The token right after SET, and the word that is: SET NOCOUNT ON, SET TRANSACTION ISOLATION LEVEL …</summary>
            public int SetOption = -1;
            public string SetOptionWord;
        }

        private static void FindInStatement(IReadOnlyList<SqlToken> tokens, SqlStatement statement, bool[] keyword)
        {
            if (statement.FirstSignificant < 0) return;
            var openerWords = new Stack<string>();
            var state = new State();
            if (statement.Kind == StatementKind.Set)
            {
                state.SetOption = NextSignificant(tokens, statement.FirstSignificant, statement.LastToken);
                if (state.SetOption >= 0 && tokens[state.SetOption].Kind == TokenKind.Word) state.SetOptionWord = tokens[state.SetOption].Text.ToUpperInvariant();
            }
            int prev = -1, prev2 = -1;

            for (int i = statement.FirstToken; i <= statement.LastToken; i++)
            {
                var t = tokens[i];
                if (t.Kind == TokenKind.Comment) continue;

                if (t.IsSymbol('('))
                    openerWords.Push(prev >= 0 && tokens[prev].Kind == TokenKind.Word ? tokens[prev].Text.ToUpperInvariant() : null);
                else if (t.IsSymbol(')'))
                {
                    if (openerWords.Count > 0) openerWords.Pop();
                }
                else if (t.Kind == TokenKind.Word && !(prev >= 0 && tokens[prev].IsSymbol('.')))
                {
                    int next = NextSignificant(tokens, i, statement.LastToken);
                    string opener = openerWords.Count > 0 ? openerWords.Peek() : null;
                    keyword[i] = IsKeyword(tokens, statement.Kind, state, i, prev, prev2, next, openerWords.Count, opener);
                }
                prev2 = prev;
                prev = i;
            }
        }

        private static bool IsKeyword(IReadOnlyList<SqlToken> tokens, StatementKind kind, State state, int i, int prev, int prev2, int next,
            int depth, string opener)
        {
            string u = tokens[i].Text.ToUpperInvariant();
            string prevWord = prev >= 0 && tokens[prev].Kind == TokenKind.Word ? tokens[prev].Text.ToUpperInvariant() : null;
            string nextWord = next >= 0 && tokens[next].Kind == TokenKind.Word ? tokens[next].Text.ToUpperInvariant() : null;
            bool nextParen = next >= 0 && tokens[next].IsSymbol('(');

            if (u == "ORDER" && depth == 0) state.SawOrderBy = true;
            if (SqlKeywords.IsReserved(u)) return true;
            if (nextParen && Functions.Contains(u)) return true;
            if ((u == "XML" || u == "JSON") && prevWord == "FOR") return true;
            if (u == "THROW" && prev < 0) return true;

            if (Types.Contains(u))
            {
                if (nextParen) return true;
                if (prev >= 0 && tokens[prev].Kind == TokenKind.Variable) return true;
                if ((opener == "CAST" || opener == "TRY_CAST") && prevWord == "AS") return true;
                if ((opener == "CONVERT" || opener == "TRY_CONVERT") && prev >= 0 && tokens[prev].IsSymbol('(')) return true;
                // A column definition: name, then its type.
                return (kind == StatementKind.Create || kind == StatementKind.Alter || kind == StatementKind.Declare) &&
                       prev >= 0 && (tokens[prev].Kind == TokenKind.Word || tokens[prev].Kind == TokenKind.QuotedIdentifier) &&
                       prev2 >= 0 && (tokens[prev2].IsSymbol('(') || tokens[prev2].IsSymbol(',') || IsWord(tokens[prev2], "ADD") || IsWord(tokens[prev2], "COLUMN"));
            }
            if (u == "MAX" && opener != null && Types.Contains(opener)) return true;

            switch (u)
            {
                case "PARTITION": return nextWord == "BY";
                case "TRY":
                case "CATCH": return depth == 0 && (prevWord == "BEGIN" || prevWord == "END");
                case "APPLY": return prevWord == "CROSS" || prevWord == "OUTER";
                case "TIES": return prevWord == "WITH";
                case "ACTION": return prevWord == "NO";
                case "NO": return nextWord == "ACTION";
                case "MATCHED": return kind == StatementKind.Merge && (prevWord == "WHEN" || prevWord == "NOT");
                case "SOURCE":
                case "TARGET": return kind == StatementKind.Merge && prevWord == "BY";
                case "USING": return kind == StatementKind.Merge && depth == 0;
                case "OFFSET":
                    if (depth == 0 && state.SawOrderBy && next >= 0 && (tokens[next].Kind == TokenKind.Number || tokens[next].Kind == TokenKind.Variable || nextParen))
                    {
                        state.AfterOffset = true;
                        return true;
                    }
                    return false;
                case "OUTPUT":
                    return depth == 0 && (kind == StatementKind.Insert || kind == StatementKind.Update || kind == StatementKind.Delete || kind == StatementKind.Merge) &&
                           !(prev >= 0 && tokens[prev].Kind == TokenKind.Variable);
                case "ROWS":
                case "ROW":
                case "NEXT":
                case "FIRST":
                case "ONLY":
                    return state.AfterOffset && depth == 0;
                case "PATH":
                case "RAW":
                case "AUTO":
                case "EXPLICIT":
                    return prevWord == "XML" || prevWord == "JSON";
                case "INCLUDE":
                    return kind == StatementKind.Create && nextParen;
            }

            if (opener == "OPTION") return true;
            if (opener == "WITH" && TableHints.Contains(u)) return true;
            if (kind == StatementKind.Set)
            {
                if (i == state.SetOption) return true;
                if (state.SetOptionWord == "TRANSACTION" && IsolationWords.Contains(u)) return true;
            }
            return false;
        }

        private static bool IsWord(SqlToken t, string word) =>
            t.Kind == TokenKind.Word && string.Equals(t.Text, word, StringComparison.OrdinalIgnoreCase);

        private static int NextSignificant(IReadOnlyList<SqlToken> tokens, int from, int last)
        {
            for (int i = from + 1; i <= last; i++)
                if (tokens[i].Kind != TokenKind.Comment) return i;
            return -1;
        }
    }
}
