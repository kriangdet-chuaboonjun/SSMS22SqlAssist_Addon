using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;

namespace SqlAssist.Core.Schema
{
    public enum ColumnKeyKind { None, PrimaryKey, ForeignKey, Parameter }

    public sealed class ColumnDetail
    {
        public string Name { get; set; }
        /// <summary>Declared type in upper case: NVARCHAR(50), DATETIME2(3).</summary>
        public string Type { get; set; }
        /// <summary>IDENTITY, or NOT NULL, or empty.</summary>
        public string Flags { get; set; }
        public ColumnKeyKind Key { get; set; }
    }

    /// <summary>What the completion list shows beside a selected table or view: its columns with type, key and
    /// nullability, and the estimated row count.</summary>
    public sealed class TableDetails
    {
        /// <summary>Null for a CTE or derived table.</summary>
        public string Schema { get; set; }
        public string Name { get; set; }
        /// <summary>"Table", "View", "CTE", "Derived table", "Procedure" or "Function".</summary>
        public string Kind { get; set; }
        /// <summary>For a function: what it returns ("MONEY", "TABLE").</summary>
        public string Returns { get; set; }
        /// <summary>The table's MS_Description, if any.</summary>
        public string Description { get; set; }
        public IReadOnlyList<ColumnDetail> Columns { get; set; }
        /// <summary>Columns left out to keep the panel on screen (see <see cref="Build"/>).</summary>
        public int HiddenColumns { get; set; }
        /// <summary>"60,533", or null for views and unknown counts.</summary>
        public string RowCount { get; set; }

        /// <summary>A procedure or function: one row per parameter — name, type, INPUT / OUTPUT / READONLY — as in
        /// "dbo.ufnGetProductDealerPrice (Function) returns MONEY".</summary>
        public static TableDetails Build(DbRoutine r) => new TableDetails
        {
            Schema = r.Schema,
            Name = r.Name,
            Kind = r.IsProcedure ? "Procedure" : "Function",
            Returns = r.ReturnType?.ToUpperInvariant(),
            Description = string.IsNullOrWhiteSpace(r.Description) ? null : r.Description.Trim(),
            Columns = r.Parameters.Select(p => new ColumnDetail
            {
                Name = p.Name,
                Type = (p.TypeName ?? "").ToUpperInvariant(),
                Flags = p.IsOutput ? "OUTPUT" : p.IsReadOnly ? "READONLY" : "INPUT",
                Key = ColumnKeyKind.Parameter,
            }).ToList(),
        };

        public static TableDetails Build(SchemaModel schema, DbObject o, int maxColumns = 60)
        {
            var fkColumns = new HashSet<string>(
                schema.ForeignKeysFrom(o).SelectMany(fk => fk.Columns.Select(c => c.from)), StringComparer.OrdinalIgnoreCase);
            // A CTE column that passes a foreign-key column through is still that key.
            bool IsForeignKey(DbColumn c) => fkColumns.Contains(c.Name) || (c.Origin != null &&
                schema.ForeignKeysFrom(c.Origin).Any(fk => fk.Columns.Any(p => string.Equals(p.from, c.OriginColumn, StringComparison.OrdinalIgnoreCase))));

            var columns = o.Columns.Take(maxColumns).Select(c => new ColumnDetail
            {
                Name = c.Name,
                Type = (c.TypeName ?? "").ToUpperInvariant(),
                // IDENTITY implies NOT NULL, so one word is enough.
                Flags = c.IsIdentity ? "IDENTITY" : c.IsNullable ? "" : "NOT NULL",
                Key = c.IsPrimaryKey ? ColumnKeyKind.PrimaryKey : IsForeignKey(c) ? ColumnKeyKind.ForeignKey : ColumnKeyKind.None,
            }).ToList();

            return new TableDetails
            {
                Schema = o.Schema,
                Name = o.Name,
                Kind = o.Kind,
                Returns = o.Kind == "Function" ? "TABLE" : null,
                Description = string.IsNullOrWhiteSpace(o.Description) ? null : o.Description.Trim(),
                Columns = columns,
                HiddenColumns = Math.Max(0, o.Columns.Count - maxColumns),
                RowCount = o.IsView || o.RowCount == null ? null : o.RowCount.Value.ToString("N0", CultureInfo.InvariantCulture),
            };
        }
    }
}
