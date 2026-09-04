using System;
using Microsoft.UI;
using Microsoft.UI.Text;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Automation.Peers;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Shapes;

namespace KeyboardDebounce.WinUI
{
    internal static class SettingsViewFactory
    {
        private static readonly Brush WhiteBrush = new SolidColorBrush(Colors.White);
        private static readonly Brush PageBackgroundBrush = new SolidColorBrush(ColorHelper.FromArgb(0xFF, 0xF7, 0xF8, 0xFB));
        private static readonly Brush PanelBackgroundBrush = new SolidColorBrush(ColorHelper.FromArgb(0xFF, 0xFC, 0xFD, 0xFF));
        private static readonly Brush BorderBrush = new SolidColorBrush(ColorHelper.FromArgb(0xFF, 0xE5, 0xE7, 0xEB));
        private static readonly Brush AccentBrush = new SolidColorBrush(ColorHelper.FromArgb(0xFF, 0x1E, 0x63, 0xD5));
        private static readonly Brush AccentSurfaceBrush = new SolidColorBrush(ColorHelper.FromArgb(0xFF, 0xF5, 0xF9, 0xFF));
        private static readonly Brush CaptionBrush = new SolidColorBrush(ColorHelper.FromArgb(0xFF, 0x60, 0x68, 0x73));

        public static Border CreateSurfaceCard(
            string title,
            string subtitle,
            out StackPanel contentPanel,
            bool accent = false)
        {
            TextBlock titleBlock;
            return CreateSurfaceCard(title, subtitle, out contentPanel, out titleBlock, accent);
        }

        public static Border CreateSurfaceCard(
            string title,
            string subtitle,
            out StackPanel contentPanel,
            out TextBlock titleBlock,
            bool accent = false)
        {
            var headerPanel = new StackPanel
            {
                Spacing = 4
            };
            titleBlock = CreateSectionTitle(title);
            headerPanel.Children.Add(titleBlock);
            if (!String.IsNullOrWhiteSpace(subtitle))
            {
                headerPanel.Children.Add(CreateCaption(subtitle));
            }

            contentPanel = new StackPanel
            {
                Spacing = 16
            };

            var layout = new StackPanel
            {
                Spacing = 16
            };
            layout.Children.Add(headerPanel);
            layout.Children.Add(contentPanel);

            return CreateSurfaceBorder(layout, accent);
        }

        public static void SetSurfaceCardAccent(Border card, bool accent)
        {
            if (card == null) return;

            card.Background = accent ? AccentSurfaceBrush : WhiteBrush;
            card.BorderBrush = accent ? AccentBrush : BorderBrush;
            card.BorderThickness = new Thickness(accent ? 1.5 : 1);
        }

        public static Border CreateFillSurfaceCard(
            string title,
            string subtitle,
            FrameworkElement headerTrailing,
            out Grid contentGrid,
            bool accent = false)
        {
            var headerGrid = new Grid
            {
                ColumnSpacing = 16
            };
            headerGrid.ColumnDefinitions.Add(new ColumnDefinition
            {
                Width = new GridLength(1, GridUnitType.Star)
            });
            headerGrid.ColumnDefinitions.Add(new ColumnDefinition
            {
                Width = GridLength.Auto
            });

            var titlePanel = new StackPanel
            {
                Spacing = 4,
                VerticalAlignment = VerticalAlignment.Center
            };
            titlePanel.Children.Add(CreateSectionTitle(title));
            if (!String.IsNullOrWhiteSpace(subtitle))
            {
                titlePanel.Children.Add(CreateCaption(subtitle));
            }
            headerGrid.Children.Add(titlePanel);

            if (headerTrailing != null)
            {
                headerTrailing.VerticalAlignment = VerticalAlignment.Center;
                Grid.SetColumn(headerTrailing, 1);
                headerGrid.Children.Add(headerTrailing);
            }

            contentGrid = new Grid
            {
                RowSpacing = 12,
                HorizontalAlignment = HorizontalAlignment.Stretch,
                VerticalAlignment = VerticalAlignment.Stretch
            };

            var layout = new Grid
            {
                RowSpacing = 12,
                HorizontalAlignment = HorizontalAlignment.Stretch,
                VerticalAlignment = VerticalAlignment.Stretch
            };
            layout.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            layout.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
            layout.Children.Add(headerGrid);
            Grid.SetRow(contentGrid, 1);
            layout.Children.Add(contentGrid);

            return CreateSurfaceBorder(layout, accent);
        }

        private static Border CreateSurfaceBorder(UIElement child, bool accent)
        {
            return new Border
            {
                Background = accent ? AccentSurfaceBrush : WhiteBrush,
                BorderBrush = accent ? AccentBrush : BorderBrush,
                BorderThickness = new Thickness(accent ? 1.5 : 1),
                CornerRadius = new CornerRadius(12),
                Padding = new Thickness(20, 18, 20, 18),
                Child = child
            };
        }

        public static Border CreatePagePanel(UIElement child)
        {
            return new Border
            {
                Background = PanelBackgroundBrush,
                BorderBrush = BorderBrush,
                BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(18),
                Padding = new Thickness(0),
                Child = child
            };
        }

        public static Grid CreateThreeColumnGrid()
        {
            var grid = new Grid
            {
                ColumnSpacing = 20,
                RowSpacing = 20
            };
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1.3, GridUnitType.Star) });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1.05, GridUnitType.Star) });
            return grid;
        }

        public static Grid CreateTwoColumnGrid()
        {
            var grid = new Grid
            {
                ColumnSpacing = 20,
                RowSpacing = 20
            };
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            return grid;
        }

        public static TextBlock CreatePageTitle(string text)
        {
            var block = new TextBlock
            {
                Text = text,
                FontSize = 30,
                FontWeight = FontWeights.SemiBold
            };
            AutomationProperties.SetHeadingLevel(block, AutomationHeadingLevel.Level1);
            return block;
        }

        public static TextBlock CreateSectionTitle(string text)
        {
            var block = new TextBlock
            {
                Text = text,
                FontSize = 16,
                FontWeight = FontWeights.SemiBold
            };
            AutomationProperties.SetHeadingLevel(block, AutomationHeadingLevel.Level2);
            return block;
        }

        public static TextBlock CreateBody(string text)
        {
            return new TextBlock
            {
                Text = text,
                FontSize = 15,
                TextWrapping = TextWrapping.Wrap
            };
        }

        public static TextBlock CreateCaption(string text)
        {
            return new TextBlock
            {
                Text = text,
                FontSize = 13,
                Foreground = CaptionBrush,
                TextWrapping = TextWrapping.Wrap
            };
        }

        public static Button CreatePrimaryButton(string text, RoutedEventHandler handler)
        {
            var button = new Button
            {
                Content = text,
                MinWidth = 148,
                Height = 42,
                CornerRadius = new CornerRadius(8)
            };
            button.Click += handler;
            return button;
        }

        public static Button CreateSecondaryButton(string text, RoutedEventHandler handler)
        {
            var button = new Button
            {
                Content = text,
                MinWidth = 132,
                Height = 42,
                CornerRadius = new CornerRadius(8)
            };
            button.Click += handler;
            return button;
        }

        public static Grid CreateIconStat(string glyph, string label, TextBlock value)
        {
            var row = new Grid
            {
                ColumnSpacing = 10,
                HorizontalAlignment = HorizontalAlignment.Stretch
            };
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(26) });
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(104) });
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

            FontIcon icon = CreateGlyph(glyph, 20);
            Grid.SetColumn(icon, 0);
            row.Children.Add(icon);

            var labelBlock = CreateBody(label);
            Grid.SetColumn(labelBlock, 1);
            value.HorizontalAlignment = HorizontalAlignment.Right;
            value.TextAlignment = TextAlignment.Right;
            Grid.SetColumn(value, 2);
            row.Children.Add(labelBlock);
            row.Children.Add(value);
            return row;
        }

        public static FontIcon CreateGlyph(string glyph, double size)
        {
            return new FontIcon
            {
                Glyph = glyph,
                FontFamily = new FontFamily("Segoe Fluent Icons"),
                FontSize = size,
                Foreground = AccentBrush
            };
        }

        public static NavigationViewItem CreateNavigationItem(
            SettingsPageId pageId,
            string text,
            string glyph)
        {
            return new NavigationViewItem
            {
                Content = text,
                Icon = CreateGlyph(glyph, 18),
                Tag = pageId
            };
        }

        public static Grid CreateLabeledSlider(
            string title,
            string subtitle,
            Slider slider,
            NumberBox box)
        {
            AutomationProperties.SetName(slider, title);
            AutomationProperties.SetName(box, title + " 数值");
            var grid = new Grid
            {
                RowSpacing = 10
            };
            grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

            var titleBlock = CreateBody(title);
            Grid.SetRow(titleBlock, 0);
            grid.Children.Add(titleBlock);

            var subtitleBlock = CreateCaption(subtitle);
            Grid.SetRow(subtitleBlock, 1);
            grid.Children.Add(subtitleBlock);

            var controlRow = new Grid
            {
                ColumnSpacing = 16
            };
            controlRow.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            controlRow.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            slider.VerticalAlignment = VerticalAlignment.Center;
            box.VerticalAlignment = VerticalAlignment.Center;
            Grid.SetColumn(slider, 0);
            Grid.SetColumn(box, 1);
            controlRow.Children.Add(slider);
            controlRow.Children.Add(box);
            Grid.SetRow(controlRow, 2);
            grid.Children.Add(controlRow);
            return grid;
        }

        public static Border CreateDivider()
        {
            return new Border
            {
                Height = 1,
                Background = BorderBrush
            };
        }

        public static TextBlock CreateValueBlock()
        {
            return new TextBlock
            {
                FontSize = 14,
                FontWeight = FontWeights.Medium,
                TextWrapping = TextWrapping.Wrap
            };
        }

        public static Brush CreateWindowBackgroundBrush()
        {
            return PageBackgroundBrush;
        }

        public static Brush CreateAccentBrush()
        {
            return AccentBrush;
        }

        public static Brush CreateBorderBrush()
        {
            return BorderBrush;
        }

        public static Brush CreateCaptionBrush()
        {
            return CaptionBrush;
        }
    }
}
