using System;

namespace SqlPrompter.Core.Editing
{
    /// <summary>Decides where a popup opens next to the caret (#451). Only numbers: the caller supplies the work area of the monitor that contains the caret, all in the same unit.</summary>
    public static class PopupPlacement
    {
        /// <summary>
        /// The top left corner of a popup of the given size. It opens right below the caret line, moves left when it would stick out on the right,
        /// and opens above the line when there is no room below. It never leaves the work area (and is cut at the top left corner when it is larger).
        /// </summary>
        /// <param name="caretX">The left edge of the caret.</param>
        /// <param name="belowLineY">The top of the popup when it opens below the line (just under the line).</param>
        /// <param name="lineHeight">The height of the caret line, to open above the line.</param>
        /// <param name="gap">The space between the line and a popup that opens above it.</param>
        public static void Place(double caretX, double belowLineY, double lineHeight, double gap, double width, double height, double areaLeft, double areaTop, double areaRight, double areaBottom, out double left, out double top)
        {
            left = Math.Max(areaLeft, Math.Min(caretX, areaRight - width));
            top = belowLineY + height > areaBottom ? Math.Max(areaTop, belowLineY - height - lineHeight - gap) : belowLineY;
        }
    }
}
