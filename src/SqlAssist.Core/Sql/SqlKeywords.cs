using System;
using System.Collections.Generic;
using System.Linq;

namespace SqlAssist.Core.Sql
{
    public static class SqlKeywords
    {
        // T-SQL reserved keywords: never valid as a bare alias or column name.
        private static readonly HashSet<string> Reserved = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "ADD","ALL","ALTER","AND","ANY","AS","ASC","AUTHORIZATION","BACKUP","BEGIN","BETWEEN","BREAK","BROWSE","BULK","BY",
            "CASCADE","CASE","CHECK","CHECKPOINT","CLOSE","CLUSTERED","COALESCE","COLLATE","COLUMN","COMMIT","COMPUTE","CONSTRAINT",
            "CONTAINS","CONTAINSTABLE","CONTINUE","CONVERT","CREATE","CROSS","CURRENT","CURRENT_DATE","CURRENT_TIME","CURRENT_TIMESTAMP",
            "CURRENT_USER","CURSOR","DATABASE","DBCC","DEALLOCATE","DECLARE","DEFAULT","DELETE","DENY","DESC","DISK","DISTINCT",
            "DISTRIBUTED","DOUBLE","DROP","DUMP","ELSE","END","ERRLVL","ESCAPE","EXCEPT","EXEC","EXECUTE","EXISTS","EXIT","EXTERNAL",
            "FETCH","FILE","FILLFACTOR","FOR","FOREIGN","FREETEXT","FREETEXTTABLE","FROM","FULL","FUNCTION","GOTO","GRANT","GROUP",
            "HAVING","HOLDLOCK","IDENTITY","IDENTITY_INSERT","IDENTITYCOL","IF","IN","INDEX","INNER","INSERT","INTERSECT","INTO","IS",
            "JOIN","KEY","KILL","LEFT","LIKE","LINENO","LOAD","MERGE","NATIONAL","NOCHECK","NONCLUSTERED","NOT","NULL","NULLIF","OF",
            "OFF","OFFSETS","ON","OPEN","OPENDATASOURCE","OPENQUERY","OPENROWSET","OPENXML","OPTION","OR","ORDER","OUTER","OVER",
            "PERCENT","PIVOT","PLAN","PRECISION","PRIMARY","PRINT","PROC","PROCEDURE","PUBLIC","RAISERROR","READ","READTEXT",
            "RECONFIGURE","REFERENCES","REPLICATION","RESTORE","RESTRICT","RETURN","REVERT","REVOKE","RIGHT","ROLLBACK","ROWCOUNT",
            "ROWGUIDCOL","RULE","SAVE","SCHEMA","SECURITYAUDIT","SELECT","SEMANTICKEYPHRASETABLE","SEMANTICSIMILARITYDETAILSTABLE",
            "SEMANTICSIMILARITYTABLE","SESSION_USER","SET","SETUSER","SHUTDOWN","SOME","STATISTICS","SYSTEM_USER","TABLE",
            "TABLESAMPLE","TEXTSIZE","THEN","TO","TOP","TRAN","TRANSACTION","TRIGGER","TRUNCATE","TRY_CONVERT","TSEQUAL","UNION",
            "UNIQUE","UNPIVOT","UPDATE","UPDATETEXT","USE","USER","VALUES","VARYING","VIEW","WAITFOR","WHEN","WHERE","WHILE","WITH",
            "WITHIN","WRITETEXT"
        };

        // Not reserved, but reading one of these as a table alias would be wrong.
        private static readonly HashSet<string> NotAnAlias = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "APPLY","GO","WINDOW","OFFSET","OUTPUT","FORCESEEK","NOLOCK"
        };

        public static bool IsReserved(string word) => word != null && Reserved.Contains(word);

        public static bool CanBeAlias(string word) =>
            !string.IsNullOrEmpty(word) && !Reserved.Contains(word) && !NotAnAlias.Contains(word);

        /// <summary>Built-in function names (upper case). The formatter re-cases them only when "(" follows.</summary>
        public static readonly HashSet<string> BuiltInFunctions = new HashSet<string>
        {
            "COUNT", "COUNT_BIG", "SUM", "AVG", "MIN", "MAX", "STDEV", "STDEVP", "VAR", "VARP", "CAST", "TRY_CAST", "PARSE", "TRY_PARSE",
            "ISNULL", "IIF", "CHOOSE", "GETDATE", "GETUTCDATE", "SYSDATETIME", "SYSUTCDATETIME", "SYSDATETIMEOFFSET", "DATEADD", "DATEDIFF",
            "DATEDIFF_BIG", "DATEPART", "DATENAME", "DATEFROMPARTS", "EOMONTH", "YEAR", "MONTH", "DAY", "LEN", "DATALENGTH", "SUBSTRING",
            "LTRIM", "RTRIM", "TRIM", "UPPER", "LOWER", "REPLACE", "REPLICATE", "REVERSE", "STUFF", "CHARINDEX", "PATINDEX", "CONCAT",
            "CONCAT_WS", "FORMAT", "STRING_AGG", "STRING_SPLIT", "ABS", "CEILING", "FLOOR", "ROUND", "POWER", "SQRT", "SIGN", "RAND",
            "ROW_NUMBER", "RANK", "DENSE_RANK", "NTILE", "LAG", "LEAD", "FIRST_VALUE", "LAST_VALUE", "NEWID", "NEWSEQUENTIALID",
            "SCOPE_IDENTITY", "IDENT_CURRENT", "OBJECT_ID", "OBJECT_NAME", "DB_NAME", "DB_ID", "SCHEMA_NAME", "COL_LENGTH", "ISNUMERIC",
            "ISDATE", "ISJSON", "JSON_VALUE", "JSON_QUERY", "JSON_MODIFY", "OPENJSON", "CHECKSUM", "BINARY_CHECKSUM", "HASHBYTES",
            "ERROR_MESSAGE", "ERROR_NUMBER", "ERROR_SEVERITY", "ERROR_STATE", "ERROR_LINE", "ERROR_PROCEDURE", "XACT_STATE", "APP_NAME",
            "HOST_NAME", "SUSER_SNAME", "SUSER_NAME", "USER_NAME",
        };

        /// <summary>Keywords and built-in functions offered in completion lists, so a fully typed keyword is an exact match
        /// (and Enter commits it) instead of committing whatever column happens to start the same way.</summary>
        public static readonly string[] CompletionKeywords = new[]
        {
            "SELECT","FROM","WHERE","AND","OR","NOT","NULL","IS","IN","EXISTS","BETWEEN","LIKE","AS","ON",
            "JOIN","INNER JOIN","LEFT JOIN","RIGHT JOIN","FULL JOIN","CROSS JOIN","CROSS APPLY","OUTER APPLY",
            "GROUP BY","ORDER BY","HAVING","DISTINCT","TOP","UNION","UNION ALL","EXCEPT","INTERSECT",
            "INSERT","INTO","VALUES","UPDATE","SET","DELETE","MERGE","USING","WHEN","THEN","ELSE","END","CASE",
            "BEGIN","COMMIT","ROLLBACK","TRANSACTION","DECLARE","EXEC","CREATE","ALTER","DROP","TRUNCATE","TABLE",
            "VIEW","PROCEDURE","FUNCTION","INDEX","RETURN","IF","WHILE","PRINT","USE","WITH","NOLOCK","ASC","DESC",
            "OFFSET","FETCH","NEXT","ROWS","ONLY","OVER","PARTITION BY","OUTPUT","COUNT","SUM","AVG","MIN","MAX",
            "CAST","CONVERT","ISNULL","COALESCE","GETDATE","DATEADD","DATEDIFF","ROW_NUMBER","NVARCHAR","VARCHAR",
            "INT","BIGINT","DECIMAL","DATETIME","DATE","BIT"
        }.Union(BuiltInFunctions).ToArray();
    }
}
