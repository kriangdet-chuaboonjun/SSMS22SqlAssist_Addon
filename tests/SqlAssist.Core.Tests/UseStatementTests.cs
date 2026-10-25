using SqlAssist.Core.Sql;
using Xunit;

namespace SqlAssist.Core.Tests
{
    public class UseStatementTests
    {
        // "|" marks the caret.
        private static string At(string sql)
        {
            int caret = sql.IndexOf('|');
            return SqlUseStatement.LastDatabaseBefore(sql.Remove(caret, 1), caret);
        }

        [Theory]
        [InlineData("USE Sales;\nSELECT * FROM |", "Sales")]
        [InlineData("USE [My Db]\nGO\nSELECT |", "My Db")]
        [InlineData("use \"Quoted\"\nSELECT |", "Quoted")]
        [InlineData("USE A\nUSE B\nSELECT |", "B")]
        [InlineData("SELECT |\nUSE Later", null)]
        [InlineData("-- USE Sales\nSELECT |", null)]
        [InlineData("/* USE Sales */ SELECT |", null)]
        [InlineData("SELECT 'USE Sales' |", null)]
        [InlineData("SELECT 1 OPTION (USE HINT('X'))\nSELECT |", null)]
        [InlineData("SELECT 1 OPTION (MAXDOP 1, USE PLAN N'<x/>')\nSELECT |", null)]
        [InlineData("USE Sal|", null)]
        [InlineData("USE |", null)]
        [InlineData("SELECT |", null)]
        public void LastDatabaseBefore(string sql, string expected) => Assert.Equal(expected, At(sql));
    }
}
