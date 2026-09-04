using System;
using System.Drawing;
using System.Windows.Forms;

namespace KeyboardDebounce
{
    internal sealed class SettingsCard : Panel
    {
        private readonly Font _titleFont;
        private readonly Label _title;
        private readonly Label _description;
        private Color _borderColor;

        public SettingsCard(string title, string description = null)
        {
            DoubleBuffered = true;
            Dock = DockStyle.Top;
            AutoSize = true;
            AutoSizeMode = AutoSizeMode.GrowAndShrink;
            Margin = new Padding(0);
            Padding = new Padding(16);
            Name = "SettingsCard";

            _titleFont = new Font("Segoe UI", 11F, FontStyle.Bold, GraphicsUnit.Point);
            _title = new Label
            {
                AutoSize = true,
                Font = _titleFont,
                Text = title ?? "",
                Margin = new Padding(0)
            };
            _description = new Label
            {
                AutoSize = true,
                Text = description ?? "",
                Visible = !String.IsNullOrWhiteSpace(description),
                Margin = new Padding(0, 4, 0, 12)
            };
            Content = new TableLayoutPanel
            {
                Dock = DockStyle.Top,
                AutoSize = true,
                AutoSizeMode = AutoSizeMode.GrowAndShrink,
                ColumnCount = 1,
                RowCount = 0,
                Margin = new Padding(0)
            };
            Content.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100F));

            var layout = new TableLayoutPanel
            {
                Dock = DockStyle.Top,
                AutoSize = true,
                AutoSizeMode = AutoSizeMode.GrowAndShrink,
                ColumnCount = 1,
                RowCount = 3,
                Margin = new Padding(0)
            };
            layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100F));
            layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            layout.Controls.Add(_title, 0, 0);
            layout.Controls.Add(_description, 0, 1);
            layout.Controls.Add(Content, 0, 2);
            Controls.Add(layout);

            ApplyPalette(SettingsThemePalette.Create(SettingsThemeKind.Light));
        }

        public TableLayoutPanel Content { get; private set; }

        internal void ApplyPalette(SettingsThemePalette palette)
        {
            if (palette == null) throw new ArgumentNullException("palette");
            BackColor = palette.Card;
            ForeColor = palette.Text;
            _title.BackColor = Color.Transparent;
            _title.ForeColor = palette.Text;
            _description.BackColor = Color.Transparent;
            _description.ForeColor = palette.MutedText;
            Content.BackColor = palette.Card;
            _borderColor = palette.Border;
            Invalidate();
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            base.OnPaint(e);
            using (var pen = new Pen(_borderColor))
            {
                e.Graphics.DrawRectangle(
                    pen,
                    0,
                    0,
                    Math.Max(0, ClientSize.Width - 1),
                    Math.Max(0, ClientSize.Height - 1));
            }
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing) _titleFont.Dispose();
            base.Dispose(disposing);
        }
    }
}
