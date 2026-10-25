using System.Collections.Generic;
using System.Linq;
using SqlAssist.Core.Schema;
using SqlAssist.Core.Sql;

namespace SqlAssist.Core.Completion
{
    public static class StarExpander
    {
        /// <summary>Builds the column list that replaces "*" (or "c.*"). Null when no table could be resolved,
        /// so the caller can leave the star alone rather than replace it with nothing.
        /// With <paramref name="linePrefix"/> (the text of the line before the star) the list is one column per line,
        /// leading commas lined up one column left of the first name:
        /// <code>
        /// SELECT wr.ReqId
        ///       ,wr.Code
        ///       ,wr.PermitNo
        /// </code>
        /// Without it, the list stays on one line ("a, b, c").</summary>
        public static string Expand(StarContext star, SchemaModel schema, string linePrefix = null, string newLine = "\n")
        {
            var columns = Columns(star, schema);
            if (columns == null) return null;
            if (linePrefix == null) return string.Join(", ", columns);

            // Keep tabs as tabs so the alignment survives any tab width; everything else becomes a space.
            var indent = new System.Text.StringBuilder(linePrefix.Length);
            foreach (char c in linePrefix) indent.Append(c == '\t' ? '\t' : ' ');
            string commaIndent = indent.Length > 0 ? indent.ToString(0, indent.Length - 1) : "";
            return string.Join(newLine + commaIndent + ",", columns);
        }

        private static List<string> Columns(StarContext star, SchemaModel schema)
        {
            var resolved = new List<(TableRef r, DbObject o)>();
            int expected = 0;
            foreach (var r in star.Tables)
            {
                var o = schema.Find(r);
                // A function the schema does not know (STRING_SPLIT, OPENJSON) is left out rather than blocking the rest.
                if (r.IsFunction && o == null) continue;
                expected++;
                if (o != null && o.Columns.Count > 0) resolved.Add((r, o));
            }
            if (resolved.Count == 0 || resolved.Count < expected) return null;

            // One table without an alias: bare names. Otherwise qualify, so the list survives later JOINs.
            bool qualify = star.Qualifier != null || resolved.Count > 1 || resolved[0].r.Alias != null;
            var columns = new List<string>();
            foreach (var (r, o) in resolved)
            {
                string prefix = qualify ? SqlIdentifier.QuoteIfNeeded(star.Qualifier ?? r.RefName) + "." : "";
                columns.AddRange(o.Columns.Select(c => prefix + SqlIdentifier.QuoteIfNeeded(c.Name)));
            }
            return columns;
        }
    }
}
