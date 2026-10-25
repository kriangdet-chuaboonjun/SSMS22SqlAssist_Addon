using System.Linq;
using SqlAssist.Core.Search;
using Xunit;

namespace SqlAssist.Core.Tests.Search
{
    public class SearchMatcherTests
    {
        private static SearchMatcher M(string text, bool matchCase = false, bool wholeWord = false, bool wildcards = false,
            SearchObjectType types = SearchObjectType.All) =>
            SearchMatcher.Create(new SearchQuery { Text = text, MatchCase = matchCase, WholeWord = wholeWord, UseWildcards = wildcards, Types = types });

        private static string[] Hits(SearchMatcher m, string text) =>
            m.FindAll(text).Select(s => text.Substring(s.Start, s.Length)).ToArray();

        [Fact]
        public void Contains_CaseInsensitiveByDefault() =>
            Assert.Equal(new[] { "Order", "order" }, Hits(M("order"), "OrderId, order_date"));

        [Fact]
        public void MatchCase_OnlyExactCase() =>
            Assert.Equal(new[] { "order" }, Hits(M("order", matchCase: true), "OrderId, order_date"));

        [Theory]
        [InlineData("OrderId")]
        [InlineData("Order_Line")]
        [InlineData("@Order")]
        [InlineData("#Order")]
        [InlineData("dbo$Order")]
        public void WholeWord_RejectsIdentifierNeighbours(string text) =>
            Assert.Empty(M("Order", wholeWord: true).FindAll(text));

        [Theory]
        [InlineData("[Order]")]
        [InlineData("dbo.Order")]
        [InlineData("FROM Order WHERE")]
        [InlineData("Order")]
        public void WholeWord_AcceptsSeparators(string text) =>
            Assert.Single(M("Order", wholeWord: true).FindAll(text));

        [Fact]
        public void WholeWord_PatternWithBrackets() =>
            Assert.Equal(new[] { "[Order]" }, Hits(M("[Order]", wholeWord: true), "SELECT * FROM [Order] o"));

        [Fact]
        public void Wildcards_StarAndQuestion()
        {
            Assert.Equal(new[] { "OrderId" }, Hits(M("Ord*Id", wildcards: true), "OrderId"));
            Assert.Equal(new[] { "Cust1" }, Hits(M("Cust?", wildcards: true), "Cust1"));
            Assert.Empty(M("Cust?", wildcards: true).FindAll("Cust"));
        }

        [Fact]
        public void Wildcards_StarStaysOnOneLine() =>
            Assert.Empty(M("SELECT*FROM", wildcards: true).FindAll("SELECT a\r\nFROM t"));

        [Fact]
        public void Wildcards_OffMeansLiteral()
        {
            Assert.Empty(M("Ord*Id").FindAll("OrderId"));
            Assert.Single(M("a*b").FindAll("x a*b y"));
        }

        [Fact]
        public void Wildcards_WholeWordLimitsToIdentifier() =>
            Assert.Equal(new[] { "OrderId", "Orders" }, Hits(M("Order*", wholeWord: true, wildcards: true), "OrderId Orders"));

        [Fact]
        public void TypeFilterExcludesIndexes() =>
            Assert.Null(M("IX_Orders", types: SearchObjectType.Table).Match(new SearchItem("db", "dbo", "IX_Orders", SearchObjectType.Index, "Orders")));

        [Fact]
        public void AllIncludesNewTypes()
        {
            var m = M("Phone");
            Assert.NotNull(m.Match(new SearchItem("db", "dbo", "Phone", SearchObjectType.UserDefinedType, subKind: "Alias")));
            Assert.NotNull(m.Match(new SearchItem("db", "dbo", "CK_Phone", SearchObjectType.Constraint, "T", subKind: "Check")));
            Assert.NotNull(m.Match(new SearchItem("db", "dbo", "IX_Phone", SearchObjectType.Index, "T")));
        }

        [Theory]
        [InlineData("")]
        [InlineData("a")]
        [InlineData("  a  ")]
        [InlineData("**")]
        public void TooShortOrOnlyWildcards_IsEmpty(string text)
        {
            var m = M(text, wildcards: true);
            Assert.True(m.IsEmpty);
            Assert.Null(m.Match(new SearchItem("db", "dbo", "anything", SearchObjectType.Table)));
        }

        [Fact]
        public void NoTypes_IsEmpty() => Assert.True(M("order", types: SearchObjectType.None).IsEmpty);

        [Fact]
        public void Match_ReportsNameAndDefinitionHits()
        {
            var proc = new SearchItem("db", "dbo", "GetOrders", SearchObjectType.Procedure,
                definition: "CREATE PROCEDURE dbo.GetOrders AS SELECT * FROM dbo.Orders");
            var m = M("orders").Match(proc);
            Assert.Equal(MatchLocation.Name | MatchLocation.Definition, m.Location);
            Assert.Single(m.NameHits);
            Assert.Equal(2, m.DefinitionHits.Count);
        }

        [Fact]
        public void Match_DefinitionOnly()
        {
            var view = new SearchItem("db", "dbo", "vActive", SearchObjectType.View, definition: "SELECT * FROM Customer");
            Assert.Equal(MatchLocation.Definition, M("customer").Match(view).Location);
        }

        [Fact]
        public void Match_ScriptIsNotSearched()
        {
            var table = new SearchItem("db", "dbo", "Orders", SearchObjectType.Table, script: "CREATE TABLE [dbo].[Orders] ([CustomerId] int NOT NULL);");
            Assert.Null(M("customer").Match(table));
            Assert.Equal(table.Script, table.PreviewText);
        }

        [Fact]
        public void Search_FiltersByTypeAndHonoursLimit()
        {
            var items = new[]
            {
                new SearchItem("db", null, "Sales", SearchObjectType.Schema),
                new SearchItem("db", "Sales", "SalesOrder", SearchObjectType.Table),
                new SearchItem("db", "Sales", "SalesId", SearchObjectType.Column, parent: "SalesOrder"),
                new SearchItem("db", "Sales", "SalesView", SearchObjectType.View),
            };
            var tablesAndColumns = M("sales", types: SearchObjectType.Table | SearchObjectType.Column).Search(items);
            Assert.Equal(new[] { "SalesOrder", "SalesId" }, tablesAndColumns.Select(r => r.Item.Name));
            Assert.Equal(2, M("sales").Search(items, limit: 2).Count);
        }
    }
}
