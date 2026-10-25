using System;
using System.Collections.Generic;
using System.Linq;

namespace SqlAssist.Core.Sql
{
    /// <summary>A table (or view, function, derived table) named in a FROM / JOIN / UPDATE / INTO.</summary>
    public sealed class TableRef
    {
        /// <summary>The database part of a three- or four-part name ("OtherDb" in OtherDb.dbo.Orders); null otherwise.</summary>
        public string Database { get; set; }
        public string Schema { get; set; }
        public string Name { get; set; }
        public string Alias { get; set; }
        public bool IsDerived { get; set; }
        public bool IsFunction { get; set; }
        /// <summary>Paren group the reference lives in (-1 = top level) and the UNION segment within it.</summary>
        public int Group { get; set; }
        public int Segment { get; set; }
        /// <summary>Token index of the FROM/JOIN/... that introduced it, within the statement (order of appearance).</summary>
        public int KeywordIndex { get; set; }
        /// <summary>Text offsets of the (possibly multi-part) name and of the alias; -1 when absent.</summary>
        public int NameStart { get; set; } = -1;
        public int NameEnd { get; set; } = -1;
        public int AliasStart { get; set; } = -1;
        public int AliasEnd { get; set; } = -1;

        /// <summary>What the rest of the query calls this table: the alias, or the name as written.</summary>
        public string RefName => Alias ?? Name;
        /// <summary>Set when the reference names a CTE of the statement, or is a derived table "(SELECT ...) d".</summary>
        public DerivedTable Derived { get; set; }
        /// <summary>True for tables, views, CTEs, derived tables and table-valued functions.</summary>
        public bool HasColumns => Derived != null || !IsDerived;

        public bool Answers(string qualifier) =>
            string.Equals(Alias, qualifier, StringComparison.OrdinalIgnoreCase) ||
            (Alias == null && string.Equals(Name, qualifier, StringComparison.OrdinalIgnoreCase));

        public override string ToString() => (Schema != null ? Schema + "." : "") + Name + (Alias != null ? " " + Alias : "");
    }

    public enum CaretContextKind
    {
        /// <summary>A table name is expected: after FROM, JOIN, UPDATE, INTO, or a comma in a FROM list.</summary>
        TableName,
        /// <summary>After "x." — x is an alias/table (columns follow) or, in table position, a schema.</summary>
        Qualified,
        /// <summary>After "JOIN t x ON " (or "AND" inside that ON): the join predicate is expected.</summary>
        JoinCondition,
        /// <summary>Anywhere else: columns, keywords and snippets.</summary>
        General
    }

    public sealed class CaretContext
    {
        public CaretContextKind Kind { get; internal set; }
        public bool AllowAlias { get; internal set; }
        public bool IsJoin { get; internal set; }
        public string Qualifier { get; internal set; }
        /// <summary>For "dbo.Customers.|" the schema in front of the qualifier.</summary>
        public string QualifierSchema { get; internal set; }
        /// <summary>The qualifier sits where a table name goes, so it names a schema ("FROM dbo.|").</summary>
        public bool QualifierIsSchema { get; internal set; }
        public bool InStringOrComment { get; internal set; }
        /// <summary>Right after a table name (or AS) in FROM/JOIN, where the user is typing an alias:
        /// a list here would turn "FROM Customers c⏎" into a committed suggestion.</summary>
        public bool IsAliasPosition { get; internal set; }
        /// <summary>After EXEC ("EXEC |", "EXEC dbo.|", "EXEC @rc = |"): procedures and functions are expected.</summary>
        public bool IsRoutineName { get; internal set; }
        /// <summary>The identifier around the caret, replaced on commit (includes a leading '[').</summary>
        public int WordStart { get; internal set; }
        public int WordEnd { get; internal set; }
        public string TypedText { get; internal set; }
        /// <summary>Tables visible at the caret: its own query block plus enclosing ones.</summary>
        public IReadOnlyList<TableRef> TablesInScope { get; internal set; }
        /// <summary>Tables of the caret's own query block only (what a new JOIN can join to).</summary>
        public IReadOnlyList<TableRef> SiblingTables { get; internal set; }
        public IReadOnlyCollection<string> AliasesInStatement { get; internal set; }
        /// <summary>In a SELECT list whose query block has no FROM yet ("SELECT |"): the completion list offers tables
        /// to expand and pick columns from, and the pick writes the FROM too.</summary>
        public bool IsSelectListWithoutFrom { get; internal set; }
        /// <summary>In a SELECT list whose query block already has tables ("SELECT | FROM t x JOIN ..."): the completion list
        /// is a column picker — the block's tables with their columns ticked straight into the list.</summary>
        public bool IsSelectListWithFrom { get; internal set; }
        /// <summary>Directly after SELECT / DISTINCT (or, with a FROM, after a comma of the list), so typing the space
        /// there opens the table tree / column picker.</summary>
        public bool IsRightAfterSelect { get; internal set; }
        /// <summary>The CTEs defined by the statement's WITH clause, in order.</summary>
        public IReadOnlyList<DerivedTable> CommonTableExpressions { get; internal set; } = new DerivedTable[0];
        /// <summary>For <see cref="CaretContextKind.JoinCondition"/>: the table this ON belongs to.</summary>
        public TableRef JoinTarget { get; internal set; }
        /// <summary>For <see cref="CaretContextKind.JoinCondition"/>: the tables named before it, which the ON can reference.</summary>
        public IReadOnlyList<TableRef> JoinPartners { get; internal set; }
        /// <summary>For <see cref="CaretContextKind.JoinCondition"/>: the ON clause typed so far, to skip predicates already there.</summary>
        public string JoinConditionSoFar { get; internal set; }
    }

    public enum HoverKind { Table, Column, Routine }

    /// <summary>What is under the mouse (a table, a column or a procedure/function) and the text span the tooltip belongs to.</summary>
    public sealed class HoverTarget
    {
        public HoverKind Kind { get; internal set; }
        /// <summary>For Table: the reference. For Column: the table its qualifier names, or null when unqualified.</summary>
        public TableRef Table { get; internal set; }
        /// <summary>For Column: the column name.</summary>
        public string Column { get; internal set; }
        /// <summary>For an unqualified Column: the tables it could belong to.</summary>
        public IReadOnlyList<TableRef> Scope { get; internal set; }
        /// <summary>For Routine: the name as written (database and schema may be null).</summary>
        public string Database { get; internal set; }
        public string Schema { get; internal set; }
        public string Name { get; internal set; }
        public int Start { get; internal set; }
        public int End { get; internal set; }
    }

    public sealed class StarContext
    {
        public int ReplaceStart { get; internal set; }
        public int ReplaceEnd { get; internal set; }
        public string Qualifier { get; internal set; }
        public IReadOnlyList<TableRef> Tables { get; internal set; }
    }

    public static class SqlContextAnalyzer
    {
        private static readonly HashSet<string> SetOperators = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "UNION", "EXCEPT", "INTERSECT" };
        private static readonly HashSet<string> ClauseWords = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
            { "SELECT", "FROM", "WHERE", "ON", "BY", "SET", "HAVING", "VALUES", "JOIN", "APPLY", "INTO", "UPDATE" };

        public static CaretContext Analyze(string text, int caret)
        {
            var tokens = SqlTokenizer.Tokenize(text);
            int wordStart = caret, wordEnd = caret;
            while (wordStart > 0 && SqlIdentifier.IsWordChar(text[wordStart - 1])) wordStart--;
            if (wordStart > 0 && text[wordStart - 1] == '[') wordStart--;
            while (wordEnd < text.Length && SqlIdentifier.IsWordChar(text[wordEnd])) wordEnd++;

            var stmt = Statement.Build(text, tokens, caret, wordStart, wordEnd);
            var ctx = new CaretContext
            {
                WordStart = wordStart,
                WordEnd = wordEnd,
                TypedText = text.Substring(wordStart, caret - wordStart).TrimStart('['),
                InStringOrComment = SqlTokenizer.IsInStringOrComment(tokens, caret),
                Kind = CaretContextKind.General,
                AliasesInStatement = new HashSet<string>(stmt.Tables.Where(t => t.Alias != null).Select(t => t.Alias), StringComparer.OrdinalIgnoreCase),
            };
            ctx.TablesInScope = stmt.TablesVisibleFrom(stmt.CaretGroup, stmt.CaretSegment);
            ctx.CommonTableExpressions = stmt.Ctes;
            ctx.SiblingTables = stmt.Tables.Where(t => t.Group == stmt.CaretGroup && t.Segment == stmt.CaretSegment && t.HasColumns).ToList();

            var sig = stmt.Sig;
            int p = stmt.LastIndexBefore(wordStart);
            if (p < 0) return ctx;

            if (sig[p].IsSymbol('.') && sig[p].End == wordStart)
            {
                ctx.Kind = CaretContextKind.Qualified;
                int q = p - 1;
                if (q >= 0 && sig[q].IsName && sig[q].End == sig[p].Start)
                {
                    ctx.Qualifier = sig[q].Value;
                    int chainStart = q;
                    if (q >= 2 && sig[q - 1].IsSymbol('.') && sig[q - 2].IsName)
                    {
                        ctx.QualifierSchema = sig[q - 2].Value;
                        chainStart = q - 2;
                    }
                    var before = TableKeywordAt(stmt, chainStart - 1);
                    ctx.IsRoutineName = IsExecAt(sig, chainStart - 1); // "EXEC dbo.|": the schema's procedures
                    ctx.QualifierIsSchema = (before.HasValue || ctx.IsRoutineName) && ctx.QualifierSchema == null;
                    if (before.HasValue)
                    {
                        ctx.AllowAlias = before.Value.allowAlias;
                        ctx.IsJoin = before.Value.isJoin;
                    }
                }
                return ctx;
            }

            if (TryJoinCondition(stmt, p, ctx)) return ctx;

            if (IsExecAt(sig, p))
            {
                ctx.IsRoutineName = true;
                return ctx;
            }

            var table = TableKeywordAt(stmt, p);
            if (table.HasValue)
            {
                ctx.Kind = CaretContextKind.TableName;
                ctx.AllowAlias = table.Value.allowAlias;
                ctx.IsJoin = table.Value.isJoin;
            }
            ctx.IsAliasPosition = stmt.AliasSlots.Contains(p) || (sig[p].IsWord("AS") && p > 0 && stmt.AliasSlots.Contains(p - 1));
            if (ctx.Kind == CaretContextKind.General && ctx.SiblingTables.Count == 0)
            {
                int clause = stmt.ClauseIndexOf(p + 1);
                ctx.IsSelectListWithoutFrom = clause >= 0 && sig[clause].IsWord("SELECT") &&
                    !stmt.Tables.Any(t => t.Group == stmt.CaretGroup && t.Segment == stmt.CaretSegment);
                ctx.IsRightAfterSelect = ctx.IsSelectListWithoutFrom && ctx.TypedText.Length == 0 &&
                    (sig[p].IsWord("SELECT") || sig[p].IsWord("DISTINCT"));
            }
            else if (ctx.Kind == CaretContextKind.General)
            {
                int clause = stmt.ClauseIndexOf(p + 1);
                ctx.IsSelectListWithFrom = clause >= 0 && sig[clause].IsWord("SELECT");
                ctx.IsRightAfterSelect = ctx.IsSelectListWithFrom && ctx.TypedText.Length == 0 &&
                    (sig[p].IsWord("SELECT") || sig[p].IsWord("DISTINCT") || sig[p].IsSymbol(','));
            }
            return ctx;
        }

        /// <summary>Finds a "*" just before the caret that stands for a select list ("SELECT *", ", *", "c.*").</summary>
        public static StarContext FindStar(string text, int caret)
        {
            if (caret <= 0 || caret > text.Length || text[caret - 1] != '*') return null;
            var tokens = SqlTokenizer.Tokenize(text);
            if (SqlTokenizer.IsInStringOrComment(tokens, caret)) return null;
            var stmt = Statement.Build(text, tokens, caret, caret, caret);
            var sig = stmt.Sig;
            int s = stmt.LastIndexBefore(caret);
            if (s < 0 || !sig[s].IsSymbol('*') || sig[s].End != caret) return null;

            int group = stmt.GroupOf[s], segment = stmt.SegmentOf[s];
            var scope = stmt.Tables.Where(t => t.Group == group && t.Segment == segment && t.HasColumns).ToList();
            var result = new StarContext { ReplaceStart = sig[s].Start, ReplaceEnd = caret };

            if (s >= 2 && sig[s - 1].IsSymbol('.') && sig[s - 2].IsName && sig[s - 2].End == sig[s - 1].Start)
            {
                string q = sig[s - 2].Value;
                var match = scope.FirstOrDefault(t => t.Answers(q))
                    ?? stmt.TablesVisibleFrom(group, segment).FirstOrDefault(t => t.HasColumns && t.Answers(q));
                if (match == null) return null;
                result.Qualifier = q;
                result.ReplaceStart = sig[s - 2].Start;
                result.Tables = new[] { match };
                return result;
            }

            if (s == 0 || !StartsSelectItem(stmt, s - 1)) return null;
            if (scope.Count == 0) return null;
            result.Tables = scope;
            return result;
        }

        /// <summary>What the mouse is over, first match wins: a table — its name in FROM/JOIN (with any schema part), its
        /// alias where it is declared, or the alias / table name used elsewhere ("wr" in "wr.ReqId"); a procedure or
        /// function — a name after EXEC or right before "("; a column — "wr.ReqId", or a bare name when the statement has
        /// tables. Reserved words, variables, strings and comments give null.</summary>
        public static HoverTarget FindHoverTarget(string text, int position)
        {
            if (position < 0 || position > text.Length) return null;
            var tokens = SqlTokenizer.Tokenize(text);
            var token = tokens.FirstOrDefault(t => t.Start <= position && position < t.End);
            if (token.Text == null || !token.IsName || token.Kind == TokenKind.Variable) return null;

            var stmt = Statement.Build(text, tokens, position, position, position);
            foreach (var r in stmt.Tables.Where(t => t.HasColumns))
            {
                if (position >= r.NameStart && position < r.NameEnd) return new HoverTarget { Table = r, Start = r.NameStart, End = r.NameEnd };
                if (position >= r.AliasStart && position < r.AliasEnd) return new HoverTarget { Table = r, Start = r.AliasStart, End = r.AliasEnd };
            }
            // A use of the alias (or of an unaliased table's name) elsewhere: "wr" in "wr.ReqId".
            var used = stmt.Tables.FirstOrDefault(t => t.HasColumns && t.Answers(token.Value));
            if (used != null) return new HoverTarget { Table = used, Start = token.Start, End = token.End };

            if (token.Kind == TokenKind.Word && SqlKeywords.IsReserved(token.Text)) return null;
            var sig = stmt.Sig;
            int k = sig.FindIndex(t => t.Start == token.Start);
            if (k < 0) return null;
            // The dotted name the token is part of: "dbo.fnTax", "wr.ReqId".
            int first = k, last = k;
            while (first >= 2 && sig[first - 1].IsSymbol('.') && sig[first - 2].IsName && sig[first - 2].End == sig[first - 1].Start && sig[first - 1].End == sig[first].Start) first -= 2;
            while (last + 2 < sig.Count && sig[last + 1].IsSymbol('.') && sig[last + 2].IsName && sig[last].End == sig[last + 1].Start && sig[last + 1].End == sig[last + 2].Start) last += 2;

            bool call = last + 1 < sig.Count && sig[last + 1].IsSymbol('(') && sig[last + 1].Start == sig[last].End;
            if (call || IsExecAt(sig, first - 1))
                return new HoverTarget
                {
                    Kind = HoverKind.Routine,
                    Name = sig[last].Value,
                    Schema = last - first >= 2 ? sig[last - 2].Value : null,
                    Database = last - first >= 4 ? sig[last - 4].Value : null,
                    Start = sig[first].Start,
                    End = sig[last].End,
                };

            var visible = stmt.TablesVisibleFrom(stmt.GroupOf[k], stmt.SegmentOf[k]).Where(t => t.HasColumns).ToList();
            if (k > first)
            {
                string qualifier = sig[k - 2].Value;
                var owner = visible.FirstOrDefault(t => t.Answers(qualifier)) ?? stmt.Tables.FirstOrDefault(t => t.HasColumns && t.Answers(qualifier));
                return owner == null ? null : new HoverTarget { Kind = HoverKind.Column, Table = owner, Column = token.Value, Start = token.Start, End = token.End };
            }
            // "AS Total" names a column alias, "AS int" a type: neither is a column of the tables.
            if (k < last || visible.Count == 0 || (k > 0 && sig[k - 1].IsWord("AS"))) return null;
            return new HoverTarget { Kind = HoverKind.Column, Column = token.Value, Scope = visible, Start = token.Start, End = token.End };
        }

        /// <summary>The identifier ending at the caret, when it could be a snippet shortcut.</summary>
        public static bool TryGetSnippetWord(string text, int caret, out string word, out int start)
        {
            word = null;
            start = caret;
            if (caret < text.Length && SqlIdentifier.IsWordChar(text[caret])) return false;
            while (start > 0 && SqlIdentifier.IsWordChar(text[start - 1])) start--;
            if (start == caret) return false;
            if (start > 0 && (text[start - 1] == '.' || text[start - 1] == '[')) return false;
            if (SqlTokenizer.IsInStringOrComment(SqlTokenizer.Tokenize(text), caret)) return false;
            word = text.Substring(start, caret - start);
            return true;
        }

        // "JOIN t x ON |" or "... ON a = b AND |": find the joined table and the tables it can be joined to.
        private static bool TryJoinCondition(Statement stmt, int p, CaretContext ctx)
        {
            var sig = stmt.Sig;
            int on;
            if (sig[p].IsWord("ON")) on = p;
            else if (sig[p].IsWord("AND") && (on = stmt.ClauseIndexOf(p)) >= 0 && sig[on].IsWord("ON")) { }
            else return false;

            var target = stmt.Tables
                .Where(t => t.KeywordIndex < on && t.Group == stmt.GroupOf[on] && sig[t.KeywordIndex].IsWord("JOIN"))
                .OrderByDescending(t => t.KeywordIndex)
                .FirstOrDefault();
            if (target == null || !target.HasColumns) return false;

            ctx.Kind = CaretContextKind.JoinCondition;
            ctx.JoinTarget = target;
            ctx.JoinPartners = stmt.Tables
                .Where(t => t != target && t.HasColumns && t.Group == target.Group && t.Segment == target.Segment && t.KeywordIndex < target.KeywordIndex)
                .ToList();
            ctx.JoinConditionSoFar = string.Join(" ", sig.Skip(on + 1).Take(p - on).Select(t => t.Text));
            return true;
        }

        // Is token k the thing right before a select-list item? (SELECT, DISTINCT, TOP n, or a comma in a select list)
        private static bool StartsSelectItem(Statement stmt, int k)
        {
            var sig = stmt.Sig;
            var t = sig[k];
            if (t.IsWord("SELECT") || t.IsWord("DISTINCT") || t.IsWord("ALL") || t.IsWord("PERCENT") || t.IsWord("TIES")) return true;
            if (t.Kind == TokenKind.Number && k > 0 && sig[k - 1].IsWord("TOP")) return true;
            if (t.IsSymbol(')'))
            {
                int open = stmt.MatchingOpen(k);
                return open > 0 && sig[open - 1].IsWord("TOP");
            }
            if (t.IsSymbol(',')) return string.Equals(stmt.ClauseOf(k), "SELECT", StringComparison.OrdinalIgnoreCase);
            return false;
        }

        // Is token k a keyword (or FROM-list comma) after which a table name goes?
        // "EXEC |", "EXECUTE |" or "EXEC @rc = |": a procedure name goes here.
        private static bool IsExecAt(IReadOnlyList<SqlToken> sig, int k)
        {
            if (k < 0 || k >= sig.Count) return false;
            if (sig[k].IsWord("EXEC") || sig[k].IsWord("EXECUTE")) return true;
            return sig[k].IsSymbol('=') && k >= 2 && sig[k - 1].Kind == TokenKind.Variable &&
                   (sig[k - 2].IsWord("EXEC") || sig[k - 2].IsWord("EXECUTE"));
        }

        private static (bool allowAlias, bool isJoin)? TableKeywordAt(Statement stmt, int k)
        {
            if (k < 0) return null;
            var sig = stmt.Sig;
            var t = sig[k];
            if (t.IsWord("JOIN")) return (true, true);
            if (t.IsWord("APPLY")) return (true, false);
            if (t.IsWord("FROM")) return (!(k > 0 && sig[k - 1].IsWord("DELETE")), false);
            if (t.IsWord("UPDATE") || t.IsWord("INTO")) return (false, false);
            if (t.IsWord("TABLE") && k > 0 && sig[k - 1].IsWord("TRUNCATE")) return (false, false);
            if (t.IsSymbol(','))
            {
                int clause = stmt.ClauseIndexOf(k);
                if (clause >= 0 && sig[clause].IsWord("FROM"))
                    return (!(clause > 0 && sig[clause - 1].IsWord("DELETE")), false);
            }
            return null;
        }

        private sealed class Statement
        {
            public List<SqlToken> Sig;
            public int[] GroupOf;
            public int[] SegmentOf;
            public Dictionary<int, int> ParentGroup = new Dictionary<int, int>();
            public List<TableRef> Tables = new List<TableRef>();
            /// <summary>Indexes of the last token of table names that could still take an alias.</summary>
            public HashSet<int> AliasSlots = new HashSet<int>();
            public int CaretGroup = -1;
            public int CaretSegment;
            public List<DerivedTable> Ctes = new List<DerivedTable>();
            // Derived-table references and the body parens, filled in once every table of the statement is parsed.
            private readonly List<(TableRef r, int open, int close)> _pendingDerived = new List<(TableRef, int, int)>();
            private readonly List<(DerivedTable cte, int open, int close, List<string> columns)> _cteBodies = new List<(DerivedTable, int, int, List<string>)>();

            public static Statement Build(string text, List<SqlToken> tokens, int caret, int wordStart, int wordEnd)
            {
                var (start, end) = Bounds(text, tokens, caret);
                var st = new Statement { Sig = new List<SqlToken>() };
                foreach (var t in tokens)
                {
                    if (t.Start < start || t.Start >= end) continue;
                    if (t.Kind == TokenKind.Comment || t.IsSymbol(';') || IsBatchSeparator(text, t)) continue;
                    if (wordEnd > wordStart && t.Start < wordEnd && t.End > wordStart) continue; // the word being typed
                    st.Sig.Add(t);
                }
                st.ComputeGroups(wordStart);
                st.ParseCtes();
                st.ParseTables();
                st.LinkDerivedTables();
                return st;
            }

            // WITH a [(c1, c2)] AS (SELECT ...), b AS (...) — only at the start of the statement, so a table hint
            // "WITH (NOLOCK)" is never mistaken for one.
            private void ParseCtes()
            {
                if (Sig.Count == 0 || !Sig[0].IsWord("WITH")) return;
                int i = 1;
                while (i < Sig.Count && Sig[i].IsName)
                {
                    var cte = new DerivedTable { Name = Sig[i].Value, IsCte = true };
                    i++;
                    List<string> columns = null;
                    if (i < Sig.Count && Sig[i].IsSymbol('('))
                    {
                        int close = MatchingClose(i);
                        columns = Sig.Skip(i + 1).Take(close - i - 1).Where(t => t.IsName).Select(t => t.Value).ToList();
                        i = close + 1;
                    }
                    if (i + 1 >= Sig.Count || !Sig[i].IsWord("AS") || !Sig[i + 1].IsSymbol('(')) break;
                    int open = i + 1, end = MatchingClose(open);
                    Ctes.Add(cte);
                    _cteBodies.Add((cte, open, end, columns));
                    i = end + 1;
                    if (i < Sig.Count && Sig[i].IsSymbol(',')) { i++; continue; }
                    break;
                }
            }

            private void LinkDerivedTables()
            {
                foreach (var (cte, open, close, columns) in _cteBodies)
                    FillDerived(cte, open, close, columns);
                foreach (var (r, open, close) in _pendingDerived)
                {
                    r.Derived = new DerivedTable { Name = r.Alias ?? "", IsCte = false };
                    FillDerived(r.Derived, open, close, null);
                }
                // A plain name that matches a CTE is that CTE, not a database table.
                foreach (var r in Tables.Where(t => t.Derived == null && !t.IsDerived && !t.IsFunction && t.Schema == null))
                    r.Derived = Ctes.FirstOrDefault(c => string.Equals(c.Name, r.Name, StringComparison.OrdinalIgnoreCase));
            }

            /// <summary>Reads the columns of a CTE / derived table from the select list of its body (open..close parens),
            /// renamed by an explicit column list when there is one.</summary>
            private void FillDerived(DerivedTable table, int open, int close, List<string> explicitColumns)
            {
                int select = -1;
                for (int k = open + 1; k < close; k++)
                    if (GroupOf[k] == open && Sig[k].IsWord("SELECT")) { select = k; break; }

                var items = new List<DerivedColumn>();
                if (select >= 0)
                {
                    int segment = SegmentOf[select];
                    table.BodyTables = Tables.Where(t => t.Group == open && t.Segment == segment).ToList();

                    int k = select + 1;
                    while (k < close && (Sig[k].IsWord("DISTINCT") || Sig[k].IsWord("ALL"))) k++;
                    if (k < close && Sig[k].IsWord("TOP"))
                    {
                        k++;
                        k = k < close && Sig[k].IsSymbol('(') ? MatchingClose(k) + 1 : k + 1;
                        while (k < close && (Sig[k].IsWord("PERCENT") || Sig[k].IsWord("WITH") || Sig[k].IsWord("TIES"))) k++;
                    }
                    var item = new List<SqlToken>();
                    for (; k <= close; k++)
                    {
                        bool atTop = k < close && GroupOf[k] == open;
                        if (k == close || (atTop && (Sig[k].IsSymbol(',') || IsSelectListEnd(Sig[k]))))
                        {
                            var column = ParseSelectItem(item);
                            if (column != null) items.Add(column);
                            item.Clear();
                            if (k == close || !Sig[k].IsSymbol(',')) break;
                            continue;
                        }
                        item.Add(Sig[k]);
                    }
                }

                if (explicitColumns != null && explicitColumns.Count > 0)
                {
                    // WITH c (a, b) AS (...): the list names the columns; sources still come from the select list by position.
                    var named = items.Where(c => !c.IsStar).ToList();
                    items = explicitColumns.Select((name, i) => new DerivedColumn
                    {
                        Name = name,
                        SourceQualifier = i < named.Count ? named[i].SourceQualifier : null,
                        SourceColumn = i < named.Count ? named[i].SourceColumn : null,
                    }).ToList();
                }
                table.Columns = items;
            }

            private static bool IsSelectListEnd(SqlToken t) =>
                t.Kind == TokenKind.Word && (SetOperators.Contains(t.Text) || t.IsWord("FROM") || t.IsWord("INTO") ||
                    t.IsWord("WHERE") || t.IsWord("GROUP") || t.IsWord("ORDER") || t.IsWord("HAVING") || t.IsWord("OPTION"));

            // "x", "t.x", "expr AS a", "expr a", "a = expr", "*", "t.*".
            private static DerivedColumn ParseSelectItem(List<SqlToken> t)
            {
                int n = t.Count;
                if (n == 0) return null;
                if (n == 1 && t[0].IsSymbol('*')) return new DerivedColumn { IsStar = true };
                if (n == 3 && t[0].IsName && t[1].IsSymbol('.') && t[2].IsSymbol('*'))
                    return new DerivedColumn { IsStar = true, StarQualifier = t[0].Value };

                string alias = null;
                List<SqlToken> expr = t;
                if (n >= 3 && t[0].IsName && t[1].IsSymbol('=') && !(t[0].Kind == TokenKind.Word && SqlKeywords.IsReserved(t[0].Text)))
                {
                    alias = t[0].Value;
                    expr = t.GetRange(2, n - 2);
                }
                else if (n >= 3 && t[n - 2].IsWord("AS") && (t[n - 1].IsName || t[n - 1].Kind == TokenKind.String))
                {
                    alias = t[n - 1].Kind == TokenKind.String ? t[n - 1].Text.Trim('\'') : t[n - 1].Value;
                    expr = t.GetRange(0, n - 2);
                }
                else if (n >= 2 && (t[n - 1].Kind == TokenKind.QuotedIdentifier || (t[n - 1].Kind == TokenKind.Word && SqlKeywords.CanBeAlias(t[n - 1].Text)))
                         && !t[n - 2].IsSymbol('.'))
                {
                    alias = t[n - 1].Value;
                    expr = t.GetRange(0, n - 1);
                }

                var column = new DerivedColumn();
                if (expr.Count == 1 && expr[0].IsName && expr[0].Kind != TokenKind.Variable)
                    column.SourceColumn = expr[0].Value;
                else if (expr.Count == 3 && expr[0].IsName && expr[1].IsSymbol('.') && expr[2].IsName)
                {
                    column.SourceQualifier = expr[0].Value;
                    column.SourceColumn = expr[2].Value;
                }
                column.Name = alias ?? column.SourceColumn;
                return column.Name == null ? null : column;
            }

            private static (int start, int end) Bounds(string text, List<SqlToken> tokens, int caret)
            {
                int start = 0, end = text.Length;
                void Boundary(int b, int tokenStart)
                {
                    if (b <= caret) start = Math.Max(start, b);
                    else end = Math.Min(end, tokenStart);
                }
                int prevEnd = 0;
                // Within the current statement: paren depth, whether it began with WITH, and its last real token.
                int depth = 0;
                bool startsWithWith = false, atStart = true;
                SqlToken last = default;
                for (int k = 0; k <= tokens.Count; k++)
                {
                    // A blank line between two tokens separates statements (the usual way SSMS scripts are laid out) —
                    // but not inside parentheses, nor between a CTE "WITH x AS (...)" and the query that uses it.
                    int gapEnd = k < tokens.Count ? tokens[k].Start : text.Length;
                    int newlines = 0, lastNewline = -1;
                    for (int i = prevEnd; i < gapEnd; i++)
                        if (text[i] == '\n') { newlines++; lastNewline = i; }
                    bool cteContinues = startsWithWith && last.Text != null && (last.IsSymbol(')') || last.IsSymbol(',') || last.IsWord("AS"));
                    if (newlines >= 2 && depth <= 0 && !cteContinues)
                    {
                        Boundary(lastNewline + 1, lastNewline + 1);
                        depth = 0; atStart = true; startsWithWith = false; last = default;
                    }
                    if (k == tokens.Count) break;

                    var t = tokens[k];
                    if (t.IsSymbol(';') || IsBatchSeparator(text, t))
                    {
                        Boundary(t.End, t.Start);
                        depth = 0; atStart = true; startsWithWith = false; last = default;
                    }
                    else if (t.Kind != TokenKind.Comment)
                    {
                        if (atStart) { startsWithWith = t.IsWord("WITH"); atStart = false; }
                        if (t.IsSymbol('(')) depth++;
                        else if (t.IsSymbol(')')) depth--;
                        last = t;
                    }
                    prevEnd = t.End;
                }
                return (start, Math.Max(start, end));
            }

            private static bool IsBatchSeparator(string text, SqlToken t)
            {
                if (!t.IsWord("GO")) return false;
                for (int i = t.Start - 1; i >= 0 && text[i] != '\n'; i--)
                    if (!char.IsWhiteSpace(text[i])) return false;
                return true;
            }

            private void ComputeGroups(int caretPos)
            {
                GroupOf = new int[Sig.Count];
                SegmentOf = new int[Sig.Count];
                var segments = new Dictionary<int, int> { [-1] = 0 };
                var stack = new Stack<int>();
                stack.Push(-1);
                bool caretSet = false;
                for (int k = 0; k < Sig.Count; k++)
                {
                    if (!caretSet && Sig[k].Start >= caretPos)
                    {
                        CaretGroup = stack.Peek();
                        CaretSegment = segments[CaretGroup];
                        caretSet = true;
                    }
                    var t = Sig[k];
                    int g = stack.Peek();
                    if (t.IsSymbol(')') && stack.Count > 1) { stack.Pop(); g = stack.Peek(); }
                    if (t.Kind == TokenKind.Word && SetOperators.Contains(t.Text)) segments[g] = segments[g] + 1;
                    GroupOf[k] = g;
                    SegmentOf[k] = segments[g];
                    if (t.IsSymbol('('))
                    {
                        ParentGroup[k] = g;
                        segments[k] = 0;
                        stack.Push(k);
                    }
                }
                if (!caretSet)
                {
                    CaretGroup = stack.Peek();
                    CaretSegment = segments[CaretGroup];
                }
            }

            private void ParseTables()
            {
                for (int k = 0; k < Sig.Count; k++)
                {
                    var t = Sig[k];
                    if (t.IsWord("FROM"))
                    {
                        bool alias = !(k > 0 && Sig[k - 1].IsWord("DELETE"));
                        int j = ParseRef(k + 1, k, alias, true);
                        while (j < Sig.Count && Sig[j].IsSymbol(',') && GroupOf[j] == GroupOf[k])
                            j = ParseRef(j + 1, k, alias, true);
                    }
                    else if (t.IsWord("JOIN") || t.IsWord("APPLY")) ParseRef(k + 1, k, true, true);
                    else if (t.IsWord("UPDATE") || t.IsWord("INTO")) ParseRef(k + 1, k, false, false);
                }
            }

            // Parses one table reference starting at k; returns the index just past it.
            private int ParseRef(int k, int keyword, bool allowAlias, bool allowFunction)
            {
                if (k >= Sig.Count) return k;
                var r = new TableRef { Group = GroupOf[keyword], Segment = SegmentOf[keyword], KeywordIndex = keyword };
                int j = k;
                if (Sig[j].IsSymbol('('))
                {
                    r.IsDerived = true;
                    int close = MatchingClose(j);
                    _pendingDerived.Add((r, j, close));
                    j = close + 1;
                }
                else
                {
                    if (!Sig[j].IsName || (Sig[j].Kind == TokenKind.Word && SqlKeywords.IsReserved(Sig[j].Text))) return k;
                    var parts = new List<string>();
                    while (j < Sig.Count && Sig[j].IsName)
                    {
                        parts.Add(Sig[j].Value);
                        j++;
                        if (j < Sig.Count && Sig[j].IsSymbol('.'))
                        {
                            j++;
                            while (j < Sig.Count && Sig[j].IsSymbol('.')) { parts.Add(null); j++; } // a..b
                            // "FROM dbo.| WHERE": the dot is still waiting for its name; WHERE is not it.
                            if (j >= Sig.Count || Sig[j].Start != Sig[j - 1].End ||
                                (Sig[j].Kind == TokenKind.Word && SqlKeywords.IsReserved(Sig[j].Text)))
                            {
                                parts.Add(null);
                                break;
                            }
                            continue;
                        }
                        break;
                    }
                    r.Name = parts[parts.Count - 1];
                    if (parts.Count >= 2) r.Schema = parts[parts.Count - 2];
                    if (parts.Count >= 3) r.Database = parts[parts.Count - 3];
                    r.NameStart = Sig[k].Start;
                    r.NameEnd = Sig[j - 1].End;
                    if (allowFunction && j < Sig.Count && Sig[j].IsSymbol('('))
                    {
                        r.IsFunction = true;
                        j = MatchingClose(j) + 1;
                    }
                    if (r.Name == null) return j;
                }

                if (allowAlias || r.IsDerived) AliasSlots.Add(j - 1);
                if (j < Sig.Count && (allowAlias || r.IsDerived))
                {
                    if (Sig[j].IsWord("AS") && j + 1 < Sig.Count && Sig[j + 1].IsName)
                    {
                        SetAlias(r, Sig[j + 1]);
                        j += 2;
                    }
                    else if ((Sig[j].Kind == TokenKind.Word && SqlKeywords.CanBeAlias(Sig[j].Text)) || Sig[j].Kind == TokenKind.QuotedIdentifier)
                    {
                        SetAlias(r, Sig[j]);
                        j++;
                    }
                }
                if (j + 1 < Sig.Count && Sig[j].IsWord("WITH") && Sig[j + 1].IsSymbol('('))
                    j = MatchingClose(j + 1) + 1; // table hints
                Tables.Add(r);
                return j;
            }

            private static void SetAlias(TableRef r, SqlToken alias)
            {
                r.Alias = alias.Value;
                r.AliasStart = alias.Start;
                r.AliasEnd = alias.End;
            }

            public int MatchingClose(int open)
            {
                int depth = 0;
                for (int k = open; k < Sig.Count; k++)
                {
                    if (Sig[k].IsSymbol('(')) depth++;
                    else if (Sig[k].IsSymbol(')') && --depth == 0) return k;
                }
                return Sig.Count - 1;
            }

            public int MatchingOpen(int close)
            {
                int depth = 0;
                for (int k = close; k >= 0; k--)
                {
                    if (Sig[k].IsSymbol(')')) depth++;
                    else if (Sig[k].IsSymbol('(') && --depth == 0) return k;
                }
                return -1;
            }

            public int LastIndexBefore(int pos)
            {
                int last = -1;
                for (int k = 0; k < Sig.Count && Sig[k].End <= pos; k++) last = k;
                return last;
            }

            /// <summary>Index of the clause keyword governing token k, walking back at the same paren depth.</summary>
            public int ClauseIndexOf(int k)
            {
                int depth = 0;
                for (int i = k - 1; i >= 0; i--)
                {
                    var t = Sig[i];
                    if (t.IsSymbol(')')) depth++;
                    else if (t.IsSymbol('(')) { if (depth == 0) return -1; depth--; }
                    else if (depth == 0 && t.Kind == TokenKind.Word && ClauseWords.Contains(t.Text)) return i;
                }
                return -1;
            }

            public string ClauseOf(int k)
            {
                int i = ClauseIndexOf(k);
                return i < 0 ? null : Sig[i].Text;
            }

            public List<TableRef> TablesVisibleFrom(int group, int segment)
            {
                var result = new List<TableRef>();
                int g = group, seg = segment;
                while (true)
                {
                    result.AddRange(Tables.Where(t => t.Group == g && t.Segment == seg));
                    if (g == -1 || !ParentGroup.TryGetValue(g, out int parent)) break;
                    seg = SegmentOf[g];
                    g = parent;
                }
                return result;
            }
        }
    }
}
