using System;

namespace SqlPrompter.Core.Completion
{
    /// <summary>
    /// Colors of the selected and the hovered row of the suggestion list (#475): the accent color of the theme blended into the background of the list, so the row
    /// stays light and the colors of the icon and the text remain visible on it. Colors are 0xRRGGBB.
    /// </summary>
    public static class RowTint
    {
        /// <summary>The text of a row should keep at least this contrast against the tinted background.</summary>
        public const double MinContrast = 3.0;

        /// <summary>The share of the accent color in a hard (typed or arrow-selected) row.</summary>
        public const double SelectedRatio = 0.28;

        /// <summary>The share of the accent color in a soft (not yet chosen) row.</summary>
        public const double SoftRatio = 0.14;

        /// <summary>The share of the accent color in the row under the mouse.</summary>
        public const double HoverRatio = 0.10;

        /// <summary>Mixes <paramref name="top"/> over <paramref name="background"/>; the ratio (0..1) is the share of <paramref name="top"/>.</summary>
        public static int Blend(int top, int background, double ratio)
        {
            ratio = Math.Max(0, Math.Min(1, ratio));
            return (Channel(top, background, ratio, 16) << 16) | (Channel(top, background, ratio, 8) << 8) | Channel(top, background, ratio, 0);
        }

        private static int Channel(int top, int background, double ratio, int shift)
        {
            var t = (top >> shift) & 0xFF;
            var b = (background >> shift) & 0xFF;
            return (int)Math.Round(t * ratio + b * (1 - ratio));
        }

        /// <summary>WCAG contrast ratio of two colors (1..21).</summary>
        public static double Contrast(int a, int b)
        {
            var la = Luminance(a);
            var lb = Luminance(b);
            var hi = Math.Max(la, lb);
            var lo = Math.Min(la, lb);
            return (hi + 0.05) / (lo + 0.05);
        }

        private static double Luminance(int color)
        {
            return 0.2126 * Linear((color >> 16) & 0xFF) + 0.7152 * Linear((color >> 8) & 0xFF) + 0.0722 * Linear(color & 0xFF);
        }

        private static double Linear(int channel)
        {
            var c = channel / 255.0;
            return c <= 0.03928 ? c / 12.92 : Math.Pow((c + 0.055) / 1.055, 2.4);
        }

        /// <summary>
        /// The background of a highlighted row: the accent blended into the list background at <paramref name="ratio"/>, weakened step by step while the text
        /// color would fall under <see cref="MinContrast"/> on it (a text that is already weak on the plain background is left at the weakest tint).
        /// </summary>
        public static int Background(int accent, int background, int text, double ratio)
        {
            var result = Blend(accent, background, ratio);
            while (ratio > 0.04 && Contrast(text, result) < MinContrast && Contrast(text, result) < Contrast(text, background))
            {
                ratio -= 0.02;
                result = Blend(accent, background, ratio);
            }

            return result;
        }
    }
}
