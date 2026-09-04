using System;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Automation.Peers;
using Microsoft.UI.Xaml.Controls;

namespace KeyboardDebounce.WinUI
{
    internal sealed class OverviewPage : SettingsPageBase
    {
        private readonly TextBlock _enabledValue;
        private readonly TextBlock _recentEventValue;
        private readonly TextBlock _gameModeValue;
        private readonly TextBlock _processCountValue;
        private readonly TextBlock _normalSensitivityValue;
        private readonly TextBlock _normalThresholdValue;
        private readonly TextBlock _normalLongHoldValue;
        private readonly TextBlock _learnedCountValue;
        private readonly TextBlock _ignoredCountValue;
        private readonly TextBlock _gameFilteredCountValue;
        private readonly TextBlock _bottomSensitivityValue;
        private readonly TextBlock _bottomThresholdValue;
        private readonly TextBlock _bottomLongHoldValue;
        private readonly TextBlock _bottomLearnedCountValue;
        private readonly TextBlock _bottomIgnoredCountValue;
        private readonly TextBlock _bottomGameFilteredCountValue;
        private readonly TextBlock _recentEventSummaryValue;
        private readonly TextBlock _gameCardStateValue;
        private readonly TextBlock _gameCardStatusValue;
        private readonly TextBlock _gameCardThresholdValue;
        private readonly TextBlock _gameCardLongHoldValue;
        private readonly TextBlock _gameCardFilteredCountValue;
        private readonly TextBlock _gameCardActiveExecutableValue;
        private readonly Border _normalCard;
        private readonly Border _gameModeCard;
        private readonly TextBlock _normalCardTitle;
        private readonly TextBlock _gameModeCardTitle;

        public OverviewPage(SettingsUiContext context)
            : base(context)
        {
            RootPanel.Children.Add(SettingsViewFactory.CreatePageTitle("概览"));

            var topGrid = SettingsViewFactory.CreateThreeColumnGrid();
            topGrid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            RootPanel.Children.Add(topGrid);

            StackPanel statusContent;
            Border statusCard = SettingsViewFactory.CreateSurfaceCard("运行状态", "当前模式、最近事件和游戏检测。", out statusContent);
            statusContent.Spacing = 10;
            statusCard.Background = new Microsoft.UI.Xaml.Media.SolidColorBrush(
                Microsoft.UI.ColorHelper.FromArgb(0xFF, 0xF7, 0xFA, 0xFF));
            statusCard.BorderBrush = new Microsoft.UI.Xaml.Media.SolidColorBrush(
                Microsoft.UI.ColorHelper.FromArgb(0xFF, 0xDB, 0xE7, 0xFA));
            _enabledValue = SettingsViewFactory.CreateValueBlock();
            _recentEventValue = SettingsViewFactory.CreateValueBlock();
            _gameModeValue = SettingsViewFactory.CreateValueBlock();
            _processCountValue = SettingsViewFactory.CreateValueBlock();
            statusContent.Children.Add(SettingsViewFactory.CreateIconStat("\uE915", "防抖状态", _enabledValue));
            statusContent.Children.Add(SettingsViewFactory.CreateIconStat("\uE9CE", "最近事件", _recentEventValue));
            statusContent.Children.Add(SettingsViewFactory.CreateIconStat("\uE7FC", "游戏模式", _gameModeValue));
            statusContent.Children.Add(SettingsViewFactory.CreateIconStat("\uE81C", "游戏 EXE 数", _processCountValue));
            Grid.SetColumn(statusCard, 0);
            topGrid.Children.Add(statusCard);

            StackPanel normalContent;
            _normalCard = SettingsViewFactory.CreateSurfaceCard(
                "普通模式",
                "日常输入的全局参数和学习摘要。",
                out normalContent,
                out _normalCardTitle,
                true);
            normalContent.Spacing = 8;
            _normalSensitivityValue = SettingsViewFactory.CreateValueBlock();
            _normalThresholdValue = SettingsViewFactory.CreateValueBlock();
            _normalLongHoldValue = SettingsViewFactory.CreateValueBlock();
            _learnedCountValue = SettingsViewFactory.CreateValueBlock();
            _ignoredCountValue = SettingsViewFactory.CreateValueBlock();
            _gameFilteredCountValue = SettingsViewFactory.CreateValueBlock();
            normalContent.Children.Add(CreateSummaryMetric("全局敏感度", _normalSensitivityValue));
            normalContent.Children.Add(CreateSummaryMetric("默认阈值", _normalThresholdValue));
            normalContent.Children.Add(CreateSummaryMetric("长按放行", _normalLongHoldValue));
            normalContent.Children.Add(CreateSummaryMetric("已学习按键", _learnedCountValue));
            normalContent.Children.Add(CreateSummaryMetric("游戏防抖键", _gameFilteredCountValue));
            normalContent.Children.Add(CreateSummaryMetric("始终忽略", _ignoredCountValue));
            normalContent.Children.Add(SettingsViewFactory.CreatePrimaryButton("调整普通防抖参数", OnGoNormal));
            Grid.SetColumn(_normalCard, 1);
            topGrid.Children.Add(_normalCard);

            StackPanel gameModeContent;
            _gameModeCard = SettingsViewFactory.CreateSurfaceCard(
                "游戏模式",
                "自动切换低干预防抖。",
                out gameModeContent,
                out _gameModeCardTitle);
            _gameCardStateValue = SettingsViewFactory.CreateValueBlock();
            _gameCardStatusValue = SettingsViewFactory.CreateValueBlock();
            _gameCardThresholdValue = SettingsViewFactory.CreateValueBlock();
            _gameCardLongHoldValue = SettingsViewFactory.CreateValueBlock();
            _gameCardFilteredCountValue = SettingsViewFactory.CreateValueBlock();
            _gameCardActiveExecutableValue = SettingsViewFactory.CreateValueBlock();
            AutomationProperties.SetLiveSetting(_gameCardStatusValue, AutomationLiveSetting.Polite);
            gameModeContent.Children.Add(CreateSummaryMetric("自动切换", _gameCardStateValue));
            gameModeContent.Children.Add(CreateSummaryMetric("当前状态", _gameCardStatusValue));
            gameModeContent.Children.Add(CreateSummaryMetric("阈值上限", _gameCardThresholdValue));
            gameModeContent.Children.Add(CreateSummaryMetric("长按放行", _gameCardLongHoldValue));
            gameModeContent.Children.Add(CreateSummaryMetric("游戏防抖键数", _gameCardFilteredCountValue));
            gameModeContent.Children.Add(CreateSummaryMetric("活动 EXE", _gameCardActiveExecutableValue));
            gameModeContent.Children.Add(SettingsViewFactory.CreatePrimaryButton("添加游戏 EXE", OnGoGameMode));
            Grid.SetColumn(_gameModeCard, 2);
            topGrid.Children.Add(_gameModeCard);

            var bottomPanel = new Grid
            {
                ColumnSpacing = 20
            };
            bottomPanel.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(4.6, GridUnitType.Star) });
            bottomPanel.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(5.4, GridUnitType.Star) });
            RootPanel.Children.Add(bottomPanel);

            StackPanel recentContent;
            Border recentCard = SettingsViewFactory.CreateSurfaceCard("最近事件", "当前只保留最新一条事件，便于快速确认运行状态。", out recentContent);
            _recentEventSummaryValue = SettingsViewFactory.CreateValueBlock();
            AutomationProperties.SetLiveSetting(_recentEventSummaryValue, AutomationLiveSetting.Polite);
            recentContent.Children.Add(_recentEventSummaryValue);
            recentContent.Children.Add(SettingsViewFactory.CreateCaption("主进程会持续更新这里的内容。"));
            Grid.SetColumn(recentCard, 0);
            bottomPanel.Children.Add(recentCard);

            StackPanel configContent;
            Border configCard = SettingsViewFactory.CreateSurfaceCard("普通模式参数摘要", "对应当前启用的普通模式基础参数。", out configContent);
            _bottomSensitivityValue = SettingsViewFactory.CreateValueBlock();
            _bottomThresholdValue = SettingsViewFactory.CreateValueBlock();
            _bottomLongHoldValue = SettingsViewFactory.CreateValueBlock();
            _bottomLearnedCountValue = SettingsViewFactory.CreateValueBlock();
            _bottomIgnoredCountValue = SettingsViewFactory.CreateValueBlock();
            _bottomGameFilteredCountValue = SettingsViewFactory.CreateValueBlock();
            var summaryGrid = new Grid { ColumnSpacing = 14 };
            for (int column = 0; column < 6; column++)
            {
                summaryGrid.ColumnDefinitions.Add(
                    new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            }
            AddCompactMetric(summaryGrid, 0, "敏感度", _bottomSensitivityValue);
            AddCompactMetric(summaryGrid, 1, "默认阈值", _bottomThresholdValue);
            AddCompactMetric(summaryGrid, 2, "长按放行", _bottomLongHoldValue);
            AddCompactMetric(summaryGrid, 3, "已学习", _bottomLearnedCountValue);
            AddCompactMetric(summaryGrid, 4, "始终忽略", _bottomIgnoredCountValue);
            AddCompactMetric(summaryGrid, 5, "游戏防抖", _bottomGameFilteredCountValue);
            configContent.Children.Add(summaryGrid);
            Grid.SetColumn(configCard, 1);
            bottomPanel.Children.Add(configCard);
        }

        internal override SettingsPageId PageId
        {
            get { return SettingsPageId.Overview; }
        }

        internal override string PageTitle
        {
            get { return "概览"; }
        }

        protected override void RefreshDataCore()
        {
            Context.Settings.Normalize();
            _enabledValue.Text = Context.Settings.Enabled ? "已启用" : "已暂停";
            _recentEventValue.Text = NormalizeStatus(Context.GetCurrentStatus());
            _recentEventSummaryValue.Text = NormalizeStatus(Context.GetCurrentStatus());
            _processCountValue.Text = (Context.Settings.GameProcesses == null ? 0 : Context.Settings.GameProcesses.Count).ToString();
            _normalSensitivityValue.Text = Context.Settings.GlobalSensitivity.ToString("0.00");
            _normalThresholdValue.Text = Context.Settings.DefaultThresholdMs + "ms";
            _normalLongHoldValue.Text = Context.Settings.LongHoldBypassMs + "ms";
            _learnedCountValue.Text = Context.Learning.Keys == null ? "0" : Context.Learning.Keys.Count.ToString();
            _ignoredCountValue.Text = Context.Settings.IgnoredKeys == null ? "0" : Context.Settings.IgnoredKeys.Count.ToString();
            _gameFilteredCountValue.Text = Context.Settings.GameModeFilteredKeys == null ? "0" : Context.Settings.GameModeFilteredKeys.Count.ToString();
            _bottomSensitivityValue.Text = _normalSensitivityValue.Text;
            _bottomThresholdValue.Text = _normalThresholdValue.Text;
            _bottomLongHoldValue.Text = _normalLongHoldValue.Text;
            _bottomLearnedCountValue.Text = _learnedCountValue.Text;
            _bottomIgnoredCountValue.Text = _ignoredCountValue.Text;
            _bottomGameFilteredCountValue.Text = _gameFilteredCountValue.Text;
            _gameCardThresholdValue.Text = Context.Settings.GameModeThresholdMs + "ms";
            _gameCardLongHoldValue.Text = Context.Settings.GameModeLongHoldBypassMs + "ms";
            _gameCardFilteredCountValue.Text = _gameFilteredCountValue.Text;
            RefreshGameModePresentation();
        }

        protected override void RefreshRuntimeStateCore()
        {
            _recentEventValue.Text = NormalizeStatus(Context.GetCurrentStatus());
            _recentEventSummaryValue.Text = NormalizeStatus(Context.GetCurrentStatus());
            RefreshGameModePresentation();
        }

        private void RefreshGameModePresentation()
        {
            GameModeRuntimeSnapshot snapshot = Context.GetCurrentGameModeSnapshot();
            string status = Context.GetCurrentGameModeStatus();
            bool isGameModeActive = snapshot != null && snapshot.IsActive;

            _gameModeValue.Text = status;
            _gameCardStateValue.Text = Context.Settings.ProcessGameModeEnabled ? "已开启" : "已关闭";
            _gameCardStatusValue.Text = status;
            _gameCardActiveExecutableValue.Text = isGameModeActive
                ? GameProcessDetector.FormatExecutableName(snapshot.ActiveExecutable)
                : "无";

            _normalCardTitle.Text = isGameModeActive ? "普通模式" : "普通模式（当前）";
            _gameModeCardTitle.Text = isGameModeActive ? "游戏模式（当前）" : "游戏模式";
            SettingsViewFactory.SetSurfaceCardAccent(_normalCard, !isGameModeActive);
            SettingsViewFactory.SetSurfaceCardAccent(_gameModeCard, isGameModeActive);
        }

        private static UIElement CreateSummaryMetric(string label, TextBlock value)
        {
            var grid = new Grid
            {
                ColumnSpacing = 16
            };
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(120) });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            var labelBlock = SettingsViewFactory.CreateCaption(label);
            Grid.SetColumn(labelBlock, 0);
            Grid.SetColumn(value, 1);
            grid.Children.Add(labelBlock);
            grid.Children.Add(value);
            return grid;
        }

        private static void AddCompactMetric(
            Grid grid,
            int column,
            string label,
            TextBlock value)
        {
            var panel = new StackPanel { Spacing = 6 };
            panel.Children.Add(SettingsViewFactory.CreateCaption(label));
            panel.Children.Add(value);
            Grid.SetColumn(panel, column);
            grid.Children.Add(panel);
        }

        private static string NormalizeStatus(string value)
        {
            return String.IsNullOrWhiteSpace(value) ? "等待按键事件" : value;
        }

        private void OnGoNormal(object sender, RoutedEventArgs e)
        {
            Context.RequestNavigation(SettingsPageId.NormalDebounce);
        }

        private void OnGoGameMode(object sender, RoutedEventArgs e)
        {
            Context.RequestNavigation(SettingsPageId.GameMode);
        }
    }
}
