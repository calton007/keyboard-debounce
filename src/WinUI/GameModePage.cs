using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Automation.Peers;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Controls.Primitives;
using Windows.Storage;
using Windows.Storage.Pickers;
using WinRT.Interop;

namespace KeyboardDebounce.WinUI
{
    internal sealed class GameModePage : SettingsPageBase
    {
        private readonly ToggleSwitch _enabledSwitch;
        private readonly TextBlock _statusValue;
        private readonly TextBlock _activeExecutableValue;
        private readonly Slider _thresholdSlider;
        private readonly NumberBox _thresholdBox;
        private readonly Slider _longHoldSlider;
        private readonly NumberBox _longHoldBox;
        private readonly ObservableCollection<ExecutableItemViewModel> _executables;
        private readonly ListView _executableList;
        private readonly TextBlock _summaryValue;
        private readonly InfoBar _statusBar;

        public GameModePage(SettingsUiContext context)
            : base(context)
        {
            _executables = new ObservableCollection<ExecutableItemViewModel>();

            RootPanel.Children.Add(SettingsViewFactory.CreatePageTitle("游戏模式"));

            var topGrid = SettingsViewFactory.CreateTwoColumnGrid();
            topGrid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            RootPanel.Children.Add(topGrid);

            StackPanel modeContent;
            Border modeCard = SettingsViewFactory.CreateSurfaceCard("当前状态", "切换自动模式并调整游戏参数。", out modeContent, true);
            modeContent.Spacing = 10;
            _enabledSwitch = new ToggleSwitch
            {
                Header = "自动切换游戏模式"
            };
            _enabledSwitch.Toggled += OnEnabledToggled;
            modeContent.Children.Add(_enabledSwitch);

            _statusValue = SettingsViewFactory.CreateValueBlock();
            AutomationProperties.SetLiveSetting(_statusValue, AutomationLiveSetting.Polite);
            _activeExecutableValue = SettingsViewFactory.CreateCaption("");
            modeContent.Children.Add(_statusValue);
            modeContent.Children.Add(_activeExecutableValue);

            _thresholdSlider = new Slider
            {
                Minimum = 20,
                Maximum = 250,
                StepFrequency = 5
            };
            _thresholdSlider.ValueChanged += OnThresholdSliderChanged;
            _thresholdBox = CreateIntegerBox(20, 250, 5);
            _thresholdBox.ValueChanged += OnThresholdBoxChanged;
            modeContent.Children.Add(SettingsViewFactory.CreateLabeledSlider(
                "阈值上限（ms）",
                "限制游戏模式的最大防抖阈值，数值越小响应越快。",
                _thresholdSlider,
                _thresholdBox));

            _longHoldSlider = new Slider
            {
                Minimum = 50,
                Maximum = 1000,
                StepFrequency = 10
            };
            _longHoldSlider.ValueChanged += OnLongHoldSliderChanged;
            _longHoldBox = CreateIntegerBox(50, 1000, 10);
            _longHoldBox.ValueChanged += OnLongHoldBoxChanged;
            modeContent.Children.Add(SettingsViewFactory.CreateLabeledSlider(
                "长按放行（ms）",
                "按键持续超过该时间后，后续重复输入直接放行。",
                _longHoldSlider,
                _longHoldBox));
            Grid.SetColumn(modeCard, 0);
            topGrid.Children.Add(modeCard);

            StackPanel executableContent;
            Border executableCard = SettingsViewFactory.CreateSurfaceCard("游戏 EXE", "可从磁盘或正在运行的程序中便捷多选。", out executableContent);
            executableContent.Spacing = 12;
            var actionGrid = new Grid
            {
                ColumnSpacing = 12,
                RowSpacing = 12
            };
            actionGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            actionGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            actionGrid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            actionGrid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

            Button browseButton = SettingsViewFactory.CreatePrimaryButton("浏览选择 EXE", OnBrowseExecutableAsync);
            Button runningButton = SettingsViewFactory.CreateSecondaryButton("从正在运行的程序添加", OnBrowseRunningAsync);
            Button removeButton = SettingsViewFactory.CreateSecondaryButton("移除所选", OnRemoveSelected);
            Button refreshButton = SettingsViewFactory.CreateSecondaryButton("刷新运行状态", OnRefreshRuntimeState);
            Button[] actions = { browseButton, runningButton, removeButton, refreshButton };
            for (int index = 0; index < actions.Length; index++)
            {
                actions[index].MinWidth = 0;
                actions[index].HorizontalAlignment = HorizontalAlignment.Stretch;
                Grid.SetColumn(actions[index], index % 2);
                Grid.SetRow(actions[index], index / 2);
                actionGrid.Children.Add(actions[index]);
            }
            executableContent.Children.Add(actionGrid);

            _summaryValue = SettingsViewFactory.CreateCaption("");
            executableContent.Children.Add(_summaryValue);

            _executableList = new ListView
            {
                ItemsSource = _executables,
                SelectionMode = ListViewSelectionMode.Multiple,
                Height = 220
            };
            _executableList.ItemTemplate = CreateExecutableTemplate();
            executableContent.Children.Add(_executableList);

            _statusBar = new InfoBar
            {
                IsOpen = false,
                IsClosable = false
            };
            AutomationProperties.SetLiveSetting(_statusBar, AutomationLiveSetting.Assertive);
            executableContent.Children.Add(_statusBar);
            Grid.SetColumn(executableCard, 1);
            topGrid.Children.Add(executableCard);
        }

        internal override SettingsPageId PageId
        {
            get { return SettingsPageId.GameMode; }
        }

        internal override string PageTitle
        {
            get { return "游戏模式"; }
        }

        protected override void RefreshDataCore()
        {
            Context.Settings.Normalize();
            _enabledSwitch.IsOn = Context.Settings.ProcessGameModeEnabled;
            _thresholdSlider.Value = Context.Settings.GameModeThresholdMs;
            _thresholdBox.Value = Context.Settings.GameModeThresholdMs;
            _longHoldSlider.Value = Context.Settings.GameModeLongHoldBypassMs;
            _longHoldBox.Value = Context.Settings.GameModeLongHoldBypassMs;
            SynchronizeExecutables(null);
            UpdateRuntimePresentation();
        }

        protected override void RefreshRuntimeStateCore()
        {
            UpdateRuntimePresentation();
        }

        private void UpdateRuntimePresentation()
        {
            GameModeRuntimeSnapshot snapshot = Context.GetCurrentGameModeSnapshot();
            if (snapshot == null) return;

            _statusValue.Text = Context.GetCurrentGameModeStatus();
            if (snapshot.IsActive && !String.IsNullOrWhiteSpace(snapshot.ActiveExecutable))
            {
                _activeExecutableValue.Text = "当前运行的 EXE："
                    + GameProcessDetector.FormatExecutableName(snapshot.ActiveExecutable);
                _activeExecutableValue.Visibility = Visibility.Visible;
            }
            else
            {
                _activeExecutableValue.Text = "";
                _activeExecutableValue.Visibility = Visibility.Collapsed;
            }

            var running = new HashSet<string>(
                snapshot.RunningConfiguredExecutables,
                StringComparer.OrdinalIgnoreCase);
            var unknown = new HashSet<string>(
                snapshot.UnknownConfiguredExecutables,
                StringComparer.OrdinalIgnoreCase);
            foreach (ExecutableItemViewModel item in _executables)
            {
                bool isRunning = running.Contains(item.NormalizedName);
                item.IsRunning = isRunning;
                item.Status = isRunning
                    ? "正在运行"
                    : unknown.Contains(item.NormalizedName)
                        ? "状态未知"
                        : "未运行";
            }

            if (snapshot.DetectionHealth == GameModeDetectionHealth.Healthy)
            {
                _statusBar.IsOpen = false;
                return;
            }

            _statusBar.Severity = snapshot.DetectionHealth == GameModeDetectionHealth.Partial
                ? InfoBarSeverity.Warning
                : InfoBarSeverity.Error;
            _statusBar.Title = snapshot.DetectionHealth == GameModeDetectionHealth.Partial
                ? "部分运行状态未知"
                : "检测状态未知";
            _statusBar.Message = snapshot.ErrorMessage;
            _statusBar.IsOpen = true;
        }

        private void SynchronizeExecutables(string preferredSelection)
        {
            string selected = preferredSelection;
            if (String.IsNullOrEmpty(selected) && _executableList.SelectedItem is ExecutableItemViewModel current)
            {
                selected = current.NormalizedName;
            }

            var configured = Context.Settings.GameProcesses ?? new List<string>();
            var target = new List<string>(configured);
            for (int index = _executables.Count - 1; index >= 0; index--)
            {
                if (ContainsExecutable(target, _executables[index].NormalizedName)) continue;
                _executables.RemoveAt(index);
            }

            for (int index = 0; index < target.Count; index++)
            {
                string normalized = target[index];
                ExecutableItemViewModel existing = FindExecutable(normalized);
                if (existing == null)
                {
                    _executables.Insert(index, new ExecutableItemViewModel(normalized));
                    continue;
                }

                int currentIndex = _executables.IndexOf(existing);
                existing.DisplayName = GameProcessDetector.FormatExecutableName(normalized);
                if (currentIndex != index)
                {
                    _executables.Move(currentIndex, index);
                }
            }

            UpdateSummary();
            if (!String.IsNullOrWhiteSpace(selected))
            {
                ExecutableItemViewModel match = FindExecutable(selected);
                if (match != null) _executableList.SelectedItem = match;
            }
        }

        private void UpdateSummary()
        {
            int count = Context.Settings.GameProcesses == null ? 0 : Context.Settings.GameProcesses.Count;
            _summaryValue.Text = count == 0
                ? "尚未添加游戏 EXE。建议直接从正在运行的程序中勾选。"
                : "共 " + count + " 个 EXE，按文件名匹配。托盘每秒检测，状态变化时自动刷新。";
        }

        private ExecutableItemViewModel FindExecutable(string normalized)
        {
            foreach (ExecutableItemViewModel item in _executables)
            {
                if (String.Equals(item.NormalizedName, normalized, StringComparison.OrdinalIgnoreCase))
                {
                    return item;
                }
            }
            return null;
        }

        private void OnEnabledToggled(object sender, RoutedEventArgs e)
        {
            if (IsRefreshing) return;
            if (Context.Settings.ProcessGameModeEnabled == _enabledSwitch.IsOn) return;
            Context.Settings.ProcessGameModeEnabled = _enabledSwitch.IsOn;
            Context.SaveSettingsAndRefresh();
        }

        private void OnThresholdSliderChanged(object sender, RangeBaseValueChangedEventArgs e)
        {
            if (IsRefreshing) return;
            int value = (int)Math.Round(e.NewValue);
            _thresholdBox.Value = value;
            if (Context.Settings.GameModeThresholdMs == value) return;
            Context.Settings.GameModeThresholdMs = value;
            Context.SaveSettingsAndRefresh();
        }

        private void OnThresholdBoxChanged(NumberBox sender, NumberBoxValueChangedEventArgs args)
        {
            if (IsRefreshing || Double.IsNaN(sender.Value)) return;
            int value = (int)Math.Max(20, Math.Min(250, Math.Round(sender.Value)));
            _thresholdSlider.Value = value;
            if (Context.Settings.GameModeThresholdMs == value) return;
            Context.Settings.GameModeThresholdMs = value;
            Context.SaveSettingsAndRefresh();
        }

        private void OnLongHoldSliderChanged(object sender, RangeBaseValueChangedEventArgs e)
        {
            if (IsRefreshing) return;
            int value = (int)Math.Round(e.NewValue);
            _longHoldBox.Value = value;
            if (Context.Settings.GameModeLongHoldBypassMs == value) return;
            Context.Settings.GameModeLongHoldBypassMs = value;
            Context.SaveSettingsAndRefresh();
        }

        private void OnLongHoldBoxChanged(NumberBox sender, NumberBoxValueChangedEventArgs args)
        {
            if (IsRefreshing || Double.IsNaN(sender.Value)) return;
            int value = (int)Math.Max(50, Math.Min(1000, Math.Round(sender.Value)));
            _longHoldSlider.Value = value;
            if (Context.Settings.GameModeLongHoldBypassMs == value) return;
            Context.Settings.GameModeLongHoldBypassMs = value;
            Context.SaveSettingsAndRefresh();
        }

        private async void OnBrowseExecutableAsync(object sender, RoutedEventArgs e)
        {
            try
            {
                var picker = new FileOpenPicker();
                picker.FileTypeFilter.Add(".exe");
                picker.SuggestedStartLocation = PickerLocationId.Desktop;
                InitializeWithWindow.Initialize(picker, SettingsWindow.CurrentWindowHandle);
                IReadOnlyList<StorageFile> files = await picker.PickMultipleFilesAsync();
                if (files == null || files.Count == 0) return;

                var selections = new List<string>();
                foreach (StorageFile file in files)
                {
                    selections.Add(file.Name);
                }

                AddExecutables(selections);
            }
            catch (Exception error)
            {
                await ShowMessageAsync(XamlRoot, "选择 EXE 失败", error.GetType().Name + " - " + error.Message);
            }
        }

        private async void OnBrowseRunningAsync(object sender, RoutedEventArgs e)
        {
            var dialog = new RunningExecutablePickerDialog(
                XamlRoot,
                Context.Settings.GameProcesses,
                GameProcessDetector.GetRunningExecutableNames);
            ContentDialogResult result = await dialog.ShowAsync();
            if (result != ContentDialogResult.Primary) return;
            AddExecutables(dialog.SelectedExecutableNames);
        }

        private void AddExecutables(IEnumerable<string> selections)
        {
            if (selections == null) return;

            Context.Settings.GameProcesses = Context.Settings.GameProcesses ?? new List<string>();
            var existing = new HashSet<string>(Context.Settings.GameProcesses, StringComparer.OrdinalIgnoreCase);
            int additions = 0;
            foreach (string selection in selections)
            {
                string normalized = GameProcessDetector.NormalizeProcessName(selection);
                if (normalized.Length == 0 || !existing.Add(normalized)) continue;
                Context.Settings.GameProcesses.Add(normalized);
                additions++;
            }

            if (additions == 0)
            {
                _statusBar.Severity = InfoBarSeverity.Warning;
                _statusBar.Title = "没有新增 EXE";
                _statusBar.Message = "所选项目已存在或无法识别。";
                _statusBar.IsOpen = true;
                return;
            }

            Context.Settings.ProcessGameModeEnabled = true;
            Context.Settings.Normalize();
            Context.SaveSettingsAndRefresh();
            _statusBar.Severity = InfoBarSeverity.Success;
            _statusBar.Title = "已更新游戏 EXE";
            _statusBar.Message = "新增 " + additions + " 个 EXE。";
            _statusBar.IsOpen = true;
        }

        private void OnRemoveSelected(object sender, RoutedEventArgs e)
        {
            if (_executableList.SelectedItems.Count == 0 || Context.Settings.GameProcesses == null) return;

            int removed = 0;
            var selectedItems = new List<object>();
            foreach (object selectedItem in _executableList.SelectedItems)
            {
                selectedItems.Add(selectedItem);
            }

            foreach (object selectedItem in selectedItems)
            {
                var item = selectedItem as ExecutableItemViewModel;
                if (item == null) continue;
                removed += Context.Settings.GameProcesses.RemoveAll(
                    delegate(string value)
                    {
                        return String.Equals(value, item.NormalizedName, StringComparison.OrdinalIgnoreCase);
                    });
            }

            if (removed == 0) return;

            Context.SaveSettingsAndRefresh();
            _statusBar.Severity = InfoBarSeverity.Success;
            _statusBar.Title = "已移除所选 EXE";
            _statusBar.Message = "共移除 " + removed + " 项。";
            _statusBar.IsOpen = true;
        }

        private void OnRefreshRuntimeState(object sender, RoutedEventArgs e)
        {
            Context.RequestGameModeRuntimeRefresh();
        }

        private static DataTemplate CreateExecutableTemplate()
        {
            return (DataTemplate)Microsoft.UI.Xaml.Markup.XamlReader.Load(
                "<DataTemplate xmlns='http://schemas.microsoft.com/winfx/2006/xaml/presentation'>"
                + "<Grid Padding='8,8' ColumnSpacing='16'>"
                + "<Grid.ColumnDefinitions>"
                + "<ColumnDefinition Width='*'/>"
                + "<ColumnDefinition Width='Auto'/>"
                + "</Grid.ColumnDefinitions>"
                + "<StackPanel Spacing='2'>"
                + "<TextBlock Text='{Binding DisplayName}' FontSize='14' FontWeight='SemiBold'/>"
                + "<TextBlock Text='{Binding Status}' FontSize='12' Foreground='#606873'/>"
                + "</StackPanel>"
                + "<TextBlock Grid.Column='1' Text='{Binding Status}' VerticalAlignment='Center' FontSize='13'/>"
                + "</Grid>"
                + "</DataTemplate>");
        }

        private static bool ContainsExecutable(IEnumerable<string> values, string normalized)
        {
            foreach (string value in values)
            {
                if (String.Equals(value, normalized, StringComparison.OrdinalIgnoreCase))
                {
                    return true;
                }
            }
            return false;
        }

        private sealed class ExecutableItemViewModel : INotifyPropertyChanged
        {
            private string _displayName;
            private string _status;
            private bool _isRunning;

            public ExecutableItemViewModel(string normalizedName)
            {
                NormalizedName = normalizedName;
                _displayName = GameProcessDetector.FormatExecutableName(normalizedName);
                _status = "状态未知";
            }

            public string NormalizedName { get; private set; }

            public string DisplayName
            {
                get { return _displayName; }
                set { SetField(ref _displayName, value); }
            }

            public string Status
            {
                get { return _status; }
                set { SetField(ref _status, value); }
            }

            public bool IsRunning
            {
                get { return _isRunning; }
                set { SetField(ref _isRunning, value); }
            }

            public event PropertyChangedEventHandler PropertyChanged;

            private void SetField<T>(ref T field, T value, [CallerMemberName] string propertyName = null)
            {
                if (EqualityComparer<T>.Default.Equals(field, value)) return;
                field = value;
                PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
            }
        }
    }
}
