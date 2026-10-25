using System;
using System.Collections.Generic;

namespace SqlAssist.Core.Search
{
    [Flags]
    public enum SearchObjectType
    {
        None = 0,
        Schema = 1,
        Table = 2,
        View = 4,
        Procedure = 8,
        Function = 16,
        Trigger = 32,
        Column = 64,
        Index = 128,
        Constraint = 256,
        UserDefinedType = 512,
        All = Schema | Table | View | Procedure | Function | Trigger | Column | Index | Constraint | UserDefinedType,
    }

    [Flags]
    public enum MatchLocation
    {
        None = 0,
        Name = 1,
        Definition = 2,
    }

    /// <summary>One searchable object in a Search Index.</summary>
    public sealed class SearchItem
    {
        public SearchItem(string database, string schema, string name, SearchObjectType type,
            string parent = null, string definition = null, string script = null,
            SearchObjectType parentType = SearchObjectType.None, string subKind = null)
        {
            Database = database; Schema = schema; Name = name; Type = type;
            Parent = parent; Definition = definition; Script = script; ParentType = parentType; SubKind = subKind;
        }

        public string Database { get; }
        /// <summary>Null for a Schema item.</summary>
        public string Schema { get; }
        public string Name { get; }
        public SearchObjectType Type { get; }
        /// <summary>Constraint: PrimaryKey, Unique, ForeignKey, Check or Default. UserDefinedType: Alias or Table. Null otherwise.</summary>
        public string SubKind { get; }
        /// <summary>The owning table or view of a Column, Trigger, Index or Constraint; null otherwise.</summary>
        public string Parent { get; }
        /// <summary>Table or View when <see cref="Parent"/> is set and known; None otherwise.</summary>
        public SearchObjectType ParentType { get; }
        /// <summary>Module source from sys.sql_modules: searched and previewed. Null for tables, columns and schemas.</summary>
        public string Definition { get; }
        /// <summary>A generated script shown in the preview but never searched (a table's CREATE TABLE; a column shares
        /// its table's instance).</summary>
        public string Script { get; }

        public string PreviewText => Definition ?? Script;
        public override string ToString() => Database + ":" + (Schema == null ? "" : Schema + ".") + Name + " (" + Type + ")";
    }

    /// <summary>What the user is looking for: the text, the match options and the object types to include.</summary>
    public sealed class SearchQuery
    {
        /// <summary>Shorter texts match too much of a large definition set to be useful while typing.</summary>
        public const int MinLength = 2;

        public string Text { get; set; }
        public bool MatchCase { get; set; }
        public bool WholeWord { get; set; }
        /// <summary>* = any run of characters on one line, ? = any one character.</summary>
        public bool UseWildcards { get; set; }
        public SearchObjectType Types { get; set; } = SearchObjectType.All;
    }

    public struct TextSpan : IEquatable<TextSpan>
    {
        public TextSpan(int start, int length) { Start = start; Length = length; }
        public int Start { get; }
        public int Length { get; }
        public int End => Start + Length;

        public bool Equals(TextSpan other) => Start == other.Start && Length == other.Length;
        public override bool Equals(object obj) => obj is TextSpan s && Equals(s);
        public override int GetHashCode() => (Start * 397) ^ Length;
        public override string ToString() => "[" + Start + ".." + End + ")";
    }

    /// <summary>A Search Item the query hit, with where it hit and the hit ranges for highlighting.</summary>
    public sealed class SearchMatch
    {
        public SearchMatch(SearchItem item, IReadOnlyList<TextSpan> nameHits, IReadOnlyList<TextSpan> definitionHits)
        {
            Item = item; NameHits = nameHits; DefinitionHits = definitionHits;
        }

        public SearchItem Item { get; }
        public IReadOnlyList<TextSpan> NameHits { get; }
        /// <summary>Ranges within <see cref="SearchItem.Definition"/>.</summary>
        public IReadOnlyList<TextSpan> DefinitionHits { get; }

        public MatchLocation Location =>
            (NameHits.Count > 0 ? MatchLocation.Name : MatchLocation.None) |
            (DefinitionHits.Count > 0 ? MatchLocation.Definition : MatchLocation.None);
    }
}
