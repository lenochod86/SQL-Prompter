namespace SqlPrompter.Core.Snippets
{
    /// <summary>Which snippet field is edited next when the keys Tab, Shift+Tab and Enter move between the fields.</summary>
    public static class FieldNavigation
    {
        /// <summary>Tab: the next field; after the last one it wraps around to the first.</summary>
        public static int Cycle(int current, int count) => count <= 0 ? -1 : (current + 1) % count;

        /// <summary>Shift+Tab: the previous field; before the first one it wraps around to the last.</summary>
        public static int CycleBack(int current, int count) => count <= 0 ? -1 : (current - 1 + count) % count;

        /// <summary>Enter: the next field, or -1 when the last field is current and the editing ends.</summary>
        public static int Advance(int current, int count) => current + 1 < count ? current + 1 : -1;
    }
}
