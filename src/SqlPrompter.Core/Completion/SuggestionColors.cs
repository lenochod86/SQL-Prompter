using SqlPrompter.Core.Snippets;

namespace SqlPrompter.Core.Completion
{
    /// <summary>
    /// Which color of the SQL editor a suggestion takes in the list (#418): the kind of the suggestion as a <see cref="SnippetHighlighter"/> class, the same classes
    /// that the previews and the editors color with. Null means the plain text color of the list (names of objects, columns, snippets and so on).
    /// </summary>
    public static class SuggestionColors
    {
        public static int? ClassOf(SuggestionKind kind)
        {
            switch (kind)
            {
                case SuggestionKind.Keyword:
                case SuggestionKind.DataType:
                    // The editor colors the names of data types like the other keywords.
                    return SnippetHighlighter.Keyword;
                case SuggestionKind.Function:
                    return SnippetHighlighter.Function;
                case SuggestionKind.Variable:
                    return SnippetHighlighter.Variable;
                default:
                    return null;
            }
        }
    }
}
