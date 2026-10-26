using System.Linq;
using SqlAssist.Core.Completion;
using SqlAssist.Core.Sql;
using Xunit;

namespace SqlAssist.Core.Tests
{
    public class FuzzyMatcherTests
    {
        [Theory]
        [InlineData("custid", "CustomerID")]
        [InlineData("od", "OrderDetails")]
        [InlineData("cust", "Customers")]
        [InlineData("id", "CustomerID")]
        [InlineData("DETAIL", "OrderDetails")]
        public void Matches(string typed, string name) => Assert.True(FuzzyMatcher.Score(typed, name) > 0);

        [Theory]
        [InlineData("xyz", "Customers")]
        [InlineData("dc", "CustomerID")]   // subsequence must start on the first letter
        [InlineData("customers2", "Customers")]
        public void DoesNotMatch(string typed, string name) => Assert.Equal(0, FuzzyMatcher.Score(typed, name));

        [Fact]
        public void Ranking_ExactPrefixInitialsSubsequence()
        {
            int exact = FuzzyMatcher.Score("orders", "Orders");
            int prefix = FuzzyMatcher.Score("ord", "Orders");
            int initials = FuzzyMatcher.Score("od", "OrderDetails");
            int subseq = FuzzyMatcher.Score("od", "Orders");
            Assert.True(exact > prefix && prefix > initials && initials > subseq);
        }

        [Fact]
        public void ShorterPrefixMatchRanksFirst() =>
            Assert.True(FuzzyMatcher.Score("ord", "Orders") > FuzzyMatcher.Score("ord", "OrderDetails"));

        [Fact]
        public void CompletionKeywordsIncludeBuiltInFunctionsOnce()
        {
            Assert.Contains("REPLACE", SqlKeywords.CompletionKeywords);
            Assert.Equal(SqlKeywords.CompletionKeywords.Length, SqlKeywords.CompletionKeywords.Distinct().Count());
        }
    }

    public class AliasGeneratorTests
    {
        [Theory]
        [InlineData("Customers", "c")]
        [InlineData("OrderDetails", "od")]
        [InlineData("order_details", "od")]
        [InlineData("tbl_order_line", "ol")]
        [InlineData("CustomerID", "ci")]
        [InlineData("#temp", "t")]
        public void Initials(string table, string alias) => Assert.Equal(alias, AliasGenerator.Generate(table, null));

        [Fact]
        public void AvoidsTakenAliases() => Assert.Equal("c3", AliasGenerator.Generate("Customers", new[] { "c", "C2" }));

        [Theory]
        [InlineData("InventoryStock", "is2")]
        [InlineData("OrderNotes", "on2")]
        [InlineData("GiftOrders", "go2")]
        public void AvoidsKeywords(string table, string alias) => Assert.Equal(alias, AliasGenerator.Generate(table, null));
    }

    public class StarExpanderTests
    {
        private static string Expand(string sql)
        {
            int caret = sql.IndexOf('|');
            var star = SqlContextAnalyzer.FindStar(sql.Remove(caret, 1), caret);
            return star == null ? null : StarExpander.Expand(star, TestSchema.Build());
        }

        [Fact]
        public void SingleTableWithoutAlias_BareNames() =>
            Assert.Equal("CustomerID, Name, Email", Expand("SELECT *| FROM Customers"));

        [Fact]
        public void AliasedTables_Qualified() =>
            Assert.Equal("c.CustomerID, c.Name, c.Email, o.OrderID, o.CustomerID, o.OrderDate",
                Expand("SELECT *| FROM Customers c JOIN Orders o ON o.CustomerID = c.CustomerID"));

        [Fact]
        public void QualifiedStar_OnlyThatTable()
        {
            const string sql = "SELECT o.*| FROM Customers c JOIN Orders o ON o.CustomerID = c.CustomerID";
            int caret = sql.IndexOf('|');
            var star = SqlContextAnalyzer.FindStar(sql.Remove(caret, 1), caret);
            Assert.Equal(7, star.ReplaceStart);
            Assert.Equal("o.OrderID, o.CustomerID, o.OrderDate", StarExpander.Expand(star, TestSchema.Build()));
        }

        [Fact]
        public void TopAndDistinct() =>
            Assert.Equal("CustomerID, Name, Email", Expand("SELECT DISTINCT TOP (10) *| FROM Customers"));

        [Fact]
        public void NamesNeedingBracketsAreBracketed() =>
            Assert.EndsWith("s.[Ongoing Qty]", Expand("SELECT *| FROM Shipments s"));

        [Theory]
        [InlineData("SELECT COUNT(*|) FROM Customers")]
        [InlineData("SELECT a *| FROM Customers")]
        [InlineData("SELECT '*|' FROM Customers")]
        [InlineData("SELECT *| FROM Unknown")]
        public void NotASelectStar(string sql) => Assert.Null(Expand(sql));

        private static string ExpandLines(string sql)
        {
            int caret = sql.IndexOf('|');
            string text = sql.Remove(caret, 1);
            var star = SqlContextAnalyzer.FindStar(text, caret);
            int lineStart = text.LastIndexOf('\n', star.ReplaceStart - 1) + 1;
            return StarExpander.Expand(star, TestSchema.Build(), text.Substring(lineStart, star.ReplaceStart - lineStart), "\n");
        }

        [Fact]
        public void LeadingCommaLayout_CommasOneColumnLeftOfNames() =>
            Assert.Equal(
                "c.CustomerID\n" +
                "      ,c.Name\n" +
                "      ,c.Email",
                ExpandLines("SELECT *| FROM Customers c"));

        [Fact]
        public void LeadingCommaLayout_FollowsIndentedLine() =>
            Assert.Equal(
                "CustomerID\n" +
                "\t      ,Name\n" +
                "\t      ,Email",
                ExpandLines("SELECT 1\n\tSELECT *| FROM Customers"));

        [Fact]
        public void LeadingCommaLayout_QualifiedStarReplacesFromQualifier() =>
            Assert.Equal(
                "o.OrderID\n" +
                "      ,o.CustomerID\n" +
                "      ,o.OrderDate",
                ExpandLines("SELECT o.*| FROM Orders o"));

        [Fact]
        public void StarInSubqueryUsesSubqueryTables() =>
            Assert.Equal("OrderID, CustomerID, OrderDate",
                Expand("SELECT * FROM Customers c WHERE EXISTS (SELECT *| FROM Orders)"));

        [Fact]
        public void UnionHalvesAreSeparate() =>
            Assert.Equal("OrderID, CustomerID, OrderDate",
                Expand("SELECT * FROM Customers UNION ALL SELECT *| FROM Orders"));
    }

    public class JoinSuggesterTests
    {
        private static string[] Suggest(string sql)
        {
            int caret = sql.IndexOf('|');
            var ctx = SqlContextAnalyzer.Analyze(sql.Remove(caret, 1), caret);
            return JoinSuggester.Suggest(TestSchema.Build(), ctx.SiblingTables, ctx.AliasesInStatement).Select(s => s.Text).ToArray();
        }

        [Fact]
        public void ChildTableFromParent() =>
            Assert.Contains("Orders o ON o.CustomerID = c.CustomerID", Suggest("SELECT * FROM Customers c JOIN |"));

        [Fact]
        public void ParentAndChildFromMiddleTable()
        {
            var s = Suggest("SELECT * FROM Orders o JOIN |");
            Assert.Contains("Customers c ON c.CustomerID = o.CustomerID", s);
            Assert.Contains("OrderDetails od ON od.OrderID = o.OrderID", s);
        }

        [Fact]
        public void NonDboSchemaIsQualified() =>
            Assert.Contains("sales.Products p ON p.ProductID = od.ProductID", Suggest("SELECT * FROM OrderDetails od JOIN |"));

        [Fact]
        public void CompositeKeyJoinsWithAnd() =>
            // LINENO is reserved in T-SQL, so it must be bracketed.
            Assert.Contains("Shipments s ON s.OrderID = od.OrderID AND s.[LineNo] = od.[LineNo]", Suggest("SELECT * FROM OrderDetails od JOIN |"));

        [Fact]
        public void NewAliasAvoidsAliasesInUse() =>
            Assert.Contains("Orders o2 ON o2.CustomerID = c.CustomerID", Suggest("SELECT * FROM Customers c JOIN Things o ON 1=1 JOIN |"));

        [Fact]
        public void SelfReference()
        {
            var s = Suggest("SELECT * FROM Employees e JOIN |");
            Assert.Contains("Employees e2 ON e2.EmployeeID = e.ManagerID", s);
            Assert.Contains("Employees e2 ON e2.ManagerID = e.EmployeeID", s);
        }

        [Fact]
        public void UnaliasedTableIsReferencedByName() =>
            Assert.Contains("Orders o ON o.CustomerID = Customers.CustomerID", Suggest("SELECT * FROM Customers JOIN |"));
    }
}
