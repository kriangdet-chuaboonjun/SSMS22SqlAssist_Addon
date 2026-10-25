using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.ComponentModel.Composition;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.VisualStudio.Language.Intellisense.AsyncCompletion;
using Microsoft.VisualStudio.Language.Intellisense.AsyncCompletion.Data;
using Microsoft.VisualStudio.Text.Editor;
using Microsoft.VisualStudio.Utilities;
using SqlAssist.Core.Completion;

namespace SqlAssist.Editor
{
    [Export(typeof(IAsyncCompletionItemManagerProvider))]
    [Name("SqlAssist fuzzy item manager")]
    [ContentType(SqlContentType.Name)]
    [Order(Before = "default")]
    internal sealed class SqlItemManagerProvider : IAsyncCompletionItemManagerProvider
    {
        private static readonly SqlItemManager Instance = new SqlItemManager();
        public IAsyncCompletionItemManager GetOrCreate(ITextView textView) => Instance;
    }

    /// <summary>Filters and ranks with <see cref="FuzzyMatcher"/>: typing "custid" finds CustomerID, "od" finds OrderDetails.</summary>
    internal sealed class SqlItemManager : IAsyncCompletionItemManager
    {
        public Task<ImmutableArray<CompletionItem>> SortCompletionListAsync(IAsyncCompletionSession session,
            AsyncCompletionSessionInitialDataSnapshot data, CancellationToken token)
        {
            try
            {
                return Task.FromResult(data.InitialItemList.OrderBy(i => i.SortText, System.StringComparer.OrdinalIgnoreCase).ToImmutableArray());
            }
            catch (Exception ex)
            {
                Log.Error("Completion sort", ex);
                return Task.FromResult(data.InitialItemList.ToImmutableArray());
            }
        }

        // Runs on every keystroke while a list is open: a failure must not reach the completion broker.
        public Task<FilteredCompletionModel> UpdateCompletionListAsync(IAsyncCompletionSession session,
            AsyncCompletionSessionDataSnapshot data, CancellationToken token)
        {
            try
            {
                return FilterAsync(session, data);
            }
            catch (Exception ex)
            {
                Log.Error("Completion filter", ex);
                // An empty list rather than the unfiltered one: nothing wrong can be committed from it.
                return Task.FromResult(new FilteredCompletionModel(ImmutableArray<CompletionItemWithHighlight>.Empty, 0,
                    data.SelectedFilters, UpdateSelectionHint.NoChange, centerSelection: true, uniqueItem: null));
            }
        }

        private static Task<FilteredCompletionModel> FilterAsync(IAsyncCompletionSession session, AsyncCompletionSessionDataSnapshot data)
        {
            session.Properties.TryGetProperty(typeof(TreeState), out TreeState picker);
            if (picker != null && !picker.IsPicker) picker = null;
            // The picker's span also covers the columns it has written; only what follows them filters.
            string typed = (picker != null ? picker.TypedAfterPicks(session, data.Snapshot) : session.ApplicableToSpan.GetText(data.Snapshot)).TrimStart('[');

            List<CompletionItem> items;
            if (typed.Length == 0)
            {
                // Nothing typed yet (list opened by "." or "JOIN "): show everything.
                items = data.InitialSortedItemList.ToList();
            }
            else
            {
                // Picker: "fir" finds p.FirstName itself, not just tables whose name matches.
                var candidates = picker == null ? data.InitialSortedItemList.ToList() : data.InitialSortedItemList
                    .SelectMany(i => TreeState.NodeOf(i) is TreeNode n && n.IsTable ? new[] { i }.Concat(picker.ChildrenOf(i, n)) : new[] { i })
                    .ToList();
                items = candidates
                    .Select((item, index) => (item, index, score: FuzzyMatcher.Score(typed, item.FilterText)))
                    .Where(x => x.score > 0)
                    .OrderByDescending(x => x.score)
                    .ThenBy(x => x.index) // ties keep group order: JOIN suggestions before the plain table list
                    .Select(x => x.item)
                    .ToList();
            }

            int selected = 0;
            var hint = typed.Length == 0 ? UpdateSelectionHint.SoftSelected : UpdateSelectionHint.Selected;
            if (items.Any(i => TreeState.NodeOf(i) != null))
            {
                var tree = TreeState.For(session);
                if (picker == null || typed.Length == 0) items = WithExpandedColumns(items, tree); // else the columns matched one by one
                if (tree.SelectAfterUpdate != null)
                {
                    selected = Math.Max(0, items.IndexOf(tree.SelectAfterUpdate));
                    tree.SelectAfterUpdate = null;
                    hint = UpdateSelectionHint.Selected;
                }
                if (tree.HasChecks) hint = UpdateSelectionHint.Selected; // Enter writes the ticked columns
            }
            // Soft selection with nothing typed, so Enter still means a new line.
            if (items.Count == 0) hint = UpdateSelectionHint.NoChange;

            return Task.FromResult(new FilteredCompletionModel(items.Select(i => new CompletionItemWithHighlight(i)).ToImmutableArray(),
                selected, data.SelectedFilters, hint, centerSelection: true, uniqueItem: null));
        }

        // The columns of each expanded table, right under it.
        private static List<CompletionItem> WithExpandedColumns(List<CompletionItem> items, TreeState tree)
        {
            var result = new List<CompletionItem>(items.Count);
            foreach (var item in items)
            {
                result.Add(item);
                var node = TreeState.NodeOf(item);
                if (node != null && node.IsTable && tree.Expanded.Contains(node.Key))
                    result.AddRange(tree.ChildrenOf(item, node));
            }
            return result;
        }
    }
}
