using System;
using System.Collections.Generic;
using System.Linq;

namespace SqlAssist.Core.Sql
{
    /// <summary>A stretch of text: Start inclusive, End exclusive.</summary>
    public readonly struct SourceSpan
    {
        public SourceSpan(int start, int end) { Start = start; End = end; }
        public int Start { get; }
        public int End { get; }
        public int Length => End - Start;
        /// <summary>The caret touches the span: inside it, or right before / after it.</summary>
        public bool Touches(int caret) => caret >= Start && caret <= End;
        public override string ToString() => Start + ".." + End;
    }

    /// <summary>One value of a VALUES row. <see cref="Slot"/> is everything between the separators around it
    /// (white space included), <see cref="Expression"/> only the tokens of the value; null for an empty slot.</summary>
    public sealed class InsertValueSlot
    {
        internal InsertValueSlot(SourceSpan slot, SourceSpan? expression) { Slot = slot; Expression = expression; }
        public SourceSpan Slot { get; }
        public SourceSpan? Expression { get; }
    }

    public sealed class InsertValueRow
    {
        internal InsertValueRow(int open, bool closed, IReadOnlyList<InsertValueSlot> slots) { Open = open; IsClosed = closed; Slots = slots; }
        /// <summary>Offset of the row's "(".</summary>
        public int Open { get; }
        /// <summary>False for a row still being typed: no ")" before the text (or the statement) ended.</summary>
        public bool IsClosed { get; }
        public IReadOnlyList<InsertValueSlot> Slots { get; }
    }

    /// <summary>What is highlighted for a caret: the column, and the value (or values) that sit in the same position.</summary>
    public sealed class InsertHighlight
    {
        internal InsertHighlight(int index, SourceSpan column, IReadOnlyList<SourceSpan> values, bool fromColumn)
        {
            Index = index; Column = column; Values = values; FromColumn = fromColumn;
        }
        /// <summary>Position in the column list, from 0.</summary>
        public int Index { get; }
        public SourceSpan Column { get; }
        /// <summary>The caret's own value, or — when the caret is on the column name — the value at that position in every row.</summary>
        public IReadOnlyList<SourceSpan> Values { get; }
        public bool FromColumn { get; }
    }

    /// <summary>"INSERT INTO t (a, b, c) VALUES (1, 2, 3), (4, 5, 6)": which value belongs to which column, by position. Built
    /// from the tokens, so commas inside strings, functions, subqueries and comments are never mistaken for separators. Only the
    /// form with a column list and VALUES is understood (not INSERT … SELECT / EXEC / DEFAULT VALUES); anything the parser is not
    /// sure of — a column that is not a plain name, a list still open, a statement that ends early — gives no map at all.</summary>
    public sealed class InsertValueMap
    {
        // Words that cannot be part of a VALUES row, so a row left open by a typo ends there instead of swallowing the next statement.
        private static readonly HashSet<string> StopAnywhere = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "INSERT", "UPDATE", "DELETE", "MERGE", "DECLARE", "EXEC", "EXECUTE", "GO", "CREATE", "ALTER", "DROP", "TRUNCATE",
            "IF", "WHILE", "PRINT", "USE", "SET", "RETURN", "THROW", "RAISERROR",
        };
        // Valid inside parentheses (a subquery), not directly in a row.
        private static readonly HashSet<string> StopAtTopLevel = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "SELECT", "WITH", "FROM", "WHERE" };

        // How many INSERTs before the caret are tried: the caret is in the nearest one, or in none.
        private const int MaxCandidates = 8;

        private readonly List<SourceSpan> _comments;

        private InsertValueMap(int start, int end, IReadOnlyList<SourceSpan> columns, IReadOnlyList<InsertValueRow> rows, List<SourceSpan> comments)
        {
            StatementStart = start; StatementEnd = end; Columns = columns; Rows = rows; _comments = comments;
        }

        /// <summary>Offset of the INSERT keyword.</summary>
        public int StatementStart { get; }
        /// <summary>Offset just past the last row (or, for a row still open, the end of what was typed).</summary>
        public int StatementEnd { get; }
        public IReadOnlyList<SourceSpan> Columns { get; }
        public IReadOnlyList<InsertValueRow> Rows { get; }

        /// <summary>The INSERT statement the caret is in, or null when it is in none or the statement is not understood.</summary>
        public static InsertValueMap Find(string text, int caret)
        {
            if (string.IsNullOrEmpty(text) || caret < 0 || caret > text.Length) return null;
            var all = SqlTokenizer.Tokenize(text);
            var sig = all.Where(t => t.Kind != TokenKind.Comment).ToList();
            var comments = all.Where(t => t.Kind == TokenKind.Comment).Select(t => new SourceSpan(t.Start, t.End)).ToList();

            int tried = 0;
            for (int k = sig.Count - 1; k >= 0 && tried < MaxCandidates; k--)
            {
                if (sig[k].Start > caret || !sig[k].IsWord("INSERT")) continue;
                tried++;
                var map = TryParse(sig, k, text.Length, comments);
                if (map != null && caret >= map.StatementStart && caret <= map.StatementEnd) return map;
            }
            return null;
        }

        /// <summary>The INSERT whose keyword starts at <paramref name="insertStart"/>, whatever the caret; null when that is not
        /// an INSERT … (columns) VALUES statement the parser understands. For callers that keep the result across caret moves.</summary>
        public static InsertValueMap Parse(string text, int insertStart)
        {
            if (string.IsNullOrEmpty(text) || insertStart < 0 || insertStart >= text.Length) return null;
            var all = SqlTokenizer.Tokenize(text);
            var sig = all.Where(t => t.Kind != TokenKind.Comment).ToList();
            var comments = all.Where(t => t.Kind == TokenKind.Comment).Select(t => new SourceSpan(t.Start, t.End)).ToList();
            int k = sig.FindIndex(t => t.Start == insertStart && t.IsWord("INSERT"));
            return k < 0 ? null : TryParse(sig, k, text.Length, comments);
        }

        /// <summary>The columns and values to highlight for the caret; null when it is not on a column name or in a value slot, or
        /// when there is no value at that position to pair it with.</summary>
        public InsertHighlight At(int caret)
        {
            if (caret < StatementStart || caret > StatementEnd) return null;
            // A comment between the values belongs to nothing.
            foreach (var c in _comments)
                if (caret > c.Start && caret <= c.End) return null;

            for (int i = 0; i < Columns.Count; i++)
            {
                if (!Columns[i].Touches(caret)) continue;
                var values = new List<SourceSpan>();
                foreach (var row in Rows)
                    if (i < row.Slots.Count && row.Slots[i].Expression.HasValue) values.Add(row.Slots[i].Expression.Value);
                return values.Count == 0 ? null : new InsertHighlight(i, Columns[i], values, true);
            }

            foreach (var row in Rows)
            {
                if (row.Slots.Count == 0 || caret < row.Slots[0].Slot.Start || caret > row.Slots[row.Slots.Count - 1].Slot.End) continue;
                for (int j = 0; j < row.Slots.Count; j++)
                {
                    if (!row.Slots[j].Slot.Touches(caret)) continue;
                    // A value past the last column has nothing to pair with: no guess.
                    if (j >= Columns.Count) return null;
                    var expression = row.Slots[j].Expression;
                    return new InsertHighlight(j, Columns[j], expression.HasValue ? new[] { expression.Value } : new SourceSpan[0], false);
                }
            }
            return null;
        }

        private static InsertValueMap TryParse(List<SqlToken> sig, int k, int textLength, List<SourceSpan> comments)
        {
            int n = sig.Count, p = k + 1;
            if (p < n && sig[p].IsWord("INTO")) p++;
            if (p >= n || !IsName(sig[p])) return null;
            p++;
            while (p + 1 < n && sig[p].IsSymbol('.') && IsName(sig[p + 1])) p += 2;
            if (p >= n || !sig[p].IsSymbol('(')) return null;
            p++;

            var columns = new List<SourceSpan>();
            while (true)
            {
                // Anything but one plain name per entry (a trailing comma, a function, an unfinished list) is not understood.
                if (p >= n || (sig[p].Kind != TokenKind.Word && sig[p].Kind != TokenKind.QuotedIdentifier) || !sig[p].Terminated) return null;
                columns.Add(new SourceSpan(sig[p].Start, sig[p].End));
                p++;
                if (p >= n) return null;
                if (sig[p].IsSymbol(',')) { p++; continue; }
                if (sig[p].IsSymbol(')')) { p++; break; }
                return null;
            }
            if (p >= n || !sig[p].IsWord("VALUES")) return null;
            int end = sig[p].End;
            p++;

            var rows = new List<InsertValueRow>();
            while (p < n && sig[p].IsSymbol('('))
            {
                var row = ParseRow(sig, ref p, textLength, out end);
                rows.Add(row);
                if (!row.IsClosed) break;
                if (p < n && sig[p].IsSymbol(',')) { p++; continue; }
                break;
            }
            return new InsertValueMap(sig[k].Start, end, columns, rows, comments);
        }

        // sig[p] is the row's "(". On return p is past its ")" (or at the token that ended it early), and end is where the row ends.
        private static InsertValueRow ParseRow(List<SqlToken> sig, ref int p, int textLength, out int end)
        {
            int n = sig.Count, open = sig[p].Start, depth = 0;
            var slots = new List<InsertValueSlot>();
            int slotStart = sig[p].End;
            int first = -1, last = -1;
            p++;

            void Finish(int slotEnd)
            {
                slots.Add(new InsertValueSlot(new SourceSpan(slotStart, slotEnd), first < 0 ? (SourceSpan?)null : new SourceSpan(first, last)));
                first = last = -1;
            }

            while (p < n)
            {
                var t = sig[p];
                if (t.Kind == TokenKind.Symbol)
                {
                    char c = t.Text[0];
                    if (c == ';') { Finish(t.Start); end = t.Start; return new InsertValueRow(open, false, slots); }
                    if (c == ')' && depth == 0)
                    {
                        Finish(t.Start);
                        end = t.End;
                        p++;
                        return new InsertValueRow(open, true, slots);
                    }
                    if (c == ',' && depth == 0) { Finish(t.Start); slotStart = t.End; p++; continue; }
                    if (c == '(') depth++;
                    else if (c == ')') depth--;
                }
                else if (t.Kind == TokenKind.Word && (StopAnywhere.Contains(t.Text) || (depth == 0 && StopAtTopLevel.Contains(t.Text))))
                {
                    Finish(t.Start);
                    end = t.Start;
                    return new InsertValueRow(open, false, slots);
                }
                if (first < 0) first = t.Start;
                last = t.End;
                p++;
            }
            // The text ended inside the row: the slot being typed runs to the end.
            Finish(textLength);
            end = textLength;
            return new InsertValueRow(open, false, slots);
        }

        private static bool IsName(SqlToken t) => t.Kind == TokenKind.Word || t.Kind == TokenKind.QuotedIdentifier || t.Kind == TokenKind.Variable;
    }
}
