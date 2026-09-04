using System;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Automation.Peers;
using Microsoft.UI.Xaml.Controls;

namespace KeyboardDebounce.WinUI
{
    internal sealed class ApplicationSettingsPage : SettingsPageBase
    {
        private readonly ToggleSwitch _startupSwitch;
        private readonly ToggleSwitch _silentRunSwitch;
        private readonly NumberBox _startupDelayBox;
        private readonly TextBox _pauseHotkeyBox;
        private readonly TextBlock _settingsPathValue;
        private readonly TextBlock _learningPathValue;
        private readonly TextBlock _adminStateValue;
        private readonly InfoBar _statusBar;

        public ApplicationSettingsPage(SettingsUiContext context)
            : base(context)
        {
            RootPanel.Children.Add(SettingsViewFactory.CreatePageTitle("应用设置"));

            var grid = SettingsViewFactory.CreateTwoColumnGrid();
            grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            RootPanel.Children.Add(grid);

            StackPanel startupContent;
            Border startupCard = SettingsViewFactory.CreateSurfaceCard("启动与显示", "控制启动方式和首次显示行为。", out startupContent, true);
            startupContent.Spacing = 10;

            var startupRows = CreateControlGrid();
            Grid.SetRow(CreateToggleRow(startupRows, 0, "开机自启", "登录系统后自动启动 KeyboardDebounce。", out _startupSwitch), 0);
            Grid.SetRow(CreateToggleRow(startupRows, 1, "静默运行", "启动后仅驻留托盘，不主动显示设置窗口。", out _silentRunSwitch), 1);

            _startupSwitch.Toggled += OnStartupToggled;
            _silentRunSwitch.Toggled += OnSilentRunToggled;

            var delayGrid = new Grid
            {
                ColumnSpacing = 16
            };
            delayGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            delayGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

            var delayText = new StackPanel
            {
                Spacing = 4
            };
            delayText.Children.Add(SettingsViewFactory.CreateBody("启动延迟"));
            delayText.Children.Add(SettingsViewFactory.CreateCaption("用于等待桌面和输入环境稳定后再加载。"));
            delayGrid.Children.Add(delayText);

            _startupDelayBox = CreateIntegerBox(0, 30000, 100);
            _startupDelayBox.ValueChanged += OnStartupDelayChanged;
            Grid.SetColumn(_startupDelayBox, 1);
            delayGrid.Children.Add(_startupDelayBox);

            startupContent.Children.Add(startupRows);
            startupContent.Children.Add(SettingsViewFactory.CreateDivider());
            startupContent.Children.Add(delayGrid);
            startupContent.Children.Add(SettingsViewFactory.CreateCaption("启动延迟与自启选项均即时保存。"));
            Grid.SetColumn(startupCard, 0);
            grid.Children.Add(startupCard);

            StackPanel hotkeyContent;
            Border hotkeyCard = SettingsViewFactory.CreateSurfaceCard("暂停热键", "修改后仍通过单独应用按钮提交。", out hotkeyContent);
            hotkeyContent.Children.Add(SettingsViewFactory.CreateCaption("输入组合键后点击应用；实际是否生效取决于现有校验与注册链路。"));

            var hotkeyRow = new Grid
            {
                ColumnSpacing = 12
            };
            hotkeyRow.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            hotkeyRow.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            _pauseHotkeyBox = new TextBox
            {
                PlaceholderText = "例如 Ctrl+Alt+F11"
            };
            hotkeyRow.Children.Add(_pauseHotkeyBox);

            var applyButton = SettingsViewFactory.CreatePrimaryButton("应用热键", OnApplyHotkey);
            applyButton.MinWidth = 128;
            Grid.SetColumn(applyButton, 1);
            hotkeyRow.Children.Add(applyButton);

            hotkeyContent.Children.Add(hotkeyRow);
            hotkeyContent.Children.Add(SettingsViewFactory.CreateCaption("留空表示取消暂停热键。保存失败时会保留原值，并在下方明确提示。"));
            Grid.SetColumn(hotkeyCard, 1);
            grid.Children.Add(hotkeyCard);

            StackPanel diagnosticsContent;
            Border diagnosticsCard = SettingsViewFactory.CreateSurfaceCard("配置与权限", "查看配置文件位置、学习数据和当前权限状态。", out diagnosticsContent);

            var diagnosticsGrid = SettingsViewFactory.CreateTwoColumnGrid();

            var metrics = CreateMetricGrid();
            _settingsPathValue = SettingsViewFactory.CreateValueBlock();
            _learningPathValue = SettingsViewFactory.CreateValueBlock();
            _adminStateValue = SettingsViewFactory.CreateValueBlock();
            _settingsPathValue.TextWrapping = TextWrapping.NoWrap;
            _settingsPathValue.TextTrimming = TextTrimming.CharacterEllipsis;
            _learningPathValue.TextWrapping = TextWrapping.NoWrap;
            _learningPathValue.TextTrimming = TextTrimming.CharacterEllipsis;
            AddMetricRow(metrics, 0, "设置文件", _settingsPathValue);
            AddMetricRow(metrics, 1, "学习文件", _learningPathValue);
            AddMetricRow(metrics, 2, "权限状态", _adminStateValue);

            Grid.SetColumn(metrics, 0);
            diagnosticsGrid.Children.Add(metrics);

            var actionPanel = new StackPanel
            {
                Spacing = 12
            };
            actionPanel.Children.Add(SettingsViewFactory.CreateBody("常用操作"));
            actionPanel.Children.Add(SettingsViewFactory.CreateCaption("需要检查权限或直接打开配置目录时使用。"));

            var actionGrid = new Grid
            {
                ColumnSpacing = 12
            };
            actionGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            actionGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

            var openButton = SettingsViewFactory.CreateSecondaryButton("打开配置目录", OnOpenConfigDirectory);
            openButton.MinWidth = 0;
            openButton.HorizontalAlignment = HorizontalAlignment.Stretch;
            actionGrid.Children.Add(openButton);

            var refreshButton = SettingsViewFactory.CreateSecondaryButton("刷新权限状态", OnRefreshAdminState);
            refreshButton.MinWidth = 0;
            refreshButton.HorizontalAlignment = HorizontalAlignment.Stretch;
            Grid.SetColumn(refreshButton, 1);
            actionGrid.Children.Add(refreshButton);

            actionPanel.Children.Add(actionGrid);

            Grid.SetColumn(actionPanel, 1);
            diagnosticsGrid.Children.Add(actionPanel);
            diagnosticsContent.Children.Add(diagnosticsGrid);
            RootPanel.Children.Add(diagnosticsCard);

            _statusBar = new InfoBar
            {
                IsOpen = false,
                IsClosable = false,
                Margin = new Thickness(0, 2, 0, 0)
            };
            AutomationProperties.SetLiveSetting(_statusBar, AutomationLiveSetting.Assertive);
            RootPanel.Children.Add(_statusBar);
        }

        internal override SettingsPageId PageId
        {
            get { return SettingsPageId.ApplicationSettings; }
        }

        internal override string PageTitle
        {
            get { return "应用设置"; }
        }

        protected override void RefreshDataCore()
        {
            Context.Settings.Normalize();
            _startupSwitch.IsOn = Context.Settings.StartWithWindows;
            _silentRunSwitch.IsOn = Context.Settings.SilentRun;
            _startupDelayBox.Value = Context.Settings.StartupDelayMs;
            _pauseHotkeyBox.Text = Context.Settings.PauseHotkey;
            _settingsPathValue.Text = Context.Store.SettingsPath;
            _learningPathValue.Text = Context.Store.LearningStatePath;
            ToolTipService.SetToolTip(_settingsPathValue, Context.Store.SettingsPath);
            ToolTipService.SetToolTip(_learningPathValue, Context.Store.LearningStatePath);
            _adminStateValue.Text = NativeMethods.IsUserAnAdmin()
                ? "当前已使用管理员权限运行"
                : "当前为普通权限；管理员窗口可能无法完整覆盖";
        }

        private void OnStartupToggled(object sender, RoutedEventArgs e)
        {
            if (IsRefreshing) return;
            if (Context.Settings.StartWithWindows == _startupSwitch.IsOn) return;
            Context.Settings.StartWithWindows = _startupSwitch.IsOn;
            Context.SaveSettingsAndRefresh();
        }

        private void OnSilentRunToggled(object sender, RoutedEventArgs e)
        {
            if (IsRefreshing) return;
            if (Context.Settings.SilentRun == _silentRunSwitch.IsOn) return;
            Context.Settings.SilentRun = _silentRunSwitch.IsOn;
            Context.SaveSettingsAndRefresh();
        }

        private void OnStartupDelayChanged(NumberBox sender, NumberBoxValueChangedEventArgs args)
        {
            if (IsRefreshing || Double.IsNaN(sender.Value)) return;
            int value = (int)Math.Max(0, Math.Min(30000, Math.Round(sender.Value)));
            if (Context.Settings.StartupDelayMs == value) return;
            Context.Settings.StartupDelayMs = value;
            Context.SaveSettingsAndRefresh();
        }

        private void OnApplyHotkey(object sender, RoutedEventArgs e)
        {
            string value = (_pauseHotkeyBox.Text ?? "").Trim();
            if (String.Equals(value, Context.Settings.PauseHotkey, StringComparison.Ordinal)) return;

            string requestedValue = value;
            Context.Settings.PauseHotkey = value;
            Context.SaveSettingsAndRefresh();

            string appliedValue = Context.Settings.PauseHotkey ?? String.Empty;
            bool success = String.Equals(requestedValue, appliedValue, StringComparison.Ordinal);

            _statusBar.Severity = success ? InfoBarSeverity.Success : InfoBarSeverity.Warning;
            _statusBar.Title = success ? "热键已应用" : "热键未生效";
            _statusBar.Message = success
                ? (String.IsNullOrWhiteSpace(appliedValue)
                    ? "暂停热键已清空。"
                    : "当前暂停热键为 " + appliedValue + "。")
                : (String.IsNullOrWhiteSpace(appliedValue)
                    ? "请求的热键未通过校验或注册，当前仍未设置暂停热键。"
                    : "请求的热键未通过校验或注册，当前保留为 " + appliedValue + "。");
            _statusBar.IsOpen = true;
        }

        private async void OnOpenConfigDirectory(object sender, RoutedEventArgs e)
        {
            try
            {
                Context.OpenSettingsDirectory();
            }
            catch (Exception error)
            {
                await ShowMessageAsync(XamlRoot, "无法打开配置目录", error.Message);
            }
        }

        private void OnRefreshAdminState(object sender, RoutedEventArgs e)
        {
            _adminStateValue.Text = NativeMethods.IsUserAnAdmin()
                ? "当前已使用管理员权限运行"
                : "当前为普通权限；管理员窗口可能无法完整覆盖";
        }

        private static Grid CreateControlGrid()
        {
            var grid = new Grid
            {
                RowSpacing = 14
            };
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            return grid;
        }

        private static Grid CreateToggleRow(Grid parent, int rowIndex, string title, string subtitle, out ToggleSwitch toggle)
        {
            parent.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

            var row = new Grid
            {
                ColumnSpacing = 16
            };
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

            var textPanel = new StackPanel
            {
                Spacing = 4
            };
            textPanel.Children.Add(SettingsViewFactory.CreateBody(title));
            textPanel.Children.Add(SettingsViewFactory.CreateCaption(subtitle));
            row.Children.Add(textPanel);

            toggle = new ToggleSwitch
            {
                VerticalAlignment = VerticalAlignment.Center
            };
            Grid.SetColumn(toggle, 1);
            row.Children.Add(toggle);

            parent.Children.Add(row);
            return row;
        }
    }
}
