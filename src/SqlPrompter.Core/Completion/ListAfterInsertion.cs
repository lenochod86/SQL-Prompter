using SqlPrompter.Core.Analysis;

namespace SqlPrompter.Core.Completion
{
    /// <summary>
    /// Decides whether the suggestion list opens by itself after something was inserted at the caret, a snippet for instance (#353): when the context at
    /// the caret has items of its own, the tables after <c>JOIN</c> and <c>FROM</c>, the columns where an expression is expected. It stays closed where
    /// there is nothing to offer but keywords and where the user types a name of their own, so no list gets in the way.
    /// </summary>
    public static class ListAfterInsertion
    {
        public static bool ShouldOpen(CaretContext context)
        {
            if (context == null || context.TypedPrefix.Length > 0)
            {
                return false;
            }

            return context.Kind == ContextKind.ObjectName || context.Kind == ContextKind.Expression || context.Kind == ContextKind.InsertColumns;
        }
    }
}
