using System;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Controls.Primitives;

namespace KeyboardDebounce.WinUI
{
    internal sealed class NormalDebouncePage : SettingsPageBase
    {
        private readonly Slider _sensitivitySlider;
        private readonly NumberBox _sensitivityBox;
        private readonly Slider _thresholdSlider;
        private readonly NumberBox _thresholdBox;
        private readonly Slider _longHoldSlider;
        private readonly NumberBox _longHoldBox;
        public NormalDebouncePage(SettingsUiContext context)
            : base(context)
        {
            RootPanel.Children.Add(SettingsViewFactory.CreatePageTitle("普通防抖"));

            var contentGrid = new Grid
            {
                ColumnSpacing = 20
            };
            contentGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1.65, GridUnitType.Star) });
            contentGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            RootPanel.Children.Add(contentGrid);

            StackPanel parameterContent;
            Border parameterCard = SettingsViewFactory.CreateSurfaceCard("核心参数", "滑块与数字框保持联动。", out parameterContent, true);
            var parameterGrid = new Grid
            {
                ColumnSpacing = 18,
                RowSpacing = 18
            };
            parameterGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            parameterGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            parameterGrid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            parameterGrid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            parameterContent.Children.Add(parameterGrid);

            _sensitivitySlider = new Slider
            {
                Minimum = 50,
                Maximum = 300,
                StepFrequency = 5
            };
            _sensitivitySlider.ValueChanged += OnSensitivitySliderChanged;
            _sensitivityBox = SettingsPageBase.CreateDecimalBox(0.5, 3.0, 0.05);
            _sensitivityBox.ValueChanged += OnSensitivityBoxChanged;
            Grid sensitivitySection = SettingsViewFactory.CreateLabeledSlider(
                "全局敏感度",
                "控制学习阈值的整体放大倍数，范围 0.50 到 3.00。",
                _sensitivitySlider,
                _sensitivityBox);
            Grid.SetColumn(sensitivitySection, 0);
            Grid.SetRow(sensitivitySection, 0);
            parameterGrid.Children.Add(sensitivitySection);

            _thresholdSlider = new Slider
            {
                Minimum = 20,
                Maximum = 250,
                StepFrequency = 5
            };
            _thresholdSlider.ValueChanged += OnThresholdSliderChanged;
            _thresholdBox = SettingsPageBase.CreateIntegerBox(20, 250, 5);
            _thresholdBox.ValueChanged += OnThresholdBoxChanged;
            Grid thresholdSection = SettingsViewFactory.CreateLabeledSlider(
                "默认阈值（ms）",
                "没有足够学习数据时使用的普通模式阈值。",
                _thresholdSlider,
                _thresholdBox);
            Grid.SetColumn(thresholdSection, 1);
            Grid.SetRow(thresholdSection, 0);
            parameterGrid.Children.Add(thresholdSection);

            _longHoldSlider = new Slider
            {
                Minimum = 250,
                Maximum = 1000,
                StepFrequency = 10
            };
            _longHoldSlider.ValueChanged += OnLongHoldSliderChanged;
            _longHoldBox = SettingsPageBase.CreateIntegerBox(250, 1000, 10);
            _longHoldBox.ValueChanged += OnLongHoldBoxChanged;
            Grid longHoldSection = SettingsViewFactory.CreateLabeledSlider(
                "长按放行（ms）",
                "按住超过该时长后，重复输入直接放行。",
                _longHoldSlider,
                _longHoldBox);
            Grid.SetColumn(longHoldSection, 0);
            Grid.SetRow(longHoldSection, 1);
            Grid.SetColumnSpan(longHoldSection, 2);
            parameterGrid.Children.Add(longHoldSection);

            Grid.SetColumn(parameterCard, 0);
            contentGrid.Children.Add(parameterCard);

            var sidePanel = new StackPanel
            {
                Spacing = 14
            };
            Grid.SetColumn(sidePanel, 1);
            contentGrid.Children.Add(sidePanel);

            StackPanel presetContent;
            Border presetCard = SettingsViewFactory.CreateSurfaceCard("快捷操作", "保留现有激进预设，不覆盖学习结果。", out presetContent);
            Button presetButton = SettingsViewFactory.CreatePrimaryButton("应用激进预设", OnApplyAggressivePreset);
            presetButton.HorizontalAlignment = HorizontalAlignment.Stretch;
            presetContent.Children.Add(presetButton);
            presetContent.Children.Add(SettingsViewFactory.CreateCaption("预设值：默认阈值 160ms、长按放行 650ms、全局敏感度 1.50。"));
            sidePanel.Children.Add(presetCard);

            StackPanel learningContent;
            Border learningCard = SettingsViewFactory.CreateSurfaceCard(
                "学习机制",
                "每个按键都有独立的学习结果。",
                out learningContent);
            learningContent.Children.Add(SettingsViewFactory.CreateBody(
                "普通模式会根据真实按键间隔逐步校准阈值，学习数据会自动保存。"));
            learningContent.Children.Add(SettingsViewFactory.CreateCaption(
                "游戏模式只会临时使用自己的阈值与按键范围，不会覆盖普通模式的学习结果。"));
            sidePanel.Children.Add(learningCard);
        }

        internal override SettingsPageId PageId
        {
            get { return SettingsPageId.NormalDebounce; }
        }

        internal override string PageTitle
        {
            get { return "普通防抖"; }
        }

        protected override void RefreshDataCore()
        {
            Context.Settings.Normalize();
            double sensitivity = Math.Round(Context.Settings.GlobalSensitivity, 2);
            _sensitivitySlider.Value = sensitivity * 100.0;
            _sensitivityBox.Value = sensitivity;
            _thresholdSlider.Value = Context.Settings.DefaultThresholdMs;
            _thresholdBox.Value = Context.Settings.DefaultThresholdMs;
            _longHoldSlider.Value = Context.Settings.LongHoldBypassMs;
            _longHoldBox.Value = Context.Settings.LongHoldBypassMs;
        }

        private void OnSensitivitySliderChanged(object sender, RangeBaseValueChangedEventArgs e)
        {
            if (IsRefreshing) return;
            double value = Math.Round(e.NewValue / 100.0, 2);
            _sensitivityBox.Value = value;
            SaveIfChanged(value, Context.Settings.GlobalSensitivity, delegate
            {
                Context.Settings.GlobalSensitivity = value;
                Context.SaveSettingsAndRefresh();
            });
        }

        private void OnSensitivityBoxChanged(NumberBox sender, NumberBoxValueChangedEventArgs args)
        {
            if (IsRefreshing || Double.IsNaN(sender.Value)) return;
            double value = Clamp(Math.Round(sender.Value, 2), 0.5, 3.0);
            _sensitivitySlider.Value = value * 100.0;
            SaveIfChanged(value, Context.Settings.GlobalSensitivity, delegate
            {
                Context.Settings.GlobalSensitivity = value;
                Context.SaveSettingsAndRefresh();
            });
        }

        private void OnThresholdSliderChanged(object sender, RangeBaseValueChangedEventArgs e)
        {
            if (IsRefreshing) return;
            int value = (int)Math.Round(e.NewValue);
            _thresholdBox.Value = value;
            SaveIfChanged(value, Context.Settings.DefaultThresholdMs, delegate
            {
                Context.Settings.DefaultThresholdMs = value;
                Context.SaveSettingsAndRefresh();
            });
        }

        private void OnThresholdBoxChanged(NumberBox sender, NumberBoxValueChangedEventArgs args)
        {
            if (IsRefreshing || Double.IsNaN(sender.Value)) return;
            int value = (int)Clamp(Math.Round(sender.Value), 20, 250);
            _thresholdSlider.Value = value;
            SaveIfChanged(value, Context.Settings.DefaultThresholdMs, delegate
            {
                Context.Settings.DefaultThresholdMs = value;
                Context.SaveSettingsAndRefresh();
            });
        }

        private void OnLongHoldSliderChanged(object sender, RangeBaseValueChangedEventArgs e)
        {
            if (IsRefreshing) return;
            int value = (int)Math.Round(e.NewValue);
            _longHoldBox.Value = value;
            SaveIfChanged(value, Context.Settings.LongHoldBypassMs, delegate
            {
                Context.Settings.LongHoldBypassMs = value;
                Context.SaveSettingsAndRefresh();
            });
        }

        private void OnLongHoldBoxChanged(NumberBox sender, NumberBoxValueChangedEventArgs args)
        {
            if (IsRefreshing || Double.IsNaN(sender.Value)) return;
            int value = (int)Clamp(Math.Round(sender.Value), 250, 1000);
            _longHoldSlider.Value = value;
            SaveIfChanged(value, Context.Settings.LongHoldBypassMs, delegate
            {
                Context.Settings.LongHoldBypassMs = value;
                Context.SaveSettingsAndRefresh();
            });
        }

        private async void OnApplyAggressivePreset(object sender, RoutedEventArgs e)
        {
            bool confirmed = await ShowConfirmationAsync(
                XamlRoot,
                "应用激进预设",
                "这会修改普通模式参数，但不会覆盖任何按键学习阈值。是否继续？",
                "应用");
            if (!confirmed) return;

            Context.Settings.GlobalSensitivity = 1.5;
            Context.Settings.DefaultThresholdMs = 160;
            Context.Settings.LongHoldBypassMs = 650;
            Context.SaveSettingsAndRefresh();
        }

        private static void SaveIfChanged(double nextValue, double currentValue, Action saveAction)
        {
            if (Math.Abs(nextValue - currentValue) < 0.0001) return;
            saveAction();
        }

        private static void SaveIfChanged(int nextValue, int currentValue, Action saveAction)
        {
            if (nextValue == currentValue) return;
            saveAction();
        }

        private static double Clamp(double value, double minimum, double maximum)
        {
            if (value < minimum) return minimum;
            return value > maximum ? maximum : value;
        }
    }
}
