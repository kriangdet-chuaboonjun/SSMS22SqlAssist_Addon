using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.ComponentModel.Composition;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.VisualStudio.Language.Intellisense.AsyncCompletion;
using Microsoft.VisualStudio.Language.Intellisense.AsyncCompletion.Data;
using Microsoft.VisualStudio.Shell;
using Microsoft.VisualStudio.Text;
using Microsoft.VisualStudio.Text.Adornments;
using Microsoft.VisualStudio.Text.Editor;
using Microsoft.VisualStudio.Utilities;
using SqlAssist.Connection;
using SqlAssist.Core.Completion;
using SqlAssist.Core.Schema;
using SqlAssist.Core.Snippets;
using SqlAssist.Core.Sql;
using SqlAssist.Schema;
using SqlAssist.Snippets;

namespace SqlAssist.Editor
{
    [Export(typeof(IAsyncCompletionSourceProvider))]
    [Name("SqlAssist completion")]
    [ContentType(SqlContentType.Name)]
    [Order(Before = "default")]
    internal sealed class SqlCompletionSourceProvider : IAsyncCompletionSourceProvider
    {
        public IAsyncCompletionSource GetOrCreate(ITextView textView) =>
            textView.Properties.GetOrCreateSingletonProperty(() => new SqlCompletionSource());
    }

    internal static class SqlContentType
    {
        /// <summary>The SSMS 22 T-SQL query editor's content type.</summary>
        public const string Name = "SQL";
    }

    /// <summary>Keys for data carried on completion items.</summary>
    internal static class ItemProps
    {
        public const string Description = "SqlAssist.Description";
        public const string Snippet = "SqlAssist.Snippet";
        /// <summary>Set on T-SQL keyword items so the list can draw them in the editor's keyword colour.</summary>
        public const string IsKeyword = "SqlAssist.IsKeyword";
        /// <summary>Lazy&lt;TableDetails&gt; on table/view items: the column panel beside the list.</summary>
        public const string Table = "SqlAssist.Table";
    }

    internal sealed class SqlCompletionSource : IAsyncCompletionSource
    {
        private static readonly TimeSpan SchemaWait = TimeSpan.FromSeconds(3);

        // Captured on the UI thread in InitializeCompletion; GetCompletionContextAsync runs on a background thread.
        private ConnectionTarget _target;

        public CompletionStartData InitializeCompletion(CompletionTrigger trigger, SnapshotPoint triggerLocation, CancellationToken token)
        {
            try
            {
                var window = TextWindow.Around(triggerLocation.Snapshot, triggerLocation.Position);
                var ctx = SqlContextAnalyzer.Analyze(window.Text, window.Caret);
                if (ctx.InStringOrComment) return CompletionStartData.DoesNotParticipateInCompletion;

                bool explicitInvoke = trigger.Reason == CompletionTriggerReason.Invoke || trigger.Reason == CompletionTriggerReason.InvokeAndCommitIfUnique;
                if (!explicitInvoke)
                {
                    if (ctx.IsAliasPosition) return CompletionStartData.DoesNotParticipateInCompletion;
                    switch (trigger.Reason)
                    {
                        case CompletionTriggerReason.Insertion:
                            char ch = trigger.Character;
                            bool starts =
                                (ch == '.' && ctx.Kind == CaretContextKind.Qualified) ||
                                (ch == ' ' && (ctx.Kind == CaretContextKind.TableName || ctx.Kind == CaretContextKind.JoinCondition || ctx.IsRightAfterSelect || ctx.IsRoutineName)) ||
                                ((char.IsLetter(ch) || ch == '_' || ch == '#' || ch == '[' || ch == '@') && ctx.TypedText.Length > 0);
                            if (!starts) return CompletionStartData.DoesNotParticipateInCompletion;
                            break;
                        case CompletionTriggerReason.Deletion:
                            if (ctx.TypedText.Length == 0) return CompletionStartData.DoesNotParticipateInCompletion;
                            break;
                        default:
                            return CompletionStartData.DoesNotParticipateInCompletion;
                    }
                }

                // Called on the UI thread, which is where SSMS's connection objects must be read.
                if (ThreadHelper.CheckAccess()) _target = ActiveConnection.TryGet();
                var span = new SnapshotSpan(triggerLocation.Snapshot, window.Offset + ctx.WordStart, ctx.WordEnd - ctx.WordStart);
                return new CompletionStartData(CompletionParticipation.ProvidesItems, span);
            }
            catch (Exception ex)
            {
                Log.Error("InitializeCompletion", ex);
                return CompletionStartData.DoesNotParticipateInCompletion;
            }
        }

        public async Task<CompletionContext> GetCompletionContextAsync(IAsyncCompletionSession session, CompletionTrigger trigger,
            SnapshotPoint triggerLocation, SnapshotSpan applicableToSpan, CancellationToken token)
        {
            try
            {
                var target = _target;
                var window = TextWindow.Around(triggerLocation.Snapshot, triggerLocation.Position);
                var ctx = SqlContextAnalyzer.Analyze(window.Text, window.Caret);
                var variables = SqlVariables.DeclaredBefore(window.Text, window.Caret);

                // "@|": only what the script declared — no schema needed.
                if (ctx.TypedText.StartsWith("@"))
                {
                    var varItems = new List<CompletionItem>();
                    AddVariableItems(varItems, variables, "0");
                    return new CompletionContext(varItems.ToImmutableArray());
                }

                var schema = await SchemaCache.Instance.GetAsync(target, SchemaWait, token).ConfigureAwait(false);

                // Not for functions: built-in ones (STRING_SPLIT, OPENJSON) are never in the schema and would reload it every 30s.
                if (schema.Objects.Count > 0 && ctx.TablesInScope.Any(t => t.HasColumns && !t.IsFunction && schema.Find(t) == null))
                    SchemaCache.Instance.RefreshIfOlderThanThrottle(target);

                var items = new List<CompletionItem>();
                switch (ctx.Kind)
                {
                    case CaretContextKind.TableName: AddTableItems(items, ctx, schema); break;
                    case CaretContextKind.Qualified: AddQualifiedItems(items, ctx, schema); break;
                    case CaretContextKind.JoinCondition:
                        AddJoinConditionItems(items, ctx, schema);
                        AddGeneralItems(items, ctx, schema); // anything else the user wants to type after ON
                        break;
                    default:
                        if (ctx.IsRoutineName)
                        {
                            // "EXEC |": procedures, then functions, each with its parameters beside the list.
                            AddRoutineItems(items, schema.Routines, qualified: false);
                            break;
                        }
                        if (ctx.IsSelectListWithoutFrom)
                        {
                            // "SELECT |": the table tree; picking from it writes the column list and the FROM.
                            var tree = TreeState.For(session);
                            tree.Schema = schema;
                            tree.TakenAliases = ctx.AliasesInStatement;
                            AddTreeItems(items, ctx, schema);
                            AddGeneralItems(items, ctx, schema, columns: false);
                        }
                        else if (ctx.IsSelectListWithFrom && AddPickerItems(items, ctx, schema, session))
                        {
                            // "SELECT | FROM t x": the column picker, then keywords and snippets for anything else.
                            AddGeneralItems(items, ctx, schema, columns: false);
                        }
                        else AddGeneralItems(items, ctx, schema);
                        AddVariableItems(items, variables, "2");
                        break;
                }
                return new CompletionContext(items.ToImmutableArray());
            }
            catch (Exception ex)
            {
                Log.Error("GetCompletionContextAsync", ex);
                return CompletionContext.Empty;
            }
        }

        public Task<object> GetDescriptionAsync(IAsyncCompletionSession session, CompletionItem item, CancellationToken token) =>
            Task.FromResult<object>(item.Properties.TryGetProperty(ItemProps.Description, out string d) ? d : null);

        // Sort-text prefixes order the groups when nothing (or a tie) decides between them.
        // "JOIN t x ON |": the predicates, ahead of the column list (group "0").
        private void AddJoinConditionItems(List<CompletionItem> items, CaretContext ctx, SchemaModel schema)
        {
            int order = 0;
            foreach (var c in JoinConditionSuggester.Suggest(schema, ctx.JoinTarget, ctx.JoinPartners, ctx.JoinConditionSoFar))
            {
                string description = c.ForeignKeyName != null ? "Foreign key " + c.ForeignKeyName : "Columns with the same name";
                // The sort key keeps the suggester's order (foreign keys, then id-like names) instead of alphabetical.
                var item = new CompletionItem(c.Text, this, Icons.Join, ImmutableArray<CompletionFilter>.Empty,
                    c.ForeignKeyName != null ? "FK" : "", c.Text, "0" + order++.ToString("D4"), c.Text, ImmutableArray<ImageElement>.Empty);
                item.Properties.AddProperty(ItemProps.Description, description);
                items.Add(item);
            }
        }

        private void AddTableItems(List<CompletionItem> items, CaretContext ctx, SchemaModel schema)
        {
            if (ctx.IsJoin)
            {
                foreach (var j in JoinSuggester.Suggest(schema, ctx.SiblingTables, ctx.AliasesInStatement))
                    items.Add(WithTable(Item(j.Text, Icons.Join, j.Text, "0", j.Target.Name, j.ForeignKeyName, "Join on " + j.ForeignKeyName), schema, j.Target));
            }
            // The statement's CTEs, right after the JOIN suggestions and ahead of the database's tables.
            foreach (var cte in ctx.CommonTableExpressions)
            {
                var o = cte.Resolve(schema);
                if (o != null) items.Add(ObjectItem(schema, o, SchemaModel.DisplayName(o), ctx, group: "1!"));
            }
            foreach (var o in schema.Objects)
                items.Add(ObjectItem(schema, o,SchemaModel.DisplayName(o), ctx));
            foreach (var s in schema.Schemas)
                items.Add(Item(s, Icons.Schema, SqlIdentifier.QuoteIfNeeded(s), "2", s, "schema", null));
            // FROM dbo.ufnGetContacts(...): table-valued functions read like tables.
            AddRoutineItems(items, schema.Routines.Where(r => r.Kind == RoutineKind.TableFunction), qualified: false, group: "2");
        }

        private void AddQualifiedItems(List<CompletionItem> items, CaretContext ctx, SchemaModel schema)
        {
            var inSchema = schema.Routines.Where(r => string.Equals(r.Schema, ctx.Qualifier, StringComparison.OrdinalIgnoreCase));
            if (ctx.QualifierIsSchema && ctx.IsRoutineName)
            {
                AddRoutineItems(items, inSchema, qualified: true); // "EXEC dbo.|"
                return;
            }
            if (ctx.QualifierIsSchema)
            {
                foreach (var o in schema.Objects.Where(o => string.Equals(o.Schema, ctx.Qualifier, StringComparison.OrdinalIgnoreCase)))
                    items.Add(ObjectItem(schema, o,SqlIdentifier.QuoteIfNeeded(o.Name), ctx));
                AddRoutineItems(items, inSchema.Where(r => r.Kind == RoutineKind.TableFunction), qualified: true, group: "2");
                return;
            }

            DbObject table = null;
            var r = ctx.TablesInScope.FirstOrDefault(t => t.HasColumns && t.Answers(ctx.Qualifier) &&
                (ctx.QualifierSchema == null || string.Equals(t.Schema, ctx.QualifierSchema, StringComparison.OrdinalIgnoreCase)));
            if (r != null) table = schema.Find(r);
            if (table == null && ctx.QualifierSchema == null)
                table = ctx.CommonTableExpressions.FirstOrDefault(c => string.Equals(c.Name, ctx.Qualifier, StringComparison.OrdinalIgnoreCase))?.Resolve(schema);
            if (table == null) table = schema.Find(ctx.QualifierSchema, ctx.Qualifier);
            if (table != null)
            {
                foreach (var c in table.Columns) items.Add(ColumnItem(schema, c, SqlIdentifier.QuoteIfNeeded(c.Name), table));
                return;
            }
            // "SELECT ... WHERE dbo." — a schema outside table position: its scalar functions first.
            AddRoutineItems(items, inSchema.Where(r => r.Kind == RoutineKind.ScalarFunction), qualified: true, group: "0");
            foreach (var o in schema.Objects.Where(o => string.Equals(o.Schema, ctx.Qualifier, StringComparison.OrdinalIgnoreCase)))
                items.Add(ObjectItem(schema, o,SqlIdentifier.QuoteIfNeeded(o.Name), ctx));
        }

        /// <summary>Procedures and functions, with the parameter panel beside the list
        /// ("dbo.ufnGetProductDealerPrice (Function) returns MONEY", @ProductID INT INPUT ...).</summary>
        /// <param name="qualified">After "schema.": insert the bare name.</param>
        /// <param name="group">Sort group; by default procedures ("1") ahead of functions ("2").</param>
        private void AddRoutineItems(List<CompletionItem> items, IEnumerable<DbRoutine> routines, bool qualified, string group = null)
        {
            foreach (var r in routines)
            {
                string name = SqlIdentifier.QuoteIfNeeded(r.Name);
                bool dbo = string.Equals(r.Schema, SchemaModel.DefaultSchema, StringComparison.OrdinalIgnoreCase);
                string insert = qualified || dbo ? name : SqlIdentifier.QuoteIfNeeded(r.Schema) + "." + name;
                var icon = r.IsProcedure ? Icons.Procedure : r.Kind == RoutineKind.TableFunction ? Icons.TableFunction : Icons.Function;
                string description = $"{r} ({(r.IsProcedure ? "procedure" : "function")}, {r.Parameters.Count} parameters)";
                var item = Item(r.Name, icon, insert, group ?? (r.IsProcedure ? "1" : "2"), r.Name, qualified || dbo ? null : r.Schema, description);
                item.Properties.AddProperty(ItemProps.Table, new Lazy<TableDetails>(() => TableDetails.Build(r)));
                items.Add(item);
            }
        }

        // Tables of the tree: the statement's CTEs first, then the database's tables and views. Their columns are added
        // under them by SqlItemManager when expanded.
        private void AddTreeItems(List<CompletionItem> items, CaretContext ctx, SchemaModel schema)
        {
            var noAlias = SqlContextAnalyzer.Analyze("", 0); // AllowAlias is false here; the commit writes the query anyway
            foreach (var o in ctx.CommonTableExpressions.Select(c => c.Resolve(schema)).Where(o => o != null))
                items.Add(TreeTableItem(schema, o, noAlias, "1!"));
            foreach (var o in schema.Objects)
                items.Add(TreeTableItem(schema, o, noAlias, "1"));
        }

        // Column picker: the query block's tables in query order, by the name the query uses ("p", "pp"), already
        // expanded. False when none of them is known, so the plain column list is used instead.
        private bool AddPickerItems(List<CompletionItem> items, CaretContext ctx, SchemaModel schema, IAsyncCompletionSession session)
        {
            var tables = ctx.SiblingTables.Select(r => (r, o: schema.Find(r))).Where(x => x.o != null && x.o.Columns.Count > 0).ToList();
            if (tables.Count == 0) return false;

            var tree = TreeState.For(session);
            tree.Schema = schema;
            tree.IsPicker = true;
            tree.Qualify = tables.Count > 1 || tables.Any(x => x.r.Alias != null);
            int order = 0;
            foreach (var (r, o) in tables)
            {
                var node = new TreeNode(o, null, r.RefName);
                string description = $"{o} ({o.Kind.ToLowerInvariant()}, {o.Columns.Count} columns)";
                var item = WithTable(Item(r.RefName, o.IsView ? Icons.View : Icons.Table, r.RefName, "1" + order++.ToString("D2"),
                    r.RefName, r.Alias != null ? SchemaModel.DisplayName(o) : null, description), schema, o);
                item.Properties.AddProperty(typeof(TreeNode), node);
                tree.Expanded.Add(node.Key);
                items.Add(item);
            }
            return true;
        }

        private CompletionItem TreeTableItem(SchemaModel schema, DbObject o, CaretContext ctx, string group)
        {
            var item = ObjectItem(schema, o, SchemaModel.DisplayName(o), ctx, group);
            item.Properties.AddProperty(typeof(TreeNode), new TreeNode(o));
            return item;
        }

        private void AddGeneralItems(List<CompletionItem> items, CaretContext ctx, SchemaModel schema, bool columns = true)
        {
            var tables = !columns ? new List<(string refName, DbObject o)>() : ctx.TablesInScope
                .Select(r => (refName: r.RefName, o: schema.Find(r)))
                .Where(x => x.o != null)
                .ToList();
            // "WITH c AS (...) SELECT |" before any FROM: the CTEs are what the query is about to read.
            if (columns && tables.Count == 0)
                tables = ctx.CommonTableExpressions
                    .Select(c => (refName: c.Name, o: c.Resolve(schema)))
                    .Where(x => x.o != null)
                    .ToList();
            var ambiguous = new HashSet<string>(
                tables.SelectMany(x => x.o.Columns.Select(c => c.Name)).GroupBy(n => n, StringComparer.OrdinalIgnoreCase).Where(g => g.Count() > 1).Select(g => g.Key),
                StringComparer.OrdinalIgnoreCase);

            var added = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var (refName, o) in tables)
            {
                foreach (var c in o.Columns)
                {
                    // A column that exists in two tables only runs when qualified, so qualify it.
                    string insert = ambiguous.Contains(c.Name)
                        ? SqlIdentifier.QuoteIfNeeded(refName) + "." + SqlIdentifier.QuoteIfNeeded(c.Name)
                        : SqlIdentifier.QuoteIfNeeded(c.Name);
                    if (added.Add(insert)) items.Add(ColumnItem(schema, c, insert, o, suffix: refName));
                }
            }

            // Scalar functions, used in expressions: after the columns, ahead of keywords.
            AddRoutineItems(items, schema.Routines.Where(r => r.Kind == RoutineKind.ScalarFunction), qualified: false, group: "2");

            foreach (var k in SqlKeywords.CompletionKeywords)
            {
                var item = Item(k, Icons.Keyword, k, "3", k.Replace(" ", ""), null, null);
                item.Properties.AddProperty(ItemProps.IsKeyword, true);
                items.Add(item);
            }

            foreach (var s in SnippetProvider.Current.All)
            {
                var item = Item(s.Key, Icons.Snippet, s.Key, "4", s.Key, s.Description, System.Text.RegularExpressions.Regex.Replace(s.Body, SnippetLiterals.CaretPattern, "",
                    System.Text.RegularExpressions.RegexOptions.IgnoreCase));
                item.Properties.AddProperty(ItemProps.Snippet, s);
                items.Add(item);
            }
        }

        // Declared @variables and @parameters; the description reads like SSMS's: "uspGetList.@MaxPrice (Parameter)".
        private void AddVariableItems(List<CompletionItem> items, IReadOnlyList<SqlVariable> variables, string group)
        {
            foreach (var v in variables)
            {
                bool parameter = v.Kind == SqlVariableKind.Parameter;
                string title = (v.Owner != null ? v.Owner + "." : "") + v.Name +
                    (parameter ? " (Parameter)" : v.Kind == SqlVariableKind.TableVariable ? " (Table variable)" : " (Variable)");
                string signature = v.Name + (v.TypeName != null ? " " + v.TypeName : "") + (v.Direction != null ? " " + v.Direction : "");
                items.Add(Item(v.Name, parameter ? Icons.Parameter : Icons.Variable, v.Name, group, v.Name,
                    v.TypeName, title + "\n" + signature));
            }
        }

        private CompletionItem ObjectItem(SchemaModel schema, DbObject o, string name, CaretContext ctx, string group = "1")
        {
            string insert = name;
            if (ctx.AllowAlias) insert += " " + AliasGenerator.Generate(o.Name, ctx.AliasesInStatement);
            string description = $"{o} ({o.Kind.ToLowerInvariant()}, {o.Columns.Count} columns)";
            string suffix = o.Schema == null ? o.Kind
                : string.Equals(o.Schema, SchemaModel.DefaultSchema, StringComparison.OrdinalIgnoreCase) ? null : o.Schema;
            var item = Item(o.Name, o.IsView ? Icons.View : Icons.Table, insert, group, o.Name, suffix, description);
            return WithTable(item, schema, o);
        }

        /// <summary>Lets the list show the table's columns beside the item (built only when the item is selected).</summary>
        private static CompletionItem WithTable(CompletionItem item, SchemaModel schema, DbObject o)
        {
            item.Properties.AddProperty(ItemProps.Table, new Lazy<TableDetails>(() => TableDetails.Build(schema, o)));
            return item;
        }

        // Description: name, type, flags, MS_Description and foreign key — shown beside the list when selected.
        private CompletionItem ColumnItem(SchemaModel schema, DbColumn c, string insert, DbObject owner, string suffix = null) =>
            Item(c.Name, Icons.Column, insert, "1", c.Name, suffix ?? c.TypeName?.ToUpperInvariant(), ColumnInfo.Describe(schema, owner, c));

        private CompletionItem Item(string display, ImageElement icon, string insert, string group, string filter, string suffix, string description)
        {
            var item = new CompletionItem(display, this, icon, ImmutableArray<CompletionFilter>.Empty, suffix ?? "",
                insert, group + display, filter, ImmutableArray<ImageElement>.Empty);
            if (description != null) item.Properties.AddProperty(ItemProps.Description, description);
            return item;
        }
    }
}
