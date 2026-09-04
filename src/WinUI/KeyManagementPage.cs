using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using Microsoft.UI.Dispatching;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace KeyboardDebounce.WinUI
{
    internal sealed class KeyManagementPage : SettingsPageBase
    {
        private readonly TextBox _searchBox;
        private readonly ComboBox _scopeBox;
        private readonly NumberBox _manualVkBox;
        private readonly ObservableCollection<KeyRowViewModel> _displayedRows;
        private readonly ListView _listView;
        private readonly DispatcherQueueTimer _searchTimer;
        private string _appliedSearchText;
        private string _sortColumnName;
        private bool _sortAscending;
        private bool _syncingRows;
        private bool _rowsInitialized;

        public KeyManagementPage(SettingsUiContext context)
            : base(context, SettingsPageLayoutMode.Fill)
        {
            _displayedRows = new ObservableCollection<KeyRowViewModel>();
            _displayedRows.CollectionChanged += OnDisplayedRowsChanged;
            _appliedSearchText = "";
            _sortColumnName = "Vk";
            _sortAscending = true;

            DispatcherQueue dispatcherQueue = DispatcherQueue.GetForCurrentThread();
            _searchTimer = dispatcherQueue.CreateTimer();
            _searchTimer.Interval = TimeSpan.FromMilliseconds(150);
            _searchTimer.Tick += OnSearchTimerTick;

            var pageGrid = (Grid)RootPanel;
            pageGrid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            pageGrid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            pageGrid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });

            TextBlock pageTitle = SettingsViewFactory.CreatePageTitle("按键管理");
            Grid.SetRow(pageTitle, 0);
            pageGrid.Children.Add(pageTitle);

            StackPanel filterContent;
            Border filterCard = SettingsViewFactory.CreateSurfaceCard(
                "筛选与添加",
                "按十进制 VK 或按键名查找，也可以直接把 VK 加入游戏防抖。",
                out filterContent,
                true);
            filterCard.Padding = new Thickness(20, 14, 20, 14);
            var filterGrid = new Grid
            {
                ColumnSpacing = 16,
                RowSpacing = 12
            };
            filterGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(2, GridUnitType.Star) });
            filterGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            filterGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(120) });
            filterGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            filterContent.Children.Add(filterGrid);
            Grid.SetRow(filterCard, 1);
            pageGrid.Children.Add(filterCard);

            _searchBox = new TextBox
            {
                Header = "搜索",
                PlaceholderText = "输入 VK 或按键名"
            };
            _searchBox.TextChanged += OnSearchTextChanged;
            Grid.SetColumn(_searchBox, 0);
            filterGrid.Children.Add(_searchBox);

            _scopeBox = new ComboBox
            {
                Header = "范围",
                ItemsSource = new[] { "全部", "仅已学习", "仅游戏防抖", "仅始终忽略" },
                SelectedIndex = 0
            };
            _scopeBox.SelectionChanged += OnScopeChanged;
            Grid.SetColumn(_scopeBox, 1);
            filterGrid.Children.Add(_scopeBox);

            _manualVkBox = CreateIntegerBox(1, 255, 1);
            _manualVkBox.Value = 65;
            _manualVkBox.Header = "手动 VK";
            Grid.SetColumn(_manualVkBox, 2);
            filterGrid.Children.Add(_manualVkBox);

            var addButton = SettingsViewFactory.CreatePrimaryButton("添加到游戏防抖", OnAddManualVk);
            addButton.VerticalAlignment = VerticalAlignment.Bottom;
            Grid.SetColumn(addButton, 3);
            filterGrid.Children.Add(addButton);

            var actionRow = new StackPanel
            {
                Orientation = Orientation.Horizontal,
                Spacing = 12
            };
            actionRow.Children.Add(SettingsViewFactory.CreateSecondaryButton("刷新按键列表", OnRefreshKeyList));
            actionRow.Children.Add(SettingsViewFactory.CreateSecondaryButton("清空忽略列表", OnClearIgnored));
            actionRow.Children.Add(SettingsViewFactory.CreateSecondaryButton("重置学习数据", OnResetLearning));

            Grid tableContent;
            Border tableCard = SettingsViewFactory.CreateFillSurfaceCard(
                "按键列表",
                "点击表头排序；复选框修改会立即保存。",
                actionRow,
                out tableContent);
            tableCard.Padding = new Thickness(20, 18, 20, 12);
            tableContent.RowSpacing = 8;
            tableContent.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            tableContent.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });

            Grid sortHeader = CreateSortHeader();
            Grid.SetRow(sortHeader, 0);
            tableContent.Children.Add(sortHeader);

            _listView = new ListView
            {
                MinHeight = 132,
                ItemsSource = _displayedRows,
                SelectionMode = ListViewSelectionMode.Single,
                IsItemClickEnabled = false,
                HorizontalAlignment = HorizontalAlignment.Stretch,
                VerticalAlignment = VerticalAlignment.Stretch
            };
            Microsoft.UI.Xaml.Automation.AutomationProperties.SetAutomationId(
                _listView,
                "KeyManagementList");
            Microsoft.UI.Xaml.Automation.AutomationProperties.SetName(
                _listView,
                "按键列表");
            ScrollViewer.SetVerticalScrollMode(_listView, ScrollMode.Auto);
            ScrollViewer.SetVerticalScrollBarVisibility(_listView, ScrollBarVisibility.Auto);
            ScrollViewer.SetHorizontalScrollMode(_listView, ScrollMode.Disabled);
            ScrollViewer.SetHorizontalScrollBarVisibility(_listView, ScrollBarVisibility.Disabled);
            _listView.ItemContainerStyle = CreateItemContainerStyle();
            _listView.ItemTemplate = CreateTemplate();
            Grid.SetRow(_listView, 1);
            tableContent.Children.Add(_listView);

            Grid.SetRow(tableCard, 2);
            pageGrid.Children.Add(tableCard);
        }

        internal override SettingsPageId PageId
        {
            get { return SettingsPageId.KeyManagement; }
        }

        internal override string PageTitle
        {
            get { return "按键管理"; }
        }

        protected override void RefreshDataCore()
        {
            RefreshRows(true);
        }

        protected override void RefreshRuntimeStateCore()
        {
            // 按键页主要承载配置编辑，runtime 心跳不需要每次重刷整表，
            // 否则切回页面时会稳定引入 100ms 级延迟。
        }

        private void RefreshRows(bool preserveState)
        {
            int? selectedVk = preserveState && _listView.SelectedItem is KeyRowViewModel selected
                ? selected.Vk
                : null;
            bool restoreFocus = preserveState && _listView.FocusState != FocusState.Unfocused;

            List<SettingsGridRow> rows = BuildRows();
            SettingsGridRowSorter.Sort(rows, _sortColumnName, _sortAscending);
            rows = SettingsFilter.Apply(rows, _appliedSearchText, GetScope());

            _syncingRows = true;
            try
            {
                if (!_rowsInitialized)
                {
                    _listView.ItemsSource = null;
                    SyncRows(rows);
                    _listView.ItemsSource = _displayedRows;
                    _rowsInitialized = true;
                }
                else
                {
                    SyncRows(rows);
                }
            }
            finally
            {
                _syncingRows = false;
            }

            if (selectedVk.HasValue)
            {
                foreach (KeyRowViewModel row in _displayedRows)
                {
                    if (row.Vk != selectedVk.Value) continue;
                    _listView.SelectedItem = row;
                    break;
                }
            }

            if (restoreFocus)
            {
                _listView.Focus(FocusState.Programmatic);
            }
        }

        private void RefreshVisibleRowValues()
        {
            _syncingRows = true;
            try
            {
                foreach (KeyRowViewModel viewModel in _displayedRows)
                {
                    KeyLearningState state = null;
                    Context.Learning.Keys.TryGetValue(viewModel.Vk, out state);
                    SettingsGridRow row = BuildRow(viewModel.Vk, state);
                    viewModel.Apply(row);
                }
            }
            finally
            {
                _syncingRows = false;
            }
        }

        private void SyncRows(IReadOnlyList<SettingsGridRow> rows)
        {
            for (int index = _displayedRows.Count - 1; index >= 0; index--)
            {
                bool keep = false;
                foreach (SettingsGridRow row in rows)
                {
                    if (row.Vk == _displayedRows[index].Vk)
                    {
                        keep = true;
                        break;
                    }
                }
                if (!keep)
                {
                    DetachRow(_displayedRows[index]);
                    _displayedRows.RemoveAt(index);
                }
            }

            for (int index = 0; index < rows.Count; index++)
            {
                SettingsGridRow target = rows[index];
                KeyRowViewModel existing = FindRow(target.Vk);
                if (existing == null)
                {
                    existing = new KeyRowViewModel(target);
                    _displayedRows.Insert(index, existing);
                    continue;
                }

                existing.Apply(target);
                int currentIndex = _displayedRows.IndexOf(existing);
                if (currentIndex != index)
                {
                    _displayedRows.Move(currentIndex, index);
                }
            }
        }

        private List<SettingsGridRow> BuildRows()
        {
            Context.Settings.Normalize();

            var rowsByVk = new Dictionary<int, KeyLearningState>(Context.Learning.Keys);
            AddConfiguredRows(rowsByVk, Context.Settings.IgnoredKeys);
            AddConfiguredRows(rowsByVk, Context.Settings.GameModeFilteredKeys);

            var rows = new List<SettingsGridRow>();
            foreach (KeyValuePair<int, KeyLearningState> pair in rowsByVk)
            {
                rows.Add(BuildRow(pair.Key, pair.Value));
            }
            return rows;
        }

        private SettingsGridRow BuildRow(int virtualKeyCode, KeyLearningState state)
        {
            return new SettingsGridRow
            {
                Vk = virtualKeyCode,
                KeyName = ((System.Windows.Forms.Keys)virtualKeyCode).ToString(),
                Ignored = Context.Engine.IsIgnored(virtualKeyCode),
                GameFiltered = Context.Settings.GameModeFilteredKeys != null
                    && Context.Settings.GameModeFilteredKeys.Contains(virtualKeyCode),
                HasLearning = state != null,
                ThresholdMs = state == null ? 0 : GetEffectiveThresholdForDisplay(state),
                AcceptedCount = state == null ? 0 : state.AcceptedCount,
                SuppressedCount = state == null ? 0 : state.SuppressedCount,
                LastSeen = state == null || state.LastSeenUtc == DateTime.MinValue
                    ? "-"
                    : state.LastSeenUtc.ToLocalTime().ToString("yyyy-MM-dd HH:mm:ss")
            };
        }

        private static void AddConfiguredRows(Dictionary<int, KeyLearningState> rowsByVk, IEnumerable<int> configuredKeys)
        {
            if (configuredKeys == null) return;
            foreach (int virtualKeyCode in configuredKeys)
            {
                if (!rowsByVk.ContainsKey(virtualKeyCode))
                {
                    rowsByVk[virtualKeyCode] = null;
                }
            }
        }

        private KeyFilterScope GetScope()
        {
            switch (_scopeBox.SelectedIndex)
            {
                case 1: return KeyFilterScope.LearnedOnly;
                case 2: return KeyFilterScope.GameFilteredOnly;
                case 3: return KeyFilterScope.IgnoredOnly;
                default: return KeyFilterScope.All;
            }
        }

        private void OnSearchTextChanged(object sender, TextChangedEventArgs e)
        {
            if (IsRefreshing) return;
            _searchTimer.Stop();
            _searchTimer.Start();
        }

        private void OnSearchTimerTick(DispatcherQueueTimer sender, object args)
        {
            _searchTimer.Stop();
            _appliedSearchText = SettingsFilter.NormalizeSearch(_searchBox.Text);
            RefreshRows(true);
        }

        private void OnScopeChanged(object sender, SelectionChangedEventArgs e)
        {
            if (IsRefreshing) return;
            RefreshRows(true);
        }

        private void OnAddManualVk(object sender, RoutedEventArgs e)
        {
            if (Context.Settings.GameModeFilteredKeys == null)
            {
                Context.Settings.GameModeFilteredKeys = new List<int>();
            }

            int virtualKeyCode = (int)Math.Round(_manualVkBox.Value);
            if (!Context.Settings.GameModeFilteredKeys.Contains(virtualKeyCode))
            {
                Context.Settings.GameModeFilteredKeys.Add(virtualKeyCode);
                Context.Settings.GameModeFilteredKeys.Sort();
            }
            Context.SaveSettingsAndRefresh();
        }

        private void OnRefreshKeyList(object sender, RoutedEventArgs e)
        {
            RefreshRows(true);
        }

        private async void OnClearIgnored(object sender, RoutedEventArgs e)
        {
            bool confirmed = await ShowConfirmationAsync(
                XamlRoot,
                "清空忽略列表",
                "确认清空所有忽略键？",
                "清空");
            if (!confirmed) return;

            Context.Engine.ClearIgnoredKeys();
            Context.SaveSettingsAndRefresh();
        }

        private async void OnResetLearning(object sender, RoutedEventArgs e)
        {
            bool confirmed = await ShowConfirmationAsync(
                XamlRoot,
                "重置学习数据",
                "确认重置所有按键学习数据？",
                "重置");
            if (!confirmed) return;

            Context.Engine.ResetLearning();
            Context.SaveLearningAndRefresh();
        }

        private Grid CreateSortHeader()
        {
            var grid = new Grid
            {
                ColumnSpacing = 12,
                Padding = new Thickness(10, 0, 10, 0)
            };
            string[] columns = { "Vk", "KeyName", "Threshold", "Accepted", "Suppressed", "LastSeen", "GameFiltered", "Ignored" };
            string[] labels = { "VK", "按键", "普通阈值", "放行", "拦截", "最近时间", "游戏防抖", "始终忽略" };
            GridLength[] widths =
            {
                new GridLength(72),
                new GridLength(1, GridUnitType.Star),
                new GridLength(84),
                new GridLength(72),
                new GridLength(72),
                new GridLength(1, GridUnitType.Star),
                new GridLength(96),
                new GridLength(96)
            };
            for (int index = 0; index < columns.Length; index++)
            {
                grid.ColumnDefinitions.Add(new ColumnDefinition
                {
                    Width = widths[index]
                });
                string columnName = columns[index];
                var button = new Button
                {
                    Content = labels[index],
                    Padding = new Thickness(0, 6, 0, 6),
                    Background = null,
                    BorderThickness = new Thickness(0),
                    HorizontalAlignment = HorizontalAlignment.Stretch,
                    HorizontalContentAlignment = HorizontalAlignment.Left
                };
                button.Click += delegate
                {
                    SortBy(columnName);
                };
                Grid.SetColumn(button, index);
                grid.Children.Add(button);
            }
            return grid;
        }

        private void SortBy(string columnName)
        {
            if (_sortColumnName == columnName)
            {
                _sortAscending = !_sortAscending;
            }
            else
            {
                _sortColumnName = columnName;
                _sortAscending = true;
            }
            RefreshRows(true);
        }

        private static DataTemplate CreateTemplate()
        {
            return (DataTemplate)Microsoft.UI.Xaml.Markup.XamlReader.Load(
                "<DataTemplate xmlns='http://schemas.microsoft.com/winfx/2006/xaml/presentation'>"
                + "<Grid Padding='10,6' MinHeight='44' ColumnSpacing='12' HorizontalAlignment='Stretch'>"
                + "<Grid.ColumnDefinitions>"
                + "<ColumnDefinition Width='72'/>"
                + "<ColumnDefinition Width='1*'/>"
                + "<ColumnDefinition Width='84'/>"
                + "<ColumnDefinition Width='72'/>"
                + "<ColumnDefinition Width='72'/>"
                + "<ColumnDefinition Width='1*'/>"
                + "<ColumnDefinition Width='96'/>"
                + "<ColumnDefinition Width='96'/>"
                + "</Grid.ColumnDefinitions>"
                + "<TextBlock Text='{Binding VkDisplay}'/>"
                + "<TextBlock Grid.Column='1' Text='{Binding KeyName}'/>"
                + "<TextBlock Grid.Column='2' Text='{Binding ThresholdDisplay}'/>"
                + "<TextBlock Grid.Column='3' Text='{Binding AcceptedDisplay}'/>"
                + "<TextBlock Grid.Column='4' Text='{Binding SuppressedDisplay}'/>"
                + "<TextBlock Grid.Column='5' Text='{Binding LastSeen}'/>"
                + "<ToggleSwitch Grid.Column='6' IsOn='{Binding GameFiltered, Mode=TwoWay}' OnContent='' OffContent='' MinWidth='0' MinHeight='0' Height='32' HorizontalAlignment='Center' VerticalAlignment='Center' AutomationProperties.Name='游戏防抖'/>"
                + "<ToggleSwitch Grid.Column='7' IsOn='{Binding Ignored, Mode=TwoWay}' OnContent='' OffContent='' MinWidth='0' MinHeight='0' Height='32' HorizontalAlignment='Center' VerticalAlignment='Center' AutomationProperties.Name='始终忽略'/>"
                + "</Grid>"
                + "</DataTemplate>");
        }

        private static Style CreateItemContainerStyle()
        {
            return (Style)Microsoft.UI.Xaml.Markup.XamlReader.Load(
                "<Style xmlns='http://schemas.microsoft.com/winfx/2006/xaml/presentation' TargetType='ListViewItem'>"
                + "<Setter Property='HorizontalContentAlignment' Value='Stretch'/>"
                + "<Setter Property='MinHeight' Value='44'/>"
                + "<Setter Property='Padding' Value='0'/>"
                + "</Style>");
        }

        internal override void OnWindowClosing()
        {
            _searchTimer.Stop();
            _searchTimer.Tick -= OnSearchTimerTick;
            _displayedRows.CollectionChanged -= OnDisplayedRowsChanged;
            foreach (KeyRowViewModel row in _displayedRows)
            {
                DetachRow(row);
            }
        }

        private void OnDisplayedRowsChanged(object sender, NotifyCollectionChangedEventArgs e)
        {
            if (e.OldItems != null)
            {
                foreach (object item in e.OldItems)
                {
                    var row = item as KeyRowViewModel;
                    if (row != null) DetachRow(row);
                }
            }
            if (e.NewItems != null)
            {
                foreach (object item in e.NewItems)
                {
                    var row = item as KeyRowViewModel;
                    if (row != null) AttachRow(row);
                }
            }
        }

        private void AttachRow(KeyRowViewModel row)
        {
            row.PropertyChanged += OnRowPropertyChanged;
        }

        private void DetachRow(KeyRowViewModel row)
        {
            row.PropertyChanged -= OnRowPropertyChanged;
        }

        private void OnRowPropertyChanged(object sender, PropertyChangedEventArgs e)
        {
            if (_syncingRows) return;
            var row = sender as KeyRowViewModel;
            if (row == null) return;

            if (e.PropertyName == "Ignored")
            {
                if (row.Ignored)
                {
                    Context.Engine.IgnoreKey(row.Vk);
                }
                else
                {
                    Context.Engine.UnignoreKey(row.Vk);
                }
                Context.SaveAllAndRefresh();
                return;
            }

            if (e.PropertyName == "GameFiltered")
            {
                if (Context.Settings.GameModeFilteredKeys == null)
                {
                    Context.Settings.GameModeFilteredKeys = new List<int>();
                }
                if (row.GameFiltered)
                {
                    if (!Context.Settings.GameModeFilteredKeys.Contains(row.Vk))
                    {
                        Context.Settings.GameModeFilteredKeys.Add(row.Vk);
                        Context.Settings.GameModeFilteredKeys.Sort();
                    }
                }
                else
                {
                    Context.Settings.GameModeFilteredKeys.Remove(row.Vk);
                }
                Context.SaveSettingsAndRefresh();
            }
        }

        private KeyRowViewModel FindRow(int vk)
        {
            foreach (KeyRowViewModel row in _displayedRows)
            {
                if (row.Vk == vk) return row;
            }
            return null;
        }

        private sealed class KeyRowViewModel : INotifyPropertyChanged
        {
            private bool _ignored;
            private bool _gameFiltered;
            private string _keyName;
            private string _thresholdDisplay;
            private string _acceptedDisplay;
            private string _suppressedDisplay;
            private string _lastSeen;

            public KeyRowViewModel(SettingsGridRow row)
            {
                Vk = row.Vk;
                Apply(row);
            }

            public int Vk { get; private set; }

            public string VkDisplay
            {
                get { return Vk.ToString("000"); }
            }

            public string KeyName
            {
                get { return _keyName; }
                private set { SetField(ref _keyName, value); }
            }

            public bool Ignored
            {
                get { return _ignored; }
                set { SetField(ref _ignored, value); }
            }

            public bool GameFiltered
            {
                get { return _gameFiltered; }
                set { SetField(ref _gameFiltered, value); }
            }

            public string ThresholdDisplay
            {
                get { return _thresholdDisplay; }
                private set { SetField(ref _thresholdDisplay, value); }
            }

            public string AcceptedDisplay
            {
                get { return _acceptedDisplay; }
                private set { SetField(ref _acceptedDisplay, value); }
            }

            public string SuppressedDisplay
            {
                get { return _suppressedDisplay; }
                private set { SetField(ref _suppressedDisplay, value); }
            }

            public string LastSeen
            {
                get { return _lastSeen; }
                private set { SetField(ref _lastSeen, value); }
            }

            public event PropertyChangedEventHandler PropertyChanged;

            public void Apply(SettingsGridRow row)
            {
                KeyName = row.KeyName;
                Ignored = row.Ignored;
                GameFiltered = row.GameFiltered;
                ThresholdDisplay = row.HasLearning ? row.ThresholdMs.ToString() : "-";
                AcceptedDisplay = row.HasLearning ? row.AcceptedCount.ToString() : "-";
                SuppressedDisplay = row.HasLearning ? row.SuppressedCount.ToString() : "-";
                LastSeen = row.LastSeen;
            }

            private void SetField<T>(ref T field, T value, [CallerMemberName] string propertyName = null)
            {
                if (EqualityComparer<T>.Default.Equals(field, value)) return;
                field = value;
                PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
            }
        }
    }
}
