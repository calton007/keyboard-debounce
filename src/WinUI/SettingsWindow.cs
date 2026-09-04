using System;
using System.Collections.Generic;
using Microsoft.UI;
using Microsoft.UI.Text;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Automation.Peers;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using Windows.Graphics;
using Windows.System;
using WinRT.Interop;

namespace KeyboardDebounce.WinUI
{
    internal sealed class SettingsWindow : Window
    {
        private const int DefaultWidthDip = 1160;
        private const int DefaultHeightDip = 760;

        private static SettingsWindow _currentInstance;

        private readonly SettingsUiContext _context;
        private readonly Dictionary<SettingsPageId, SettingsPageBase> _pages;
        private readonly Dictionary<SettingsPageId, Func<SettingsPageBase>> _pageFactories;
        private readonly Dictionary<SettingsPageId, Button> _navigationButtons;
        private readonly Dictionary<SettingsPageId, Border> _navigationUnderlines;
        private readonly ContentControl _contentHost;
        private readonly ToggleSwitch _enabledSwitch;
        private readonly TextBlock _enabledLabel;
        private readonly InfoBar _statusBar;
        private AppWindow _appWindow;
        private System.Drawing.Icon _windowIcon;
        private SettingsPageId _activePageId;
        private bool _shellRefreshing;
        private bool _allowClose;
        private bool _appWindowClosingHooked;

        internal SettingsWindow(SettingsUiContext context)
        {
            if (context == null) throw new ArgumentNullException("context");

            _currentInstance = this;
            _context = context;
            _pages = new Dictionary<SettingsPageId, SettingsPageBase>();
            _navigationButtons = new Dictionary<SettingsPageId, Button>();
            _navigationUnderlines = new Dictionary<SettingsPageId, Border>();
            _pageFactories = new Dictionary<SettingsPageId, Func<SettingsPageBase>>
            {
                { SettingsPageId.Overview, delegate { return new OverviewPage(_context); } },
                { SettingsPageId.NormalDebounce, delegate { return new NormalDebouncePage(_context); } },
                { SettingsPageId.GameMode, delegate { return new GameModePage(_context); } },
                { SettingsPageId.KeyManagement, delegate { return new KeyManagementPage(_context); } },
                { SettingsPageId.ApplicationSettings, delegate { return new ApplicationSettingsPage(_context); } }
            };

            Title = "KeyboardDebounce";

            var root = new Grid
            {
                Background = SettingsViewFactory.CreateWindowBackgroundBrush(),
                RequestedTheme = ElementTheme.Light
            };
            root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            root.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
            root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            root.KeyDown += OnRootKeyDown;

            var topBar = new Grid
            {
                Margin = new Thickness(12, 12, 12, 0),
                ColumnSpacing = 12
            };
            topBar.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            topBar.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            Grid.SetRow(topBar, 0);
            root.Children.Add(topBar);

            var navigationBar = new StackPanel
            {
                Orientation = Orientation.Horizontal,
                Spacing = 4,
                HorizontalAlignment = HorizontalAlignment.Left,
                VerticalAlignment = VerticalAlignment.Center
            };
            navigationBar.Children.Add(CreateNavigationButton(SettingsPageId.Overview, "概览", "\uE80F"));
            navigationBar.Children.Add(CreateNavigationButton(SettingsPageId.NormalDebounce, "普通防抖", "\uE9E9"));
            navigationBar.Children.Add(CreateNavigationButton(SettingsPageId.GameMode, "游戏模式", "\uE7FC"));
            navigationBar.Children.Add(CreateNavigationButton(SettingsPageId.KeyManagement, "按键管理", "\uE765"));
            navigationBar.Children.Add(CreateNavigationButton(SettingsPageId.ApplicationSettings, "应用设置", "\uE713"));
            Grid.SetColumn(navigationBar, 0);
            topBar.Children.Add(navigationBar);

            var togglePanel = new StackPanel
            {
                Orientation = Orientation.Horizontal,
                Spacing = 8,
                VerticalAlignment = VerticalAlignment.Center,
                Margin = new Thickness(0, 0, 4, 0)
            };
            _enabledLabel = new TextBlock
            {
                FontSize = 13,
                FontWeight = FontWeights.Medium,
                VerticalAlignment = VerticalAlignment.Center
            };
            _enabledSwitch = new ToggleSwitch
            {
                OnContent = String.Empty,
                OffContent = String.Empty
            };
            AutomationProperties.SetName(_enabledSwitch, "启用或暂停防抖");
            _enabledSwitch.Toggled += OnEnabledToggled;
            togglePanel.Children.Add(_enabledLabel);
            togglePanel.Children.Add(_enabledSwitch);
            Grid.SetColumn(togglePanel, 1);
            topBar.Children.Add(togglePanel);

            _contentHost = new ContentControl
            {
                ContentTransitions = null,
                HorizontalContentAlignment = HorizontalAlignment.Stretch,
                VerticalContentAlignment = VerticalAlignment.Stretch
            };
            Border contentSurface = SettingsViewFactory.CreatePagePanel(_contentHost);
            contentSurface.Margin = new Thickness(12, 10, 12, 12);
            Grid.SetRow(contentSurface, 1);
            root.Children.Add(contentSurface);

            _statusBar = new InfoBar
            {
                Margin = new Thickness(12, 0, 12, 14),
                IsClosable = false,
                IsOpen = true,
                Severity = InfoBarSeverity.Informational,
                Visibility = Visibility.Collapsed
            };
            AutomationProperties.SetLiveSetting(_statusBar, AutomationLiveSetting.Assertive);
            Grid.SetRow(_statusBar, 2);
            root.Children.Add(_statusBar);

            AddKeyboardShortcut(root, VirtualKey.Number1, SettingsPageId.Overview);
            AddKeyboardShortcut(root, VirtualKey.Number2, SettingsPageId.NormalDebounce);
            AddKeyboardShortcut(root, VirtualKey.Number3, SettingsPageId.GameMode);
            AddKeyboardShortcut(root, VirtualKey.Number4, SettingsPageId.KeyManagement);
            AddKeyboardShortcut(root, VirtualKey.Number5, SettingsPageId.ApplicationSettings);

            Content = root;

            _context.NavigateRequested = NavigateTo;
            _context.StateChanged += OnContextStateChanged;

            Closed += OnWindowClosed;
            NavigateTo(SettingsPageId.Overview);
            PreloadPages();
            RefreshData();
        }

        internal static IntPtr CurrentWindowHandle
        {
            get
            {
                return _currentInstance == null
                    ? IntPtr.Zero
                    : WindowNative.GetWindowHandle(_currentInstance);
            }
        }

        internal void ShowWindow()
        {
            EnsureAppWindow();
            if (_appWindow != null)
            {
                _appWindow.Show();
            }
            Activate();
            RefreshData();
        }

        internal void RefreshData()
        {
            _context.Settings.Normalize();
            _shellRefreshing = true;
            try
            {
                _enabledSwitch.IsOn = _context.Settings.Enabled;
                _enabledLabel.Text = _context.Settings.Enabled ? "已启用" : "已暂停";
            }
            finally
            {
                _shellRefreshing = false;
            }

            SettingsPageBase page = GetOrCreatePage(_activePageId);
            page.ActivatePage();
            UpdateStatus();
        }

        internal void RefreshRuntimeState()
        {
            SettingsPageBase page = GetOrCreatePage(_activePageId);
            page.RefreshRuntimeState();
            UpdateStatus();
        }

        internal void CloseForExit()
        {
            _allowClose = true;
            if (_appWindow != null && _appWindowClosingHooked)
            {
                _appWindow.Closing -= OnAppWindowClosing;
                _appWindowClosingHooked = false;
            }
            Close();
        }

        private Button CreateNavigationButton(SettingsPageId pageId, string text, string glyph)
        {
            var content = new Grid
            {
                RowSpacing = 6
            };
            content.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            content.RowDefinitions.Add(new RowDefinition { Height = new GridLength(3) });

            var labelRow = new StackPanel
            {
                Orientation = Orientation.Horizontal,
                Spacing = 8
            };
            labelRow.Children.Add(SettingsViewFactory.CreateGlyph(glyph, 16));
            labelRow.Children.Add(new TextBlock
            {
                Text = text,
                FontSize = 13,
                FontWeight = FontWeights.Medium,
                VerticalAlignment = VerticalAlignment.Center
            });
            content.Children.Add(labelRow);

            var underline = new Border
            {
                Height = 3,
                Background = SettingsViewFactory.CreateAccentBrush(),
                CornerRadius = new CornerRadius(2),
                Visibility = Visibility.Collapsed
            };
            Grid.SetRow(underline, 1);
            content.Children.Add(underline);

            var button = new Button
            {
                Content = content,
                Padding = new Thickness(14, 10, 14, 7),
                CornerRadius = new CornerRadius(8),
                BorderThickness = new Thickness(0),
                Background = new SolidColorBrush(Colors.Transparent),
                HorizontalAlignment = HorizontalAlignment.Left,
                FocusVisualPrimaryBrush = SettingsViewFactory.CreateAccentBrush(),
                FocusVisualSecondaryBrush = SettingsViewFactory.CreateAccentBrush(),
                FocusVisualPrimaryThickness = new Thickness(2),
                FocusVisualSecondaryThickness = new Thickness(0)
            };
            AutomationProperties.SetName(button, text);
            button.Click += delegate
            {
                if (_activePageId != pageId)
                {
                    NavigateTo(pageId);
                }
            };
            _navigationButtons.Add(pageId, button);
            _navigationUnderlines.Add(pageId, underline);
            return button;
        }

        private void NavigateTo(SettingsPageId pageId)
        {
            _activePageId = pageId;
            SettingsPageBase page = GetOrCreatePage(pageId);
            _contentHost.Content = page;
            UpdateNavigationStates();
            page.ActivatePage();
            UpdateStatus();
        }

        private SettingsPageBase GetOrCreatePage(SettingsPageId pageId)
        {
            SettingsPageBase page;
            if (_pages.TryGetValue(pageId, out page))
            {
                return page;
            }

            page = _pageFactories[pageId]();
            _pages.Add(pageId, page);
            return page;
        }

        private void PreloadPages()
        {
            SettingsPageId[] pageIds =
            {
                SettingsPageId.NormalDebounce,
                SettingsPageId.GameMode,
                SettingsPageId.KeyManagement,
                SettingsPageId.ApplicationSettings
            };
            foreach (SettingsPageId pageId in pageIds)
            {
                GetOrCreatePage(pageId).RefreshData();
            }
        }

        private void UpdateNavigationStates()
        {
            foreach (KeyValuePair<SettingsPageId, Button> pair in _navigationButtons)
            {
                bool selected = pair.Key == _activePageId;
                pair.Value.Background = new SolidColorBrush(
                    selected
                        ? ColorHelper.FromArgb(0xFF, 0xF5, 0xF9, 0xFF)
                        : Colors.Transparent);
                pair.Value.BorderBrush = new SolidColorBrush(
                    Colors.Transparent);
                pair.Value.BorderThickness = new Thickness(0);
                _navigationUnderlines[pair.Key].Visibility = selected
                    ? Visibility.Visible
                    : Visibility.Collapsed;
            }
        }

        private void UpdateStatus()
        {
            string recent;
            try
            {
                recent = _context.GetCurrentStatus();
            }
            catch (Exception error)
            {
                recent = "状态读取失败：" + error.GetType().Name + " - " + error.Message;
            }

            bool showStatus = _activePageId != SettingsPageId.Overview
                && IsImportantShellStatus(recent);
            _statusBar.Title = "最近事件";
            _statusBar.Message = String.IsNullOrWhiteSpace(recent) ? "等待按键事件。" : recent;
            _statusBar.Severity = showStatus ? InfoBarSeverity.Error : InfoBarSeverity.Informational;
            _statusBar.Visibility = showStatus ? Visibility.Visible : Visibility.Collapsed;
            _statusBar.IsOpen = showStatus;
        }

        private static bool IsImportantShellStatus(string recent)
        {
            if (String.IsNullOrWhiteSpace(recent)) return false;

            string[] keywords =
            {
                "失败",
                "错误",
                "异常",
                "无法",
                "冲突",
                "拒绝"
            };

            foreach (string keyword in keywords)
            {
                if (recent.IndexOf(keyword, StringComparison.OrdinalIgnoreCase) >= 0)
                {
                    return true;
                }
            }

            return false;
        }

        private void EnsureAppWindow()
        {
            IntPtr hwnd = WindowNative.GetWindowHandle(this);
            if (hwnd == IntPtr.Zero) return;

            if (_appWindow == null)
            {
                WindowId windowId = Win32Interop.GetWindowIdFromWindow(hwnd);
                _appWindow = AppWindow.GetFromWindowId(windowId);
                if (_appWindow == null) return;
            }

            _appWindow.Resize(ScaleClientSize(hwnd, DefaultWidthDip, DefaultHeightDip));
            _appWindow.SetPresenter(AppWindowPresenterKind.Default);
            if (_windowIcon == null)
            {
                _windowIcon = AppIcon.Load();
            }
            WindowIconManager.Apply(hwnd, _windowIcon);
            if (_appWindowClosingHooked) return;

            _appWindowClosingHooked = true;
            _appWindow.Closing += OnAppWindowClosing;
        }

        private static SizeInt32 ScaleClientSize(IntPtr hwnd, int widthDip, int heightDip)
        {
            uint dpi = NativeMethods.GetDpiForWindow(hwnd);
            if (dpi == 0)
            {
                dpi = 96;
            }

            double scale = dpi / 96.0;
            return new SizeInt32(
                Math.Max(960, (int)Math.Round(widthDip * scale)),
                Math.Max(640, (int)Math.Round(heightDip * scale)));
        }

        private void AddKeyboardShortcut(UIElement element, VirtualKey key, SettingsPageId pageId)
        {
            var accelerator = new KeyboardAccelerator
            {
                Key = key,
                Modifiers = VirtualKeyModifiers.Menu
            };
            accelerator.Invoked += delegate(KeyboardAccelerator sender, KeyboardAcceleratorInvokedEventArgs args)
            {
                NavigateTo(pageId);
                args.Handled = true;
            };
            element.KeyboardAccelerators.Add(accelerator);
        }

        private void OnRootKeyDown(object sender, KeyRoutedEventArgs args)
        {
            if (args.OriginalSource is TextBox || args.OriginalSource is NumberBox)
            {
                return;
            }

            if (args.Key == VirtualKey.Left)
            {
                NavigateTo(PreviousPage(_activePageId));
                args.Handled = true;
                return;
            }

            if (args.Key == VirtualKey.Right)
            {
                NavigateTo(NextPage(_activePageId));
                args.Handled = true;
            }
        }

        private static SettingsPageId PreviousPage(SettingsPageId pageId)
        {
            switch (pageId)
            {
                case SettingsPageId.NormalDebounce: return SettingsPageId.Overview;
                case SettingsPageId.GameMode: return SettingsPageId.NormalDebounce;
                case SettingsPageId.KeyManagement: return SettingsPageId.GameMode;
                case SettingsPageId.ApplicationSettings: return SettingsPageId.KeyManagement;
                default: return SettingsPageId.ApplicationSettings;
            }
        }

        private static SettingsPageId NextPage(SettingsPageId pageId)
        {
            switch (pageId)
            {
                case SettingsPageId.Overview: return SettingsPageId.NormalDebounce;
                case SettingsPageId.NormalDebounce: return SettingsPageId.GameMode;
                case SettingsPageId.GameMode: return SettingsPageId.KeyManagement;
                case SettingsPageId.KeyManagement: return SettingsPageId.ApplicationSettings;
                default: return SettingsPageId.Overview;
            }
        }

        private void OnEnabledToggled(object sender, RoutedEventArgs e)
        {
            if (_shellRefreshing) return;
            if (_context.Settings.Enabled == _enabledSwitch.IsOn) return;
            _context.Settings.Enabled = _enabledSwitch.IsOn;
            _context.SaveSettingsAndRefresh();
        }

        private void OnContextStateChanged(object sender, SettingsUiStateChangedEventArgs e)
        {
            if (!DispatcherQueue.HasThreadAccess)
            {
                DispatcherQueue.TryEnqueue(delegate { OnContextStateChanged(sender, e); });
                return;
            }

            if ((e.ChangeKind & SettingsUiChangeKind.Navigation) == SettingsUiChangeKind.Navigation) return;

            if ((e.ChangeKind & SettingsUiChangeKind.Runtime) == SettingsUiChangeKind.Runtime
                && (e.ChangeKind & (SettingsUiChangeKind.Settings | SettingsUiChangeKind.Learning)) == SettingsUiChangeKind.None)
            {
                RefreshRuntimeState();
                return;
            }

            RefreshData();
        }

        private void OnAppWindowClosing(AppWindow sender, AppWindowClosingEventArgs args)
        {
            if (_allowClose) return;
            args.Cancel = true;
            sender.Hide();
        }

        private void OnWindowClosed(object sender, WindowEventArgs args)
        {
            foreach (SettingsPageBase page in _pages.Values)
            {
                page.OnWindowClosing();
            }
            _context.StateChanged -= OnContextStateChanged;
            Closed -= OnWindowClosed;
            if (_context.NavigateRequested == NavigateTo)
            {
                _context.NavigateRequested = null;
            }
            if (_appWindow != null && _appWindowClosingHooked)
            {
                _appWindow.Closing -= OnAppWindowClosing;
                _appWindowClosingHooked = false;
            }
            if (_windowIcon != null)
            {
                _windowIcon.Dispose();
                _windowIcon = null;
            }
            if (ReferenceEquals(_currentInstance, this))
            {
                _currentInstance = null;
            }
        }
    }
}
