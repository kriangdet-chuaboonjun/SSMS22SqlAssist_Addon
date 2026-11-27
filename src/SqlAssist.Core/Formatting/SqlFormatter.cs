using System;
using System.Collections.Generic;
using System.Linq;
using SqlAssist.Core.Sql;

namespace SqlAssist.Core.Formatting
{
    /// <summary>Formats T-SQL: indentation, line breaks, spacing, keyword case and comma position, from the existing tokenizer
    /// rather than a full grammar, so comments, hints, GO, and half-written statements all survive. The only things it ever
    /// changes are whitespace, the case of keywords, and which end of a line a list comma sits at; it checks that on the
    /// result (every token and comment still there, in order) and leaves the text alone, saying why, when it cannot be
    /// sure. Formatting its own output changes nothing.</summary>
    public static class SqlFormatter
    {
        /// <param name="baseIndent">Spaces added in front of every line but the first: for a selection that starts partway
        /// across an indented line.</param>
        public static FormatResult Format(string sql, FormatOptions options = null, int baseIndent = 0)
        {
            sql = sql ?? "";
            options = options ?? new FormatOptions();
            if (string.IsNullOrWhiteSpace(sql)) return new FormatResult(sql, null, false);

            try
            {
                var scan = SqlStatementScanner.Scan(sql);
                if (scan.Problems.Count > 0)
                {
                    var problem = scan.Problems[0];
                    var (line, column) = scan.LineAndColumn(problem.Offset);
                    return Fail(sql, "บรรทัด " + line + " คอลัมน์ " + column + ": " + problem.Message);
                }

                string newline = options.Newline ?? DetectNewline(sql);
                var keywords = KeywordCaser.Find(scan);
                var tokens = Prepare(scan, options.KeywordCase, keywords);
                string text = new FormatLayout(scan, tokens, options, newline, Math.Max(0, baseIndent)).Run();
                if (sql.EndsWith("\n", StringComparison.Ordinal) || sql.EndsWith("\r", StringComparison.Ordinal)) text += newline;

                string difference = Compare(scan, keywords, SqlStatementScanner.Scan(text));
                if (difference != null)
                    return Fail(sql, "ตรวจพบว่าผลที่จัดแล้วต่างจากต้นฉบับมากกว่าช่องว่าง (" + difference + ") จึงไม่ได้แก้ข้อความ");
                return new FormatResult(text, null, !string.Equals(text, sql, StringComparison.Ordinal));
            }
            catch (ItemBuilder.NestingLimitExceededException ex)
            {
                return Fail(sql, ex.Message + " จึงไม่ได้แก้ข้อความ");
            }
            catch (Exception ex)
            {
                return Fail(sql, "จัดรูปแบบไม่สำเร็จ (" + ex.GetType().Name + ") จึงไม่ได้แก้ข้อความ");
            }
        }

        /// <summary>Where a caret at <paramref name="offset"/> in the original text belongs in the formatted text. Formatting only
        /// moves whitespace (and commas), so the caret stays beside the same characters: before the one it was before, or after the
        /// one it was after; at the end it stays at the end.</summary>
        public static int MapOffset(string original, int offset, string formatted)
        {
            original = original ?? "";
            formatted = formatted ?? "";
            offset = Math.Max(0, Math.Min(offset, original.Length));
            if (offset == original.Length) return formatted.Length;

            int before = 0;
            for (int i = 0; i < offset; i++)
                if (IsSignificant(original[i])) before++;

            if (IsSignificant(original[offset])) return IndexOfSignificant(formatted, before + 1, formatted.Length);

            int afterPrevious = before == 0 ? 0 : IndexOfSignificant(formatted, before, formatted.Length) + 1;
            bool anythingAfter = false;
            for (int i = offset; i < original.Length && !anythingAfter; i++)
                anythingAfter = IsSignificant(original[i]);
            if (anythingAfter) return afterPrevious;
            // Only whitespace after the caret: keep its distance from the end, but not before the last character.
            return Math.Max(afterPrevious, formatted.Length - (original.Length - offset));
        }

        /// <summary>Commas are left out: they may move to the other end of a line, so counting them would skew the caret.</summary>
        private static bool IsSignificant(char c) => !char.IsWhiteSpace(c) && c != ',';

        /// <summary>The index of the <paramref name="number"/>-th significant character (1-based), or <paramref name="fallback"/>.</summary>
        private static int IndexOfSignificant(string text, int number, int fallback)
        {
            int seen = 0;
            for (int i = 0; i < text.Length; i++)
                if (IsSignificant(text[i]) && ++seen == number) return i;
            return fallback;
        }

        private static FormatResult Fail(string original, string error) => new FormatResult(original, error, false);

        private static string DetectNewline(string text)
        {
            if (text.Contains("\r\n")) return "\r\n";
            if (text.Contains("\n")) return "\n";
            if (text.Contains("\r")) return "\r";
            return "\n";
        }

        private static List<FToken> Prepare(ScanResult scan, KeywordCase keywordCase, bool[] keywords)
        {
            var result = new List<FToken>(scan.Tokens.Count);
            for (int i = 0; i < scan.Tokens.Count; i++)
            {
                var raw = scan.Tokens[i];
                int gapStart = i == 0 ? 0 : scan.Tokens[i - 1].End;
                var token = new FToken { Raw = raw, Text = raw.Text };

                int lineStart = -1, breaks = 0;
                for (int c = gapStart; c < raw.Start; c++)
                {
                    if (scan.Text[c] == '\r' && c + 1 < raw.Start && scan.Text[c + 1] == '\n') continue;
                    if (scan.Text[c] == '\n' || scan.Text[c] == '\r') { breaks++; lineStart = c + 1; }
                }
                token.NewlinesBefore = Math.Min(breaks, 2);
                token.SpaceBefore = raw.Start > gapStart;
                if (raw.Kind == TokenKind.Comment)
                {
                    if (raw.Text.StartsWith("--", StringComparison.Ordinal)) token.Text = raw.Text.TrimEnd();
                    else if (i == 0 || breaks > 0) token.OriginalIndent = raw.Start - (lineStart >= 0 ? lineStart : 0);
                }
                if (keywords[i])
                {
                    if (keywordCase == KeywordCase.Upper) token.Text = raw.Text.ToUpperInvariant();
                    else if (keywordCase == KeywordCase.Lower) token.Text = raw.Text.ToLowerInvariant();
                }
                result.Add(token);
            }
            return result;
        }

        /// <summary>Null when the formatted text has the same tokens as the original (keywords compared without case) and the same
        /// comments (compared without whitespace); otherwise what differs. Commas are counted but not placed, as they may move.</summary>
        private static string Compare(ScanResult original, bool[] keywords, ScanResult formatted)
        {
            var before = Significant(original, keywords, out var commentsBefore, out int commasBefore);
            var after = Significant(formatted, null, out var commentsAfter, out int commasAfter);
            // A line holding only "go" is a batch separator to SSMS, so a layout that leaves one alone on a line (a column called
            // go, say) would change what the script does.
            if (original.Batches.Count != formatted.Batches.Count ||
                original.Batches.Select(b => b.HasGo).SequenceEqual(formatted.Batches.Select(b => b.HasGo)) == false)
                return "การแบ่ง batch ด้วย GO ต่างไป";
            if (commasBefore != commasAfter) return "จำนวนเครื่องหมาย , ไม่เท่ากัน";
            if (before.Count != after.Count) return "จำนวน token ไม่เท่ากัน";
            for (int i = 0; i < before.Count; i++)
            {
                var comparison = before[i].keyword ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;
                if (before[i].kind != after[i].kind || !string.Equals(before[i].text, after[i].text, comparison))
                    return "'" + before[i].text + "' เป็น '" + after[i].text + "'";
            }
            if (commentsBefore.Count != commentsAfter.Count) return "จำนวน comment ไม่เท่ากัน";
            for (int i = 0; i < commentsBefore.Count; i++)
                if (commentsBefore[i] != commentsAfter[i]) return "comment ถูกเปลี่ยน";
            return null;
        }

        private static List<(TokenKind kind, string text, bool keyword)> Significant(ScanResult scan, bool[] keywords, out List<string> comments, out int commas)
        {
            var list = new List<(TokenKind, string, bool)>();
            comments = new List<string>();
            commas = 0;
            for (int i = 0; i < scan.Tokens.Count; i++)
            {
                var t = scan.Tokens[i];
                if (t.Kind == TokenKind.Comment) comments.Add(new string(t.Text.Where(c => !char.IsWhiteSpace(c)).ToArray()));
                else if (t.IsSymbol(',')) commas++;
                else list.Add((t.Kind, t.Text, keywords != null && keywords[i]));
            }
            return list;
        }
    }
}
