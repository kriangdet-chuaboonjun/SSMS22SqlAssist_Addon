using System.Collections.Generic;
using System.Data.SqlClient;
using System.Threading;
using System.Threading.Tasks;
using SqlAssist.Core.Schema;

namespace SqlAssist.Schema
{
    /// <summary>Reads tables, views, their columns, foreign keys and procedure/function parameters of one database in a
    /// single round trip.</summary>
    internal static class SchemaLoader
    {
        private const string Sql = @"
SET NOCOUNT ON;
WITH pk AS (
    SELECT ic.object_id, ic.column_id
    FROM sys.indexes i
    JOIN sys.index_columns ic ON ic.object_id = i.object_id AND ic.index_id = i.index_id
    WHERE i.is_primary_key = 1
), rc AS (
    -- Estimated row count: heap (0) or clustered index (1) partitions, the same figure SSMS shows.
    SELECT object_id, SUM(rows) AS row_count FROM sys.partitions WHERE index_id IN (0, 1) GROUP BY object_id
)
SELECT s.name, o.name, o.type, c.name, t.name, c.max_length, c.precision, c.scale,
       c.is_nullable, c.is_identity, CAST(CASE WHEN pk.column_id IS NULL THEN 0 ELSE 1 END AS bit), rc.row_count,
       CAST(cd.value AS nvarchar(2000)), CAST(td.value AS nvarchar(2000)), c.is_computed
FROM sys.objects o
JOIN sys.schemas s ON s.schema_id = o.schema_id
JOIN sys.columns c ON c.object_id = o.object_id
JOIN sys.types t ON t.user_type_id = c.user_type_id
LEFT JOIN pk ON pk.object_id = c.object_id AND pk.column_id = c.column_id
LEFT JOIN rc ON rc.object_id = o.object_id
-- MS_Description: what SSMS's table designer and most documentation tools write.
LEFT JOIN sys.extended_properties cd ON cd.class = 1 AND cd.major_id = c.object_id AND cd.minor_id = c.column_id AND cd.name = 'MS_Description'
LEFT JOIN sys.extended_properties td ON td.class = 1 AND td.major_id = o.object_id AND td.minor_id = 0 AND td.name = 'MS_Description'
-- Table-valued functions' result columns come along; they are attached to the routine, not listed as tables.
WHERE o.type IN ('U', 'V', 'IF', 'TF', 'FT') AND o.is_ms_shipped = 0
ORDER BY s.name, o.name, c.column_id;

SELECT fk.name, ps.name, pt.name, rs.name, rt.name, pc.name, rc.name
FROM sys.foreign_keys fk
JOIN sys.foreign_key_columns fkc ON fkc.constraint_object_id = fk.object_id
JOIN sys.objects pt ON pt.object_id = fkc.parent_object_id
JOIN sys.schemas ps ON ps.schema_id = pt.schema_id
JOIN sys.columns pc ON pc.object_id = fkc.parent_object_id AND pc.column_id = fkc.parent_column_id
JOIN sys.objects rt ON rt.object_id = fkc.referenced_object_id
JOIN sys.schemas rs ON rs.schema_id = rt.schema_id
JOIN sys.columns rc ON rc.object_id = fkc.referenced_object_id AND rc.column_id = fkc.referenced_column_id
ORDER BY fk.object_id, fkc.constraint_column_id;

-- Procedures and functions with their parameters (a routine without any has one NULL row). Parameter 0 is a
-- scalar function's return value.
SELECT s.name, o.name, o.type, p.name, t.name, p.max_length, p.precision, p.scale, p.is_output, p.is_readonly,
       p.parameter_id, CAST(d.value AS nvarchar(2000))
FROM sys.objects o
JOIN sys.schemas s ON s.schema_id = o.schema_id
LEFT JOIN sys.parameters p ON p.object_id = o.object_id
LEFT JOIN sys.types t ON t.user_type_id = p.user_type_id
LEFT JOIN sys.extended_properties d ON d.class = 1 AND d.major_id = o.object_id AND d.minor_id = 0 AND d.name = 'MS_Description'
WHERE o.type IN ('P', 'PC', 'FN', 'IF', 'TF', 'FS', 'FT') AND o.is_ms_shipped = 0
ORDER BY s.name, o.name, p.parameter_id;";

        public static async Task<SchemaModel> LoadAsync(string connectionString, CancellationToken token)
        {
            var objects = new List<DbObject>();
            var byKey = new Dictionary<string, DbObject>(System.StringComparer.OrdinalIgnoreCase);
            var fks = new List<ForeignKey>();
            var routines = new List<DbRoutine>();
            var functionColumns = new Dictionary<string, DbColumn[]>(System.StringComparer.OrdinalIgnoreCase);

            using (var conn = new SqlConnection(connectionString))
            {
                await conn.OpenAsync(token).ConfigureAwait(false);
                using (var cmd = new SqlCommand(Sql, conn) { CommandTimeout = 30 })
                using (var reader = await cmd.ExecuteReaderAsync(token).ConfigureAwait(false))
                {
                    string curSchema = null, curName = null, curType = null;
                    long? curRows = null;
                    string curDescription = null;
                    var columns = new List<DbColumn>();
                    void FlushObject()
                    {
                        if (curName == null) return;
                        string type = curType.Trim();
                        if (type != "U" && type != "V")
                        {
                            functionColumns[curSchema + "." + curName] = columns.ToArray();
                            return;
                        }
                        var o = new DbObject(curSchema, curName, type == "V", columns.ToArray(), curRows, curDescription);
                        objects.Add(o);
                        byKey[curSchema + "." + curName] = o;
                    }

                    while (await reader.ReadAsync(token).ConfigureAwait(false))
                    {
                        string schema = reader.GetString(0), name = reader.GetString(1);
                        if (schema != curSchema || name != curName)
                        {
                            FlushObject();
                            curSchema = schema; curName = name; curType = reader.GetString(2);
                            curRows = reader.IsDBNull(11) ? (long?)null : reader.GetInt64(11);
                            curDescription = reader.IsDBNull(13) ? null : reader.GetString(13);
                            columns.Clear();
                        }
                        columns.Add(new DbColumn(
                            reader.GetString(3),
                            SqlTypeFormatter.Format(reader.GetString(4), reader.GetInt16(5), reader.GetByte(6), reader.GetByte(7)),
                            isNullable: reader.GetBoolean(8),
                            isIdentity: reader.GetBoolean(9),
                            isPrimaryKey: reader.GetBoolean(10),
                            description: reader.IsDBNull(12) ? null : reader.GetString(12),
                            isComputed: reader.GetBoolean(14)));
                    }
                    FlushObject();

                    await reader.NextResultAsync(token).ConfigureAwait(false);
                    string curFk = null;
                    DbObject from = null, to = null;
                    var pairs = new List<(string, string)>();
                    void FlushKey()
                    {
                        if (curFk != null && from != null && to != null) fks.Add(new ForeignKey(curFk, from, to, pairs.ToArray()));
                    }

                    while (await reader.ReadAsync(token).ConfigureAwait(false))
                    {
                        string fkName = reader.GetString(0);
                        if (fkName != curFk)
                        {
                            FlushKey();
                            curFk = fkName;
                            byKey.TryGetValue(reader.GetString(1) + "." + reader.GetString(2), out from);
                            byKey.TryGetValue(reader.GetString(3) + "." + reader.GetString(4), out to);
                            pairs.Clear();
                        }
                        pairs.Add((reader.GetString(5), reader.GetString(6)));
                    }
                    FlushKey();

                    await reader.NextResultAsync(token).ConfigureAwait(false);
                    string rSchema = null, rName = null, rReturns = null, rDescription = null;
                    var rKind = RoutineKind.Procedure;
                    var parameters = new List<DbParameter>();
                    void FlushRoutine()
                    {
                        if (rName != null) routines.Add(new DbRoutine(rSchema, rName, rKind, parameters.ToArray(), rReturns, rDescription,
                            functionColumns.TryGetValue(rSchema + "." + rName, out var tvf) ? tvf : null));
                    }

                    while (await reader.ReadAsync(token).ConfigureAwait(false))
                    {
                        string schema = reader.GetString(0), name = reader.GetString(1);
                        if (schema != rSchema || name != rName)
                        {
                            FlushRoutine();
                            rSchema = schema; rName = name; rReturns = null;
                            rDescription = reader.IsDBNull(11) ? null : reader.GetString(11);
                            string type = reader.GetString(2).Trim();
                            rKind = type == "P" || type == "PC" ? RoutineKind.Procedure
                                : type == "FN" || type == "FS" ? RoutineKind.ScalarFunction
                                : RoutineKind.TableFunction;
                            parameters.Clear();
                        }
                        if (reader.IsDBNull(10) || reader.IsDBNull(4)) continue;
                        string typeName = SqlTypeFormatter.Format(reader.GetString(4), reader.GetInt16(5), reader.GetByte(6), reader.GetByte(7));
                        if (reader.GetInt32(10) == 0) { rReturns = typeName; continue; }
                        parameters.Add(new DbParameter(reader.GetString(3), typeName,
                            isOutput: reader.GetBoolean(8),
                            isReadOnly: reader.GetBoolean(9)));
                    }
                    FlushRoutine();
                }
            }
            return new SchemaModel(objects, fks, routines);
        }
    }
}
