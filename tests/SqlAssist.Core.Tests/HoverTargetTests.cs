using System.Linq;
using SqlAssist.Core.Sql;
using Xunit;

namespace SqlAssist.Core.Tests
{
    public class HoverTargetTests
    {
        // "|" marks the mouse position; returns (table name, hovered text) or null.
        private static (string table, string span)? Hover(string sql)
        {
            int pos = sql.IndexOf('|');
            string text = sql.Remove(pos, 1);
            var h = SqlContextAnalyzer.FindHoverTarget(text, pos);
            return h == null ? ((string, string)?)null : (h.Table.Name, text.Substring(h.Start, h.End - h.Start));
        }

        private const string Query = "SELECT wr.ReqId FROM Work_Request wr\nJOIN Work_InitialApproval wia ON wr.ReqId = wia.ReqId";

        [Fact]
        public void TableNameInJoin() =>
            Assert.Equal(("Work_InitialApproval", "Work_InitialApproval"), Hover(Query.Replace("Work_Initial", "Work_Ini|tial")));

        [Fact]
        public void TableNameWithSchema_SpanCoversWholeName() =>
            Assert.Equal(("Customers", "dbo.Customers"), Hover("SELECT * FROM dbo.Cust|omers c"));

        [Fact]
        public void AliasWhereDeclared() =>
            Assert.Equal(("Work_InitialApproval", "wia"), Hover(Query.Replace("Approval wia", "Approval w|ia")));

        [Fact]
        public void AliasUsedElsewhere() =>
            Assert.Equal(("Work_Request", "wr"), Hover("SELECT w|r.ReqId FROM Work_Request wr"));

        [Fact]
        public void UnaliasedTableNameUsedAsQualifier() =>
            Assert.Equal(("Orders", "Orders"), Hover("SELECT Ord|ers.OrderID FROM Orders"));

        [Theory]
        [InlineData("SEL|ECT 1 FROM Work_Request wr")]
        [InlineData("SELECT 'Work_Req|uest' FROM Work_Request wr")]
        [InlineData("SELECT @v|ar FROM Work_Request wr")]
        [InlineData("SELECT Req|Id")]
        public void NothingToShow(string sql) => Assert.Null(Target(sql));

        private static HoverTarget Target(string sql)
        {
            int pos = sql.IndexOf('|');
            return SqlContextAnalyzer.FindHoverTarget(sql.Remove(pos, 1), pos);
        }

        private static string SpanOf(string sql, HoverTarget h) => sql.Replace("|", "").Substring(h.Start, h.End - h.Start);

        [Theory]
        [InlineData("SELECT * FROM Work_Request w|r")]
        [InlineData("SELECT w|r.ReqId FROM Work_Request wr")]
        [InlineData("SELECT * FROM dbo.Cust|omers c")]
        public void TableCasesAreKindTable(string sql) => Assert.Equal(HoverKind.Table, Target(sql).Kind);

        [Fact]
        public void QualifiedColumn()
        {
            const string sql = "SELECT wr.Req|Id FROM Work_Request wr";
            var h = Target(sql);
            Assert.Equal(HoverKind.Column, h.Kind);
            Assert.Equal("Work_Request", h.Table.Name);
            Assert.Equal("ReqId", h.Column);
            Assert.Equal("ReqId", SpanOf(sql, h));
        }

        [Theory]
        [InlineData("SELECT wr.Code AS Tot|al FROM Work_Request wr")]
        [InlineData("SELECT CAST(wr.Code AS in|t) FROM Work_Request wr")]
        public void NameAfterAsIsNotAColumn(string sql) => Assert.Null(Target(sql));

        [Fact]
        public void UnqualifiedColumn_ScopeHasBothTables()
        {
            var h = Target(Query.Replace("SELECT wr.ReqId", "SELECT Req|Id"));
            Assert.Equal(HoverKind.Column, h.Kind);
            Assert.Null(h.Table);
            Assert.Equal("ReqId", h.Column);
            Assert.Equal(new[] { "Work_Request", "Work_InitialApproval" }, h.Scope.Select(t => t.Name));
        }

        [Fact]
        public void ExecProcedure()
        {
            const string sql = "EXEC dbo.usp_Ge|tOrders 1";
            var h = Target(sql);
            Assert.Equal(HoverKind.Routine, h.Kind);
            Assert.Equal("dbo", h.Schema);
            Assert.Equal("usp_GetOrders", h.Name);
            Assert.Equal("dbo.usp_GetOrders", SpanOf(sql, h));
        }

        [Fact]
        public void ThreePartTableNameKeepsItsDatabase() =>
            Assert.Equal("OtherDb", Target("SELECT * FROM OtherDb.dbo.Ord|ers o").Table.Database);

        [Fact]
        public void TwoPartTableNameHasNoDatabase() => Assert.Null(Target("SELECT * FROM dbo.Ord|ers o").Table.Database);

        [Fact]
        public void ThreePartRoutineKeepsItsDatabase()
        {
            var h = Target("EXEC OtherDb.dbo.usp_|X");
            Assert.Equal("OtherDb", h.Database);
            Assert.Equal("dbo", h.Schema);
        }

        [Fact]
        public void ExecWithReturnValue() => Assert.Equal("usp_X", Target("EXEC @rc = usp_|X").Name);

        [Fact]
        public void ScalarFunctionCall()
        {
            var h = Target("SELECT dbo.fnTa|x(1) FROM Work_Request wr");
            Assert.Equal(HoverKind.Routine, h.Kind);
            Assert.Equal("fnTax", h.Name);
            Assert.Equal("dbo", h.Schema);
        }

        // Core does not know built-in function names; the editor finds no such routine and shows nothing.
        [Fact]
        public void BuiltInFunctionIsAnUnresolvedRoutine() => Assert.Equal(HoverKind.Routine, Target("SELECT GETD|ATE()").Kind);

        [Fact]
        public void DerivedTableAlias_ShowsItsColumns()
        {
            const string sql = "SELECT * FROM (SELECT 1 a) dt";
            var h = SqlContextAnalyzer.FindHoverTarget(sql, sql.Length - 1);
            Assert.NotNull(h.Table.Derived);
            Assert.Equal("dt", sql.Substring(h.Start, h.End - h.Start));
        }
    }
}
