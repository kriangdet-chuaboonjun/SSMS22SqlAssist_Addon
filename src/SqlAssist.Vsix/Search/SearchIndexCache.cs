using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Tasks;
using SqlAssist.Connection;
using SqlAssist.Core.Search;

namespace SqlAssist.Search
{
    /// <summary>Shares in-flight index loads and keeps the most recently used indexes within a memory budget.</summary>
    internal sealed class SearchIndexCache
    {
        private const long TotalBudget = 64L * 1024 * 1024;
        private const long IndexBudget = 16L * 1024 * 1024;
        private const int EntryLimit = 24;
        private static readonly TimeSpan StaleAfter = TimeSpan.FromMinutes(10);
        private static readonly TimeSpan RetryFailedAfter = TimeSpan.FromMinutes(1);
        public static SearchIndexCache Instance { get; } = new SearchIndexCache();

        private readonly Dictionary<(string server, string database), Entry> _entries =
            new Dictionary<(string server, string database), Entry>();
        private readonly SemaphoreSlim _loadSlot = new SemaphoreSlim(1);
        private long _access;
        private long _bytes;

        private sealed class Entry
        {
            public Task<IReadOnlyList<SearchItem>> Load;
            public DateTime StartedUtc;
            public IReadOnlyList<SearchItem> Items;
            public bool Ready;
            public long Bytes;
            public long Access;
        }

        /// <summary>Returns null for indexes that must be searched in pages.</summary>
        public async Task<IReadOnlyList<SearchItem>> GetAsync(ConnectionTarget server, string database, CancellationToken token)
        {
            token.ThrowIfCancellationRequested();
            var key = (server.ServerKey, database);
            Task<IReadOnlyList<SearchItem>> load;
            lock (_entries)
            {
                if (!_entries.TryGetValue(key, out var entry))
                {
                    entry = new Entry();
                    _entries.Add(key, entry);
                }
                entry.Access = ++_access;
                var age = DateTime.UtcNow - entry.StartedUtc;
                if (entry.Load == null || entry.Load.IsCompleted &&
                    age > (entry.Load.IsFaulted ? RetryFailedAfter : StaleAfter))
                {
                    entry.StartedUtc = DateTime.UtcNow;
                    entry.Load = Task.Run(() => LoadAsync(server, key, entry));
                }

                if (entry.Ready) return entry.Items;
                load = entry.Load;
            }

            var cancelled = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            using (token.Register(() => cancelled.TrySetResult(true)))
                await Task.WhenAny(load, cancelled.Task).ConfigureAwait(false);
            token.ThrowIfCancellationRequested();
            return await load.ConfigureAwait(false);
        }

        public void Clear()
        {
            lock (_entries)
            {
                _entries.Clear();
                _bytes = 0;
            }
            Log.Info("Search index cache cleared");
        }

        private async Task<IReadOnlyList<SearchItem>> LoadAsync(ConnectionTarget server, (string server, string database) key, Entry entry)
        {
            await _loadSlot.WaitAsync().ConfigureAwait(false);
            try
            {
                lock (_entries)
                {
                    if (!_entries.TryGetValue(key, out var current) || current != entry)
                        throw new OperationCanceledException("Search index cache was cleared");
                }
                var items = await SearchIndexLoader.LoadIfSmallAsync(server, key.database, IndexBudget, CancellationToken.None).ConfigureAwait(false);
                long bytes = items == null ? 0 : EstimateBytes(items);
                if (bytes > IndexBudget)
                {
                    items = null;
                    bytes = 0;
                }

                lock (_entries)
                {
                    if (_entries.TryGetValue(key, out var current) && current == entry)
                    {
                        _bytes += bytes - entry.Bytes;
                        entry.Bytes = bytes;
                        entry.Items = items;
                        entry.Ready = true;
                        Evict(entry);
                        Log.Info($"Search index of {server.Server}/{key.database}: {(items == null ? "paged" : items.Count + " items")}; cache {_bytes / 1024} KiB in {_entries.Count} entries");
                    }
                }
                return items;
            }
            catch (Exception ex)
            {
                lock (_entries) Evict(entry);
                if (!(ex is OperationCanceledException)) Log.Error("Loading search index of " + server.Server + "/" + key.database, ex);
                throw;
            }
            finally
            {
                _loadSlot.Release();
            }
        }

        private void Evict(Entry justLoaded)
        {
            while (_bytes > TotalBudget || _entries.Count > EntryLimit)
            {
                var victim = _entries.Where(pair => pair.Value != justLoaded && pair.Value.Load.IsCompleted)
                    .OrderBy(pair => pair.Value.Access).FirstOrDefault();
                if (victim.Value == null) break;
                _entries.Remove(victim.Key);
                _bytes -= victim.Value.Bytes;
                Log.Info($"Evicted search index of {victim.Key.database}; cache {_bytes / 1024} KiB");
            }
        }

        private static long EstimateBytes(IReadOnlyList<SearchItem> items)
        {
            var strings = new HashSet<string>(StringInstanceComparer.Instance);
            long bytes = items.Count * 128L; // SearchItem, list reference and allocation overhead.
            foreach (var item in items)
            {
                Add(item.Database);
                Add(item.Schema);
                Add(item.Name);
                Add(item.Parent);
                Add(item.Definition);
                Add(item.Script);
                Add(item.SubKind);
            }
            return bytes;

            void Add(string value)
            {
                if (value != null && strings.Add(value)) bytes += 32L + value.Length * 2L;
            }
        }

        private sealed class StringInstanceComparer : IEqualityComparer<string>
        {
            public static readonly StringInstanceComparer Instance = new StringInstanceComparer();
            public bool Equals(string x, string y) => ReferenceEquals(x, y);
            public int GetHashCode(string obj) => RuntimeHelpers.GetHashCode(obj);
        }
    }
}
