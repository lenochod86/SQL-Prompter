using System;
using System.Drawing;
using System.Runtime.InteropServices;
using System.Windows.Forms;
using Microsoft.VisualStudio.PlatformUI;
using Microsoft.VisualStudio.Shell;

namespace SqlPrompter.Ssms
{
    /// <summary>
    /// Gives a WinForms window the theme of SSMS (#346): the colors of the tool windows (background, text, border) are applied to the form and to
    /// every control in it, also to controls added later (the settings pages) and again when the theme changes while the window is open; the title bar
    /// follows a dark theme. SSMS types stay out of the rest of the code: windows only call <see cref="Apply"/>.
    /// </summary>
    internal static class WinFormsTheme
    {
        private const int DwmUseImmersiveDarkMode = 20;

        [DllImport("uxtheme.dll", CharSet = CharSet.Unicode)]
        private static extern int SetWindowTheme(IntPtr window, string subAppName, string subIdList);

        [DllImport("dwmapi.dll")]
        private static extern int DwmSetWindowAttribute(IntPtr window, int attribute, ref int value, int size);

        /// <summary>The colors of the current theme, derived from the tool window colors.</summary>
        private sealed class Palette
        {
            public Color Back;
            public Color Fore;
            public Color Border;
            public Color Input;
            public Color Button;
            public Color Muted;
            public bool Dark;
        }

        /// <summary>The palette of the last application, read by the paint handlers of the buttons (it follows a theme change).</summary>
        private static Palette latest;

        private static Palette Current()
        {
            ThreadHelper.ThrowIfNotOnUIThread();
            var back = VSColorTheme.GetThemedColor(EnvironmentColors.ToolWindowBackgroundColorKey);
            var fore = VSColorTheme.GetThemedColor(EnvironmentColors.ToolWindowTextColorKey);
            var border = VSColorTheme.GetThemedColor(EnvironmentColors.ToolWindowBorderColorKey);
            return new Palette
            {
                Back = back,
                Fore = fore,
                Border = Blend(back, fore, 0.35),
                Input = Blend(back, fore, 0.07),
                Button = Blend(back, fore, 0.14),
                Muted = Blend(fore, back, 0.45),
                Dark = Brightness(back) < 128,
            };
        }

        /// <summary>The background of an input area (a preview or a box) in the current theme.</summary>
        public static Color InputBack
        {
            get
            {
                ThreadHelper.ThrowIfNotOnUIThread();
                return Current().Input;
            }
        }

        /// <summary>Themes <paramref name="form"/> and keeps it themed (new controls, a changed theme) until it is closed.</summary>
        public static void Apply(Form form)
        {
            ThreadHelper.ThrowIfNotOnUIThread();
            try
            {
                void Refresh()
                {
                    ThreadHelper.ThrowIfNotOnUIThread();
                    var palette = Current();
                    latest = palette;
                    form.BackColor = palette.Back;
                    form.ForeColor = palette.Fore;
                    Tree(form, palette);
                    DarkTitleBar(form, palette);
                }

                Watch(form, Refresh);
                Refresh();
                VSColorTheme.ThemeChanged += OnThemeChanged;
                form.Disposed += (s, e) => VSColorTheme.ThemeChanged -= OnThemeChanged;
                form.HandleCreated += (s, e) => DarkTitleBar(form, Current());

                void OnThemeChanged(ThemeChangedEventArgs args)
                {
                    ThreadHelper.ThrowIfNotOnUIThread();
                    if (!form.IsDisposed)
                    {
                        Refresh();
                    }
                }
            }
            catch (Exception ex)
            {
                Log.Error("Theming a window failed", ex);
            }
        }

        /// <summary>A control added later (to the form or to any control in it) gets the colors as well.</summary>
        private static void Watch(Control control, Action refresh)
        {
            control.ControlAdded += (s, e) =>
            {
                Watch(e.Control, refresh);
                refresh();
            };
            foreach (Control child in control.Controls)
            {
                Watch(child, refresh);
            }
        }

        private static void Tree(Control control, Palette p)
        {
            ThreadHelper.ThrowIfNotOnUIThread();
            foreach (Control child in control.Controls)
            {
                One(child, p);
                Tree(child, p);
            }
        }

        /// <summary>The native scroll bars and drop-down arrows of a control follow a dark theme (Windows 10 1809 and later; ignored elsewhere).</summary>
        private static void NativeTheme(Control control, Palette p)
        {
            void Set()
            {
                try
                {
                    SetWindowTheme(control.Handle, p.Dark ? (control is TextBox || control is ComboBox ? "DarkMode_CFD" : "DarkMode_Explorer") : "Explorer", null);
                }
                catch (Exception)
                {
                }
            }

            if (control.IsHandleCreated)
            {
                Set();
            }
            else
            {
                control.HandleCreated += (s, e) => Set();
            }
        }

        private static void One(Control control, Palette p)
        {
            ThreadHelper.ThrowIfNotOnUIThread();
            if (!(control is Button) && !(control is Label) && !(control is CheckBox))
            {
                NativeTheme(control, p);
            }

            switch (control)
            {
                case FormatPreview preview:
                    preview.BackColor = p.Input;
                    preview.ForeColor = p.Fore;
                    return;
                case Button button:
                    if (button.BackColor.IsSystemColor || button.BackColor == Color.Empty || button.BackColor == p.Button)
                    {
                        button.FlatStyle = FlatStyle.Flat;
                        button.FlatAppearance.BorderColor = p.Border;
                        button.BackColor = p.Button;
                        button.ForeColor = p.Fore;
                        button.UseVisualStyleBackColor = false;
                        button.FlatAppearance.BorderSize = 1;
                        button.FlatAppearance.MouseOverBackColor = Blend(p.Button, p.Fore, 0.12);
                        button.FlatAppearance.MouseDownBackColor = Blend(p.Button, p.Fore, 0.22);
                        if (!(button.Tag is string tag) || tag != "themed")
                        {
                            button.Tag = "themed";
                            // The flat button draws a thick border in the text color when it is the default or focused one; a frame of our own covers it.
                            button.Paint += (s, e) =>
                            {
                                var b = (Button)s;
                                var theme = latest ?? p;
                                using (var outer = new Pen(Blend(theme.Back, theme.Fore, 0.35)))
                                using (var inner = new Pen(b.Enabled ? b.BackColor : theme.Back))
                                {
                                    e.Graphics.DrawRectangle(inner, 1, 1, b.Width - 3, b.Height - 3);
                                    e.Graphics.DrawRectangle(outer, 0, 0, b.Width - 1, b.Height - 1);
                                }
                            };
                        }
                    }

                    return;
                case TreeView _:
                case TextBox _:
                case ComboBox _:
                case ListBox _:
                case ListView _:
                case NumericUpDown _:
                case RichTextBox _:
                    control.BackColor = p.Input;
                    control.ForeColor = p.Fore;
                    if (control is ComboBox combo)
                    {
                        combo.FlatStyle = FlatStyle.Flat;
                    }

                    return;
                case DataGridView grid:
                    grid.BackgroundColor = p.Input;
                    grid.GridColor = p.Border;
                    grid.EnableHeadersVisualStyles = false;
                    grid.ColumnHeadersDefaultCellStyle.BackColor = p.Button;
                    grid.ColumnHeadersDefaultCellStyle.ForeColor = p.Fore;
                    grid.ColumnHeadersDefaultCellStyle.SelectionBackColor = p.Button;
                    grid.ColumnHeadersDefaultCellStyle.SelectionForeColor = p.Fore;
                    grid.DefaultCellStyle.BackColor = p.Input;
                    grid.DefaultCellStyle.ForeColor = p.Fore;
                    return;
                case Label label:
                    if (label.ForeColor == SystemColors.GrayText || label.ForeColor == p.Muted)
                    {
                        label.ForeColor = p.Muted;
                    }
                    else if (label.ForeColor == SystemColors.ControlText || label.ForeColor == SystemColors.WindowText || label.ForeColor == p.Fore)
                    {
                        label.ForeColor = p.Fore;
                    }

                    label.BackColor = Color.Transparent;
                    return;
                case Panel panel when panel.Width == 1 || panel.Height == 1:
                    panel.BackColor = p.Border;
                    return;
                default:
                    if (control.BackColor.IsSystemColor || control.BackColor == Color.Empty)
                    {
                        control.BackColor = p.Back;
                    }

                    if (control.ForeColor.IsSystemColor || control.ForeColor == Color.Empty)
                    {
                        control.ForeColor = p.Fore;
                    }

                    return;
            }
        }

        private static void DarkTitleBar(Form form, Palette p)
        {
            if (!form.IsHandleCreated)
            {
                return;
            }

            try
            {
                var value = p.Dark ? 1 : 0;
                DwmSetWindowAttribute(form.Handle, DwmUseImmersiveDarkMode, ref value, sizeof(int));
            }
            catch (Exception)
            {
                // An older Windows without the attribute keeps its light title bar.
            }
        }

        private static int Brightness(Color c) => (c.R * 299 + c.G * 587 + c.B * 114) / 1000;

        private static Color Blend(Color from, Color to, double amount) => Color.FromArgb(
            (int)(from.R + (to.R - from.R) * amount),
            (int)(from.G + (to.G - from.G) * amount),
            (int)(from.B + (to.B - from.B) * amount));
    }
}
