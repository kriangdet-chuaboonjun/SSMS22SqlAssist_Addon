using System;
using System.Collections.Generic;
using System.Linq;
using SqlAssist.Core.Formatting;
using SqlAssist.Core.Sql;
using Xunit;

namespace SqlAssist.Core.Tests
{
    public class SqlFormatterTests
    {
        private static string L(params string[] lines) => string.Join("\n", lines);

        private static string Fmt(string sql, FormatOptions options = null)
        {
            var result = SqlFormatter.Format(sql, options);
            Assert.True(result.IsSuccess, result.Error);
            return result.Text;
        }

        private static FormatOptions Trailing => new FormatOptions { CommaStyle = CommaStyle.Trailing };

        [Theory]
        [InlineData(false)]
        [InlineData(true)]
        public void ExcessiveNestingReturnsTheOriginalSql(bool useCase)
        {
            string sql = useCase
                ? "SELECT " + string.Concat(Enumerable.Repeat("CASE WHEN 1 = 1 THEN ", 256)) + "1"
                    + string.Concat(Enumerable.Repeat(" END", 256)) + ";"
                : "SELECT " + new string('(', 256) + "1" + new string(')', 256) + ";";

            var result = SqlFormatter.Format(sql);

            Assert.False(result.IsSuccess);
            Assert.False(result.Changed);
            Assert.Equal(sql, result.Text);
            Assert.Contains("ซ้อน", result.Error);
        }

        // ---- SELECT ----

        [Fact]
        public void SelectJoinWhereGroupOrder()
        {
            Assert.Equal(L(
                "SELECT",
                "    a.id",
                "  , a.name",
                "  , COUNT(*) AS cnt",
                "FROM dbo.Orders a",
                "INNER JOIN dbo.Customers c",
                "    ON c.id = a.cid",
                "        AND c.active = 1",
                "LEFT JOIN x",
                "    ON x.id = a.id",
                "WHERE a.status IN (1, 2, 3)",
                "    AND (a.x IS NULL OR a.y > 5)",
                "GROUP BY",
                "    a.id",
                "  , a.name",
                "HAVING COUNT(*) > 1",
                "ORDER BY a.id DESC"),
                Fmt("select a.id,a.name,count(*) as cnt from dbo.Orders a inner join dbo.Customers c on c.id=a.cid and c.active=1 " +
                    "left join x on x.id=a.id where a.status in (1,2,3) and (a.x is null or a.y>5) group by a.id,a.name having count(*)>1 order by a.id desc"));
        }

        [Fact]
        public void TrailingCommas()
        {
            Assert.Equal(L("SELECT", "    a.id,", "    a.name,", "    COUNT(*) AS cnt", "FROM t"),
                Fmt("select a.id,a.name,count(*) as cnt from t", Trailing));
        }

        [Fact]
        public void IndentSizeTwo()
        {
            Assert.Equal(L("SELECT", "  a", ", b", "FROM t", "WHERE x = 1", "  AND y = 2"),
                Fmt("select a,b from t where x=1 and y=2", new FormatOptions { IndentSize = 2 }));
            Assert.Equal(L("SELECT", "        a", "      , b", "FROM t"),
                Fmt("select a,b from t", new FormatOptions { IndentSize = 8 }));
        }

        [Fact]
        public void IndentSizeIsKeptInARange()
        {
            Assert.Equal(L("SELECT", "  a", ", b"), Fmt("select a,b", new FormatOptions { IndentSize = 0 }));
            Assert.Equal(L("SELECT", "        a", "      , b"), Fmt("select a,b", new FormatOptions { IndentSize = 99 }));
        }

        [Fact]
        public void OneColumnStaysOnTheSelectLine()
        {
            Assert.Equal(L("SELECT *", "FROM t", "WHERE id = 1"), Fmt("select * from t where id=1"));
            Assert.Equal(L("SELECT COUNT(*)", "FROM t"), Fmt("select count(*) from t"));
        }

        [Fact]
        public void TopAndDistinctStayOnTheSelectLine()
        {
            Assert.Equal(L("SELECT DISTINCT TOP 10", "    a", "  , b", "FROM t"), Fmt("select distinct top 10 a,b from t"));
            Assert.Equal(L("SELECT TOP (5) *", "FROM t"), Fmt("select top(5) * from t"));
            Assert.Equal(L("SELECT TOP 5 PERCENT WITH TIES a", "FROM t", "ORDER BY a"), Fmt("select top 5 percent with ties a from t order by a"));
        }

        [Fact]
        public void OrderByAndGroupByListsBreakFromTwoItems()
        {
            Assert.Equal(L("SELECT a", "FROM t", "ORDER BY", "    a DESC", "  , b"), Fmt("select a from t order by a desc,b"));
            Assert.Equal(L("SELECT a", "FROM t", "ORDER BY a"), Fmt("select a from t order by a"));
        }

        [Fact]
        public void BetweenKeepsItsAnd()
        {
            Assert.Equal(L("SELECT a", "FROM t", "WHERE a BETWEEN 1 AND 5", "    AND b = 2"), Fmt("select a from t where a between 1 and 5 and b=2"));
        }

        [Fact]
        public void SetOperatorsGetTheirOwnLine()
        {
            Assert.Equal(L("SELECT a", "FROM t", "UNION ALL", "SELECT b", "FROM u"), Fmt("select a from t union all select b from u"));
            Assert.Equal(L("SELECT a", "EXCEPT", "SELECT b"), Fmt("select a except select b"));
        }

        [Fact]
        public void OldStyleCommaJoin()
        {
            Assert.Equal(L("SELECT *", "FROM", "    a", "  , b", "WHERE a.id = b.id"), Fmt("select * from a,b where a.id=b.id"));
        }

        [Fact]
        public void SelectInto()
        {
            Assert.Equal(L("SELECT a", "INTO #t", "FROM u"), Fmt("select a into #t from u"));
        }

        [Fact]
        public void OffsetFetchAndForXml()
        {
            Assert.Equal(L("SELECT a", "FROM t", "ORDER BY a", "OFFSET 5 ROWS FETCH NEXT 10 ROWS ONLY"), Fmt("select a from t order by a offset 5 rows fetch next 10 rows only"));
            Assert.Equal(L("SELECT a", "FROM t", "FOR XML PATH('')"), Fmt("select a from t for xml path('')"));
            Assert.Equal(L("SELECT a", "FROM t", "OPTION (RECOMPILE)"), Fmt("select a from t option (recompile)"));
            // Offset, rows, next are only keywords here; elsewhere they are column names and stay as typed.
            Assert.Equal(L("SELECT", "    offset", "  , rows", "  , next", "FROM t"), Fmt("select offset,rows,next from t"));
        }

        // ---- subqueries ----

        [Fact]
        public void SubqueryInIn()
        {
            Assert.Equal(L("SELECT a", "FROM t", "WHERE a IN (", "    SELECT b", "    FROM u", "    WHERE c = 1", ")"),
                Fmt("select a from t where a in (select b from u where c=1)"));
        }

        [Fact]
        public void SubqueryInExistsThenAnd()
        {
            Assert.Equal(L("SELECT a", "FROM t", "WHERE EXISTS (", "    SELECT 1", "    FROM u", "    WHERE u.id = t.id", ")", "    AND x = 1"),
                Fmt("select a from t where exists(select 1 from u where u.id=t.id) and x=1"));
        }

        [Fact]
        public void SubqueryInsideAContinuationLineIsIndentedFromThatLine()
        {
            Assert.Equal(L("SELECT a", "FROM t", "WHERE x = 1", "    AND a IN (", "        SELECT b", "        FROM u", "    )"),
                Fmt("select a from t where x=1 and a in (select b from u)"));
        }

        [Fact]
        public void DerivedTablesAndJoinedSubqueries()
        {
            Assert.Equal(L("SELECT *", "FROM (", "    SELECT a", "    FROM t", ") d", "WHERE d.a = 1"),
                Fmt("select * from (select a from t) d where d.a=1"));
            Assert.Equal(L("SELECT *", "FROM a", "JOIN (", "    SELECT x", "    FROM b", ") b", "    ON b.x = a.x"),
                Fmt("select * from a join (select x from b) b on b.x=a.x"));
        }

        [Fact]
        public void CrossApply()
        {
            Assert.Equal(L("SELECT *", "FROM a", "CROSS APPLY (", "    SELECT TOP 1 x", "    FROM b", "    WHERE b.id = a.id", ") AS c"),
                Fmt("select * from a cross apply (select top 1 x from b where b.id=a.id) as c"));
            Assert.Equal(L("SELECT *", "FROM a", "OUTER APPLY dbo.f(a.id) AS c"), Fmt("select * from a outer apply dbo.f(a.id) as c"));
        }

        [Fact]
        public void ScalarSubqueryInTheSelectList()
        {
            Assert.Equal(L("SELECT", "    a", "  , (", "        SELECT MAX(x)", "        FROM u", "    ) AS m", "FROM t"),
                Fmt("select a,(select max(x) from u) as m from t"));
        }

        // ---- CASE ----

        [Fact]
        public void CaseWithSeveralWhensGetsLines()
        {
            Assert.Equal(L(
                "SELECT",
                "    CASE",
                "        WHEN a = 1 THEN 'x'",
                "        WHEN a = 2 THEN 'y'",
                "        ELSE 'z'",
                "    END AS label",
                "  , b",
                "FROM t"),
                Fmt("select case when a=1 then 'x' when a=2 then 'y' else 'z' end as label,b from t"));
        }

        [Fact]
        public void CaseWithOneWhenStaysOnTheLine()
        {
            Assert.Equal(L("SELECT CASE WHEN a = 1 THEN 'x' ELSE 'y' END", "FROM t"), Fmt("select case when a=1 then 'x' else 'y' end from t"));
        }

        [Fact]
        public void SimpleCaseAndNestedCase()
        {
            Assert.Equal(L(
                "SELECT",
                "    CASE a",
                "        WHEN 1 THEN CASE WHEN b = 1 THEN 'p' ELSE 'q' END",
                "        WHEN 2 THEN 'r'",
                "    END",
                "  , c",
                "FROM t"),
                Fmt("select case a when 1 then case when b=1 then 'p' else 'q' end when 2 then 'r' end,c from t"));
        }

        // ---- CTE ----

        [Fact]
        public void CteWithOneDefinition()
        {
            Assert.Equal(L("WITH c AS (", "    SELECT a", "    FROM t", ")", "SELECT *", "FROM c"), Fmt("with c as (select a from t) select * from c"));
        }

        [Fact]
        public void CteWithSeveralDefinitions()
        {
            string sql = "with a as (select 1 as x),b(y) as (select 2) select * from a,b";
            Assert.Equal(L("WITH a AS (", "    SELECT 1 AS x", ")", ", b (y) AS (", "    SELECT 2", ")", "SELECT *", "FROM", "    a", "  , b"), Fmt(sql));
            Assert.Equal(L("WITH a AS (", "    SELECT 1 AS x", "),", "b (y) AS (", "    SELECT 2", ")", "SELECT *", "FROM", "    a,", "    b"), Fmt(sql, Trailing));
        }

        [Fact]
        public void CteInFrontOfDml()
        {
            Assert.Equal(L("WITH c AS (", "    SELECT a", "    FROM t", ")", "DELETE FROM c", "WHERE a = 1"), Fmt("with c as (select a from t) delete from c where a=1"));
        }

        // ---- DML ----

        [Fact]
        public void Update()
        {
            Assert.Equal(L("UPDATE t", "SET", "    a = 1", "  , b = 2", "FROM t", "JOIN u", "    ON u.id = t.id", "WHERE t.x = 3"),
                Fmt("update t set a=1,b=2 from t join u on u.id=t.id where t.x=3"));
            Assert.Equal(L("UPDATE t", "SET a = 1", "WHERE id = 2"), Fmt("update t set a=1 where id=2"));
            Assert.Equal(L("UPDATE TOP (10) t", "SET a = 1"), Fmt("update top (10) t set a=1"));
        }

        [Fact]
        public void UpdateWithOutputAndOption()
        {
            Assert.Equal(L("UPDATE t", "SET a = 1", "OUTPUT inserted.a INTO @x", "WHERE id = 2", "OPTION (MAXDOP 1)"),
                Fmt("update t set a=1 output inserted.a into @x where id=2 option (maxdop 1)"));
        }

        [Fact]
        public void Delete()
        {
            Assert.Equal(L("DELETE FROM t", "WHERE id = 1"), Fmt("delete from t where id=1"));
            Assert.Equal(L("DELETE t", "FROM t", "JOIN u", "    ON u.id = t.id", "WHERE u.x = 1"), Fmt("delete t from t join u on u.id=t.id where u.x=1"));
            Assert.Equal(L("DELETE TOP (5) FROM t"), Fmt("delete top (5) from t"));
        }

        [Fact]
        public void InsertValues()
        {
            Assert.Equal(L("INSERT INTO dbo.T (a, b)", "VALUES", "    (1, 'x')", "  , (2, 'y')"), Fmt("insert into dbo.T(a,b) values(1,'x'),(2,'y')"));
            Assert.Equal(L("INSERT INTO t (a)", "VALUES (1)"), Fmt("insert into t(a) values(1)"));
            Assert.Equal("INSERT t DEFAULT VALUES", Fmt("insert t default values"));
        }

        [Fact]
        public void InsertSelectAndExec()
        {
            Assert.Equal(L("INSERT INTO t (a, b)", "SELECT", "    x", "  , y", "FROM u"), Fmt("insert into t(a,b) select x,y from u"));
            Assert.Equal(L("INSERT INTO t", "EXEC dbo.p @a = 1"), Fmt("insert into t exec dbo.p @a=1"));
        }

        [Fact]
        public void Merge()
        {
            Assert.Equal(L(
                "MERGE INTO t AS x",
                "USING s",
                "    ON x.id = s.id",
                "WHEN MATCHED THEN",
                "    UPDATE SET a = s.a",
                "WHEN NOT MATCHED THEN",
                "    INSERT (a) VALUES (s.a)",
                "WHEN NOT MATCHED BY SOURCE THEN",
                "    DELETE;"),
                Fmt("merge into t as x using s on x.id=s.id when matched then update set a=s.a when not matched then insert (a) values (s.a) when not matched by source then delete;"));
        }

        // ---- long groups ----

        [Fact]
        public void LongInListsAreWrappedOnePerLine()
        {
            string values = string.Join(",", Enumerable.Range(1000001, 10));
            string expected = L("SELECT a", "FROM t", "WHERE a IN (", string.Join("\n", Enumerable.Range(1000001, 10).Select((n, i) => i == 0 ? "    " + n : "  , " + n)), ")");
            Assert.Equal(expected, Fmt("select a from t where a in (" + values + ")"));
        }

        [Fact]
        public void ShortInListsStayOnTheLine()
        {
            Assert.Equal(L("SELECT a", "FROM t", "WHERE a IN (1, 2, 3, 4, 5, 6, 7, 8, 9, 10)"), Fmt("select a from t where a in (1,2,3,4,5,6,7,8,9,10)"));
        }

        [Fact]
        public void LongConditionGroupsAreWrapped()
        {
            string sql = "select a from t where (aaaaaaaaaaaaaaa = 1 or bbbbbbbbbbbbbbbbbb = 2 or cccccccccccccccccc = 3 or dddddddddddddddddd = 4 or eeeeeeeeeeeeeeeee = 5)";
            Assert.Equal(L("SELECT a", "FROM t", "WHERE (", "    aaaaaaaaaaaaaaa = 1", "    OR bbbbbbbbbbbbbbbbbb = 2", "    OR cccccccccccccccccc = 3", "    OR dddddddddddddddddd = 4", "    OR eeeeeeeeeeeeeeeee = 5", ")"),
                Fmt(sql));
        }

        [Fact]
        public void FunctionCallsNeverWrap()
        {
            string args = string.Join(", ", Enumerable.Range(1, 40).Select(i => "col" + i));
            string result = Fmt("select concat(" + args + ") from t");
            Assert.Equal(L("SELECT CONCAT(" + args + ")", "FROM t"), result);
        }

        // ---- operators and spacing ----

        [Fact]
        public void OperatorSpacing()
        {
            Assert.Equal(L("SELECT a", "FROM t", "WHERE a = -1", "    AND b - -c > 0", "    AND d <= e", "    AND f <> g", "    AND h != i", "    AND j >= 1.5e-3"),
                Fmt("select a from t where a=-1 and b - -c>0 and d<=e and f<>g and h!=i and j>=1.5e-3"));
        }

        [Fact]
        public void UnaryMinusAfterKeywordAndParen()
        {
            Assert.Equal(L("SELECT (-1)", "FROM t", "WHERE a > -b", "    AND c IN (-1, -2)"), Fmt("select (-1) from t where a>-b and c in (-1,-2)"));
        }

        [Fact]
        public void MinusMinusIsNeverCreatedFromTwoMinuses()
        {
            string result = Fmt("select a - -b, - -c, 1 - -1 from t");
            Assert.DoesNotContain("--", result);
        }

        [Fact]
        public void DotsAndStarsAndFunctionCalls()
        {
            Assert.Equal(L("SELECT t.*", "FROM dbo.t t"), Fmt("select t . * from dbo . t t"));
            Assert.Equal(L("SELECT", "    ISNULL(a, 0)", "  , SUM(b) OVER (PARTITION BY c ORDER BY d) AS s", "FROM t"),
                Fmt("select isnull(a,0), sum(b) over(partition by c order by d) as s from t"));
        }

        [Fact]
        public void TableVariablesAndTempTables()
        {
            Assert.Equal(L("SELECT a", "FROM #t", "JOIN @v v", "    ON v.id = #t.id", "JOIN ##g", "    ON ##g.id = #t.id"), Fmt("select a from #t join @v v on v.id=#t.id join ##g on ##g.id=#t.id"));
        }

        // ---- what must not change ----

        [Fact]
        public void StringsAndQuotedIdentifiersAreUntouched()
        {
            Assert.Equal(L("SELECT", "    [My Col]", "  , @v", "  , N'it''s select from'", "  , \"Quoted Id\"", "FROM [dbo].[Order] WITH (NOLOCK)", "WHERE x = 'a;b  c'"),
                Fmt("select [My Col],@v,N'it''s select from',\"Quoted Id\" from [dbo].[Order] with (nolock) where x='a;b  c'"));
        }

        [Fact]
        public void IdentifiersKeepTheirCaseEvenWhenTheyLookLikeKeywords()
        {
            Assert.Equal(L("SELECT", "    Name", "  , date", "  , t.Key", "  , t.Order", "  , Count", "FROM t"),
                Fmt("select Name,date,t.Key,t.Order,Count from t"));
            Assert.Equal(L("SELECT", "    id", "  , YEAR(d) AS y", "FROM T"), Fmt("select id,year(d) as y from T"));
        }

        [Fact]
        public void KeywordCaseOptions()
        {
            Assert.Equal(L("select A", "from T", "where X in (1)"), Fmt("SELECT A FROM T WHERE X IN (1)", new FormatOptions { KeywordCase = KeywordCase.Lower }));
            Assert.Equal(L("Select a", "From t"), Fmt("Select a From t", new FormatOptions { KeywordCase = KeywordCase.Preserve }));
            Assert.Equal(L("SELECT a", "FROM t"), Fmt("Select a From t", new FormatOptions { KeywordCase = KeywordCase.Upper }));
        }

        [Fact]
        public void FunctionsAndTypesFollowKeywordCase()
        {
            Assert.Equal(L("SELECT", "    CAST(a AS VARCHAR(10)) AS s", "  , CONVERT(INT, b) AS i", "FROM t"),
                Fmt("select cast(a as varchar(10)) as s, convert(int, b) as i from t"));
            Assert.Equal(L("select", "    cast(a as varchar(10)) as s", "  , convert(int, b) as i", "from t"),
                Fmt("SELECT CAST(a AS VARCHAR(10)) AS s, CONVERT(INT, b) AS i FROM t", new FormatOptions { KeywordCase = KeywordCase.Lower }));
            // A column called date or int is a column, whatever case it is written in.
            Assert.Equal(L("SELECT", "    date", "  , [int]", "FROM t"), Fmt("select date,[int] from t"));
        }

        // ---- comments ----

        [Fact]
        public void TrailingCommentsStayOnTheirLine()
        {
            Assert.Equal(L("SELECT", "    a -- first", "  , b /* inline */", "FROM t -- tail"), Fmt("select a, -- first\nb /* inline */ from t -- tail"));
            Assert.Equal(L("SELECT", "    a, -- first", "    b /* inline */", "FROM t -- tail"), Fmt("select a, -- first\nb /* inline */ from t -- tail", Trailing));
        }

        [Fact]
        public void CommentLinesKeepTheirPlace()
        {
            Assert.Equal(L("-- header", "SELECT a", "-- about from", "FROM t"), Fmt("-- header\nselect a\n-- about from\nfrom t"));
            Assert.Equal(L("SELECT", "    -- pick these", "    a", "  , b", "FROM t"), Fmt("select\n-- pick these\na,b from t"));
        }

        [Fact]
        public void ALineCommentInsideAnExpressionForcesTheBreakAndNothingIsLost()
        {
            string result = Fmt("select a + -- c\n b from t");
            Assert.Equal(L("SELECT a + -- c", "    b", "FROM t"), result);
        }

        [Fact]
        public void BlockCommentsKeepTheirText()
        {
            string result = Fmt("select /* one\n   two */ a from t");
            Assert.Contains("/* one\n   two */", result);
            Assert.Contains("SELECT", result);
        }

        [Fact]
        public void MultiLineBlockCommentIsMovedAsAWhole()
        {
            string result = Fmt("begin\nselect 1\n    /*\n     * note\n     */\nend");
            Assert.Equal(L("BEGIN", "    SELECT 1", "    /*", "     * note", "     */", "END"), result);
        }

        [Fact]
        public void CommentSyntaxInsideStringsIsNotAComment()
        {
            Assert.Equal(L("SELECT", "    '-- not a comment'", "  , '/* nor this */'", "FROM t"), Fmt("select '-- not a comment','/* nor this */' from t"));
        }

        // ---- batches ----

        [Fact]
        public void GoStaysOnItsOwnLine()
        {
            Assert.Equal(L("SELECT 1", "GO", "SELECT 2"), Fmt("select 1\ngo\nselect 2"));
            Assert.Equal(L("SELECT 1", "", "GO 5", "", "SELECT 2"), Fmt("select 1\n\n go 5\n\nselect 2"));
            Assert.Equal(L("SELECT 1", "GO -- done", "SELECT 2"), Fmt("select 1\ngo -- done\nselect 2"));
        }

        [Fact]
        public void BlankLinesBetweenStatementsAreKeptAsOne()
        {
            Assert.Equal(L("SELECT 1", "SELECT 2", "", "SELECT 3"), Fmt("select 1\nselect 2\n\n\n\nselect 3"));
        }

        [Fact]
        public void AFinalNewlineStaysOrStaysAway()
        {
            Assert.Equal("SELECT 1\n", Fmt("select 1\n"));
            Assert.Equal("SELECT 1", Fmt("select 1"));
            Assert.Equal("SELECT 1\r\nGO\r\n", Fmt("select 1\r\ngo\r\n"));
        }

        [Fact]
        public void NewlineStyleFollowsTheText()
        {
            Assert.Equal("SELECT a\r\nFROM t", Fmt("select a\r\nfrom t"));
            Assert.Equal("SELECT\r\n    a\r\n  , b\r\nFROM t", Fmt("select a,b\r\nfrom t"));
            Assert.Equal("SELECT\n    a\n  , b\nFROM t", Fmt("select a,b\nfrom t"));
        }

        // ---- control flow ----

        [Fact]
        public void IfElse()
        {
            Assert.Equal(L("IF @a = 1", "    SELECT 1", "ELSE", "    SELECT 2"), Fmt("if @a=1 select 1 else select 2"));
            Assert.Equal(L("IF @a = 1", "    SELECT 1", "ELSE IF @a = 2", "    SELECT 2", "ELSE", "    SELECT 3"), Fmt("if @a=1 select 1 else if @a=2 select 2 else select 3"));
        }

        [Fact]
        public void IfWithBlocks()
        {
            Assert.Equal(L(
                "IF EXISTS (",
                "    SELECT 1",
                "    FROM t",
                ")",
                "BEGIN",
                "    UPDATE t",
                "    SET a = 1",
                "END",
                "ELSE",
                "BEGIN",
                "    DELETE FROM t",
                "END"),
                Fmt("if exists(select 1 from t) begin update t set a=1 end else begin delete from t end"));
        }

        [Fact]
        public void NestedSingleStatementBodies()
        {
            Assert.Equal(L("IF @a = 1", "    IF @b = 2", "        PRINT 'x'", "    ELSE", "        PRINT 'y'", "PRINT 'z'"),
                Fmt("if @a=1 if @b=2 print 'x' else print 'y' print 'z'"));
        }

        [Fact]
        public void TryCatchAndWhile()
        {
            Assert.Equal(L("BEGIN TRY", "    SELECT 1", "END TRY", "BEGIN CATCH", "    SELECT 2", "END CATCH"),
                Fmt("begin try select 1 end try begin catch select 2 end catch"));
            Assert.Equal(L("WHILE @i < 3", "BEGIN", "    SET @i += 1", "END"), Fmt("while @i<3 begin set @i+=1 end"));
        }

        [Fact]
        public void BeginTransactionIsNotABlock()
        {
            Assert.Equal(L("BEGIN TRANSACTION", "UPDATE t", "SET a = 1", "COMMIT"), Fmt("begin transaction update t set a=1 commit"));
        }

        [Fact]
        public void ProcedureHeaderKeepsItsAsLinedUpWithCreate()
        {
            Assert.Equal(L("CREATE PROCEDURE dbo.p", "    @a INT,", "    @b INT = 1", "AS", "BEGIN", "    SELECT @a", "END"),
                Fmt("create procedure dbo.p\n @a int,\n @b int = 1\nas\nbegin select @a end"));
        }

        [Fact]
        public void NestedParenthesesAreNotSpacedApart()
        {
            Assert.Equal(L("SELECT", "    f((", "        SELECT 1", "    ))", "FROM t"), Fmt("select f((select 1)) from t"));
            Assert.Equal(L("SELECT ((1 + 2) * 3)", "FROM t"), Fmt("select ((1+2)*3) from t"));
        }

        [Fact]
        public void XmlAndJsonClausesInsideASubqueryAreKeywordsToo()
        {
            Assert.Equal(L("SELECT", "    STUFF((", "        SELECT ',' + n", "        FROM t", "        FOR XML PATH('')", "    ), 1, 1, '')", "FROM u"),
                Fmt("select stuff((select ',' + n from t for xml path('')), 1, 1, '') from u"));
            Assert.Equal(L("SELECT a", "FROM t", "FOR JSON AUTO"), Fmt("select a from t for json auto"));
        }

        [Fact]
        public void ThrowAndIncludeAreKeywords()
        {
            Assert.Equal(L("BEGIN CATCH", "    THROW;", "END CATCH"), Fmt("begin catch throw; end catch"));
            Assert.Equal("CREATE INDEX ix ON t (a) INCLUDE (b)", Fmt("create index ix on t(a) include (b)"));
        }

        [Fact]
        public void ProcedureBody()
        {
            Assert.Equal(L("CREATE PROCEDURE dbo.p @a INT AS", "BEGIN", "    SET NOCOUNT ON", "    SELECT @a", "END"),
                Fmt("create procedure dbo.p @a int as begin set nocount on select @a end"));
        }

        [Fact]
        public void StatementEndsAtSemicolon()
        {
            Assert.Equal(L("SELECT a;", "SELECT b;"), Fmt("select a; select b;"));
            Assert.Equal(L("SELECT", "    a", "  , b;"), Fmt("select a,b ;"));
        }

        // ---- statements it only tidies ----

        [Fact]
        public void OtherStatementsKeepTheirLineBreaks()
        {
            Assert.Equal(L("DECLARE @a INT", "    , @b VARCHAR(10) = 'x'"), Fmt("declare @a int\n,@b varchar(10)='x'"));
            Assert.Equal(L("EXEC dbo.p @a = 1,", "    @b = 2"), Fmt("exec dbo.p @a=1,\n @b=2"));
        }

        [Fact]
        public void CreateTableKeepsItsColumnLines()
        {
            Assert.Equal(L("CREATE TABLE t (", "    a INT NOT NULL,", "    b VARCHAR(10)", ")"),
                Fmt("create table t(\n a int not null,\n   b varchar(10)\n)"));
        }

        [Fact]
        public void ColumnNamesInDdlAreNotMistakenForTypes()
        {
            Assert.Equal(L("CREATE TABLE t (", "    date DATE,", "    Time time2", ")"),
                Fmt("create table t(\n date date,\n Time time2\n)"));
        }

        [Fact]
        public void FragmentsAreTidied()
        {
            Assert.Equal("a = 1 AND b = 2", Fmt("a=1  and   b=2"));
            Assert.Equal(L("WHERE a = 1", "    AND b = 2"), Fmt("where a=1 and b=2"));
        }

        [Fact]
        public void EmptyAndBlankTextComesBackAsItIs()
        {
            Assert.Equal("", Fmt(""));
            var blank = SqlFormatter.Format("  \r\n  ");
            Assert.True(blank.IsSuccess);
            Assert.False(blank.Changed);
        }

        // ---- selections ----

        [Fact]
        public void BaseIndentIsAddedToEveryLineButTheFirst()
        {
            var result = SqlFormatter.Format("select a,b from t", null, 4);
            Assert.True(result.IsSuccess);
            Assert.Equal(L("SELECT", "        a", "      , b", "    FROM t"), result.Text);
        }

        // ---- when the text cannot be formatted ----

        [Theory]
        [InlineData("select 'abc from t", "string")]
        [InlineData("select a from t /* open", "comment")]
        [InlineData("select [abc from t", "[")]
        [InlineData("select (a + 1 from t", "วงเล็บ")]
        [InlineData("select a) from t", "วงเล็บ")]
        public void IncompleteTextIsReportedAndLeftAlone(string sql, string reason)
        {
            var result = SqlFormatter.Format(sql);
            Assert.False(result.IsSuccess);
            Assert.Contains(reason, result.Error);
            Assert.Equal(sql, result.Text);
            Assert.False(result.Changed);
        }

        [Fact]
        public void TheErrorSaysWhere()
        {
            var result = SqlFormatter.Format("select a\nfrom t\nwhere (x = 1");
            Assert.Contains("บรรทัด 3", result.Error);
        }

        // ---- properties over a corpus ----

        private static readonly string[] Corpus =
        {
            "select a.id,a.name,count(*) as cnt from dbo.Orders a inner join dbo.Customers c on c.id=a.cid and c.active=1 left join x on x.id=a.id where a.status in (1,2,3) and (a.x is null or a.y>5) group by a.id,a.name having count(*)>1 order by a.id desc",
            "with a as (select 1 as x),b(y) as (select 2) select * from a,b union all select 3,4 order by 1",
            "update t set a=1,b=case when c=1 then 2 when c=2 then 3 else 4 end from t join u on u.id=t.id where t.x in (select y from z where z.q=1) and t.k between 1 and 2",
            "delete t from t join u on u.id=t.id where u.x=1 option (maxdop 1)",
            "insert into dbo.T(a,b,c) values(1,'x',null),(2,'y',N'z');select scope_identity()",
            "merge into t as x using (select 1 as id) s on x.id=s.id when matched then update set a=1 when not matched then insert (id) values (s.id) output $action;",
            "if @a=1 begin select 1 end else if @a=2 select 2 else begin print 'x' end",
            "begin try begin transaction update t set a=1 commit end try begin catch if @@trancount>0 rollback throw end catch",
            "create procedure dbo.p @a int, @b varchar(10) = 'x' as begin set nocount on; select * from t where a=@a and b like @b + '%' end",
            "declare @t table (a int, b varchar(10)); insert into @t select a,b from u; select * from @t",
            "select a, -- c1\n b /* c2 */, c from t -- c3\n where x=1 -- c4\n -- c5\n and y=2",
            "-- head\nselect 1\ngo\n\n/* block\n   comment */\nselect 2\ngo 3\n",
            "select cast(a as decimal(18,2)) as x, convert(varchar(10), b, 120) as y, 1.5e-3 as z, $5 as m, 0x1F as h, -a as n, a-b as o from t where a<>b and c!=d and e>=f",
            "select * from a cross apply dbo.f(a.id) b outer apply (select top 1 x from c where c.id=a.id order by x) d for xml path('')",
            "select a from t where a in (1000001,1000002,1000003,1000004,1000005,1000006,1000007,1000008,1000009,1000010) and (aaaaaaaaaaaaaaa = 1 or bbbbbbbbbbbbbbbbbb = 2 or cccccccccccccccccc = 3 or dddddddddddddddddd = 4)",
            "select 'it''s -- not a comment', \"quoted id\", [br]]acket], N'/* nor */' from [dbo].[Order]",
            "alter table t add constraint fk foreign key (a) references u (b) on delete cascade on update no action",
            "create table t(\n a int not null,\n b varchar(10) default ('x'),\n constraint pk primary key (a)\n)\ngo\ncreate index ix on t(b)",
            "grant select, insert on t to u",
            "select a from t order by a offset 5 rows fetch next 10 rows only",
        };

        public static IEnumerable<object[]> CorpusCases() => Corpus.Select(c => new object[] { c });

        [Theory]
        [MemberData(nameof(CorpusCases))]
        public void FormattingTwiceChangesNothing(string sql)
        {
            foreach (var options in new[] { new FormatOptions(), Trailing, new FormatOptions { IndentSize = 2, KeywordCase = KeywordCase.Lower } })
            {
                var first = SqlFormatter.Format(sql, options);
                Assert.True(first.IsSuccess, first.Error);
                var second = SqlFormatter.Format(first.Text, options);
                Assert.True(second.IsSuccess, second.Error);
                Assert.Equal(first.Text, second.Text);
                Assert.False(second.Changed);
            }
        }

        [Theory]
        [MemberData(nameof(CorpusCases))]
        public void FormattingKeepsEveryTokenAndComment(string sql)
        {
            foreach (var options in new[] { new FormatOptions(), Trailing, new FormatOptions { KeywordCase = KeywordCase.Lower } })
            {
                var result = SqlFormatter.Format(sql, options);
                Assert.True(result.IsSuccess, result.Error);
                Assert.Equal(Meaning(sql), Meaning(result.Text), StringComparer.Ordinal);
            }
        }

        [Theory]
        [MemberData(nameof(CorpusCases))]
        public void PreserveCaseNeverChangesAnyLetter(string sql)
        {
            var result = SqlFormatter.Format(sql, new FormatOptions { KeywordCase = KeywordCase.Preserve });
            Assert.True(result.IsSuccess, result.Error);
            Assert.Equal(StripLayout(sql), StripLayout(result.Text));
        }

        private static readonly HashSet<string> MayBeRecased = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            // built-in functions and types
            "count", "cast", "convert", "decimal", "varchar", "int", "isnull", "scope_identity",
            // keywords that are not reserved, only where their position says so
            "go", "partition", "apply", "nocount", "offset", "rows", "next", "only", "xml", "path", "maxdop", "matched", "source",
            "output", "using", "nolock", "recompile", "try", "catch", "no", "action", "throw", "include",
        };

        [Theory]
        [MemberData(nameof(CorpusCases))]
        public void OnlyKeywordsAreEverRecased(string sql)
        {
            var before = SqlStatementScanner.Scan(sql).Tokens.Where(t => t.Kind == TokenKind.Word).Select(t => t.Text).ToList();
            var result = SqlFormatter.Format(sql, new FormatOptions { KeywordCase = KeywordCase.Upper });
            Assert.True(result.IsSuccess, result.Error);
            var after = SqlStatementScanner.Scan(result.Text).Tokens.Where(t => t.Kind == TokenKind.Word).Select(t => t.Text).ToList();
            Assert.Equal(before.Count, after.Count);
            for (int i = 0; i < before.Count; i++)
            {
                if (before[i] == after[i]) continue;
                Assert.True(SqlKeywords.IsReserved(before[i]) || MayBeRecased.Contains(before[i]),
                    "'" + before[i] + "' became '" + after[i] + "' but is not a keyword");
            }
        }

        /// <summary>Every token's text in order, with case folded only for words the formatter may re-case, and commas
        /// left out because they move to the other end of the line.</summary>
        private static string Meaning(string sql)
        {
            var scan = SqlStatementScanner.Scan(sql);
            var parts = new List<string>();
            foreach (var t in scan.Tokens)
            {
                if (t.Kind == TokenKind.Comment) parts.Add("C:" + new string(t.Text.Where(c => !char.IsWhiteSpace(c)).ToArray()));
                else if (t.IsSymbol(',')) continue;
                else if (t.Kind == TokenKind.Word) parts.Add("W:" + t.Text.ToUpperInvariant());
                else parts.Add(t.Kind + ":" + t.Text);
            }
            return string.Join("|", parts);
        }

        /// <summary>The text without whitespace and without moving commas: what a no-case-change format must leave alone.</summary>
        private static string StripLayout(string sql) => new string(sql.Where(c => !char.IsWhiteSpace(c) && c != ',').ToArray());
    }
}
