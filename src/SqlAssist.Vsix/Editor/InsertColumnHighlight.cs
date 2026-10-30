using System;
using System.Collections.Generic;
using System.ComponentModel.Composition;
using System.Linq;
using System.Windows.Media;
using Microsoft.VisualStudio.Threading;
using Microsoft.VisualStudio.Shell;
using Microsoft.VisualStudio.Text;
using Microsoft.VisualStudio.Text.Classification;
using Microsoft.VisualStudio.Text.Editor;
using Microsoft.VisualStudio.Text.Tagging;
using Microsoft.VisualStudio.Utilities;
using SqlAssist.Core.Sql;
using SqlAssist.Settings;

namespace SqlAssist.Editor
{
    [Export(typeof(EditorFormatDefinition))]
    [Name(InsertColumnHighlightTagger.ColumnFormat)]
    [UserVisible(true)]
    internal sealed class InsertColumnMarkerFormat : MarkerFormatDefinition
    {
        public InsertColumnMarkerFormat()
        {
            // Translucent, so text drawn over it stays readable in both the Light and the Dark theme.
            DisplayName = "SqlAssist: INSERT column";
            BackgroundColor = Color.FromArgb(0x70, 0xFF, 0xB0, 0x20);
            ForegroundColor = Color.FromArgb(0xFF, 0xD9, 0x8A, 0x00);
            ZOrder = 5;
        }
    }

    [Export(typeof(EditorFormatDefinition))]
    [Name(InsertColumnHighlightTagger.ValueFormat)]
    [UserVisible(true)]
    internal sealed class InsertValueMarkerFormat : MarkerFormatDefinition
    {
        public InsertValueMarkerFormat()
        {
            DisplayName = "SqlAssist: INSERT value";
            BackgroundColor = Color.FromArgb(0x60, 0x3C, 0x9B, 0xFF);
            ForegroundColor = Color.FromArgb(0xFF, 0x1E, 0x7A, 0xE0);
            ZOrder = 5;
        }
    }

    [Export(typeof(IViewTaggerProvider))]
    [ContentType(SqlContentType.Name)]
    [TagType(typeof(TextMarkerTag))]
    internal sealed class InsertColumnHighlightTaggerProvider : IViewTaggerProvider
    {
        public ITagger<T> CreateTagger<T>(ITextView textView, ITextBuffer buffer) where T : ITag
        {
            if (typeof(T) != typeof(TextMarkerTag)) return null;
            // Only the query window's own buffer: a projection or a nested buffer would be tagged a second time.
            if (textView == null || buffer == null || buffer != textView.TextBuffer) return null;
            try
            {
                return textView.Properties.GetOrCreateSingletonProperty(() => new InsertColumnHighlightTagger(textView)) as ITagger<T>;
            }
            catch (Exception ex)
            {
                Log.Error("INSERT column highlight: creating the tagger", ex);
                return null;
            }
        }
    }

    /// <summary>In "INSERT INTO t (a, b) VALUES (1, 2)" marks the column and the value at the caret's position, as text markers
    /// (nothing is written to the buffer, the selection and the undo stack are not touched). The statement is read from the
    /// tokens by Core's <see cref="InsertValueMap"/>; the result is kept per snapshot, so moving the caret inside a statement
    /// looks up the map again instead of parsing it again.</summary>
    internal sealed class InsertColumnHighlightTagger : ITagger<TextMarkerTag>
    {
        public const string ColumnFormat = "SqlAssist.InsertColumn";
        public const string ValueFormat = "SqlAssist.InsertValue";

        // How far back an INSERT is looked for (in chunks, nearest first), and how much text after the caret is read at first;
        // a statement that runs on past that is read once more up to MaxForward (a 1,000-row INSERT is about 100,000 characters).
        private const int BackLimit = 300000;
        private const int Chunk = 4096;
        private const int FirstForward = 8000;
        private const int MaxForward = 600000;
        // Real INSERT keywords tried, nearest first: the others are in strings or comments, or are not the form that is read.
        private const int Candidates = 8;

        private readonly ITextView _view;
        private readonly Dictionary<int, Parsed> _parsed = new Dictionary<int, Parsed>();
        private ITextSnapshot _parsedSnapshot;
        // The statement last read, so the next read after an edit inside it starts with a window long enough.
        private int _lastStart = -1, _lastLength;
        private Marks _marks;
        private bool _scheduled, _disposed, _failed;

        public InsertColumnHighlightTagger(ITextView view)
        {
            _view = view;
            _view.Caret.PositionChanged += OnChanged;
            _view.TextBuffer.PostChanged += OnChanged;
            _view.Closed += OnClosed;
        }

        public event EventHandler<SnapshotSpanEventArgs> TagsChanged;

        public IEnumerable<ITagSpan<TextMarkerTag>> GetTags(NormalizedSnapshotSpanCollection spans)
        {
            var result = new List<ITagSpan<TextMarkerTag>>();
            try
            {
                var marks = _marks;
                if (marks == null || spans.Count == 0) return result;
                var snapshot = spans[0].Snapshot;
                Add(result, marks.Columns, ColumnFormat, snapshot, spans);
                Add(result, marks.Values, ValueFormat, snapshot, spans);
            }
            catch (Exception ex)
            {
                if (!_failed) Log.Error("INSERT column highlight: tags", ex);
                _failed = true;
                result.Clear();
            }
            return result;
        }

        private static void Add(List<ITagSpan<TextMarkerTag>> result, List<SnapshotSpan> marked, string format, ITextSnapshot snapshot, NormalizedSnapshotSpanCollection wanted)
        {
            foreach (var span in marked)
            {
                var now = span.TranslateTo(snapshot, SpanTrackingMode.EdgeExclusive);
                if (wanted.IntersectsWith(now)) result.Add(new TagSpan<TextMarkerTag>(now, new TextMarkerTag(format)));
            }
        }

        // Not IDisposable: one tagger serves every consumer of the view, and the editor disposes taggers when a consumer goes
        // away. It lives and dies with the view, so it is the view closing that ends it.
        private void OnClosed(object sender, EventArgs e)
        {
            try
            {
                if (_disposed) return;
                _disposed = true;
                _view.Caret.PositionChanged -= OnChanged;
                _view.TextBuffer.PostChanged -= OnChanged;
                _view.Closed -= OnClosed;
                _marks = null;
                _parsed.Clear();
                _parsedSnapshot = null;
            }
            catch (Exception ex)
            {
                Log.Error("INSERT column highlight: closing", ex);
            }
        }

        private void OnChanged(object sender, EventArgs e)
        {
            if (_disposed || _scheduled) return;
            // Once the editor has finished with the caret and the text, so the marks never describe a snapshot that is gone.
            _scheduled = true;
            try
            {
                // Fire and forget by design: Update catches everything itself.
#pragma warning disable VSSDK007
                ThreadHelper.JoinableTaskFactory.RunAsync(async () =>
                {
                    try
                    {
                        await ThreadHelper.JoinableTaskFactory.SwitchToMainThreadAsync(alwaysYield: true);
                        Update();
                    }
                    finally
                    {
                        _scheduled = false;   // also when the switch never completes (shutdown), so the next change can schedule again
                    }
                }).FileAndForget("SqlAssist/InsertColumnHighlight");
#pragma warning restore VSSDK007
            }
            catch (Exception ex)
            {
                _scheduled = false;
                Log.Error("INSERT column highlight: scheduling", ex);
            }
        }

        private void Update()
        {
            ThreadHelper.ThrowIfNotOnUIThread();
            _scheduled = false;
            if (_disposed) return;
            try
            {
                Marks next = SettingsProvider.HighlightInsertColumns ? Compute() : null;
                Apply(next);
                _failed = false;
            }
            catch (Exception ex)
            {
                // Never into the editor; once per run of failures, so a bad statement does not flood the log.
                if (!_failed) Log.Error("INSERT column highlight", ex);
                _failed = true;
                try { Apply(null); }
                catch (Exception clearing) { _marks = null; Log.Error("INSERT column highlight: clearing", clearing); }
            }
        }

        private Marks Compute()
        {
            ThreadHelper.ThrowIfNotOnUIThread();
            var snapshot = _view.TextSnapshot;
            int caret = _view.Caret.Position.BufferPosition.TranslateTo(snapshot, PointTrackingMode.Positive).Position;
            if (snapshot.Length == 0) return null;

            if (!ReferenceEquals(_parsedSnapshot, snapshot))
            {
                _parsed.Clear();
                _parsedSnapshot = snapshot;
            }

            int limit = Math.Max(0, caret - BackLimit);
            int before = caret;   // INSERT keywords are looked for before this position
            for (int tried = 0; tried < Candidates; tried++)
            {
                int start = FindInsertBefore(snapshot, before, limit);
                if (start < 0) break;
                before = start;

                var parsed = ParsedAt(snapshot, start, caret);
                if (parsed.Map == null) continue;   // not an INSERT … VALUES we read (INSERT … SELECT, a comment): try the one before
                // The nearest readable statement is the only one that can hold the caret.
                var hit = parsed.Map.At(caret - start);
                return hit == null ? null : ToMarks(snapshot, start, hit);
            }
            return null;
        }

        /// <summary>Start of the nearest whole word INSERT that begins before <paramref name="before"/> (and not before
        /// <paramref name="limit"/>) and is followed by something a table name can start with; -1 when there is none. Looks at one
        /// chunk of the buffer at a time, nearest first, so a caret far from any INSERT costs a few chunks, not a copy of the script.</summary>
        private static int FindInsertBefore(ITextSnapshot snapshot, int before, int limit)
        {
            const string Word = "INSERT";
            int end = before;
            while (end > limit)
            {
                // Overlaps the previous chunk by the word's length, so a keyword cut by the boundary is still found whole.
                int start = Math.Max(limit, end - Chunk);
                int length = Math.Min(snapshot.Length, end + Word.Length) - start;
                string text = snapshot.GetText(start, length);
                for (int at = text.Length; at > 0;)
                {
                    at = text.LastIndexOf(Word, at - 1, StringComparison.OrdinalIgnoreCase);
                    if (at < 0) break;
                    int position = start + at;
                    if (position >= end) continue;
                    bool wordBefore = position > 0 && SqlIdentifier.IsWordChar(snapshot[position - 1]);
                    int after = position + Word.Length;
                    if (wordBefore || (after < snapshot.Length && SqlIdentifier.IsWordChar(snapshot[after]))) continue;
                    if (StartsAName(snapshot, after)) return position;
                }
                end = start;
            }
            return -1;
        }

        // INSERT 'x' … or INSERT (…) can not be "INSERT [INTO] name (": no need to read such a candidate.
        private static bool StartsAName(ITextSnapshot snapshot, int position)
        {
            for (int i = position; i < snapshot.Length && i < position + 256; i++)
            {
                char c = snapshot[i];
                if (char.IsWhiteSpace(c)) continue;
                return char.IsLetter(c) || c == '_' || c == '[' || c == '"' || c == '#' || c == '@';
            }
            return false;
        }

        private Parsed ParsedAt(ITextSnapshot snapshot, int start, int caret)
        {
            // A statement read from a window that ended before the caret may have been cut short: read it again with more.
            if (_parsed.TryGetValue(start, out var known) && (!known.Truncated || caret <= known.WindowEnd)) return known;

            // After an edit inside a long statement, start with a window as long as the statement was.
            int expected = start == _lastStart ? start + _lastLength : 0;
            int end = Math.Min(snapshot.Length, Math.Max(Math.Max(caret, start) + FirstForward, expected + FirstForward));
            var parsed = Read(snapshot, start, end);
            if (parsed.Truncated && end < snapshot.Length)
                parsed = Read(snapshot, start, Math.Min(snapshot.Length, start + MaxForward));
            _parsed[start] = parsed;
            if (parsed.Map != null) { _lastStart = start; _lastLength = parsed.Map.StatementEnd; }
            return parsed;
        }

        private static Parsed Read(ITextSnapshot snapshot, int start, int end)
        {
            var map = InsertValueMap.Parse(snapshot.GetText(start, end - start), 0);
            return new Parsed(map, end, truncated: map != null && start + map.StatementEnd >= end && end < snapshot.Length);
        }

        private static Marks ToMarks(ITextSnapshot snapshot, int offset, InsertHighlight hit)
        {
            var columns = new List<SnapshotSpan> { new SnapshotSpan(snapshot, offset + hit.Column.Start, hit.Column.Length) };
            var values = hit.Values.Where(v => v.Length > 0).Select(v => new SnapshotSpan(snapshot, offset + v.Start, v.Length)).ToList();
            return new Marks(snapshot, columns, values);
        }

        private void Apply(Marks next)
        {
            var old = _marks;
            if (old == null && next == null) return;
            if (old != null && next != null && old.SameAs(next)) return;
            _marks = next;

            // Both the old and the new marks are redrawn: one span from the first to the last of them.
            var current = _view.TextSnapshot;
            int from = int.MaxValue, to = -1;
            foreach (var marks in new[] { old, next })
            {
                if (marks == null) continue;
                foreach (var span in marks.Columns.Concat(marks.Values))
                {
                    var now = span.TranslateTo(current, SpanTrackingMode.EdgeInclusive);
                    from = Math.Min(from, now.Start.Position);
                    to = Math.Max(to, now.End.Position);
                }
            }
            if (to >= from) TagsChanged?.Invoke(this, new SnapshotSpanEventArgs(new SnapshotSpan(current, from, to - from)));
        }

        private sealed class Parsed
        {
            public Parsed(InsertValueMap map, int windowEnd, bool truncated) { Map = map; WindowEnd = windowEnd; Truncated = truncated; }
            public InsertValueMap Map { get; }
            /// <summary>Buffer position where the text that was read ended.</summary>
            public int WindowEnd { get; }
            /// <summary>The statement was still open where the text that was read ended.</summary>
            public bool Truncated { get; }
        }

        private sealed class Marks
        {
            public Marks(ITextSnapshot snapshot, List<SnapshotSpan> columns, List<SnapshotSpan> values)
            {
                Snapshot = snapshot; Columns = columns; Values = values;
            }
            public ITextSnapshot Snapshot { get; }
            public List<SnapshotSpan> Columns { get; }
            public List<SnapshotSpan> Values { get; }

            public bool SameAs(Marks other) =>
                ReferenceEquals(Snapshot, other.Snapshot) && Columns.SequenceEqual(other.Columns) && Values.SequenceEqual(other.Values);
        }
    }
}
