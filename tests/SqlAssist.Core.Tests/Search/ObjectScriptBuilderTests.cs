using SqlAssist.Core.Search;
using Xunit;

namespace SqlAssist.Core.Tests.Search
{
    public class ObjectScriptBuilderTests
    {
        [Fact]
        public void FilteredIndexWithInclude() => Assert.Equal(
            "CREATE UNIQUE NONCLUSTERED INDEX [IX_a] ON [dbo].[T] ([a], [b] DESC)\r\nINCLUDE ([c])\r\nWHERE ([a] IS NOT NULL);",
            ObjectScriptBuilder.Index("dbo", "T", "IX_a", true, false, new[] { ("a", false), ("b", true) }, new[] { "c" }, "([a] IS NOT NULL)"));

        [Fact]
        public void PlainClusteredIndex() => Assert.Equal(
            "CREATE CLUSTERED INDEX [CIX] ON [s].[T]]x] ([a]);",
            ObjectScriptBuilder.Index("s", "T]x", "CIX", false, true, new[] { ("a", false) }, new string[0], null));

        [Fact]
        public void CheckConstraint() => Assert.Equal(
            "ALTER TABLE [dbo].[T] ADD CONSTRAINT [CK_q] CHECK ([Qty]>(0));",
            ObjectScriptBuilder.Constraint("dbo", "T", "CK_q", "CHECK ([Qty]>(0))"));

        [Fact]
        public void AliasType() =>
            Assert.Equal("CREATE TYPE [dbo].[Phone] FROM varchar(20) NOT NULL;", ObjectScriptBuilder.AliasType("dbo", "Phone", "varchar(20)", false));

        [Fact]
        public void NullableAliasType() =>
            Assert.Equal("CREATE TYPE [dbo].[Note] FROM nvarchar(max) NULL;", ObjectScriptBuilder.AliasType("dbo", "Note", "nvarchar(max)", true));

        [Fact]
        public void TableTypeHeader() => Assert.Equal(
            "CREATE TYPE [dbo].[OrderList] AS TABLE (\n    [Id] int NOT NULL\n);",
            TableScriptBuilder.BuildTableType("dbo", "OrderList", new[] { new TableColumn("Id", "int", false) }, null, "\n"));
    }
}
