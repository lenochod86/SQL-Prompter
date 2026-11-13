using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.Windows.Forms;
using SqlPrompter.Core.Editing;

namespace SqlPrompter.Ssms
{
    /// <summary>
    /// The columns the selection of the results grid covers, read from the grid control that has the focus (#446). The grid has no API for an extension;
    /// it is a WinForms control (Microsoft.SqlServer.GridControl) with the property <c>SelectedCells</c>, a collection of blocks (<c>X</c> and <c>Width</c>:
    /// the first column and how many), found by reflection so that nothing of the SSMS types is referenced. Nothing is read when the focus is elsewhere
    /// or the shape of the control is not the expected one: the caller then leaves the command as it is.
    /// </summary>
    internal static class GridSelectionReader
    {
        [DllImport("user32.dll")]
        private static extern IntPtr GetFocus();

        /// <summary>True when the focused grid has cells of more than one column selected; false when it is one column, nothing, or not known.</summary>
        public static bool SelectionSpansSeveralColumns()
        {
            try
            {
                var blocks = ReadBlocks();
                return blocks != null && GridSelection.SpansSeveralColumns(blocks);
            }
            catch (Exception ex)
            {
                Log.Write("Reading the selection of the results grid failed: " + ex.GetType().Name + ": " + ex.Message);
                return false;
            }
        }

        private static List<(int FirstColumn, int LastColumn)> ReadBlocks()
        {
            var focused = GetFocus();
            var control = focused == IntPtr.Zero ? null : Control.FromChildHandle(focused);
            for (; control != null; control = control.Parent)
            {
                var property = control.GetType().GetProperty("SelectedCells");
                if (property == null || property.PropertyType.Name != "BlockOfCellsCollection")
                {
                    continue;
                }

                var collection = property.GetValue(control);
                if (collection == null)
                {
                    return null;
                }

                var type = collection.GetType();
                var count = (int)type.GetProperty("Count").GetValue(collection);
                var item = type.GetProperty("Item", new[] { typeof(int) });
                var blocks = new List<(int, int)>();
                for (var i = 0; i < count; i++)
                {
                    var block = item.GetValue(collection, new object[] { i });
                    var blockType = block.GetType();
                    var x = (int)blockType.GetProperty("X").GetValue(block);
                    var width = (int)blockType.GetProperty("Width").GetValue(block);
                    blocks.Add((x, x + Math.Max(1, width) - 1));
                }

                return blocks;
            }

            return null;
        }
    }
}
