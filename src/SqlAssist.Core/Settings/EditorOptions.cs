namespace SqlAssist.Core.Settings
{
    /// <summary>What SqlAssist does by itself while the user is typing or moving the caret in a query window.</summary>
    public sealed class EditorOptions
    {
        /// <summary>In "INSERT INTO t (a, b) VALUES (1, 2)", mark the column and the value that sit in the same position as the caret.</summary>
        public bool HighlightInsertColumns { get; set; } = true;
    }
}
