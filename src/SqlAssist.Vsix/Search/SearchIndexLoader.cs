using System.Collections.Generic;
using System.Data.SqlClient;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using SqlAssist.Core.Schema;
using SqlAssist.Core.Search;

namespace SqlAssist.Search
{
    /// <summary>Reads one database's Search Index (schemas, objects with their module source, columns, indexes, constraints
    /// and user-defined types) in a single round trip, and lists the databases a search can cover.</summary>
    internal static class SearchIndexLoader
    {
        // Schema ids 2-4 are guest, INFORMATION_SCHEMA and sys; 16384+ are the db_* role schemas.
        private const string IndexSql = @"
SET NOCOUNT ON;
SELECT s.name FROM sys.schemas s WHERE s.schema_id NOT IN (2, 3, 4) AND s.schema_id < 16384 ORDER BY s.name;

SELECT o.object_id, s.name, o.name, o.type, p.name, m.definition, p.type
FROM sys.objects o
JOIN sys.schemas s ON s.schema_id = o.schema_id
LEFT JOIN sys.objects p ON p.object_id = o.parent_object_id AND o.type = 'TR'
LEFT JOIN sys.sql_modules m ON m.object_id = o.object_id
WHERE o.is_ms_shipped = 0 AND o.type IN ('U', 'V', 'P', 'PC', 'FN', 'IF', 'TF', 'FS', 'FT', 'TR')
ORDER BY s.name, o.name;

SELECT c.object_id, c.name, t.name, c.max_length, c.precision, c.scale, c.is_nullable
FROM sys.columns c
JOIN sys.objects o ON o.object_id = c.object_id
JOIN sys.types t ON t.user_type_id = c.user_type_id
WHERE o.is_ms_shipped = 0 AND o.type IN ('U', 'V')
ORDER BY c.object_id, c.column_id;

-- shortcut: rowstore indexes only (columnstore, XML and spatial ones are left out); add them with their own scripts if asked.
SELECT i.object_id, i.index_id, i.name, CAST(CASE WHEN i.type = 1 THEN 1 ELSE 0 END AS bit), i.is_unique, i.is_primary_key,
       i.is_unique_constraint, i.filter_definition, c.name, ic.is_descending_key, ic.is_included_column
FROM sys.indexes i
JOIN sys.objects o ON o.object_id = i.object_id
JOIN sys.index_columns ic ON ic.object_id = i.object_id AND ic.index_id = i.index_id
JOIN sys.columns c ON c.object_id = ic.object_id AND c.column_id = ic.column_id
WHERE i.type IN (1, 2) AND i.is_hypothetical = 0 AND o.is_ms_shipped = 0 AND o.type IN ('U', 'V')
ORDER BY i.object_id, i.index_id, ic.is_included_column, ic.key_ordinal, ic.index_column_id;

SELECT fk.object_id, fk.parent_object_id, fk.name, rs.name, rt.name, pc.name, rc.name
FROM sys.foreign_keys fk
JOIN sys.foreign_key_columns fkc ON fkc.constraint_object_id = fk.object_id
JOIN sys.columns pc ON pc.object_id = fkc.parent_object_id AND pc.column_id = fkc.parent_column_id
JOIN sys.objects rt ON rt.object_id = fkc.referenced_object_id
JOIN sys.schemas rs ON rs.schema_id = rt.schema_id
JOIN sys.columns rc ON rc.object_id = fkc.referenced_object_id AND rc.column_id = fkc.referenced_column_id
WHERE fk.is_ms_shipped = 0
ORDER BY fk.object_id, fkc.constraint_column_id;

SELECT cc.parent_object_id, cc.name, cc.definition FROM sys.check_constraints cc WHERE cc.is_ms_shipped = 0;

SELECT dc.parent_object_id, dc.name, dc.definition, c.name
FROM sys.default_constraints dc
JOIN sys.columns c ON c.object_id = dc.parent_object_id AND c.column_id = dc.parent_column_id
WHERE dc.is_ms_shipped = 0;

SELECT t.user_type_id, s.name, t.name, t.is_table_type, bt.name, t.max_length, t.precision, t.scale, t.is_nullable
FROM sys.types t
JOIN sys.schemas s ON s.schema_id = t.schema_id
LEFT JOIN sys.types bt ON bt.user_type_id = t.system_type_id
WHERE t.is_user_defined = 1
ORDER BY s.name, t.name;

SELECT tt.user_type_id, c.name, t.name, c.max_length, c.precision, c.scale, c.is_nullable
FROM sys.table_types tt
JOIN sys.columns c ON c.object_id = tt.type_table_object_id
JOIN sys.types t ON t.user_type_id = c.user_type_id
ORDER BY tt.user_type_id, c.column_id;";

        private const string DatabasesSql = @"
SELECT name FROM sys.databases
WHERE database_id > 4 AND state = 0 AND HAS_DBACCESS(name) = 1
ORDER BY name;";

        private sealed class ObjectRow
        {
            public string Schema, Name, Parent, Definition;
            public SearchObjectType Type, ParentType;
            public readonly List<TableColumn> Columns = new List<TableColumn>();
            public PrimaryKey Key;
        }

        public static string ForDatabase(string connectionString, string database) =>
            new SqlConnectionStringBuilder(connectionString) { InitialCatalog = database }.ConnectionString;

        /// <summary>User databases that are online and that this login can open. System databases are left out.</summary>
        public static async Task<List<string>> LoadDatabasesAsync(string connectionString, CancellationToken token)
        {
            var names = new List<string>();
            using (var conn = new SqlConnection(connectionString))
            {
                await conn.OpenAsync(token).ConfigureAwait(false);
                using (var cmd = new SqlCommand(DatabasesSql, conn) { CommandTimeout = 30 })
                using (var reader = await cmd.ExecuteReaderAsync(token).ConfigureAwait(false))
                    while (await reader.ReadAsync(token).ConfigureAwait(false))
                        names.Add(reader.GetString(0));
            }
            return names;
        }

        /// <summary>The login's default database, for a query window whose connection names none.</summary>
        public static async Task<string> CurrentDatabaseAsync(string connectionString, CancellationToken token)
        {
            using (var conn = new SqlConnection(connectionString))
            {
                await conn.OpenAsync(token).ConfigureAwait(false);
                using (var cmd = new SqlCommand("SELECT DB_NAME();", conn) { CommandTimeout = 10 })
                    return (string)await cmd.ExecuteScalarAsync(token).ConfigureAwait(false);
            }
        }

        public static async Task<List<SearchItem>> LoadAsync(string connectionString, string database, CancellationToken token)
        {
            var items = new List<SearchItem>();
            var objects = new List<ObjectRow>();
            var byId = new Dictionary<int, ObjectRow>();
            var extra = new List<SearchItem>();

            using (var conn = new SqlConnection(ForDatabase(connectionString, database)))
            {
                await conn.OpenAsync(token).ConfigureAwait(false);
                // Module source of a big database can be large; allow longer than the schema load.
                using (var cmd = new SqlCommand(IndexSql, conn) { CommandTimeout = 120 })
                using (var reader = await cmd.ExecuteReaderAsync(token).ConfigureAwait(false))
                {
                    while (await reader.ReadAsync(token).ConfigureAwait(false))
                        items.Add(new SearchItem(database, null, reader.GetString(0), SearchObjectType.Schema));

                    await reader.NextResultAsync(token).ConfigureAwait(false);
                    while (await reader.ReadAsync(token).ConfigureAwait(false))
                    {
                        var row = new ObjectRow
                        {
                            Schema = reader.GetString(1),
                            Name = reader.GetString(2),
                            Type = MapType(reader.GetString(3).Trim()),
                            Parent = reader.IsDBNull(4) ? null : reader.GetString(4),
                            // NULL for encrypted modules and CLR objects: still found by name.
                            Definition = reader.IsDBNull(5) ? null : reader.GetString(5),
                            ParentType = reader.IsDBNull(6) ? SearchObjectType.None : MapType(reader.GetString(6).Trim()),
                        };
                        objects.Add(row);
                        byId[reader.GetInt32(0)] = row;
                    }

                    await reader.NextResultAsync(token).ConfigureAwait(false);
                    while (await reader.ReadAsync(token).ConfigureAwait(false))
                    {
                        if (!byId.TryGetValue(reader.GetInt32(0), out var owner)) continue;
                        string type = SqlTypeFormatter.Format(reader.GetString(2), reader.GetInt16(3), reader.GetByte(4), reader.GetByte(5));
                        owner.Columns.Add(new TableColumn(reader.GetString(1), type, reader.GetBoolean(6)));
                    }

                    // Indexes: primary keys also feed the table's CREATE TABLE preview; PK and UNIQUE constraints are listed
                    // as constraints, the rest as indexes.
                    await reader.NextResultAsync(token).ConfigureAwait(false);
                    ObjectRow ixOwner = null;
                    string ixName = null, ixFilter = null;
                    bool ixClustered = false, ixUnique = false, ixPrimary = false, ixConstraint = false;
                    var ixKeys = new List<(string column, bool descending)>();
                    var ixIncluded = new List<string>();
                    void FlushIndex()
                    {
                        if (ixOwner == null) return;
                        if (ixPrimary || ixConstraint)
                        {
                            if (ixPrimary) ixOwner.Key = new PrimaryKey(ixName, ixClustered, ixKeys.Select(k => k.column).ToArray());
                            string body = (ixPrimary ? "PRIMARY KEY " : "UNIQUE ") + (ixClustered ? "CLUSTERED" : "NONCLUSTERED") + " (" +
                                string.Join(", ", ixKeys.Select(k => TableScriptBuilder.Bracket(k.column) + (k.descending ? " DESC" : ""))) + ")";
                            extra.Add(Child(database, ixOwner, ixName, SearchObjectType.Constraint, ixPrimary ? "PrimaryKey" : "Unique",
                                ObjectScriptBuilder.Constraint(ixOwner.Schema, ixOwner.Name, ixName, body)));
                        }
                        else
                        {
                            extra.Add(Child(database, ixOwner, ixName, SearchObjectType.Index, null,
                                ObjectScriptBuilder.Index(ixOwner.Schema, ixOwner.Name, ixName, ixUnique, ixClustered, ixKeys.ToArray(), ixIncluded.ToArray(), ixFilter)));
                        }
                    }
                    (int, int) lastIndex = (-1, -1);
                    while (await reader.ReadAsync(token).ConfigureAwait(false))
                    {
                        var id = (reader.GetInt32(0), reader.GetInt32(1));
                        if (id != lastIndex)
                        {
                            FlushIndex();
                            lastIndex = id;
                            byId.TryGetValue(id.Item1, out ixOwner);
                            ixName = reader.GetString(2);
                            ixClustered = reader.GetBoolean(3);
                            ixUnique = reader.GetBoolean(4);
                            ixPrimary = reader.GetBoolean(5);
                            ixConstraint = reader.GetBoolean(6);
                            ixFilter = reader.IsDBNull(7) ? null : reader.GetString(7);
                            ixKeys.Clear();
                            ixIncluded.Clear();
                        }
                        if (reader.GetBoolean(10)) ixIncluded.Add(reader.GetString(8));
                        else ixKeys.Add((reader.GetString(8), reader.GetBoolean(9)));
                    }
                    FlushIndex();

                    await reader.NextResultAsync(token).ConfigureAwait(false);
                    ObjectRow fkOwner = null;
                    string fkName = null, fkTarget = null;
                    var fkFrom = new List<string>();
                    var fkTo = new List<string>();
                    void FlushForeignKey()
                    {
                        if (fkOwner == null) return;
                        string body = "FOREIGN KEY (" + string.Join(", ", fkFrom.Select(TableScriptBuilder.Bracket)) + ") REFERENCES " + fkTarget +
                            " (" + string.Join(", ", fkTo.Select(TableScriptBuilder.Bracket)) + ")";
                        extra.Add(Child(database, fkOwner, fkName, SearchObjectType.Constraint, "ForeignKey",
                            ObjectScriptBuilder.Constraint(fkOwner.Schema, fkOwner.Name, fkName, body)));
                    }
                    int lastFk = -1;
                    while (await reader.ReadAsync(token).ConfigureAwait(false))
                    {
                        int id = reader.GetInt32(0);
                        if (id != lastFk)
                        {
                            FlushForeignKey();
                            lastFk = id;
                            byId.TryGetValue(reader.GetInt32(1), out fkOwner);
                            fkName = reader.GetString(2);
                            fkTarget = TableScriptBuilder.Bracket(reader.GetString(3)) + "." + TableScriptBuilder.Bracket(reader.GetString(4));
                            fkFrom.Clear();
                            fkTo.Clear();
                        }
                        fkFrom.Add(reader.GetString(5));
                        fkTo.Add(reader.GetString(6));
                    }
                    FlushForeignKey();

                    await reader.NextResultAsync(token).ConfigureAwait(false);
                    while (await reader.ReadAsync(token).ConfigureAwait(false))
                    {
                        if (!byId.TryGetValue(reader.GetInt32(0), out var owner)) continue;
                        string name = reader.GetString(1);
                        extra.Add(Child(database, owner, name, SearchObjectType.Constraint, "Check",
                            ObjectScriptBuilder.Constraint(owner.Schema, owner.Name, name, "CHECK " + reader.GetString(2))));
                    }

                    await reader.NextResultAsync(token).ConfigureAwait(false);
                    while (await reader.ReadAsync(token).ConfigureAwait(false))
                    {
                        if (!byId.TryGetValue(reader.GetInt32(0), out var owner)) continue;
                        string name = reader.GetString(1);
                        extra.Add(Child(database, owner, name, SearchObjectType.Constraint, "Default",
                            ObjectScriptBuilder.Constraint(owner.Schema, owner.Name, name,
                                "DEFAULT " + reader.GetString(2) + " FOR " + TableScriptBuilder.Bracket(reader.GetString(3)))));
                    }

                    // User-defined types: alias types are scripted at once, table types once their columns are read.
                    await reader.NextResultAsync(token).ConfigureAwait(false);
                    var tableTypes = new Dictionary<int, (string schema, string name, List<TableColumn> columns)>();
                    while (await reader.ReadAsync(token).ConfigureAwait(false))
                    {
                        string schema = reader.GetString(1), name = reader.GetString(2);
                        if (reader.GetBoolean(3))
                        {
                            tableTypes[reader.GetInt32(0)] = (schema, name, new List<TableColumn>());
                            continue;
                        }
                        string script = reader.IsDBNull(4) ? null : ObjectScriptBuilder.AliasType(schema, name,
                            SqlTypeFormatter.Format(reader.GetString(4), reader.GetInt16(5), reader.GetByte(6), reader.GetByte(7)), reader.GetBoolean(8));
                        extra.Add(new SearchItem(database, schema, name, SearchObjectType.UserDefinedType, script: script, subKind: "Alias"));
                    }

                    await reader.NextResultAsync(token).ConfigureAwait(false);
                    while (await reader.ReadAsync(token).ConfigureAwait(false))
                    {
                        if (!tableTypes.TryGetValue(reader.GetInt32(0), out var tt)) continue;
                        string type = SqlTypeFormatter.Format(reader.GetString(2), reader.GetInt16(3), reader.GetByte(4), reader.GetByte(5));
                        tt.columns.Add(new TableColumn(reader.GetString(1), type, reader.GetBoolean(6)));
                    }
                    foreach (var tt in tableTypes.Values)
                        extra.Add(new SearchItem(database, tt.schema, tt.name, SearchObjectType.UserDefinedType,
                            script: TableScriptBuilder.BuildTableType(tt.schema, tt.name, tt.columns, null), subKind: "Table"));
                }
            }

            foreach (var o in objects)
            {
                string script = o.Type == SearchObjectType.Table ? TableScriptBuilder.Build(o.Schema, o.Name, o.Columns, o.Key) : null;
                items.Add(new SearchItem(database, o.Schema, o.Name, o.Type, o.Parent, o.Definition, script, o.ParentType));
                // A column previews its owner: the same string instance, so it costs no memory per column.
                string ownerPreview = o.Definition ?? script;
                foreach (var c in o.Columns)
                    items.Add(new SearchItem(database, o.Schema, c.Name, SearchObjectType.Column, o.Name, null, ownerPreview, o.Type));
            }
            items.AddRange(extra);
            return items;
        }

        /// <summary>An index or constraint of a table or view; found by name only, its script is the preview.</summary>
        private static SearchItem Child(string database, ObjectRow owner, string name, SearchObjectType type, string subKind, string script) =>
            new SearchItem(database, owner.Schema, name, type, owner.Name, null, script, owner.Type, subKind);

        private static SearchObjectType MapType(string type)
        {
            switch (type)
            {
                case "U": return SearchObjectType.Table;
                case "V": return SearchObjectType.View;
                case "P": case "PC": return SearchObjectType.Procedure;
                case "TR": return SearchObjectType.Trigger;
                default: return SearchObjectType.Function;
            }
        }
    }
}
