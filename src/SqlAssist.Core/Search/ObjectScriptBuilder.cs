using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace SqlAssist.Core.Search
{
    /// <summary>Short scripts for the Search preview of indexes, constraints and alias types (CRLF, every name bracketed).
    /// Like <see cref="TableScriptBuilder"/>, not a full scripter: no storage, compression or other index options.</summary>
    public static class ObjectScriptBuilder
    {
        public static string Index(string schema, string table, string name, bool unique, bool clustered,
            IReadOnlyList<(string column, bool descending)> keys, IReadOnlyList<string> included, string filter)
        {
            var sb = new StringBuilder("CREATE ");
            if (unique) sb.Append("UNIQUE ");
            sb.Append(clustered ? "CLUSTERED" : "NONCLUSTERED").Append(" INDEX ").Append(B(name))
              .Append(" ON ").Append(B(schema)).Append('.').Append(B(table))
              .Append(" (").Append(string.Join(", ", keys.Select(k => B(k.column) + (k.descending ? " DESC" : "")))).Append(')');
            if (included.Count > 0) sb.Append("\r\nINCLUDE (").Append(string.Join(", ", included.Select(B))).Append(')');
            if (!string.IsNullOrEmpty(filter)) sb.Append("\r\nWHERE ").Append(filter);
            return sb.Append(';').ToString();
        }

        /// <param name="body">What follows the constraint name: "CHECK ([Qty]>(0))", "DEFAULT (getdate()) FOR [Created]",
        /// "FOREIGN KEY ([a]) REFERENCES [dbo].[t] ([b])", "PRIMARY KEY CLUSTERED ([a])".</param>
        public static string Constraint(string schema, string table, string name, string body) =>
            "ALTER TABLE " + B(schema) + "." + B(table) + " ADD CONSTRAINT " + B(name) + " " + body + ";";

        public static string AliasType(string schema, string name, string baseType, bool nullable) =>
            "CREATE TYPE " + B(schema) + "." + B(name) + " FROM " + baseType + (nullable ? " NULL;" : " NOT NULL;");

        private static string B(string name) => TableScriptBuilder.Bracket(name);
    }
}
