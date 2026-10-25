using System.Linq;
using SqlAssist.Core.Completion;
using SqlAssist.Core.Schema;
using SqlAssist.Core.Sql;
using Xunit;

namespace SqlAssist.Core.Tests
{
    public class TvfColumnTests
    {
        private static readonly SchemaModel Schema = new SchemaModel(
            new[] { TestSchema.Customers, TestSchema.Orders },
            new ForeignKey[0],
            new[]
            {
                new DbRoutine("dbo", "fnOrders", RoutineKind.TableFunction, new[] { new DbParameter("@cust", "int") },
                    columns: new[] { new DbColumn("OrderID", "int"), new DbColumn("Total", "money") }),
                new DbRoutine("dbo", "fnScalar", RoutineKind.ScalarFunction, new DbParameter[0], "int"),
            });

        private static TableRef RefNamed(string sql, string alias)
        {
            // Caret after a space: a word right at the caret is still being typed and not yet an alias.
            var ctx = SqlContextAnalyzer.Analyze(sql + " ", sql.Length + 1);
            return ctx.TablesInScope.Single(t => t.Alias == alias);
        }

        [Fact]
        public void FunctionRefHasColumns()
        {
            var ctx = SqlContextAnalyzer.Analyze("SELECT f. FROM dbo.fnOrders(1) f", 9);
            var r = ctx.TablesInScope.Single(t => t.Alias == "f");
            Assert.True(r.IsFunction);
            Assert.True(r.HasColumns);
            Assert.Equal(new[] { "OrderID", "Total" }, Schema.Find(r).Columns.Select(c => c.Name));
        }

        [Fact]
        public void StarExpandsFunctionColumns()
        {
            const string sql = "SELECT * FROM dbo.fnOrders(1) f";
            var star = SqlContextAnalyzer.FindStar(sql, 8);
            Assert.Contains("f.Total", StarExpander.Expand(star, Schema));
        }

        [Fact]
        public void CrossApplyFunctionHasColumns() =>
            Assert.NotNull(Schema.Find(RefNamed("SELECT * FROM Orders o CROSS APPLY dbo.fnOrders(o.CustomerID) f", "f")));

        [Fact]
        public void ScalarFunctionHasNoTable() => Assert.Null(Schema.FindFunctionTable("dbo", "fnScalar"));

        [Fact]
        public void FunctionTableIsTheSameInstanceEveryTime() =>
            Assert.Same(Schema.FindFunctionTable("dbo", "fnOrders"), Schema.FindFunctionTable(null, "fnOrders"));

        [Fact]
        public void FunctionDetailsSayReturnsTable()
        {
            var d = TableDetails.Build(Schema, Schema.FindFunctionTable("dbo", "fnOrders"));
            Assert.Equal("Function", d.Kind);
            Assert.Equal("TABLE", d.Returns);
        }

        [Fact]
        public void HoverOnFunctionName()
        {
            const string sql = "SELECT * FROM dbo.fnOrders(1) f";
            var h = SqlContextAnalyzer.FindHoverTarget(sql, sql.IndexOf("fnOrders") + 2);
            Assert.True(h.Table.IsFunction);
            Assert.Equal("dbo.fnOrders", sql.Substring(h.Start, h.End - h.Start));
        }

        [Fact]
        public void StarSkipsUnknownFunction_AsBeforeFunctionsHadColumns()
        {
            const string sql = "SELECT * FROM Orders o CROSS APPLY STRING_SPLIT(o.Note, ',') s";
            var star = SqlContextAnalyzer.FindStar(sql, 8);
            Assert.Equal("o.OrderID, o.CustomerID, o.OrderDate", StarExpander.Expand(star, Schema));
        }

        [Fact]
        public void BuiltInFunctionResolvesToNull()
        {
            var r = RefNamed("SELECT * FROM STRING_SPLIT(@s, ',') s", "s");
            Assert.True(r.HasColumns);
            Assert.Null(Schema.Find(r));
        }
    }
}
