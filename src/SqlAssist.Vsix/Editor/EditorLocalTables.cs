using System;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.VisualStudio.Text;
using SqlAssist.Core.Sql;

namespace SqlAssist.Editor
{
    /// <summary>Shares analysis of an immutable snapshot between completion, Tab and its hint.</summary>
    internal static class EditorLocalTables
    {
        private static readonly ConditionalWeakTable<ITextBuffer, BufferWork> Buffers =
            new ConditionalWeakTable<ITextBuffer, BufferWork>();

        public static Task<LocalTableIndex> GetAsync(ITextSnapshot snapshot) =>
            Buffers.GetValue(snapshot.TextBuffer, _ => new BufferWork()).GetAsync(snapshot);

        private sealed class BufferWork
        {
            private readonly SemaphoreSlim _parseSlot = new SemaphoreSlim(1);
            private ITextSnapshot _latest;
            private Task<LocalTableIndex> _index;

            public Task<LocalTableIndex> GetAsync(ITextSnapshot snapshot)
            {
                lock (this)
                {
                    if (ReferenceEquals(_latest, snapshot)) return _index;
                    if (_latest != null && snapshot.Version.VersionNumber < _latest.Version.VersionNumber)
                        return Task.FromCanceled<LocalTableIndex>(new CancellationToken(true));
                    _latest = snapshot;
                    return _index = Task.Run(() => ParseAsync(snapshot));
                }
            }

            private async Task<LocalTableIndex> ParseAsync(ITextSnapshot snapshot)
            {
                await Task.Delay(100).ConfigureAwait(false);
                await _parseSlot.WaitAsync().ConfigureAwait(false);
                try
                {
                    lock (this)
                    {
                        if (!ReferenceEquals(_latest, snapshot) || !ReferenceEquals(snapshot.TextBuffer.CurrentSnapshot, snapshot))
                            throw new OperationCanceledException();
                    }
                    string text = snapshot.GetText();
                    return LocalTableIndex.Parse(text.IndexOf('#') < 0 && text.IndexOf('@') < 0 ? "" : text);
                }
                finally
                {
                    _parseSlot.Release();
                }
            }
        }
    }
}
