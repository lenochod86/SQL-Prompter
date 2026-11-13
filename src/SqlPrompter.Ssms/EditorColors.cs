using System;
using System.Windows.Media;
using Microsoft.VisualStudio.ComponentModelHost;
using Microsoft.VisualStudio.PlatformUI;
using Microsoft.VisualStudio.Shell;
using Microsoft.VisualStudio.Shell.Interop;
using Microsoft.VisualStudio.Text.Classification;
using SqlPrompter.Core.Snippets;

#pragma warning disable VSTHRD010 // used on the UI thread only (WPF and WinForms controls); the services read here are those of the editor
namespace SqlPrompter.Ssms
{
    /// <summary>
    /// The colors of the SQL editor of SSMS for the places where we show SQL ourselves (the completion list, the preview of the history, the preview of the
    /// formatting style, the snippet editor, the definition of an object). The SQL editor is colored by the language service of SSMS, whose colorable items
    /// (<c>Keyword</c>, <c>Comment</c>, <c>Identifier</c>, <c>Number</c>, <c>SQL String</c>, <c>SQL System Function</c>, <c>SQL Operator</c>,
    /// <c>SQL Stored Procedure</c>, <c>SQL System Table</c>) are visible in the classification format map of the editor under these names, so the colors
    /// follow the theme and the user's Fonts and Colors. A name is registered there only after the editor has colored a text that uses it, so when the map
    /// does not know a name yet the color saved in Fonts and Colors is used, and then the color of the built-in palette (the values SSMS 22 has).
    /// </summary>
    internal static class EditorColors
    {
        private struct Item
        {
            public Item(string name, uint light, uint dark)
            {
                Name = name;
                Light = Color.FromRgb((byte)(light >> 16), (byte)(light >> 8), (byte)light);
                Dark = Color.FromRgb((byte)(dark >> 16), (byte)(dark >> 8), (byte)dark);
            }

            public string Name { get; }

            public Color Light { get; }

            public Color Dark { get; }
        }

        // The editor's colorable items by SnippetHighlighter class, with the colors SSMS 22 has in the light and the dark theme (verified, docs/architecture.md).
        private static readonly Item Keyword = new Item("Keyword", 0x0000FF, 0x569CD6);
        private static readonly Item Comment = new Item("Comment", 0x008000, 0x57A64A);
        private static readonly Item Identifier = new Item("Identifier", 0x000000, 0xDCDCDC);
        private static readonly Item Number = new Item("Number", 0x000000, 0xB5CEA8);
        private static readonly Item StringLiteral = new Item("SQL String", 0xFF0000, 0xCB4141);
        private static readonly Item Function = new Item("SQL System Function", 0xFF00FF, 0xC975D5);
        private static readonly Item Operator = new Item("SQL Operator", 0x808080, 0x818181);
        private static readonly Item StoredProcedure = new Item("SQL Stored Procedure", 0x800000, 0xB3725B);
        private static readonly Item SystemTable = new Item("SQL System Table", 0x008000, 0xB9E873);

        /// <summary>The color of every class of <see cref="SnippetHighlighter"/> (plain text first), for the current theme.</summary>
        public static Color[] ByKind()
        {
            var themed = VSColorTheme.GetThemedColor(EnvironmentColors.ToolWindowBackgroundColorKey);
            var dark = (themed.R * 299 + themed.G * 587 + themed.B * 114) / 1000 < 128;
            var plain = Get(Identifier, dark);
            var colors = new Color[SnippetHighlighter.Operator + 3];
            colors[SnippetHighlighter.Plain] = plain;
            colors[SnippetHighlighter.Keyword] = Get(Keyword, dark);
            colors[SnippetHighlighter.String] = Get(StringLiteral, dark);
            colors[SnippetHighlighter.Comment] = Get(Comment, dark);

            // A variable (@name) has the color of an identifier in the editor.
            colors[SnippetHighlighter.Variable] = plain;

            // Fields of a snippet ($CURSOR$) are not an editor class: an own color.
            colors[SnippetHighlighter.Field] = dark ? Color.FromRgb(0xC5, 0x86, 0xC0) : Color.FromRgb(0xAF, 0x00, 0xDB);
            colors[SnippetHighlighter.Function] = Get(Function, dark);
            colors[SnippetHighlighter.Number] = Get(Number, dark);
            colors[SnippetHighlighter.Operator] = Get(Operator, dark);
            colors[SnippetHighlighter.SystemProcedure] = Get(StoredProcedure, dark);
            colors[SnippetHighlighter.SystemTable] = Get(SystemTable, dark);
            return colors;
        }

        private static Color Get(Item item, bool dark)
        {
            var color = Read(item.Name) ?? ReadSaved(item.Name);
            if (color.HasValue)
            {
                return color.Value;
            }

            return dark ? item.Dark : item.Light;
        }

        /// <summary>The color Fonts and Colors has saved for a colorable item of the Text Editor category, when it is an explicit RGB value (not an index of the palette).</summary>
        private static Color? ReadSaved(string displayName)
        {
            try
            {
                if (!(Package.GetGlobalService(typeof(SVsFontAndColorStorage)) is IVsFontAndColorStorage storage))
                {
                    return null;
                }

                var category = new Guid("A27B4E24-A735-4d1d-B8E7-9716E1E3D8E0");
                if (storage.OpenCategory(ref category, (uint)(__FCSTORAGEFLAGS.FCSF_READONLY | __FCSTORAGEFLAGS.FCSF_LOADDEFAULTS)) != 0)
                {
                    return null;
                }

                try
                {
                    var info = new ColorableItemInfo[1];
                    if (storage.GetItem(displayName, info) != 0 || info[0].bForegroundValid == 0 || (info[0].crForeground & 0xFF000000) != 0)
                    {
                        return null;
                    }

                    var rgb = info[0].crForeground;
                    return Color.FromRgb((byte)rgb, (byte)(rgb >> 8), (byte)(rgb >> 16));
                }
                finally
                {
                    storage.CloseCategory();
                }
            }
            catch (Exception ex)
            {
                Log.Error("Reading the saved color of " + displayName + " failed", ex);
                return null;
            }
        }
        /// <summary>The background of the editor, or the background of the tool windows when the editor does not say.</summary>
        public static Color Background()
        {
            try
            {
                var model = (IComponentModel)Package.GetGlobalService(typeof(SComponentModel));
                var map = model?.GetService<IEditorFormatMapService>()?.GetEditorFormatMap("text");
                var properties = map?.GetProperties("Plain Text");
                if (properties != null && properties.Contains("Background") && properties["Background"] is SolidColorBrush brush)
                {
                    return brush.Color;
                }
            }
            catch (Exception ex)
            {
                Log.Error("Reading the background of the editor failed", ex);
            }

            return Convert(VSColorTheme.GetThemedColor(EnvironmentColors.ToolWindowBackgroundColorKey));
        }

        private static Color Convert(System.Drawing.Color color) => Color.FromRgb(color.R, color.G, color.B);

        private static Color? Read(string classification)
        {
            try
            {
                var model = (IComponentModel)Package.GetGlobalService(typeof(SComponentModel));
                var maps = model?.GetService<IClassificationFormatMapService>();
                var registry = model?.GetService<IClassificationTypeRegistryService>();
                var type = registry?.GetClassificationType(classification);
                var map = maps?.GetClassificationFormatMap("text");
                if (type == null || map == null)
                {
                    return null;
                }

                return (map.GetTextProperties(type).ForegroundBrush as SolidColorBrush)?.Color;
            }
            catch (Exception ex)
            {
                Log.Error("Reading the color of " + classification + " failed", ex);
                return null;
            }
        }
    }
}
