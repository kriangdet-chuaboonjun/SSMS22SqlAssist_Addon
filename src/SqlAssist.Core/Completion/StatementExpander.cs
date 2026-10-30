using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using SqlAssist.Core.Schema;
using SqlAssist.Core.Sql;

namespace SqlAssist.Core.Completion
{
    public enum ExpansionKind { Insert, Update, Exec, Alter }

    /// <summary>"INSERT INTO t|", "UPDATE t|", "EXEC p|" or "ALTER PROCEDURE p|" with the caret right after the name.</summary>
    public sealed class ExpansionTarget
    {
        public ExpansionKind Kind { get; internal set; }
        public string Schema { get; internal set; }
        public string Name { get; internal set; }
        /// <summary>Text offset of the statement's first word (INSERT / UPDATE / EXEC / ALTER).</summary>
        public int StatementStart { get; internal set; }
        /// <summary>Text offset where the name starts (its schema, when written, included).</summary>
        public int NameStart { get; internal set; }
        /// <summary>Text offset just past the name (the caret).</summary>
        public int NameEnd { get; internal set; }
        /// <summary>For ALTER: PROCEDURE, FUNCTION, VIEW or TRIGGER as typed.</summary>
        public string ObjectType { get; internal set; }
    }

    /// <summary>Text for a statement template. <see cref="Text"/> goes at the caret (after the name);
    /// <see cref="Prefix"/>, when set, goes in front of the statement (DECLAREs for EXEC's OUTPUT parameters).</summary>
    public sealed class Expansion
    {
        public Expansion(string text, int caretOffset, string prefix = null) { Text = text; CaretOffset = caretOffset; Prefix = prefix ?? ""; }
        public string Text { get; }
        /// <summary>Where the caret goes, as an offset into <see cref="Text"/>.</summary>
        public int CaretOffset { get; }
        public string Prefix { get; }
    }

    /// <summary>Tab after "INSERT INTO t", "UPDATE t", "EXEC p" or "ALTER PROCEDURE p" writes the full statement:
    /// the column list and VALUES, the SET list and a WHERE on the primary key, the parameter list, or the object's
    /// current definition as ALTER.</summary>
    public static class StatementExpander
    {
        private const string Item = "    ";

        // What a statement already has after its table name when the user wrote the rest of it themselves.
        private static readonly string[] InsertContinuations = { "VALUES", "SELECT", "EXEC", "EXECUTE", "DEFAULT", "WITH", "OUTPUT" };
        private static readonly string[] UpdateContinuations = { "SET", "WITH", "AS" };

        public static ExpansionTarget Find(string text, int caret)
        {
            var target = FindName(text, caret);
            if (target == null) return null;
            if ((target.Kind == ExpansionKind.Insert || target.Kind == ExpansionKind.Update) && HasContinuation(text, caret, target.Kind))
                return null;
            return target;
        }

        /// <summary>The next word after the table name already belongs to this statement: "INSERT INTO t" ⏎ "(a, b)",
        /// ⏎ "VALUES …", "UPDATE t" ⏎ "SET …". A blank line in between ends the statement, so what follows is something else.</summary>
        private static bool HasContinuation(string text, int caret, ExpansionKind kind)
        {
            int from = caret;
            foreach (var t in SqlTokenizer.Tokenize(text).Where(t => t.Start >= caret))
            {
                if (IsBlankLineBetween(text, from, t.Start)) return false;
                if (t.Kind == TokenKind.Comment) { from = t.End; continue; }
                if (kind == ExpansionKind.Insert && t.IsSymbol('(')) return true;
                foreach (string word in kind == ExpansionKind.Insert ? InsertContinuations : UpdateContinuations)
                    if (t.IsWord(word)) return true;
                return false;
            }
            return false;
        }

        private static bool IsBlankLineBetween(string text, int from, int to)
        {
            int breaks = 0;
            for (int i = from; i < to; i++) if (text[i] == '\n') breaks++;
            return breaks >= 2;
        }

        private static ExpansionTarget FindName(string text, int caret)
        {
            if (caret <= 0 || caret > text.Length) return null;
            // Only at the end of a line: "INSERT INTO t| (a, b)" already has its template.
            for (int j = caret; j < text.Length && text[j] != '\n' && text[j] != '\r'; j++)
                if (!char.IsWhiteSpace(text[j])) return null;

            var tokens = SqlTokenizer.Tokenize(text);
            if (SqlTokenizer.IsInStringOrComment(tokens, caret)) return null;
            var sig = tokens.Where(t => t.Kind != TokenKind.Comment && t.Start < caret).ToList();
            if (sig.Count < 2) return null;

            var last = sig[sig.Count - 1];
            if (!IsObjectName(last) || last.End != caret) return null;
            if (last.Kind == TokenKind.Word && SqlKeywords.IsReserved(last.Text)) return null; // "INSERT INTO|"
            var parts = new List<string> { last.Value };
            int k = sig.Count - 1;
            while (k >= 2 && sig[k - 1].IsSymbol('.') && IsObjectName(sig[k - 2]) &&
                   sig[k - 2].End == sig[k - 1].Start && sig[k - 1].End == sig[k].Start)
            {
                parts.Insert(0, sig[k - 2].Value);
                k -= 2;
            }
            if (parts.Count > 3) return null;

            var target = new ExpansionTarget
            {
                Name = parts[parts.Count - 1],
                Schema = parts.Count >= 2 ? parts[parts.Count - 2] : null,
                NameStart = sig[k].Start,
                NameEnd = caret,
            };
            int b = k - 1;
            if (b < 0) return null;
            var before = sig[b];

            if (before.IsWord("INTO") && b >= 1 && sig[b - 1].IsWord("INSERT")) return With(target, ExpansionKind.Insert, sig[b - 1]);
            if (before.IsWord("INSERT")) return With(target, ExpansionKind.Insert, before);
            if (before.IsWord("UPDATE"))
            {
                // "ON UPDATE", "FOR UPDATE", "AFTER UPDATE": not an UPDATE statement.
                if (b >= 1 && (sig[b - 1].IsWord("ON") || sig[b - 1].IsWord("FOR") || sig[b - 1].IsWord("AFTER") || sig[b - 1].IsSymbol(','))) return null;
                return With(target, ExpansionKind.Update, before);
            }
            if (IsExec(before)) return With(target, ExpansionKind.Exec, before);
            // EXEC @rc = dbo.p
            if (before.IsSymbol('=') && b >= 2 && sig[b - 1].Kind == TokenKind.Variable && IsExec(sig[b - 2]))
                return With(target, ExpansionKind.Exec, sig[b - 2]);
            if ((before.IsWord("PROCEDURE") || before.IsWord("PROC") || before.IsWord("FUNCTION") || before.IsWord("VIEW") || before.IsWord("TRIGGER")) &&
                b >= 1 && sig[b - 1].IsWord("ALTER") && !(b >= 2 && sig[b - 2].IsWord("OR")))
            {
                target.ObjectType = before.Text.ToUpperInvariant();
                return With(target, ExpansionKind.Alter, sig[b - 1]);
            }
            return null;
        }

        /// <summary>The column list and VALUES with a typed placeholder per column, identity, computed and rowversion
        /// columns left out. Null when the table has no column to insert.</summary>
        /// <param name="indent">Leading whitespace of the statement's line.</param>
        public static Expansion BuildInsert(DbObject table, string indent, string newLine)
        {
            var columns = table.Columns.Where(Writable).ToList();
            if (columns.Count == 0) return null;

            var sb = new StringBuilder();
            sb.Append(newLine).Append(indent).Append('(');
            AppendItems(sb, columns.Select(c => SqlIdentifier.QuoteIfNeeded(c.Name)).ToList(), indent, newLine, out _);
            sb.Append(newLine).Append(indent).Append(')');
            sb.Append(newLine).Append(indent).Append("VALUES");
            sb.Append(newLine).Append(indent).Append('(');
            AppendItems(sb, columns.Select(c => DefaultValue(c.TypeName)).ToList(), indent, newLine, out int caret);
            sb.Append(newLine).Append(indent).Append(");");
            return new Expansion(sb.ToString(), caret);
        }

        /// <summary>SET for every writable non-key column and WHERE on the primary key, whose values are @parameters
        /// DECLAREd (typed, NULL) in front of the statement. Without a primary key the WHERE holds only a TODO comment:
        /// the statement is a syntax error until the user says which rows, never an UPDATE of the whole table.</summary>
        /// <param name="declared">@variables the batch already declares; they are used but not DECLAREd again.</param>
        public static Expansion BuildUpdate(DbObject table, string indent, string newLine, IEnumerable<string> declared = null)
        {
            var set = table.Columns.Where(c => Writable(c) && !c.IsPrimaryKey).ToList();
            if (set.Count == 0) return null;
            var keys = table.Columns.Where(c => c.IsPrimaryKey).ToList();
            var known = new HashSet<string>(declared ?? Enumerable.Empty<string>(), StringComparer.OrdinalIgnoreCase);

            var sb = new StringBuilder();
            sb.Append(newLine).Append(indent).Append("SET");
            AppendItems(sb, set.Select(c => SqlIdentifier.QuoteIfNeeded(c.Name) + " = " + DefaultValue(c.TypeName)).ToList(), indent, newLine, out int caret);
            // The caret goes on the first value, after "name = ".
            caret += SqlIdentifier.QuoteIfNeeded(set[0].Name).Length + 3;

            var declare = new StringBuilder();
            sb.Append(newLine).Append(indent).Append("WHERE ");
            if (keys.Count == 0)
            {
                sb.Append("/* TODO: choose the rows to update before running */ ");
                caret = sb.Length;
                sb.Append(';');
                return new Expansion(sb.ToString(), caret);
            }
            var used = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            for (int i = 0; i < keys.Count; i++)
            {
                string parameter = ParameterName(keys[i].Name, used);
                if (i > 0) sb.Append(newLine).Append(indent).Append("  AND ");
                sb.Append(SqlIdentifier.QuoteIfNeeded(keys[i].Name)).Append(" = ").Append(parameter);
                if (!known.Contains(parameter))
                    declare.Append("DECLARE ").Append(parameter).Append(' ')
                           .Append(string.IsNullOrWhiteSpace(keys[i].TypeName) ? "sql_variant" : keys[i].TypeName)
                           .Append(" = NULL;").Append(newLine).Append(indent);
            }
            sb.Append(';');
            return new Expansion(sb.ToString(), caret, declare.ToString());
        }

        /// <summary>One item per line, comma at the end of every line but the last. <paramref name="caret"/> is the
        /// offset of the first item.</summary>
        private static void AppendItems(StringBuilder sb, IReadOnlyList<string> items, string indent, string newLine, out int caret)
        {
            caret = -1;
            for (int i = 0; i < items.Count; i++)
            {
                sb.Append(newLine).Append(indent).Append(Item);
                if (caret < 0) caret = sb.Length;
                sb.Append(items[i]);
                if (i < items.Count - 1) sb.Append(',');
            }
        }

        /// <summary>"@" and the column name with anything a variable name cannot hold turned into "_"; a name
        /// already taken by an earlier key column gets a number.</summary>
        private static string ParameterName(string column, HashSet<string> used)
        {
            var sb = new StringBuilder("@");
            foreach (char c in column) sb.Append(char.IsLetterOrDigit(c) || c == '_' ? c : '_');
            if (sb.Length == 1 || char.IsDigit(sb[1])) sb.Insert(1, '_');
            string name = sb.ToString(), candidate = name;
            for (int n = 2; !used.Add(candidate); n++) candidate = name + n;
            return candidate;
        }

        /// <summary>Whether Tab after "EXEC p" has a parameter list to write: a procedure (a function is not EXECuted)
        /// with at least one parameter. The editor's hint and <see cref="BuildExec"/> go by the same answer.</summary>
        public static bool HasExecTemplate(DbRoutine routine) =>
            routine != null && routine.IsProcedure && routine.Parameters.Count > 0;

        /// <summary>One "@p = value" per parameter, aligned under the first. OUTPUT and table-valued parameters get a
        /// variable, DECLAREd in front of the EXEC. Null for a procedure without parameters.</summary>
        /// <param name="linePrefix">Text of the line up to the caret ("EXEC dbo.p"), for alignment.</param>
        public static Expansion BuildExec(DbRoutine routine, string linePrefix, string indent, string newLine)
        {
            if (routine.Parameters.Count == 0) return null;
            string pad = Whitespace(linePrefix);
            var sb = new StringBuilder();
            var declare = new StringBuilder();
            int caret = -1;
            for (int i = 0; i < routine.Parameters.Count; i++)
            {
                var p = routine.Parameters[i];
                bool variable = p.IsOutput || p.IsReadOnly;
                sb.Append(i == 0 ? " " : newLine + pad + ",").Append(p.Name).Append(" = ");
                if (caret < 0) caret = sb.Length;
                if (variable)
                {
                    sb.Append(p.Name).Append(p.IsOutput ? " OUTPUT" : "");
                    declare.Append("DECLARE ").Append(p.Name).Append(' ').Append(p.TypeName).Append(';').Append(newLine).Append(indent);
                }
                else sb.Append(DefaultValue(p.TypeName));
                sb.Append(" -- ").Append(p.TypeName);
            }
            return new Expansion(sb.ToString(), caret, declare.ToString());
        }

        /// <summary>The object's definition with its leading CREATE (or CREATE OR ALTER) turned into ALTER;
        /// null when the definition does not start with CREATE.</summary>
        public static string AlterScript(string definition)
        {
            if (string.IsNullOrEmpty(definition)) return null;
            var words = SqlTokenizer.Tokenize(definition).Where(t => t.Kind != TokenKind.Comment).Take(3).ToList();
            if (words.Count == 0 || !words[0].IsWord("CREATE")) return null;
            int end = words.Count >= 3 && words[1].IsWord("OR") && words[2].IsWord("ALTER") ? words[2].End : words[0].End;
            return (definition.Substring(0, words[0].Start) + "ALTER" + definition.Substring(end)).TrimEnd();
        }

        /// <summary>A placeholder of the column's type: N'' for Unicode text, 0 for numbers, GETDATE() for dates.</summary>
        public static string DefaultValue(string typeName)
        {
            string t = (typeName ?? "").ToLowerInvariant();
            int paren = t.IndexOf('(');
            if (paren >= 0) t = t.Substring(0, paren);
            switch (t.Trim())
            {
                case "nvarchar": case "nchar": case "ntext": case "sysname": return "N''";
                case "varchar": case "char": case "text": case "xml": return "''";
                case "int": case "bigint": case "smallint": case "tinyint": case "bit": case "decimal": case "numeric":
                case "money": case "smallmoney": case "float": case "real": return "0";
                case "datetime": case "datetime2": case "smalldatetime": return "GETDATE()";
                case "date": return "CAST(GETDATE() AS date)";
                case "datetimeoffset": return "SYSDATETIMEOFFSET()";
                case "time": return "'00:00:00'";
                case "uniqueidentifier": return "NEWID()";
                case "binary": case "varbinary": case "image": return "0x";
                default: return "NULL";
            }
        }

        private static bool Writable(DbColumn c) =>
            !c.IsIdentity && !c.IsComputed &&
            !string.Equals(c.TypeName, "timestamp", StringComparison.OrdinalIgnoreCase) &&
            !string.Equals(c.TypeName, "rowversion", StringComparison.OrdinalIgnoreCase);

        private static bool IsObjectName(SqlToken t) => (t.Kind == TokenKind.Word || t.Kind == TokenKind.QuotedIdentifier);

        private static bool IsExec(SqlToken t) => t.IsWord("EXEC") || t.IsWord("EXECUTE");

        private static ExpansionTarget With(ExpansionTarget target, ExpansionKind kind, SqlToken first)
        {
            target.Kind = kind;
            target.StatementStart = first.Start;
            return target;
        }

        private static string Whitespace(string prefix)
        {
            var sb = new StringBuilder(prefix.Length);
            foreach (char c in prefix) sb.Append(c == '\t' ? '\t' : ' ');
            return sb.ToString();
        }
    }
}
