using System;
using System.Threading.Tasks;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;

namespace KeyboardDebounce.WinUI
{
    internal enum SettingsPageLayoutMode
    {
        Scroll,
        Fill
    }

    internal abstract class SettingsPageBase : UserControl
    {
        private readonly Panel _rootPanel;
        private bool _refreshing;
        private int _settingsVersionSeen;
        private int _learningVersionSeen;
        private int _runtimeVersionSeen;

        protected SettingsPageBase(SettingsUiContext context)
            : this(context, SettingsPageLayoutMode.Scroll)
        {
        }

        protected SettingsPageBase(SettingsUiContext context, SettingsPageLayoutMode layoutMode)
        {
            if (context == null) throw new ArgumentNullException("context");

            Context = context;
            RequestedTheme = ElementTheme.Light;

            if (layoutMode == SettingsPageLayoutMode.Fill)
            {
                _rootPanel = new Grid
                {
                    RowSpacing = 16,
                    Margin = new Thickness(24),
                    HorizontalAlignment = HorizontalAlignment.Stretch,
                    VerticalAlignment = VerticalAlignment.Stretch
                };
                Content = _rootPanel;
            }
            else
            {
                _rootPanel = new StackPanel
                {
                    Spacing = 24,
                    Margin = new Thickness(24),
                    HorizontalAlignment = HorizontalAlignment.Stretch
                };
                Content = new ScrollViewer
                {
                    Content = _rootPanel,
                    HorizontalContentAlignment = HorizontalAlignment.Stretch,
                    HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled,
                    VerticalScrollMode = ScrollMode.Auto,
                    VerticalScrollBarVisibility = ScrollBarVisibility.Auto
                };
            }

            HorizontalAlignment = HorizontalAlignment.Stretch;
            VerticalAlignment = VerticalAlignment.Stretch;
        }

        protected SettingsUiContext Context { get; private set; }

        internal abstract SettingsPageId PageId { get; }
        internal abstract string PageTitle { get; }

        protected bool IsRefreshing
        {
            get { return _refreshing; }
        }

        protected Panel RootPanel
        {
            get { return _rootPanel; }
        }

        internal void ActivatePage()
        {
            if (_settingsVersionSeen != Context.SettingsVersion
                || _learningVersionSeen != Context.LearningVersion)
            {
                RefreshData();
                return;
            }

            if (_runtimeVersionSeen != Context.RuntimeVersion)
            {
                RefreshRuntimeState();
            }
        }

        internal void RefreshData()
        {
            RunRefresh(RefreshDataCore);
            _settingsVersionSeen = Context.SettingsVersion;
            _learningVersionSeen = Context.LearningVersion;
            _runtimeVersionSeen = Context.RuntimeVersion;
        }

        internal void RefreshRuntimeState()
        {
            RunRefresh(RefreshRuntimeStateCore);
            _runtimeVersionSeen = Context.RuntimeVersion;
        }

        protected abstract void RefreshDataCore();

        protected virtual void RefreshRuntimeStateCore()
        {
        }

        internal virtual void OnWindowClosing()
        {
        }

        protected void RunRefresh(Action action)
        {
            _refreshing = true;
            try
            {
                action();
            }
            finally
            {
                _refreshing = false;
            }
        }

        protected static Grid CreateMetricGrid()
        {
            var grid = new Grid
            {
                ColumnSpacing = 16,
                RowSpacing = 10
            };
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(112) });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            return grid;
        }

        protected static void AddMetricRow(Grid grid, int rowIndex, string label, FrameworkElement value)
        {
            grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            var labelBlock = SettingsViewFactory.CreateCaption(label);
            Grid.SetRow(labelBlock, rowIndex);
            Grid.SetColumn(labelBlock, 0);
            Grid.SetRow(value, rowIndex);
            Grid.SetColumn(value, 1);
            grid.Children.Add(labelBlock);
            grid.Children.Add(value);
        }

        protected static NumberBox CreateIntegerBox(int minimum, int maximum, int smallChange)
        {
            return new NumberBox
            {
                Minimum = minimum,
                Maximum = maximum,
                SmallChange = smallChange,
                SpinButtonPlacementMode = NumberBoxSpinButtonPlacementMode.Compact,
                Width = 120
            };
        }

        protected static NumberBox CreateDecimalBox(double minimum, double maximum, double smallChange)
        {
            return new NumberBox
            {
                Minimum = minimum,
                Maximum = maximum,
                SmallChange = smallChange,
                SpinButtonPlacementMode = NumberBoxSpinButtonPlacementMode.Compact,
                Width = 140
            };
        }

        protected int GetEffectiveThresholdForDisplay(KeyLearningState state)
        {
            if (state == null) return 0;

            int value = (int)Math.Round(state.ThresholdMs * Context.Settings.GlobalSensitivity);
            if (value < 20) return 20;
            if (value > 250) return 250;
            return value;
        }

        protected static async Task<bool> ShowConfirmationAsync(
            XamlRoot xamlRoot,
            string title,
            string message,
            string primaryText)
        {
            var dialog = new ContentDialog
            {
                XamlRoot = xamlRoot,
                Title = title,
                Content = new TextBlock
                {
                    Text = message,
                    TextWrapping = TextWrapping.Wrap
                },
                PrimaryButtonText = primaryText,
                CloseButtonText = "取消",
                DefaultButton = ContentDialogButton.Close,
                RequestedTheme = ElementTheme.Light
            };

            return await dialog.ShowAsync() == ContentDialogResult.Primary;
        }

        protected static async Task ShowMessageAsync(
            XamlRoot xamlRoot,
            string title,
            string message)
        {
            var dialog = new ContentDialog
            {
                XamlRoot = xamlRoot,
                Title = title,
                Content = new TextBlock
                {
                    Text = message,
                    TextWrapping = TextWrapping.Wrap
                },
                CloseButtonText = "关闭",
                RequestedTheme = ElementTheme.Light
            };

            await dialog.ShowAsync();
        }
    }
}
