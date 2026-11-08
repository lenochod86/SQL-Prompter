using System;
using System.Windows.Media;
using Microsoft.VisualStudio.ComponentModelHost;
using Microsoft.VisualStudio.PlatformUI;
using Microsoft.VisualStudio.Shell;
using Microsoft.VisualStudio.Text.Classification;
using SqlPrompter.Core.Snippets;

#pragma warning disable VSTHRD010 // used on the UI thread only (WPF and WinForms controls); the services read here are those of the editor
namespace SqlPrompter.Ssms
{
    /// <summary>
    /// The colors of the SQL editor of SSMS for the places where we show SQL ourselves (the preview of the history, the preview of the formatting style, the
    /// definition of an object): read from the classification format map of the editor (<c>keyword</c>, <c>string</c>, <c>comment</c>, <c>number</c>,
    /// <c>identifier</c>, <c>keyword - control</c> for the functions) and its plain text background, so the colors follow the theme and the user's
    /// font and color settings. Where the editor has no answer the colors of the Dark+/Light+ palette are used.
    /// </summary>
    internal static class EditorColors
    {
        /// <summary>The color of every kind of <see cref="SnippetHighlighter"/> (plain text first), for the current theme.</summary>
        public static Color[] ByKind()
        {
            var themed = VSColorTheme.GetThemedColor(EnvironmentColors.ToolWindowBackgroundColorKey);
            var dark = (themed.R * 299 + themed.G * 587 + themed.B * 114) / 1000 < 128;
            var colors = new[]
            {
                Read("identifier") ?? Convert(VSColorTheme.GetThemedColor(EnvironmentColors.ToolWindowTextColorKey)),
                Read("keyword") ?? (dark ? Color.FromRgb(0x56, 0x9C, 0xD6) : Color.FromRgb(0x00, 0x00, 0xFF)),
                Read("string") ?? (dark ? Color.FromRgb(0xD6, 0x9D, 0x85) : Color.FromRgb(0xA3, 0x15, 0x15)),
                Read("comment") ?? (dark ? Color.FromRgb(0x57, 0xA6, 0x4A) : Color.FromRgb(0x00, 0x80, 0x00)),
                dark ? Color.FromRgb(0x9C, 0xDC, 0xFE) : Color.FromRgb(0x1F, 0x37, 0x7F),
                dark ? Color.FromRgb(0xC5, 0x86, 0xC0) : Color.FromRgb(0xAF, 0x00, 0xDB),
                Read("keyword - control") ?? (dark ? Color.FromRgb(0xD8, 0xA0, 0xDF) : Color.FromRgb(0x8F, 0x08, 0xC4)),
                Read("number") ?? (dark ? Color.FromRgb(0xB5, 0xCE, 0xA8) : Color.FromRgb(0x09, 0x86, 0x58)),
            };
            return colors;
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
