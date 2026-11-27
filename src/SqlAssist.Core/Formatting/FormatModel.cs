using System;
using System.Collections.Generic;
using SqlAssist.Core.Sql;

namespace SqlAssist.Core.Formatting
{
    /// <summary>A token plus what the formatter needs to know about its surroundings in the original text.</summary>
    internal sealed class FToken
    {
        public SqlToken Raw;
        /// <summary>Text to write: the original, with keywords re-cased and line comments trimmed.</summary>
        public string Text;
        /// <summary>Line breaks between the token before and this one (0, 1, or 2 for "a blank line or more").</summary>
        public int NewlinesBefore;
        /// <summary>Any whitespace between the token before and this one.</summary>
        public bool SpaceBefore;
        /// <summary>For a block comment that starts its line: the width of the whitespace before it; otherwise -1.</summary>
        public int OriginalIndent = -1;

        public TokenKind Kind => Raw.Kind;
        public bool IsComment => Raw.Kind == TokenKind.Comment;
        public bool IsLineComment => IsComment && Raw.Text.StartsWith("--", StringComparison.Ordinal);
        public bool IsBlockComment => IsComment && !IsLineComment;
        public bool IsWordKind => Raw.Kind == TokenKind.Word;
        public bool IsSymbolKind => Raw.Kind == TokenKind.Symbol;

        public bool IsWord(string word) => Raw.Kind == TokenKind.Word && string.Equals(Raw.Text, word, StringComparison.OrdinalIgnoreCase);
        public bool IsSym(string symbol) => Raw.Kind == TokenKind.Symbol && Raw.Text == symbol;

        public string Upper => Raw.Kind == TokenKind.Word ? Raw.Text.ToUpperInvariant() : null;

        public static FToken Synthetic(string symbol) => new FToken
        {
            Raw = new SqlToken(TokenKind.Symbol, 0, symbol.Length, symbol, true), Text = symbol,
        };
    }

    internal abstract class Item
    {
    }

    internal sealed class TokItem : Item
    {
        public TokItem(FToken token) { T = token; }
        public FToken T { get; }
        public bool IsComment => T.IsComment;
        public bool IsWord(string word) => T.IsWord(word);
        public bool IsSym(string symbol) => T.IsSym(symbol);
    }

    /// <summary>Everything between a pair of parentheses, which are not part of <see cref="Children"/>.</summary>
    internal sealed class GroupItem : Item
    {
        public FToken Open;
        public FToken Close;
        public List<Item> Children = new List<Item>();
    }

    /// <summary>CASE … END, both words included in <see cref="Children"/>.</summary>
    internal sealed class CaseItem : Item
    {
        public List<Item> Children = new List<Item>();
    }

    /// <summary>Turns a run of tokens into a tree of items: parentheses and CASE … END become single items, so every
    /// layout rule can say "at this level" without caring what is nested inside.</summary>
    internal static class ItemBuilder
    {
        // Layout walks these frames recursively. Bound the depth while building
        // the tree iteratively, before layout can use the editor thread's stack.
        private const int MaxNesting = 128;

        internal sealed class NestingLimitExceededException : Exception
        {
            public NestingLimitExceededException()
                : base("วงเล็บและ CASE ซ้อนกันเกิน " + MaxNesting + " ชั้น")
            {
            }
        }

        private sealed class Frame
        {
            public GroupItem Group;   // null for a CASE frame and for the root
            public CaseItem Case;     // null for a group frame and for the root
            public List<Item> Items;
        }

        public static List<Item> Build(IReadOnlyList<FToken> tokens, int first, int last)
        {
            var root = new Frame { Items = new List<Item>() };
            var stack = new List<Frame> { root };

            for (int i = first; i <= last; i++)
            {
                var t = tokens[i];
                var top = stack[stack.Count - 1];

                if (t.IsSym("("))
                {
                    var group = new GroupItem { Open = t };
                    stack.Add(new Frame { Group = group, Items = group.Children });
                }
                else if (t.IsSym(")") && stack.Exists(f => f.Group != null))
                {
                    while (stack[stack.Count - 1].Group == null) Flatten(stack);
                    var done = stack[stack.Count - 1];
                    stack.RemoveAt(stack.Count - 1);
                    done.Group.Close = t;
                    stack[stack.Count - 1].Items.Add(done.Group);
                }
                else if (t.IsWord("CASE") && !PartOfName(tokens, i))
                {
                    var @case = new CaseItem();
                    @case.Children.Add(new TokItem(t));
                    stack.Add(new Frame { Case = @case, Items = @case.Children });
                }
                else if (t.IsWord("END") && top.Case != null)
                {
                    top.Items.Add(new TokItem(t));
                    stack.RemoveAt(stack.Count - 1);
                    stack[stack.Count - 1].Items.Add(top.Case);
                }
                else top.Items.Add(new TokItem(t));

                if (stack.Count - 1 > MaxNesting) throw new NestingLimitExceededException();
            }
            while (stack.Count > 1) Flatten(stack);
            return root.Items;
        }

        /// <summary>A CASE that never ended (or a group that never closed) becomes plain items in its parent.</summary>
        private static void Flatten(List<Frame> stack)
        {
            var frame = stack[stack.Count - 1];
            stack.RemoveAt(stack.Count - 1);
            var parent = stack[stack.Count - 1].Items;
            if (frame.Group != null)
            {
                parent.Add(new TokItem(frame.Group.Open));
                parent.AddRange(frame.Items);
            }
            else parent.AddRange(frame.Items);
        }

        private static bool PartOfName(IReadOnlyList<FToken> tokens, int i) =>
            i > 0 && tokens[i - 1].IsSym(".");
    }
}
