using System;
using System.Drawing;
using System.Windows.Forms;

namespace KeyboardDebounce
{
    internal static class SettingsThemeStyler
    {
        public static void Apply(Control root, SettingsThemePalette palette)
        {
            if (root == null) throw new ArgumentNullException("root");
            if (palette == null) throw new ArgumentNullException("palette");
            ApplyControl(root, palette, palette.Page);
        }

        private static void ApplyControl(
            Control control,
            SettingsThemePalette palette,
            Color inheritedBackground)
        {
            Color childBackground = inheritedBackground;
            control.ForeColor = palette.Text;

            var card = control as SettingsCard;
            if (card != null)
            {
                card.ApplyPalette(palette);
                childBackground = palette.Card;
            }
            else if (String.Equals(control.Tag as string, "Settings.CardSurface", StringComparison.Ordinal))
            {
                control.BackColor = palette.Card;
                childBackground = palette.Card;
            }
            else if (control is TextBoxBase || control is ListBox || control is ComboBox || control is NumericUpDown)
            {
                control.BackColor = palette.Input;
                childBackground = palette.Input;
            }
            else if (control is DataGridView)
            {
                ApplyGrid((DataGridView)control, palette);
                childBackground = palette.Card;
            }
            else if (control is Button)
            {
                var button = (Button)control;
                button.UseVisualStyleBackColor = false;
                button.FlatStyle = FlatStyle.Flat;
                bool primary = String.Equals(
                    button.Tag as string,
                    "Settings.PrimaryAction",
                    StringComparison.Ordinal);
                button.BackColor = primary ? palette.Accent : palette.Card;
                button.ForeColor = primary ? GetAccentTextColor(palette) : palette.Text;
                button.FlatAppearance.BorderColor = primary ? palette.Accent : palette.Border;
                button.FlatAppearance.MouseOverBackColor = primary ? palette.Accent : palette.Selection;
                button.FlatAppearance.MouseDownBackColor = palette.Accent;
                childBackground = button.BackColor;
            }
            else if (control is Label)
            {
                control.BackColor = Color.Transparent;
                if (String.Equals(
                    control.Tag as string,
                    "Settings.MutedText",
                    StringComparison.Ordinal))
                {
                    control.ForeColor = palette.Kind == SettingsThemeKind.HighContrast
                        ? palette.Text
                        : palette.MutedText;
                }
                else if (String.Equals(
                    control.Tag as string,
                    "Settings.ErrorText",
                    StringComparison.Ordinal))
                {
                    control.ForeColor = palette.Error;
                }
            }
            else
            {
                control.BackColor = inheritedBackground;
                childBackground = control.BackColor;
            }

            foreach (Control child in control.Controls)
            {
                ApplyControl(child, palette, childBackground);
            }
        }

        private static void ApplyGrid(DataGridView grid, SettingsThemePalette palette)
        {
            grid.EnableHeadersVisualStyles = false;
            grid.BackgroundColor = palette.Card;
            grid.BorderStyle = BorderStyle.FixedSingle;
            grid.GridColor = palette.Border;
            grid.DefaultCellStyle.BackColor = palette.Card;
            grid.DefaultCellStyle.ForeColor = palette.Text;
            grid.DefaultCellStyle.SelectionBackColor = palette.Accent;
            grid.DefaultCellStyle.SelectionForeColor = GetAccentTextColor(palette);
            grid.ColumnHeadersDefaultCellStyle.BackColor = palette.Page;
            grid.ColumnHeadersDefaultCellStyle.ForeColor = palette.Text;
            grid.ColumnHeadersDefaultCellStyle.SelectionBackColor = palette.Page;
            grid.ColumnHeadersDefaultCellStyle.SelectionForeColor = palette.Text;
            grid.RowHeadersDefaultCellStyle.BackColor = palette.Page;
            grid.RowHeadersDefaultCellStyle.ForeColor = palette.Text;
        }

        internal static Color GetAccentTextColor(SettingsThemePalette palette)
        {
            if (palette.Kind == SettingsThemeKind.HighContrast) return SystemColors.HighlightText;
            return palette.Kind == SettingsThemeKind.Dark ? palette.Page : Color.White;
        }
    }
}
