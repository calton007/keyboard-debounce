using System;
using System.Drawing;
using System.Windows.Forms;

namespace KeyboardDebounce
{
    internal enum SettingsThemeKind
    {
        Light,
        Dark,
        HighContrast
    }

    internal sealed class SettingsThemePalette
    {
        private SettingsThemePalette(
            SettingsThemeKind kind,
            Color page,
            Color card,
            Color text,
            Color mutedText,
            Color border,
            Color accent,
            Color input,
            Color selection,
            Color error)
        {
            Kind = kind;
            Page = page;
            Card = card;
            Text = text;
            MutedText = mutedText;
            Border = border;
            Accent = accent;
            Input = input;
            Selection = selection;
            Error = error;
        }

        public SettingsThemeKind Kind { get; private set; }
        public Color Page { get; private set; }
        public Color Card { get; private set; }
        public Color Text { get; private set; }
        public Color MutedText { get; private set; }
        public Color Border { get; private set; }
        public Color Accent { get; private set; }
        public Color Input { get; private set; }
        public Color Selection { get; private set; }
        public Color Error { get; private set; }

        public static SettingsThemePalette Create(SettingsThemeKind kind)
        {
            switch (kind)
            {
                case SettingsThemeKind.Dark:
                    return new SettingsThemePalette(
                        kind,
                        Color.FromArgb(0x11, 0x18, 0x27),
                        Color.FromArgb(0x1F, 0x29, 0x37),
                        Color.FromArgb(0xF9, 0xFA, 0xFB),
                        Color.FromArgb(0xD1, 0xD5, 0xDB),
                        Color.FromArgb(0x4B, 0x55, 0x63),
                        Color.FromArgb(0x60, 0xA5, 0xFA),
                        Color.FromArgb(0x11, 0x18, 0x27),
                        Color.FromArgb(0x1D, 0x4E, 0x89),
                        Color.FromArgb(0xFC, 0xA5, 0xA5));
                case SettingsThemeKind.HighContrast:
                    return new SettingsThemePalette(
                        kind,
                        SystemColors.Window,
                        SystemColors.Window,
                        SystemColors.WindowText,
                        SystemColors.GrayText,
                        SystemColors.WindowText,
                        SystemColors.Highlight,
                        SystemColors.Window,
                        SystemColors.Highlight,
                        SystemColors.HotTrack);
                default:
                    return new SettingsThemePalette(
                        SettingsThemeKind.Light,
                        Color.FromArgb(0xF3, 0xF4, 0xF6),
                        Color.White,
                        Color.FromArgb(0x11, 0x18, 0x27),
                        Color.FromArgb(0x4B, 0x55, 0x63),
                        Color.FromArgb(0xD1, 0xD5, 0xDB),
                        Color.FromArgb(0x25, 0x63, 0xEB),
                        Color.White,
                        Color.FromArgb(0xDB, 0xEA, 0xFE),
                        Color.FromArgb(0xB9, 0x1C, 0x1C));
            }
        }
    }

    internal interface ISettingsThemeService : IDisposable
    {
        event EventHandler ThemeChanged;

        SettingsThemeKind CurrentKind { get; }
        SettingsThemePalette CurrentPalette { get; }
        string LastError { get; }

        void ApplyTitleBar(Form form);
    }
}
