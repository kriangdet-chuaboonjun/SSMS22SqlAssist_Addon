using SqlAssist.Core.Formatting;
using SqlAssist.Core.Settings;
using Xunit;

namespace SqlAssist.Core.Tests
{
    public class SettingsTests
    {
        [Fact]
        public void DefaultsWhenThereIsNothingSaved()
        {
            foreach (string xml in new[] { null, "", "   ", "not xml at all", "<other/>", "<SqlAssistSettings/>" })
            {
                var settings = SqlAssistSettings.Parse(xml);
                Assert.Equal(KeywordCase.Upper, settings.Format.KeywordCase);
                Assert.Equal(4, settings.Format.IndentSize);
                Assert.Equal(CommaStyle.Leading, settings.Format.CommaStyle);
            }
        }

        [Fact]
        public void RoundTrip()
        {
            var settings = new SqlAssistSettings();
            settings.Format.KeywordCase = KeywordCase.Lower;
            settings.Format.IndentSize = 2;
            settings.Format.CommaStyle = CommaStyle.Trailing;

            var read = SqlAssistSettings.Parse(settings.ToXml());
            Assert.Equal(KeywordCase.Lower, read.Format.KeywordCase);
            Assert.Equal(2, read.Format.IndentSize);
            Assert.Equal(CommaStyle.Trailing, read.Format.CommaStyle);
        }

        [Fact]
        public void BadValuesFallBackOneByOne()
        {
            var read = SqlAssistSettings.Parse("<SqlAssistSettings><Format KeywordCase=\"Shouting\" IndentSize=\"many\" CommaStyle=\"Trailing\"/></SqlAssistSettings>");
            Assert.Equal(KeywordCase.Upper, read.Format.KeywordCase);
            Assert.Equal(4, read.Format.IndentSize);
            Assert.Equal(CommaStyle.Trailing, read.Format.CommaStyle);
        }

        [Theory]
        [InlineData("0", 2)]
        [InlineData("1", 2)]
        [InlineData("3", 3)]
        [InlineData("8", 8)]
        [InlineData("40", 8)]
        [InlineData("-5", 2)]
        public void IndentSizeIsKeptInRange(string value, int expected)
        {
            var read = SqlAssistSettings.Parse("<SqlAssistSettings><Format IndentSize=\"" + value + "\"/></SqlAssistSettings>");
            Assert.Equal(expected, read.Format.IndentSize);
        }

        [Fact]
        public void UnknownPartsAreIgnoredAndTheFileMayBeEdited()
        {
            var read = SqlAssistSettings.Parse("<?xml version=\"1.0\"?>\n<SqlAssistSettings>\n  <Future Option=\"1\"/>\n  <Format commastyle=\"trailing\" keywordcase=\"preserve\"/>\n</SqlAssistSettings>");
            Assert.Equal(CommaStyle.Trailing, read.Format.CommaStyle);
            Assert.Equal(KeywordCase.Preserve, read.Format.KeywordCase);
        }

        [Fact]
        public void TheNewlineIsNotASetting()
        {
            var settings = new SqlAssistSettings();
            settings.Format.Newline = "\r\n";
            Assert.DoesNotContain("Newline", settings.ToXml());
            Assert.Null(SqlAssistSettings.Parse(settings.ToXml()).Format.Newline);
        }

        [Fact]
        public void InsertHighlightIsOnByDefaultAndKeepsWhatWasChosen()
        {
            Assert.True(SqlAssistSettings.Parse(null).Editor.HighlightInsertColumns);
            Assert.True(SqlAssistSettings.Parse("<SqlAssistSettings><Editor/></SqlAssistSettings>").Editor.HighlightInsertColumns);
            Assert.True(SqlAssistSettings.Parse("<SqlAssistSettings><Editor HighlightInsertColumns='maybe'/></SqlAssistSettings>").Editor.HighlightInsertColumns);
            Assert.False(SqlAssistSettings.Parse("<SqlAssistSettings><Editor highlightinsertcolumns='FALSE'/></SqlAssistSettings>").Editor.HighlightInsertColumns);

            var settings = new SqlAssistSettings();
            settings.Editor.HighlightInsertColumns = false;
            Assert.False(SqlAssistSettings.Parse(settings.ToXml()).Editor.HighlightInsertColumns);
            settings.Editor.HighlightInsertColumns = true;
            Assert.True(SqlAssistSettings.Parse(settings.ToXml()).Editor.HighlightInsertColumns);
        }

        [Fact]
        public void RiskCheckDefaults()
        {
            var risk = SqlAssistSettings.Parse(null).Risk;
            Assert.True(risk.UpdateWithoutWhere);
            Assert.True(risk.DeleteWithoutWhere);
            Assert.True(risk.TruncateTable);
            Assert.True(risk.DropTable);
            Assert.False(risk.SkipTempTables);
            // Warning before Execute is opt-in: it only does anything once someone has tried it in their SSMS.
            Assert.False(risk.WarnBeforeExecute);
        }

        [Fact]
        public void RiskCheckRoundTrip()
        {
            var settings = new SqlAssistSettings();
            settings.Risk.UpdateWithoutWhere = false;
            settings.Risk.DropTable = false;
            settings.Risk.SkipTempTables = true;
            settings.Risk.WarnBeforeExecute = true;
            settings.Format.IndentSize = 6;

            var read = SqlAssistSettings.Parse(settings.ToXml());
            Assert.False(read.Risk.UpdateWithoutWhere);
            Assert.True(read.Risk.DeleteWithoutWhere);
            Assert.True(read.Risk.TruncateTable);
            Assert.False(read.Risk.DropTable);
            Assert.True(read.Risk.SkipTempTables);
            Assert.True(read.Risk.WarnBeforeExecute);
            Assert.Equal(6, read.Format.IndentSize);
        }

        [Fact]
        public void RiskCheckBadValuesKeepTheirDefaults()
        {
            var read = SqlAssistSettings.Parse("<SqlAssistSettings><RiskCheck DeleteWithoutWhere=\"maybe\" TruncateTable=\"false\" WarnBeforeExecute=\"TRUE\" SkipTempTables=\"1\"/></SqlAssistSettings>");
            Assert.True(read.Risk.DeleteWithoutWhere);
            Assert.False(read.Risk.TruncateTable);
            Assert.True(read.Risk.WarnBeforeExecute);
            Assert.False(read.Risk.SkipTempTables);
        }
    }

    public class FormatCursorTests
    {
        private const string Original = "select a,b from t";
        private const string Formatted = "SELECT\n    a\n  , b\nFROM t";

        [Theory]
        [InlineData(0, 0)]     // before select
        [InlineData(7, 11)]    // before a
        [InlineData(8, 12)]    // at the comma: right after a
        [InlineData(14, 22)]   // inside FROM
        [InlineData(16, 24)]   // before t
        [InlineData(17, 25)]   // end of text
        public void CaretStaysWithTheSameCharacters(int offset, int expected)
        {
            Assert.Equal(expected, SqlFormatter.MapOffset(Original, offset, Formatted));
        }

        [Fact]
        public void CaretInTheMiddleOfAWordFollowsTheWord()
        {
            Assert.Equal(1, SqlFormatter.MapOffset("select", 1, "SELECT"));
            Assert.Equal(3, SqlFormatter.MapOffset("select", 3, "SELECT"));
        }

        [Fact]
        public void CaretAtTheEndStaysAtTheEnd()
        {
            Assert.Equal(6, SqlFormatter.MapOffset("select", 6, "SELECT"));
            Assert.Equal(7, SqlFormatter.MapOffset("select\n", 7, "SELECT\n"));
        }

        [Fact]
        public void CommasMovingDoNotThrowTheCaretOff()
        {
            // "a,\n b" becomes "a\n, b": the caret just before b stays just before b, and one at the comma stays after a.
            Assert.Equal(4, SqlFormatter.MapOffset("a,\n b", 4, "a\n, b"));
            Assert.Equal(1, SqlFormatter.MapOffset("a,\n b", 1, "a\n, b"));
        }

        [Fact]
        public void CaretInTrailingWhitespaceKeepsItsDistanceFromTheEnd()
        {
            // the distance from the end is kept (two before it), but never ends up before the last character
            Assert.Equal(6, SqlFormatter.MapOffset("select  \n", 7, "SELECT\n"));
            // 15 characters, caret 3 from the end; the formatted text has 14, so the caret goes 3 from its end
            Assert.Equal(11, SqlFormatter.MapOffset("select a      \n", 12, "SELECT a\n\n\n\n\n\n"));
        }

        [Fact]
        public void OffsetsOutsideTheTextAreClamped()
        {
            Assert.Equal(0, SqlFormatter.MapOffset("a", -3, "A"));
            Assert.Equal(1, SqlFormatter.MapOffset("a", 99, "A"));
            Assert.Equal(0, SqlFormatter.MapOffset("", 0, ""));
        }
    }
}
