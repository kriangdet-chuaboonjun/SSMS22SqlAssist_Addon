using System;
using System.Collections.Generic;
using System.Linq;
using SqlAssist.Core.Sql;

namespace SqlAssist.Core.Schema
{
    /// <summary>The column information shown beside a selected column in the completion list:
    /// <code>
    /// Sales_CTE.SalesPersonID (Column)
    /// SalesPersonID  INT  NOT NULL
    /// Sales person who created the sales order.
    /// Foreign key to Sales.SalesPerson.BusinessEntityID
    /// </code>
    /// The description is the column's MS_Description; for a CTE column it is the one of the column passed through.</summary>
    public static class ColumnInfo
    {
        /// <summary>The table a hovered column belongs to: the one its qualifier names, or, unqualified, the only table in
        /// <paramref name="scope"/> that has it. Null when that cannot be told for sure — several tables have it, or a
        /// table in scope is unknown to the schema (a #temp table, a table variable) and might be the one.</summary>
        public static (DbObject owner, DbColumn column)? FindOwner(SchemaModel schema, TableRef table, IReadOnlyList<TableRef> scope, string column)
        {
            var resolved = (table != null ? new[] { table } : scope ?? new TableRef[0]).Select(schema.Find).ToList();
            if (table == null && resolved.Any(o => o == null)) return null;
            var matches = resolved
                .Where(o => o != null)
                .Select(o => (owner: o, column: o.Columns.FirstOrDefault(c => string.Equals(c.Name, column, StringComparison.OrdinalIgnoreCase))))
                .Where(x => x.column != null)
                .ToList();
            return matches.Count == 1 ? matches[0] : ((DbObject, DbColumn)?)null;
        }

        public static string Describe(SchemaModel schema, DbObject owner, DbColumn column)
        {
            var lines = new List<string> { $"{owner}.{column.Name} (Column)" };

            string type = string.IsNullOrEmpty(column.TypeName) ? "" : "  " + column.TypeName.ToUpperInvariant();
            string flags = column.IsPrimaryKey ? "  PRIMARY KEY" : "";
            flags += column.IsIdentity ? "  IDENTITY" : column.IsNullable ? "" : "  NOT NULL";
            lines.Add(column.Name + type + flags);

            if (!string.IsNullOrWhiteSpace(column.Description)) lines.Add(column.Description.Trim());

            // Keys live on the real table, so a CTE column asks the table it came from.
            var table = column.Origin ?? owner;
            string name = column.OriginColumn ?? column.Name;
            foreach (var fk in schema.ForeignKeysFrom(table))
                foreach (var (from, to) in fk.Columns.Where(c => string.Equals(c.from, name, StringComparison.OrdinalIgnoreCase)))
                {
                    string text = $"Foreign key to {fk.To}.{to}";
                    if (column.Description == null || column.Description.IndexOf(text, StringComparison.OrdinalIgnoreCase) < 0)
                        lines.Add(text);
                }
            return string.Join("\n", lines);
        }
    }
}
