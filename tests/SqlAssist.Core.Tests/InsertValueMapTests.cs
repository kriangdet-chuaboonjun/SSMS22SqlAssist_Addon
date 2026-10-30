using System.Diagnostics;
using System.Linq;
using System.Text;
using SqlAssist.Core.Sql;
using Xunit;

namespace SqlAssist.Core.Tests
{
    public class InsertValueMapTests
    {
        private const string Two =
            "INSERT INTO dbo.Table1 (Id, Code, Name, CreatedAt)\n" +
            "VALUES\n" +
            "    (1, N'A01', N'สมชาย', CONVERT(datetime, '2026-10-30', 121)),\n" +
            "    (2, N'A02', N'สมศรี', GETDATE());";

        /// <summary>The caret is written as '|'. Returns "Column=value;value" or null.</summary>
        private static string Highlight(string sqlWithCaret)
        {
            int caret = sqlWithCaret.IndexOf('|');
            string sql = sqlWithCaret.Remove(caret, 1);
            var map = InsertValueMap.Find(sql, caret);
            var h = map?.At(caret);
            if (h == null) return null;
            return sql.Substring(h.Column.Start, h.Column.Length) + "=" +
                   string.Join(";", h.Values.Select(v => sql.Substring(v.Start, v.Length)));
        }

        private static string Caret(string sql, string marker, int offsetInMarker = 0, int occurrence = 0)
        {
            int at = -1;
            for (int i = 0; i <= occurrence; i++) at = sql.IndexOf(marker, at + 1, System.StringComparison.Ordinal);
            Assert.True(at >= 0, "marker not found: " + marker);
            return sql.Insert(at + offsetInMarker, "|");
        }

        [Theory]
        [InlineData("(1", 1, "Id=1")]
        [InlineData("N'A01'", 3, "Code=N'A01'")]
        [InlineData("N'สมชาย'", 4, "Name=N'สมชาย'")]
        [InlineData("CONVERT(", 3, "CreatedAt=CONVERT(datetime, '2026-10-30', 121)")]
        [InlineData("datetime, '2026", 2, "CreatedAt=CONVERT(datetime, '2026-10-30', 121)")] // inside the function's own commas
        [InlineData("'2026-10-30'", 6, "CreatedAt=CONVERT(datetime, '2026-10-30', 121)")]
        [InlineData("121)),", 3, "CreatedAt=CONVERT(datetime, '2026-10-30', 121)")]
        public void FirstRow_ValueGivesItsColumn(string marker, int offset, string expected) =>
            Assert.Equal(expected, Highlight(Caret(Two, marker, offset)));

        [Theory]
        [InlineData("(2", 1, "Id=2")]
        [InlineData("N'A02'", 2, "Code=N'A02'")]
        [InlineData("N'สมศรี'", 5, "Name=N'สมศรี'")]
        [InlineData("GETDATE()", 3, "CreatedAt=GETDATE()")]
        [InlineData("GETDATE()", 8, "CreatedAt=GETDATE()")] // between the parentheses
        public void SecondRow_UsesTheRowTheCaretIsIn(string marker, int offset, string expected) =>
            Assert.Equal(expected, Highlight(Caret(Two, marker, offset)));

        [Theory]
        [InlineData("Id,", 1, "Id=1;2")]
        [InlineData("Id,", 2, "Id=1;2")] // right after the name
        [InlineData("Code,", 2, "Code=N'A01';N'A02'")]
        [InlineData("Name,", 0, "Name=N'สมชาย';N'สมศรี'")]
        [InlineData("CreatedAt)", 4, "CreatedAt=CONVERT(datetime, '2026-10-30', 121);GETDATE()")]
        public void ColumnNameGivesTheValueInEachRow(string marker, int offset, string expected) =>
            Assert.Equal(expected, Highlight(Caret(Two, marker, offset)));

        [Theory]
        [InlineData("INSERT")]
        [InlineData("VALUES")]
        public void KeywordsHighlightNothing(string marker) => Assert.Null(Highlight(Caret(Two, marker, 2)));

        [Fact]
        public void CaretAfterTheStatementHighlightsNothing() => Assert.Null(Highlight(Two + "\nSELECT 1|"));

        [Fact]
        public void WhiteSpaceInASlotBelongsToThatValue() =>
            Assert.Equal("Code=N'A01'", Highlight("INSERT INTO t (Id, Code) VALUES (1,|   N'A01')"));

        [Fact]
        public void CaretRightBeforeACommaStaysWithTheValueBeforeIt() =>
            Assert.Equal("Id=1", Highlight("INSERT INTO t (Id, Code) VALUES (1|, 2)"));

        [Fact]
        public void CaretRightAfterACommaIsTheNextValue() =>
            Assert.Equal("Code=2", Highlight("INSERT INTO t (Id, Code) VALUES (1,| 2)"));

        [Fact]
        public void AnEmptySlotStillNamesItsColumnButMarksNoValue()
        {
            const string sql = "INSERT INTO t (Id, Code) VALUES (1, )";
            int caret = sql.IndexOf(')', sql.IndexOf("VALUES")) ;
            var h = InsertValueMap.Find(sql, caret).At(caret);
            Assert.Equal("Code", sql.Substring(h.Column.Start, h.Column.Length));
            Assert.Empty(h.Values);
        }

        [Theory]
        [InlineData("INSERT INTO t (a, b, c) VALUES (N'x,|y', 2, 3)", "a=N'x,y'")]
        [InlineData("INSERT INTO t (a, b) VALUES (N'O''Brien, Pat|', 2)", "a=N'O''Brien, Pat'")]
        [InlineData("INSERT INTO t (a, b) VALUES (N'O''Brien', 2|)", "b=2")]
        [InlineData("INSERT INTO t (a, b) VALUES ('it''s', |2)", "b=2")]
        [InlineData("INSERT INTO t (a, b) VALUES (COALESCE(x, y, z|), 2)", "a=COALESCE(x, y, z)")]
        [InlineData("INSERT INTO t (a, b) VALUES (1, (SELECT TOP 1 x, y| FROM q WHERE z IN (1, 2)))", "b=(SELECT TOP 1 x, y FROM q WHERE z IN (1, 2))")]
        [InlineData("INSERT INTO t (a, b) VALUES (CASE WHEN x = 1 THEN 'a' ELSE 'b|' END, 2)", "a=CASE WHEN x = 1 THEN 'a' ELSE 'b' END")]
        [InlineData("INSERT INTO t (a, b, c) VALUES (NULL, DEFAULT|, 3)", "b=DEFAULT")]
        [InlineData("INSERT INTO t (a, b) VALUES (-1.5|, 0x1F)", "a=-1.5")]
        [InlineData("INSERT INTO t (a, b) VALUES (@x + @y|, [col,x])", "a=@x + @y")]
        public void CommasInsideValuesDoNotSplitThem(string sqlWithCaret, string expected) =>
            Assert.Equal(expected, Highlight(sqlWithCaret));

        [Fact]
        public void BracketedAndQuotedColumnsAndMultiPartTarget()
        {
            const string sql = "INSERT [dbo].[My Table] ([Order Id], \"Qty,x\", [Sel]]ect])\nVALUES (1, 2, 3)";
            int caret = sql.IndexOf("Qty") + 1;
            var h = InsertValueMap.Find(sql, caret).At(caret);
            Assert.Equal("\"Qty,x\"", sql.Substring(h.Column.Start, h.Column.Length));
            Assert.Equal("2", sql.Substring(h.Values[0].Start, h.Values[0].Length));
        }

        [Fact]
        public void CommentsBetweenItemsAreSkippedAndAreNotHighlighted()
        {
            const string sql =
                "INSERT INTO t (a, -- first, second\n" +
                "   /* x, y */ b)\n" +
                "VALUES (1, -- note, with comma\n" +
                "   2 /* tail, */)";
            Assert.Equal("b=2", Highlight(sql.Insert(sql.LastIndexOf("2 /*"), "|")));
            Assert.Equal("a=1", Highlight(sql.Insert(sql.IndexOf("1,"), "|")));
            Assert.Null(Highlight(sql.Insert(sql.IndexOf("with comma") + 2, "|")));   // in a comment
            Assert.Null(Highlight(sql.Insert(sql.IndexOf("x, y") + 1, "|")));          // in a comment
            Assert.Equal("a=1", Highlight(sql.Insert(sql.IndexOf("a,") + 1, "|")));
        }

        [Fact]
        public void MultiLineListsAndRows()
        {
            const string sql =
                "INSERT INTO dbo.T\n(\n    Id,\n    Code\n)\nVALUES\n(\n    1,\n    N'a'\n),\n(\n    2,\n    N'b'\n);";
            Assert.Equal("Code=N'b'", Highlight(Caret(sql, "N'b'", 1)));
            Assert.Equal("Id=1;2", Highlight(Caret(sql, "Id,", 1)));
        }

        [Fact]
        public void OnlyTheInsertTheCaretIsInIsUsed()
        {
            const string sql =
                "INSERT INTO a (x, y) VALUES (1, 2);\n" +
                "SELECT 1;\n" +
                "INSERT INTO b (p, q) VALUES (3, 4), (5, 6);\n" +
                "INSERT INTO c (m) VALUES (7)";
            Assert.Equal("y=2", Highlight(Caret(sql, "2)")));
            Assert.Equal("q=4;6", Highlight(Caret(sql, "q)", 0)));
            Assert.Equal("p=5", Highlight(Caret(sql, "5")));
            Assert.Equal("m=7", Highlight(Caret(sql, "7)")));
            Assert.Null(Highlight(Caret(sql, "SELECT", 2)));
            // A column of the first INSERT does not pull values from the others.
            Assert.Equal("x=1", Highlight(Caret(sql, "x,", 0)));
        }

        [Fact]
        public void ColumnsAndValuesOfDifferentLengthOnlyPairWhatMatches()
        {
            // Three values for two columns: the third has nothing to pair with.
            Assert.Equal("b=2", Highlight("INSERT INTO t (a, b) VALUES (1, 2|, 3)"));
            Assert.Null(Highlight("INSERT INTO t (a, b) VALUES (1, 2, 3|)"));
            // Two values for three columns: the column with no value finds none.
            Assert.Equal("c=3", Highlight("INSERT INTO t (a, b, c) VALUES (1, 2), (3, 4, 3|)"));
            Assert.Equal("c=3", Highlight("INSERT INTO t (a, b, c) VALUES (1, 2), (3, 4, 3)".Insert("INSERT INTO t (a, b, c".Length - 1, "|")));
            Assert.Null(Highlight("INSERT INTO t (a, b, c|) VALUES (1, 2)"));
        }

        [Theory]
        [InlineData("INSERT INTO t (a, b) VALUES (1, N'x|", "b=N'x")]            // string still open
        [InlineData("INSERT INTO t (a, b) VALUES (1, 2|", "b=2")]                 // row not closed
        [InlineData("INSERT INTO t (a, b) VALUES (|", "a=")]                  // slot just opened: the column, no value yet
        [InlineData("INSERT INTO t (a, b) VALUES (1|", "a=1")]
        [InlineData("INSERT INTO t (a, b) VALUES (1, 2), (3|", "a=3")]            // second row just started
        [InlineData("INSERT INTO t (a, b) VALUES (1, CONVERT(int, |", "b=CONVERT(int,")]
        public void StatementsStillBeingTypedKeepWhatIsCertain(string sqlWithCaret, string expected) =>
            Assert.Equal(expected, Highlight(sqlWithCaret));

        [Theory]
        [InlineData("INSERT INTO t (a, b) VALUES|")]
        [InlineData("INSERT INTO t (a, |")]            // column list still open
        [InlineData("INSERT INTO t (a, b|")]
        [InlineData("INSERT INTO t (a, b,|) VALUES (1, 2, 3)")]   // empty entry in the list
        [InlineData("INSERT INTO t (a, b + 1|) VALUES (1, 2)")]   // not a plain name
        [InlineData("INSERT INTO t (a, b) SELECT 1|, 2")]
        [InlineData("INSERT INTO t (a, b) EXEC p 1|, 2")]
        [InlineData("INSERT INTO t VALUES (1|, 2)")]              // no column list
        [InlineData("INSERT INTO t DEFAULT VALUES|")]
        [InlineData("SELECT (1|, 2)")]
        [InlineData("CREATE TRIGGER x ON t AFTER INSERT AS SELECT 1|")]
        public void WhatIsNotUnderstoodHighlightsNothing(string sqlWithCaret) => Assert.Null(Highlight(sqlWithCaret));

        [Fact]
        public void ARowLeftOpenDoesNotSwallowTheNextStatement()
        {
            const string sql = "INSERT INTO t (a, b) VALUES (1, \nSELECT * FROM x WHERE y = 2\n";
            Assert.Null(Highlight(Caret(sql, "FROM", 1)));
            Assert.Null(Highlight(Caret(sql, "y = 2", 1)));
            // The value typed before it is still paired.
            Assert.Equal("a=1", Highlight(Caret(sql, "1,", 1)));
        }

        [Fact]
        public void AnUnclosedParenthesisStopsAtTheNextStatementKeyword()
        {
            const string sql = "INSERT INTO t (a, b) VALUES (1, (2 + \nUPDATE u SET v = 3";
            Assert.Null(Highlight(Caret(sql, "SET v", 2)));
        }

        [Fact]
        public void CaretOnlyInTheStatementItsInsertStartsAfter()
        {
            const string sql = "INSERT INTO t (a, b) VALUES (1, 2);\n-- INSERT INTO q (z) VALUES (9)\n";
            Assert.Null(Highlight(Caret(sql, "9", 0)));
        }

        [Fact]
        public void ParseGivesTheStatementWhateverTheCaret()
        {
            const string sql = "SELECT 1;\nINSERT INTO a (x, y) VALUES (1, 2), (3, 4);\nINSERT INTO b (p) VALUES (5)";
            int first = sql.IndexOf("INSERT INTO a");
            var map = InsertValueMap.Parse(sql, first);
            Assert.NotNull(map);
            Assert.Equal(first, map.StatementStart);
            Assert.Equal(2, map.Rows.Count);
            Assert.Equal(2, map.Columns.Count);
            Assert.Equal("y", sql.Substring(map.At(sql.IndexOf("2)")).Column.Start, 1));
            Assert.Null(map.At(sql.IndexOf("(5)") + 1));     // belongs to the next INSERT
            Assert.Null(InsertValueMap.Parse(sql, first + 1)); // not at an INSERT keyword
            Assert.Null(InsertValueMap.Parse(sql, 0));
        }

        [Fact]
        public void ReadsTheStructureOnceForALargeInsert()
        {
            var sb = new StringBuilder("INSERT INTO dbo.Big (Id, Code, Name, CreatedAt)\nVALUES\n");
            for (int i = 0; i < 1000; i++)
                sb.Append(i == 0 ? "    " : "   ,").Append("(").Append(i).Append(", N'C").Append(i).Append("', N'Name, ").Append(i)
                  .Append("', CONVERT(datetime, '2026-10-30', 121))\n");
            string sql = sb.ToString();
            int caret = sql.IndexOf("N'C777'") + 3;

            var watch = Stopwatch.StartNew();
            var map = InsertValueMap.Find(sql, caret);
            var h = map.At(caret);
            watch.Stop();

            Assert.Equal("Code", sql.Substring(h.Column.Start, h.Column.Length));
            Assert.Equal("N'C777'", sql.Substring(h.Values[0].Start, h.Values[0].Length));
            Assert.Equal(1000, map.Rows.Count);
            Assert.True(watch.ElapsedMilliseconds < 1000, "took " + watch.ElapsedMilliseconds + " ms");
            // Looking up another caret in the same map is a lookup, not a parse.
            watch.Restart();
            for (int i = 0; i < 1000; i++) map.At(caret + i);
            Assert.True(watch.ElapsedMilliseconds < 500, "At took " + watch.ElapsedMilliseconds + " ms for 1000 lookups");
        }
    }
}
