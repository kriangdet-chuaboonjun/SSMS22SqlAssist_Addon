using SqlAssist.Core.Search;
using Xunit;

namespace SqlAssist.Core.Tests.Search
{
    public class ObjectUrnTests
    {
        private const string Srv = "Server[@Name='.\\SQLEXPRESS']";

        [Fact]
        public void ServerQuotesName() => Assert.Equal(Srv, ObjectUrn.Server(".\\SQLEXPRESS"));

        [Fact]
        public void Procedure() =>
            Assert.Equal(new[] { Srv + "/Database[@Name='Shop']/StoredProcedure[@Name='GetOrders' and @Schema='dbo']" },
                ObjectUrn.Candidates(Srv, new SearchItem("Shop", "dbo", "GetOrders", SearchObjectType.Procedure)));

        [Fact]
        public void SchemaAndQuotesDoubled() =>
            Assert.Equal(new[] { Srv + "/Database[@Name='O''Neil']/Schema[@Name='Sales']" },
                ObjectUrn.Candidates(Srv, new SearchItem("O'Neil", null, "Sales", SearchObjectType.Schema)));

        [Fact]
        public void ColumnOfKnownView() =>
            Assert.Equal(new[] { Srv + "/Database[@Name='Shop']/View[@Name='vOrders' and @Schema='dbo']/Column[@Name='Id']" },
                ObjectUrn.Candidates(Srv, new SearchItem("Shop", "dbo", "Id", SearchObjectType.Column, "vOrders", parentType: SearchObjectType.View)));

        [Fact]
        public void TriggerOfUnknownOwner_TriesTableThenView()
        {
            var urns = ObjectUrn.Candidates(Srv, new SearchItem("Shop", "dbo", "trAudit", SearchObjectType.Trigger, "Orders"));
            Assert.Equal(2, urns.Count);
            Assert.EndsWith("/Table[@Name='Orders' and @Schema='dbo']/Trigger[@Name='trAudit']", urns[0]);
            Assert.EndsWith("/View[@Name='Orders' and @Schema='dbo']/Trigger[@Name='trAudit']", urns[1]);
        }

        [Fact]
        public void IndexOfView() =>
            Assert.Equal(new[] { Srv + "/Database[@Name='Shop']/View[@Name='vOrders' and @Schema='dbo']/Index[@Name='IX_v']" },
                ObjectUrn.Candidates(Srv, new SearchItem("Shop", "dbo", "IX_v", SearchObjectType.Index, "vOrders", parentType: SearchObjectType.View)));

        [Fact]
        public void PrimaryKeyIsAnIndexNode() =>
            Assert.EndsWith("/Table[@Name='Orders' and @Schema='dbo']/Index[@Name='PK_Orders']",
                ObjectUrn.Candidates(Srv, new SearchItem("Shop", "dbo", "PK_Orders", SearchObjectType.Constraint, "Orders",
                    parentType: SearchObjectType.Table, subKind: "PrimaryKey"))[0]);

        [Fact]
        public void ForeignKey() =>
            Assert.Equal(new[] { Srv + "/Database[@Name='Shop']/Table[@Name='Orders' and @Schema='dbo']/ForeignKey[@Name='FK_x']" },
                ObjectUrn.Candidates(Srv, new SearchItem("Shop", "dbo", "FK_x", SearchObjectType.Constraint, "Orders",
                    parentType: SearchObjectType.Table, subKind: "ForeignKey")));

        [Fact]
        public void CheckConstraint() =>
            Assert.EndsWith("/Table[@Name='Orders' and @Schema='dbo']/Check[@Name='CK_q']",
                ObjectUrn.Candidates(Srv, new SearchItem("Shop", "dbo", "CK_q", SearchObjectType.Constraint, "Orders",
                    parentType: SearchObjectType.Table, subKind: "Check"))[0]);

        [Fact]
        public void DefaultGoesToTable() =>
            Assert.Equal(new[] { Srv + "/Database[@Name='Shop']/Table[@Name='Orders' and @Schema='dbo']" },
                ObjectUrn.Candidates(Srv, new SearchItem("Shop", "dbo", "DF_x", SearchObjectType.Constraint, "Orders",
                    parentType: SearchObjectType.Table, subKind: "Default")));

        [Fact]
        public void TableType() =>
            Assert.Equal(new[] { Srv + "/Database[@Name='Shop']/UserDefinedTableType[@Name='OrderList' and @Schema='dbo']" },
                ObjectUrn.Candidates(Srv, new SearchItem("Shop", "dbo", "OrderList", SearchObjectType.UserDefinedType, subKind: "Table")));

        [Fact]
        public void AliasType() =>
            Assert.Equal(new[] { Srv + "/Database[@Name='Shop']/UserDefinedDataType[@Name='Phone' and @Schema='dbo']" },
                ObjectUrn.Candidates(Srv, new SearchItem("Shop", "dbo", "Phone", SearchObjectType.UserDefinedType, subKind: "Alias")));

        [Fact]
        public void OwnerStripsLastStep() =>
            Assert.Equal(Srv + "/Database[@Name='Shop']/Table[@Name='a/b]' and @Schema='dbo']",
                ObjectUrn.Owner(Srv + "/Database[@Name='Shop']/Table[@Name='a/b]' and @Schema='dbo']/Column[@Name='x']"));
    }
}
