using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Automation.Peers;
using Microsoft.UI.Xaml.Controls;

namespace KeyboardDebounce.WinUI
{
    internal sealed class RunningExecutablePickerDialog : ContentDialog
    {
        private readonly Func<IReadOnlyList<string>> _loadExecutables;
        private readonly HashSet<string> _excluded;
        private readonly ObservableCollection<ExecutableOptionViewModel> _items;
        private readonly List<string> _availableExecutables;
        private readonly TextBox _searchBox;
        private readonly ListView _listView;
        private readonly InfoBar _statusBar;
        private CancellationTokenSource _loadCancellation;

        public RunningExecutablePickerDialog(
            XamlRoot xamlRoot,
            IEnumerable<string> excludedExecutables,
            Func<IReadOnlyList<string>> loadExecutables)
        {
            if (loadExecutables == null) throw new ArgumentNullException("loadExecutables");

            _loadExecutables = loadExecutables;
            _excluded = NormalizeSet(excludedExecutables);
            _items = new ObservableCollection<ExecutableOptionViewModel>();
            _availableExecutables = new List<string>();

            XamlRoot = xamlRoot;
            RequestedTheme = ElementTheme.Light;
            Title = "从正在运行的程序中添加";
            PrimaryButtonText = "添加所选";
            CloseButtonText = "取消";
            IsPrimaryButtonEnabled = false;
            DefaultButton = ContentDialogButton.Close;
            PrimaryButtonClick += OnPrimaryButtonClick;
            Opened += OnOpened;
            Closed += OnClosed;

            _searchBox = new TextBox
            {
                PlaceholderText = "搜索 EXE 名称"
            };
            _searchBox.TextChanged += OnSearchTextChanged;

            _statusBar = new InfoBar
            {
                IsOpen = true,
                IsClosable = false,
                Severity = InfoBarSeverity.Informational,
                Message = "正在读取运行中的 EXE…"
            };
            AutomationProperties.SetLiveSetting(_statusBar, AutomationLiveSetting.Polite);

            _listView = new ListView
            {
                Height = 380,
                SelectionMode = ListViewSelectionMode.Multiple,
                IsItemClickEnabled = false,
                ItemsSource = _items
            };
            _listView.ItemTemplate = CreateTemplate();
            _listView.SelectionChanged += OnSelectionChanged;

            Content = new StackPanel
            {
                Spacing = 12,
                Children =
                {
                    SettingsViewFactory.CreateCaption("这里只显示当前正在运行、且尚未添加到游戏模式中的 EXE。"),
                    _searchBox,
                    _statusBar,
                    _listView
                }
            };
        }

        internal IReadOnlyList<string> SelectedExecutableNames { get; private set; }

        private async void OnOpened(ContentDialog sender, ContentDialogOpenedEventArgs args)
        {
            await LoadAsync();
        }

        private void OnClosed(ContentDialog sender, ContentDialogClosedEventArgs args)
        {
            if (_loadCancellation != null)
            {
                _loadCancellation.Cancel();
                _loadCancellation.Dispose();
                _loadCancellation = null;
            }

            PrimaryButtonClick -= OnPrimaryButtonClick;
            Opened -= OnOpened;
            Closed -= OnClosed;
        }

        private async Task LoadAsync()
        {
            if (_loadCancellation != null)
            {
                _loadCancellation.Cancel();
                _loadCancellation.Dispose();
            }

            _loadCancellation = new CancellationTokenSource();
            CancellationToken cancellationToken = _loadCancellation.Token;
            _statusBar.Severity = InfoBarSeverity.Informational;
            _statusBar.Message = "正在读取运行中的 EXE…";
            _statusBar.IsOpen = true;
            IsPrimaryButtonEnabled = false;

            try
            {
                IReadOnlyList<string> results = await Task.Run(_loadExecutables, cancellationToken);
                if (cancellationToken.IsCancellationRequested) return;

                _availableExecutables.Clear();
                foreach (string executable in results)
                {
                    string normalized = GameProcessDetector.NormalizeProcessName(executable);
                    if (normalized.Length == 0 || _excluded.Contains(normalized)) continue;
                    if (ContainsExecutable(_availableExecutables, normalized)) continue;
                    _availableExecutables.Add(normalized);
                }

                ApplyFilter();

                _statusBar.Severity = InfoBarSeverity.Informational;
                _statusBar.Message = _items.Count == 0
                    ? "当前没有可添加的运行中 EXE。"
                    : "找到 " + _items.Count + " 个可添加的运行中 EXE。";
            }
            catch (OperationCanceledException)
            {
            }
            catch (Exception error)
            {
                _items.Clear();
                _statusBar.Severity = InfoBarSeverity.Error;
                _statusBar.Message = "读取运行中 EXE 失败：" + error.GetType().Name + " - " + error.Message;
            }
        }

        private void OnSearchTextChanged(object sender, TextChangedEventArgs e)
        {
            ApplyFilter();
        }

        private void OnSelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            IsPrimaryButtonEnabled = _listView.SelectedItems.Count > 0;
            PrimaryButtonText = _listView.SelectedItems.Count > 0
                ? "添加所选（" + _listView.SelectedItems.Count + "）"
                : "添加所选";
        }

        private void OnPrimaryButtonClick(ContentDialog sender, ContentDialogButtonClickEventArgs args)
        {
            var selections = new List<string>();
            foreach (object selectedItem in _listView.SelectedItems)
            {
                var item = selectedItem as ExecutableOptionViewModel;
                if (item != null) selections.Add(item.NormalizedName);
            }
            selections.Sort(StringComparer.OrdinalIgnoreCase);
            SelectedExecutableNames = selections;
        }

        internal static IReadOnlyList<string> FilterExecutableNames(
            IEnumerable<string> names,
            string searchText)
        {
            string normalizedSearch = (searchText ?? "").Trim();
            var result = new List<string>();
            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            if (names != null)
            {
                foreach (string name in names)
                {
                    string normalized = GameProcessDetector.NormalizeProcessName(name);
                    if (normalized.Length == 0 || !seen.Add(normalized)) continue;
                    string display = normalized + ".exe";
                    if (normalizedSearch.Length == 0
                        || display.IndexOf(normalizedSearch, StringComparison.OrdinalIgnoreCase) >= 0)
                    {
                        result.Add(normalized);
                    }
                }
            }

            result.Sort(StringComparer.OrdinalIgnoreCase);
            return result;
        }

        private void ApplyFilter()
        {
            IReadOnlyList<string> filtered = FilterExecutableNames(_availableExecutables, _searchBox.Text);
            _items.Clear();
            foreach (string executable in filtered)
            {
                _items.Add(new ExecutableOptionViewModel(executable));
            }
            OnSelectionChanged(_listView, null);
        }

        private static DataTemplate CreateTemplate()
        {
            return (DataTemplate)Microsoft.UI.Xaml.Markup.XamlReader.Load(
                "<DataTemplate xmlns='http://schemas.microsoft.com/winfx/2006/xaml/presentation'>"
                + "<Grid Padding='8,6' ColumnSpacing='12'>"
                + "<Grid.ColumnDefinitions>"
                + "<ColumnDefinition Width='*'/>"
                + "</Grid.ColumnDefinitions>"
                + "<TextBlock Text='{Binding DisplayName}' FontSize='14'/>"
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

        private static HashSet<string> NormalizeSet(IEnumerable<string> values)
        {
            var result = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            if (values == null) return result;
            foreach (string value in values)
            {
                string normalized = GameProcessDetector.NormalizeProcessName(value);
                if (normalized.Length > 0) result.Add(normalized);
            }
            return result;
        }

        private sealed class ExecutableOptionViewModel
        {
            public ExecutableOptionViewModel(string normalizedName)
            {
                NormalizedName = normalizedName;
                DisplayName = normalizedName + ".exe";
            }

            public string NormalizedName { get; private set; }
            public string DisplayName { get; private set; }
        }
    }
}
