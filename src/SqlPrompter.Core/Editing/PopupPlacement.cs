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

        /// <summary>
        /// The vertical placement of a list next to the caret line (#501). It never covers the line: below it when the list fits there, otherwise above it
        /// (its bottom edge ends <paramref name="gap"/> above the line) when it fits there, otherwise on the side with more room, and then the list shrinks
        /// (but not below <paramref name="minHeight"/>, which may then stick out of the area, never over the line).
        /// </summary>
        /// <param name="lineTop">The top of the caret line.</param>
        /// <param name="lineBottom">The bottom of the caret line.</param>
        /// <param name="height">The wanted height of the list.</param>
        /// <param name="minHeight">The smallest height the list may shrink to.</param>
        /// <param name="gap">The space between the line and the list.</param>
        /// <param name="top">The top of the list.</param>
        /// <param name="fitHeight">The height the list gets (at most <paramref name="height"/>).</param>
        public static void PlaceVertical(double lineTop, double lineBottom, double height, double minHeight, double gap, double areaTop, double areaBottom, out double top, out double fitHeight)
        {
            var roomBelow = areaBottom - (lineBottom + gap);
            var roomAbove = lineTop - gap - areaTop;
            bool below;
            if (height <= roomBelow)
            {
                below = true;
            }
            else if (height <= roomAbove)
            {
                below = false;
            }
            else
            {
                below = roomBelow >= roomAbove;
            }

            var room = below ? roomBelow : roomAbove;
            fitHeight = Math.Max(Math.Min(height, room), Math.Min(height, minHeight));
            top = below ? lineBottom + gap : lineTop - gap - fitHeight;
        }
    }
}
