using System.Collections.Generic;
using System.Text;

namespace SqlAssist.Core.Search
{
    public sealed class TableColumn
    {
        public TableColumn(string name, string typeName, bool isNullable) { Name = name; TypeName = typeName; IsNullable = isNullable; }
        public string Name { get; }
        /// <summary>Already formatted, e.g. nvarchar(50) (see <see cref="Schema.SqlTypeFormatter"/>).</summary>
        public string TypeName { get; }
        public bool IsNullable { get; }
    }

    public sealed class PrimaryKey
    {
        public PrimaryKey(string name, bool isClustered, IReadOnlyList<string> columns) { Name = name; IsClustered = isClustered; Columns = columns; }
        public string Name { get; }
        public bool IsClustered { get; }
        /// <summary>In key order.</summary>
        public IReadOnlyList<string> Columns { get; }
    }

    /// <summary>A short CREATE TABLE for the Search preview: columns, types, nullability and the primary key.
    /// Not a full scripter: no defaults, identity, computed columns, indexes or other constraints.</summary>
    public static class TableScriptBuilder
    {
        public static string Build(string schema, string name, IReadOnlyList<TableColumn> columns, PrimaryKey primaryKey, string newline = "\r\n") =>
            Build("CREATE TABLE ", schema, name, columns, primaryKey, newline);

        /// <summary>A user-defined table type: the same body as <see cref="Build(string, string, IReadOnlyList{TableColumn}, PrimaryKey, string)"/>.</summary>
        public static string BuildTableType(string schema, string name, IReadOnlyList<TableColumn> columns, PrimaryKey primaryKey, string newline = "\r\n") =>
            Build("CREATE TYPE ", schema, name, columns, primaryKey, newline, " AS TABLE");

        private static string Build(string create, string schema, string name, IReadOnlyList<TableColumn> columns, PrimaryKey primaryKey,
            string newline, string afterName = "")
        {
            var lines = new List<string>();
            foreach (var c in columns)
                lines.Add("    " + Bracket(c.Name) + " " + c.TypeName + (c.IsNullable ? " NULL" : " NOT NULL"));
            if (primaryKey != null && primaryKey.Columns.Count > 0)
            {
                var keys = new List<string>();
                foreach (var k in primaryKey.Columns) keys.Add(Bracket(k));
                lines.Add("    CONSTRAINT " + Bracket(primaryKey.Name) + " PRIMARY KEY " +
                    (primaryKey.IsClustered ? "CLUSTERED" : "NONCLUSTERED") + " (" + string.Join(", ", keys) + ")");
            }

            var sb = new StringBuilder();
            sb.Append(create).Append(Bracket(schema)).Append('.').Append(Bracket(name)).Append(afterName).Append(" (").Append(newline);
            sb.Append(string.Join("," + newline, lines));
            if (lines.Count > 0) sb.Append(newline);
            sb.Append(");");
            return sb.ToString();
        }

        /// <summary>Always brackets, as SSMS scripts do.</summary>
        public static string Bracket(string name) => "[" + name.Replace("]", "]]") + "]";
    }
}
