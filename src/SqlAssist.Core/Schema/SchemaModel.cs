using System;
using System.Collections.Generic;
using System.Linq;
using SqlAssist.Core.Sql;

namespace SqlAssist.Core.Schema
{
    public sealed class DbColumn
    {
        public DbColumn(string name, string typeName, bool isNullable = true, bool isIdentity = false, bool isPrimaryKey = false,
            string description = null, DbObject origin = null, string originColumn = null, bool isComputed = false)
        {
            Name = name; TypeName = typeName; IsNullable = isNullable; IsIdentity = isIdentity; IsPrimaryKey = isPrimaryKey;
            Description = description; Origin = origin; OriginColumn = originColumn; IsComputed = isComputed;
        }
        /// <summary>A computed column: INSERT and UPDATE templates leave it out.</summary>
        public bool IsComputed { get; }
        public string Name { get; }
        /// <summary>Declared type (nvarchar(50)); empty when unknown, e.g. a CTE column computed by an expression.</summary>
        public string TypeName { get; }
        public bool IsNullable { get; }
        public bool IsIdentity { get; }
        public bool IsPrimaryKey { get; }
        /// <summary>The column's MS_Description extended property, if the database has one.</summary>
        public string Description { get; }
        /// <summary>For a CTE / derived-table column that passes a real column through: that table and column,
        /// so its foreign keys can still be described.</summary>
        public DbObject Origin { get; }
        public string OriginColumn { get; }
    }

    public sealed class DbObject
    {
        public DbObject(string schema, string name, bool isView, IReadOnlyList<DbColumn> columns, long? rowCount = null,
            string description = null, string kind = null)
        {
            Schema = schema; Name = name; IsView = isView; Columns = columns; RowCount = rowCount;
            Description = description; Kind = kind ?? (isView ? "View" : "Table");
        }
        /// <summary>Null for objects that only exist in the query (CTEs, derived tables).</summary>
        public string Schema { get; }
        public string Name { get; }
        public bool IsView { get; }
        public IReadOnlyList<DbColumn> Columns { get; }
        /// <summary>Estimated rows (sum of heap/clustered partitions); null for views or when unknown.</summary>
        public long? RowCount { get; }
        /// <summary>The table's MS_Description extended property, if any.</summary>
        public string Description { get; }
        /// <summary>"Table", "View", "CTE" or "Derived table".</summary>
        public string Kind { get; }
        public override string ToString() => Schema == null ? Name : Schema + "." + Name;
    }

    /// <summary>A stored procedure or function parameter.</summary>
    public sealed class DbParameter
    {
        public DbParameter(string name, string typeName, bool isOutput = false, bool isReadOnly = false)
        {
            Name = name; TypeName = typeName; IsOutput = isOutput; IsReadOnly = isReadOnly;
        }
        /// <summary>With the leading '@'.</summary>
        public string Name { get; }
        public string TypeName { get; }
        public bool IsOutput { get; }
        /// <summary>A table-valued parameter.</summary>
        public bool IsReadOnly { get; }
    }

    public enum RoutineKind { Procedure, ScalarFunction, TableFunction }

    /// <summary>A stored procedure or function: EXEC templates, and the completion list's parameter panel.</summary>
    public sealed class DbRoutine
    {
        public DbRoutine(string schema, string name, RoutineKind kind, IReadOnlyList<DbParameter> parameters,
            string returnType = null, string description = null, IReadOnlyList<DbColumn> columns = null)
        {
            Schema = schema; Name = name; Kind = kind; Parameters = parameters;
            ReturnType = returnType ?? (kind == RoutineKind.TableFunction ? "TABLE" : null);
            Description = description;
            Columns = columns ?? new DbColumn[0];
        }
        public string Schema { get; }
        public string Name { get; }
        public RoutineKind Kind { get; }
        public bool IsProcedure => Kind == RoutineKind.Procedure;
        public IReadOnlyList<DbParameter> Parameters { get; }
        /// <summary>A scalar function's return type ("money"); "TABLE" for table-valued functions; null for procedures.</summary>
        public string ReturnType { get; }
        /// <summary>The MS_Description extended property, if any.</summary>
        public string Description { get; }
        /// <summary>A table-valued function's result columns; empty for everything else.</summary>
        public IReadOnlyList<DbColumn> Columns { get; }
        public override string ToString() => Schema + "." + Name;
    }

    public sealed class ForeignKey
    {
        public ForeignKey(string name, DbObject from, DbObject to, IReadOnlyList<(string from, string to)> columns)
        {
            Name = name; From = from; To = to; Columns = columns;
        }
        public string Name { get; }
        /// <summary>The referencing (child) table.</summary>
        public DbObject From { get; }
        /// <summary>The referenced (parent) table.</summary>
        public DbObject To { get; }
        /// <summary>Column pairs in key order: From column → To column.</summary>
        public IReadOnlyList<(string from, string to)> Columns { get; }
    }

    public sealed class SchemaModel
    {
        public const string DefaultSchema = "dbo";

        private readonly Dictionary<string, List<DbObject>> _byName = new Dictionary<string, List<DbObject>>(StringComparer.OrdinalIgnoreCase);
        private readonly Dictionary<DbObject, List<ForeignKey>> _from = new Dictionary<DbObject, List<ForeignKey>>();
        private readonly Dictionary<DbObject, List<ForeignKey>> _to = new Dictionary<DbObject, List<ForeignKey>>();
        private readonly Dictionary<DbRoutine, DbObject> _functionTables = new Dictionary<DbRoutine, DbObject>();

        public SchemaModel(IReadOnlyList<DbObject> objects, IReadOnlyList<ForeignKey> foreignKeys, IReadOnlyList<DbRoutine> routines = null)
        {
            Objects = objects;
            ForeignKeys = foreignKeys;
            Routines = routines ?? new DbRoutine[0];
            foreach (var r in Routines)
                if (r.Columns.Count > 0)
                    _functionTables[r] = new DbObject(r.Schema, r.Name, false, r.Columns, description: r.Description, kind: "Function");
            foreach (var o in objects)
            {
                if (!_byName.TryGetValue(o.Name, out var list)) _byName[o.Name] = list = new List<DbObject>();
                list.Add(o);
            }
            foreach (var fk in foreignKeys)
            {
                Add(_from, fk.From, fk);
                Add(_to, fk.To, fk);
            }
        }

        public static SchemaModel Empty { get; } = new SchemaModel(new DbObject[0], new ForeignKey[0]);

        public IReadOnlyList<DbObject> Objects { get; }
        public IReadOnlyList<ForeignKey> ForeignKeys { get; }

        public IReadOnlyList<DbRoutine> Routines { get; }

        /// <summary>Finds a procedure or function, with the same schema rule as <see cref="Find(string, string)"/>.</summary>
        public DbRoutine FindRoutine(string schema, string name)
        {
            var list = Routines.Where(r => string.Equals(r.Name, name, StringComparison.OrdinalIgnoreCase)).ToList();
            if (schema != null) return list.FirstOrDefault(r => string.Equals(r.Schema, schema, StringComparison.OrdinalIgnoreCase));
            return list.FirstOrDefault(r => string.Equals(r.Schema, DefaultSchema, StringComparison.OrdinalIgnoreCase))
                ?? (list.Count == 1 ? list[0] : null);
        }

        public IEnumerable<string> Schemas => Objects.Select(o => o.Schema).Distinct(StringComparer.OrdinalIgnoreCase);

        /// <summary>Finds a table or view. Without a schema, dbo wins, then any unique match.</summary>
        public DbObject Find(string schema, string name)
        {
            if (name == null || !_byName.TryGetValue(name, out var list)) return null;
            if (schema != null) return list.FirstOrDefault(o => string.Equals(o.Schema, schema, StringComparison.OrdinalIgnoreCase));
            return list.FirstOrDefault(o => string.Equals(o.Schema, DefaultSchema, StringComparison.OrdinalIgnoreCase))
                ?? (list.Count == 1 ? list[0] : null);
        }

        /// <summary>The object a FROM/JOIN reference names: a CTE or derived table of the query when it is one,
        /// otherwise a table or view of the database.</summary>
        public DbObject Find(TableRef r)
        {
            if (r.Derived != null) return r.Derived.Resolve(this);
            if (r.IsFunction) return FindFunctionTable(r.Schema, r.Name);
            return r.HasColumns ? Find(r.Schema, r.Name) : null;
        }

        /// <summary>Whether the query names a table or view this schema does not have (it may have just been created, so a
        /// reload is worth it). Functions do not count: built-in ones (STRING_SPLIT, OPENJSON) and old-style hints
        /// ("FROM t (NOLOCK)") are never in the schema and would reload it again and again.</summary>
        public bool HasUnknownTables(IEnumerable<TableRef> tables) => tables.Any(t => t.HasColumns && !t.IsFunction && Find(t) == null);

        /// <summary>A table-valued function as a table of its result columns (the same instance every time), so
        /// "FROM dbo.fn(1) f" works like a table; null for other routines or when the columns are unknown.</summary>
        public DbObject FindFunctionTable(string schema, string name)
        {
            var routine = FindRoutine(schema, name);
            return routine != null && _functionTables.TryGetValue(routine, out var table) ? table : null;
        }

        public IReadOnlyList<ForeignKey> ForeignKeysFrom(DbObject o) => _from.TryGetValue(o, out var l) ? l : (IReadOnlyList<ForeignKey>)new ForeignKey[0];
        public IReadOnlyList<ForeignKey> ForeignKeysTo(DbObject o) => _to.TryGetValue(o, out var l) ? l : (IReadOnlyList<ForeignKey>)new ForeignKey[0];

        /// <summary>The name to insert for an object: dbo objects bare, others schema-qualified.</summary>
        public static string DisplayName(DbObject o)
        {
            string name = SqlIdentifier.QuoteIfNeeded(o.Name);
            return o.Schema == null || string.Equals(o.Schema, DefaultSchema, StringComparison.OrdinalIgnoreCase)
                ? name
                : SqlIdentifier.QuoteIfNeeded(o.Schema) + "." + name;
        }

        private static void Add(Dictionary<DbObject, List<ForeignKey>> map, DbObject key, ForeignKey fk)
        {
            if (!map.TryGetValue(key, out var list)) map[key] = list = new List<ForeignKey>();
            list.Add(fk);
        }
    }
}
