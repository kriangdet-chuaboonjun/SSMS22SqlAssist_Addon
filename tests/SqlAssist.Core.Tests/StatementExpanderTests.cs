using SqlAssist.Core.Completion;
using SqlAssist.Core.Schema;
using Xunit;

namespace SqlAssist.Core.Tests
{
    public class StatementExpanderTests
    {
        private static readonly DbObject Department = new DbObject("HumanResources", "Department", false, new[]
        {
            new DbColumn("DepartmentID", "smallint", isNullable: false, isIdentity: true, isPrimaryKey: true),
            new DbColumn("Name", "nvarchar(50)", isNullable: false),
            new DbColumn("GroupName", "nvarchar(50)"),
            new DbColumn("ModifiedDate", "datetime", isNullable: false),
            new DbColumn("NameLength", "int", isComputed: true),
            new DbColumn("RowVer", "timestamp", isNullable: false),
        });

        private static readonly DbRoutine UpdatePerson = new DbRoutine("Person", "sp_UpdatePerson", RoutineKind.Procedure, new[]
        {
            new DbParameter("@BusinessEntityID", "int"),
            new DbParameter("@FirstName", "nvarchar(50)"),
            new DbParameter("@Result", "int", isOutput: true),
        });

        private static ExpansionTarget At(string sqlWithCaret)
        {
            int caret = sqlWithCaret.IndexOf('|');
            return StatementExpander.Find(sqlWithCaret.Remove(caret, 1), caret);
        }

        [Theory]
        [InlineData("INSERT INTO HumanResources.Department|", ExpansionKind.Insert, "HumanResources", "Department")]
        [InlineData("insert Department|", ExpansionKind.Insert, null, "Department")]
        [InlineData("UPDATE [dbo].[Customers]|", ExpansionKind.Update, "dbo", "Customers")]
        [InlineData("EXEC Person.sp_UpdatePerson|", ExpansionKind.Exec, "Person", "sp_UpdatePerson")]
        [InlineData("EXECUTE @rc = dbo.usp_Do|", ExpansionKind.Exec, "dbo", "usp_Do")]
        [InlineData("ALTER PROCEDURE dbo.usp_Do|", ExpansionKind.Alter, "dbo", "usp_Do")]
        [InlineData("ALTER VIEW v_Orders|   ", ExpansionKind.Alter, null, "v_Orders")]
        [InlineData("GO\n  EXEC sp_who|", ExpansionKind.Exec, null, "sp_who")]
        [InlineData("INSERT INTO [dbo].[Table 1]|", ExpansionKind.Insert, "dbo", "Table 1")]
        [InlineData("INSERT INTO dbo.T|\n\n(SELECT 1)", ExpansionKind.Insert, "dbo", "T")] // a blank line ends the statement
        [InlineData("UPDATE dbo.T|\nSELECT 1", ExpansionKind.Update, "dbo", "T")]
        [InlineData("INSERT INTO dbo.T|\nGO", ExpansionKind.Insert, "dbo", "T")]
        public void Finds(string sql, ExpansionKind kind, string schema, string name)
        {
            var t = At(sql);
            Assert.NotNull(t);
            Assert.Equal((kind, schema, name), (t.Kind, t.Schema, t.Name));
        }

        [Theory]
        [InlineData("INSERT INTO Department| (Name) VALUES ('x')")] // template already there
        [InlineData("INSERT INTO|")]
        [InlineData("SELECT * FROM Department|")]
        [InlineData("-- EXEC dbo.p|")]
        [InlineData("SELECT 'EXEC dbo.p|")]
        [InlineData("ALTER TABLE Department|")]
        [InlineData("CREATE OR ALTER PROCEDURE dbo.p|")]
        [InlineData("FOREIGN KEY (a) REFERENCES t (a) ON UPDATE CASCADE|")]
        [InlineData("INSERT INTO Depart|ment")]
        [InlineData("INSERT INTO dbo.T|\n(a, b)")]
        [InlineData("INSERT INTO dbo.T|\n  -- columns\n(a, b)")]
        [InlineData("INSERT INTO dbo.T|\nVALUES (1)")]
        [InlineData("INSERT INTO dbo.T|\nSELECT 1")]
        [InlineData("INSERT INTO dbo.T|\nDEFAULT VALUES")]
        [InlineData("UPDATE dbo.T|\nSET a = 1")]
        [InlineData("UPDATE dbo.T|\nWITH (ROWLOCK) SET a = 1")]
        [InlineData("UPDATE dbo.T|\nAS x SET a = 1")]
        public void IgnoresOtherPlaces(string sql) => Assert.Null(At(sql));

        [Fact]
        public void Find_ReportsStatementStart()
        {
            var t = At("  EXEC @rc = dbo.p|");
            Assert.Equal(2, t.StatementStart);
        }

        [Theory]
        [InlineData("EXEC sp_who|", "sp_who")]
        [InlineData("  EXEC @rc = dbo.p|", "dbo.p")]
        [InlineData("GO\nexecute [Person].[sp_UpdatePerson]|", "[Person].[sp_UpdatePerson]")]
        public void Find_ReportsTheNameSpan(string sqlWithCaret, string name)
        {
            string sql = sqlWithCaret.Replace("|", "");
            var t = At(sqlWithCaret);
            Assert.Equal(name, sql.Substring(t.NameStart, t.NameEnd - t.NameStart));
        }

        [Fact]
        public void Exec_TemplateNeedsAProcedureWithParameters()
        {
            Assert.True(StatementExpander.HasExecTemplate(UpdatePerson));
            Assert.False(StatementExpander.HasExecTemplate(new DbRoutine("dbo", "p", RoutineKind.Procedure, new DbParameter[0])));
            Assert.False(StatementExpander.HasExecTemplate(new DbRoutine("dbo", "f", RoutineKind.ScalarFunction, new[] { new DbParameter("@x", "int") }, "int")));
            Assert.False(StatementExpander.HasExecTemplate(null));
        }

        private static readonly DbObject Table1 = new DbObject("dbo", "Table1", false, new[]
        {
            new DbColumn("Id", "int", isNullable: false, isPrimaryKey: true),
            new DbColumn("Code", "nvarchar(20)", isNullable: false),
            new DbColumn("Name", "nvarchar(100)"),
        });

        [Fact]
        public void Insert_ListsColumnsAndPlaceholdersInTheSameOrder()
        {
            var e = StatementExpander.BuildInsert(Table1, "", "\n");
            Assert.Equal(
                "\n(" +
                "\n    Id," +
                "\n    Code," +
                "\n    Name" +
                "\n)" +
                "\nVALUES" +
                "\n(" +
                "\n    0," +
                "\n    N''," +
                "\n    N''" +
                "\n);", e.Text);
            Assert.Equal("0,", e.Text.Substring(e.CaretOffset, 2)); // the caret is on the first value
            Assert.Equal("", e.Prefix);
        }

        [Fact]
        public void Insert_SkipsIdentityComputedAndRowversion()
        {
            var e = StatementExpander.BuildInsert(Department, "\t", "\n");
            Assert.Equal(
                "\n\t(" +
                "\n\t    Name," +
                "\n\t    GroupName," +
                "\n\t    ModifiedDate" +
                "\n\t)" +
                "\n\tVALUES" +
                "\n\t(" +
                "\n\t    N''," +
                "\n\t    N''," +
                "\n\t    GETDATE()" +
                "\n\t);", e.Text);
            Assert.Equal("N''", e.Text.Substring(e.CaretOffset, 3));
        }

        [Fact]
        public void Insert_QuotesNamesThatNeedIt()
        {
            var t = new DbObject("dbo", "T", false, new[] { new DbColumn("Order No", "int"), new DbColumn("Select", "int") });
            var e = StatementExpander.BuildInsert(t, "", "\n");
            Assert.StartsWith("\n(\n    [Order No],\n    [Select]\n)", e.Text);
        }

        [Fact]
        public void Insert_NothingWritableIsNothingToExpand()
        {
            var t = new DbObject("dbo", "T", false, new[]
            {
                new DbColumn("Id", "int", isIdentity: true, isPrimaryKey: true),
                new DbColumn("RowVer", "rowversion"),
                new DbColumn("Calc", "int", isComputed: true),
            });
            Assert.Null(StatementExpander.BuildInsert(t, "", "\n"));
        }

        [Fact]
        public void Update_SetsNonKeyColumnsAndFiltersOnAParameterDeclaredAbove()
        {
            var e = StatementExpander.BuildUpdate(Table1, "", "\n");
            Assert.Equal(
                "\nSET" +
                "\n    Code = N''," +
                "\n    Name = N''" +
                "\nWHERE Id = @Id;", e.Text);
            Assert.Equal("DECLARE @Id int = NULL;\n", e.Prefix);
            Assert.Equal("N''", e.Text.Substring(e.CaretOffset, 3)); // the first value in SET
        }

        [Fact]
        public void Update_SkipsIdentityComputedRowversionAndKeysInSet()
        {
            var e = StatementExpander.BuildUpdate(Department, "\t", "\n");
            Assert.Equal(
                "\n\tSET" +
                "\n\t    Name = N''," +
                "\n\t    GroupName = N''," +
                "\n\t    ModifiedDate = GETDATE()" +
                "\n\tWHERE DepartmentID = @DepartmentID;", e.Text);
            Assert.Equal("DECLARE @DepartmentID smallint = NULL;\n\t", e.Prefix);
        }

        [Fact]
        public void Update_CompositeKeyJoinsWithAnd()
        {
            var t = new DbObject("dbo", "OrderLine", false, new[]
            {
                new DbColumn("OrderId", "int", isNullable: false, isPrimaryKey: true),
                new DbColumn("LineNo", "smallint", isNullable: false, isPrimaryKey: true),
                new DbColumn("Sku", "varchar(30)", isNullable: false, isPrimaryKey: true),
                new DbColumn("Qty", "decimal(18,2)"),
            });
            var e = StatementExpander.BuildUpdate(t, "", "\n");
            Assert.Equal(
                "\nSET" +
                "\n    Qty = 0" +
                "\nWHERE OrderId = @OrderId" +
                "\n  AND [LineNo] = @LineNo" +
                "\n  AND Sku = @Sku;", e.Text);
            Assert.Equal("DECLARE @OrderId int = NULL;\nDECLARE @LineNo smallint = NULL;\nDECLARE @Sku varchar(30) = NULL;\n", e.Prefix);
        }

        [Fact]
        public void Update_ParameterNamesAreLegalAndUnique()
        {
            var t = new DbObject("dbo", "T", false, new[]
            {
                new DbColumn("Order No", "int", isNullable: false, isPrimaryKey: true),
                new DbColumn("Order_No", "int", isNullable: false, isPrimaryKey: true),
                new DbColumn("Val", "int"),
            });
            var e = StatementExpander.BuildUpdate(t, "", "\n");
            Assert.EndsWith("\nWHERE [Order No] = @Order_No\n  AND Order_No = @Order_No2;", e.Text);
            Assert.Equal("DECLARE @Order_No int = NULL;\nDECLARE @Order_No2 int = NULL;\n", e.Prefix);
        }

        [Fact]
        public void Update_DoesNotRedeclareAParameterTheBatchAlreadyHas()
        {
            var e = StatementExpander.BuildUpdate(Table1, "", "\n", new[] { "@id" });
            Assert.Equal("", e.Prefix);
            Assert.EndsWith("\nWHERE Id = @Id;", e.Text);
        }

        [Fact]
        public void Update_WithoutPrimaryKeyLeavesATodoThatCannotRun()
        {
            var t = new DbObject("dbo", "Log", false, new[] { new DbColumn("Msg", "varchar(100)"), new DbColumn("Level", "int") });
            var e = StatementExpander.BuildUpdate(t, "", "\n");
            Assert.Equal(
                "\nSET" +
                "\n    Msg = ''," +
                "\n    Level = 0" +
                "\nWHERE /* TODO: choose the rows to update before running */ ;", e.Text);
            Assert.Equal("", e.Prefix);
            Assert.DoesNotContain("1=1", e.Text.Replace(" ", ""));
            // The caret sits before the ';', where the condition goes.
            Assert.Equal(";", e.Text.Substring(e.CaretOffset));
        }

        [Fact]
        public void Update_OnlyKeyColumnsIsNothingToExpand()
        {
            var t = new DbObject("dbo", "Link", false, new[]
            {
                new DbColumn("A", "int", isNullable: false, isPrimaryKey: true),
                new DbColumn("B", "int", isNullable: false, isPrimaryKey: true),
            });
            Assert.Null(StatementExpander.BuildUpdate(t, "", "\n"));
        }

        [Fact]
        public void Exec_AlignsParametersAndDeclaresOutputs()
        {
            const string line = "EXEC Person.sp_UpdatePerson";
            var e = StatementExpander.BuildExec(UpdatePerson, line, "", "\n");
            string pad = new string(' ', line.Length); // the comma sits under the space, so the @s line up
            Assert.Equal(
                " @BusinessEntityID = 0 -- int" +
                "\n" + pad + ",@FirstName = N'' -- nvarchar(50)" +
                "\n" + pad + ",@Result = @Result OUTPUT -- int", e.Text);
            Assert.Equal("DECLARE @Result int;\n", e.Prefix);
            Assert.Equal("0", e.Text.Substring(e.CaretOffset, 1));
        }

        [Fact]
        public void Exec_NoParametersIsNothingToExpand() =>
            Assert.Null(StatementExpander.BuildExec(new DbRoutine("dbo", "p", RoutineKind.Procedure, new DbParameter[0]), "EXEC p", "", "\n"));

        [Theory]
        [InlineData("CREATE PROCEDURE dbo.p AS SELECT 1\r\n", "ALTER PROCEDURE dbo.p AS SELECT 1")]
        [InlineData("-- header\r\n/* note */\r\ncreate   view v AS SELECT 1", "-- header\r\n/* note */\r\nALTER   view v AS SELECT 1")]
        [InlineData("CREATE OR ALTER FUNCTION f() RETURNS int AS BEGIN RETURN 1 END", "ALTER FUNCTION f() RETURNS int AS BEGIN RETURN 1 END")]
        [InlineData("SELECT 1", null)]
        public void AlterScript(string definition, string expected) =>
            Assert.Equal(expected, StatementExpander.AlterScript(definition));

        [Fact]
        public void SchemaModel_FindsRoutinesLikeTables()
        {
            var schema = new SchemaModel(new DbObject[0], new ForeignKey[0], new[] { UpdatePerson });
            Assert.Same(UpdatePerson, schema.FindRoutine(null, "SP_UPDATEPERSON"));
            Assert.Same(UpdatePerson, schema.FindRoutine("Person", "sp_UpdatePerson"));
            Assert.Null(schema.FindRoutine("dbo", "sp_UpdatePerson"));
        }
    }
}
