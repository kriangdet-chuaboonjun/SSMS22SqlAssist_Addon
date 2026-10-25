using System.Linq;
using SqlAssist.Core.Schema;
using SqlAssist.Core.Sql;
using Xunit;

namespace SqlAssist.Core.Tests
{
    /// <summary>Which table a hovered column belongs to, and when the schema is too incomplete to tell.</summary>
    public class ColumnOwnerTests
    {
        private static readonly SchemaModel Schema = TestSchema.Build();

        private static HoverTarget Hover(string sql)
        {
            int pos = sql.IndexOf('|');
            return SqlContextAnalyzer.FindHoverTarget(sql.Remove(pos, 1), pos);
        }

        private static string OwnerOf(string sql)
        {
            var h = Hover(sql);
            var found = ColumnInfo.FindOwner(Schema, h.Table, h.Scope, h.Column);
            return found?.owner.Name;
        }

        [Fact]
        public void QualifiedColumn() => Assert.Equal("Orders", OwnerOf("SELECT o.Order|Date FROM Orders o"));

        [Fact]
        public void UnqualifiedColumnInOneTable() => Assert.Equal("Customers", OwnerOf("SELECT Em|ail FROM Orders o JOIN Customers c ON c.CustomerID = o.CustomerID"));

        [Fact]
        public void UnqualifiedColumnInTwoTables_NoGuess() => Assert.Null(OwnerOf("SELECT Customer|ID FROM Orders o JOIN Customers c ON c.CustomerID = o.CustomerID"));

        // #ids is not in the schema and may well have the column: naming Orders would be a guess.
        [Fact]
        public void UnknownTableInScope_NoGuess() => Assert.Null(OwnerOf("SELECT OrderI|D FROM Orders o JOIN #ids i ON i.Id = o.OrderID"));

        [Fact]
        public void UnknownColumn() => Assert.Null(OwnerOf("SELECT o.No|pe FROM Orders o"));

        [Fact]
        public void HasUnknownTables_IgnoresFunctions()
        {
            var tables = SqlContextAnalyzer.Analyze("SELECT * FROM Orders o CROSS APPLY STRING_SPLIT(o.Note, ',') s ", 61).TablesInScope;
            Assert.False(Schema.HasUnknownTables(tables));
        }

        [Fact]
        public void HasUnknownTables_NewTable() =>
            Assert.True(Schema.HasUnknownTables(SqlContextAnalyzer.Analyze("SELECT * FROM NewTable n ", 25).TablesInScope));

        [Fact]
        public void HasUnknownTables_OldStyleNolockHintIsNotAnUnknownTable() =>
            Assert.False(Schema.HasUnknownTables(SqlContextAnalyzer.Analyze("SELECT * FROM Orders (NOLOCK) ", 30).TablesInScope));
    }
}
