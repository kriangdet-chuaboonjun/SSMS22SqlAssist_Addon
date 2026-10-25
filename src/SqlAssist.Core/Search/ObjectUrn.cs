using System.Collections.Generic;

namespace SqlAssist.Core.Search
{
    /// <summary>SMO URNs for Search Items, the paths Object Explorer navigates by:
    /// <c>Server[@Name='s']/Database[@Name='d']/Table[@Name='t' and @Schema='dbo']/Column[@Name='c']</c>.</summary>
    public static class ObjectUrn
    {
        /// <summary>The URNs to try in order. More than one when the owner of a column or trigger could be a table or a
        /// view and the index does not say which.</summary>
        public static List<string> Candidates(string serverUrn, SearchItem item)
        {
            var urns = new List<string>();
            string db = serverUrn + "/Database[@Name=" + Quote(item.Database) + "]";
            switch (item.Type)
            {
                case SearchObjectType.Schema:
                    urns.Add(db + "/Schema[@Name=" + Quote(item.Name) + "]");
                    break;
                case SearchObjectType.Table:
                case SearchObjectType.View:
                case SearchObjectType.Procedure:
                case SearchObjectType.Function:
                    urns.Add(db + "/" + Element(item.Type) + Named(item.Name, item.Schema));
                    break;
                case SearchObjectType.UserDefinedType:
                    urns.Add(db + "/" + (item.SubKind == "Table" ? "UserDefinedTableType" : "UserDefinedDataType") + Named(item.Name, item.Schema));
                    break;
                case SearchObjectType.Column:
                case SearchObjectType.Trigger:
                case SearchObjectType.Index:
                case SearchObjectType.Constraint:
                    // SMO keeps a default under its column, which the item does not record: go to the table instead.
                    string child = item.SubKind == "Default" ? "" : "/" + ChildElement(item) + "[@Name=" + Quote(item.Name) + "]";
                    var owners = item.ParentType == SearchObjectType.View ? new[] { SearchObjectType.View }
                        : item.ParentType == SearchObjectType.Table ? new[] { SearchObjectType.Table }
                        : new[] { SearchObjectType.Table, SearchObjectType.View };
                    foreach (var owner in owners)
                        urns.Add(db + "/" + Element(owner) + Named(item.Parent, item.Schema) + child);
                    break;
            }
            return urns;
        }

        public static string Server(string serverName) => "Server[@Name=" + Quote(serverName) + "]";

        /// <summary>The URN of the object a column or trigger belongs to: where to go when the child node itself is not
        /// found (Object Explorer shows no node for some of them).</summary>
        public static string Owner(string urn)
        {
            int slash = urn.LastIndexOf("]/", System.StringComparison.Ordinal);
            return slash < 0 ? urn : urn.Substring(0, slash + 1);
        }

        // Primary keys and unique constraints are Index nodes in SMO.
        private static string ChildElement(SearchItem item)
        {
            switch (item.Type)
            {
                case SearchObjectType.Column: return "Column";
                case SearchObjectType.Trigger: return "Trigger";
                case SearchObjectType.Index: return "Index";
                default: return item.SubKind == "ForeignKey" ? "ForeignKey" : item.SubKind == "Check" ? "Check" : "Index";
            }
        }

        private static string Element(SearchObjectType type)
        {
            switch (type)
            {
                case SearchObjectType.Table: return "Table";
                case SearchObjectType.View: return "View";
                case SearchObjectType.Procedure: return "StoredProcedure";
                default: return "UserDefinedFunction";
            }
        }

        private static string Named(string name, string schema) =>
            "[@Name=" + Quote(name) + " and @Schema=" + Quote(schema) + "]";

        /// <summary>URN string literal: single quotes, doubled inside.</summary>
        public static string Quote(string value) => "'" + (value ?? "").Replace("'", "''") + "'";
    }
}
