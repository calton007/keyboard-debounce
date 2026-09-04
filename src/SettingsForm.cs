using System;
using System.Collections.Generic;
using System.Drawing;
using System.Windows.Forms;
using System.Windows.Forms.Automation;

namespace KeyboardDebounce
{
    public sealed class SettingsForm : Form
    {
        private const int NavigationWidth = 188;
        private const int HeaderHeight = 68;
        private const int FooterHeight = 36;

        private readonly AppSettings _settings;
        private readonly SettingsUiContext _context;
        private readonly ISettingsThemeService _themeService;
        private readonly Dictionary<SettingsPageId, SettingsPageBase> _pages;
        private readonly Dictionary<SettingsPageId, RadioButton> _navigationButtons;
        private readonly List<SettingsPageId> _navigationOrder;
        private readonly Panel _navigation;
        private readonly Panel _header;
        private readonly Panel _pageHost;
        private readonly Panel _footer;
        private readonly Label _pageTitle;
        private readonly CheckBox _enabled;
        private readonly Label _status;
        private readonly Timer _refreshTimer;
        private readonly Font _windowFont;
        private readonly Icon _windowIcon;
        private readonly Font _pageTitleFont;
        private readonly Font _brandFont;
        private SettingsPageId _activePageId;
        private bool _refreshing;
        private bool _updatingNavigation;
        private bool _themeUpdatePending;
        private string _uiError;

        public SettingsForm(
            AppSettings settings,
            LearningState learning,
            DebounceEngine engine,
            SettingsStore store,
            Action save,
            Action saveLearning,
            Func<string> getStatus)
            : this(
                settings,
                learning,
                engine,
                store,
                save,
                saveLearning,
                getStatus,
                null)
        {
        }

        public SettingsForm(
            AppSettings settings,
            LearningState learning,
            DebounceEngine engine,
            SettingsStore store,
            Action save,
            Action saveLearning,
            Func<string> getStatus,
            Func<string> getGameModeStatus)
            : this(
                settings,
                learning,
                engine,
                store,
                save,
                saveLearning,
                getStatus,
                getGameModeStatus,
                null)
        {
        }

        internal SettingsForm(
            AppSettings settings,
            LearningState learning,
            DebounceEngine engine,
            SettingsStore store,
            Action save,
            Action saveLearning,
            Func<string> getStatus,
            Func<string> getGameModeStatus,
            ISettingsThemeService themeService)
        {
            if (settings == null) throw new ArgumentNullException("settings");
            if (learning == null) throw new ArgumentNullException("learning");
            if (engine == null) throw new ArgumentNullException("engine");
            if (store == null) throw new ArgumentNullException("store");

            _settings = settings;
            _themeService = themeService ?? new SystemSettingsThemeService();
            _pages = new Dictionary<SettingsPageId, SettingsPageBase>();
            _navigationButtons = new Dictionary<SettingsPageId, RadioButton>();
            _navigationOrder = new List<SettingsPageId>
            {
                SettingsPageId.Overview,
                SettingsPageId.NormalDebounce,
                SettingsPageId.GameMode,
                SettingsPageId.KeyManagement,
                SettingsPageId.ApplicationSettings
            };
            _context = new SettingsUiContext(
                settings,
                learning,
                engine,
                store,
                save,
                saveLearning,
                getStatus,
                getGameModeStatus);
            _context.NavigateRequested = delegate(SettingsPageId pageId) { NavigateTo(pageId, true); };
            _context.RefreshRequested = RefreshData;

            SuspendLayout();
            AutoScaleDimensions = new SizeF(96F, 96F);
            AutoScaleMode = AutoScaleMode.Dpi;
            _windowFont = new Font("Segoe UI", 9F, FontStyle.Regular, GraphicsUnit.Point);
            _windowIcon = AppIcon.Load();
            Font = _windowFont;
            _pageTitleFont = new Font("Segoe UI", 18F, FontStyle.Bold, GraphicsUnit.Point);
            _brandFont = new Font("Segoe UI", 11F, FontStyle.Bold, GraphicsUnit.Point);
            Text = "Keyboard Debounce";
            Icon = _windowIcon;
            Size = new Size(1160, 760);
            MinimumSize = new Size(960, 640);
            StartPosition = FormStartPosition.CenterScreen;
            KeyPreview = true;
            Name = "SettingsForm";

            var root = new TableLayoutPanel
            {
                Dock = DockStyle.Fill,
                ColumnCount = 2,
                RowCount = 1,
                Margin = new Padding(0),
                Padding = new Padding(0),
                Name = "SettingsShell"
            };
            root.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, NavigationWidth));
            root.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100F));
            root.RowStyles.Add(new RowStyle(SizeType.Percent, 100F));
            Controls.Add(root);

            _navigation = BuildNavigation();
            root.Controls.Add(_navigation, 0, 0);

            var main = new TableLayoutPanel
            {
                Dock = DockStyle.Fill,
                ColumnCount = 1,
                RowCount = 3,
                Margin = new Padding(0),
                Padding = new Padding(0),
                Name = "SettingsMain"
            };
            main.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100F));
            main.RowStyles.Add(new RowStyle(SizeType.Absolute, HeaderHeight));
            main.RowStyles.Add(new RowStyle(SizeType.Percent, 100F));
            main.RowStyles.Add(new RowStyle(SizeType.Absolute, FooterHeight));
            root.Controls.Add(main, 1, 0);

            _header = new Panel
            {
                Dock = DockStyle.Fill,
                Margin = new Padding(0),
                Padding = new Padding(24, 4, 24, 4),
                Name = "SettingsHeader",
                Tag = "Settings.CardSurface"
            };
            var headerLayout = new TableLayoutPanel
            {
                Dock = DockStyle.Fill,
                ColumnCount = 2,
                RowCount = 1,
                Margin = new Padding(0),
                Tag = "Settings.CardSurface"
            };
            headerLayout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100F));
            headerLayout.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
            headerLayout.RowStyles.Add(new RowStyle(SizeType.Percent, 100F));
            var heading = new TableLayoutPanel
            {
                Dock = DockStyle.Fill,
                ColumnCount = 1,
                RowCount = 1,
                Margin = new Padding(0),
                Tag = "Settings.CardSurface"
            };
            heading.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100F));
            heading.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            _pageTitle = new Label
            {
                AutoSize = true,
                Anchor = AnchorStyles.Left,
                Font = _pageTitleFont,
                TextAlign = ContentAlignment.MiddleLeft,
                AutoEllipsis = true,
                Margin = new Padding(0),
                Name = "PageTitleLabel"
            };
            heading.Controls.Add(_pageTitle, 0, 0);
            headerLayout.Controls.Add(heading, 0, 0);
            _enabled = new CheckBox
            {
                AutoSize = true,
                Anchor = AnchorStyles.Right,
                Text = "启用防抖",
                Name = "EnabledToggle",
                AccessibleName = "启用或暂停防抖",
                AccessibleDescription = "立即保存全局防抖启用状态。"
            };
            _enabled.CheckedChanged += OnEnabledChanged;
            headerLayout.Controls.Add(_enabled, 1, 0);
            _header.Controls.Add(headerLayout);
            main.Controls.Add(_header, 0, 0);

            _pageHost = new Panel
            {
                Dock = DockStyle.Fill,
                Margin = new Padding(0),
                Name = "SettingsPageHost",
                AccessibleName = "设置页面内容"
            };
            main.Controls.Add(_pageHost, 0, 1);

            _footer = new Panel
            {
                Dock = DockStyle.Fill,
                Margin = new Padding(0),
                Padding = new Padding(12, 0, 12, 0),
                Name = "SettingsStatusBar",
                Tag = "Settings.CardSurface"
            };
            _status = new Label
            {
                Dock = DockStyle.Fill,
                TextAlign = ContentAlignment.MiddleLeft,
                AutoEllipsis = true,
                Name = "StatusLiveRegion",
                AccessibleName = "最近事件和错误状态",
                LiveSetting = AutomationLiveSetting.Polite
            };
            _footer.Controls.Add(_status);
            main.Controls.Add(_footer, 0, 2);

            AddPage(new OverviewSettingsPage(_context));
            AddPage(new NormalDebounceSettingsPage(_context));
            AddPage(new GameModeSettingsPage(_context));
            AddPage(new KeyManagementSettingsPage(_context));
            AddPage(new ApplicationSettingsPage(_context));

            _themeService.ThemeChanged += OnThemeChanged;
            HandleCreated += OnHandleCreated;
            Shown += OnShown;

            _refreshTimer = new Timer { Interval = 500 };
            _refreshTimer.Tick += OnRefreshTimerTick;

            NavigateTo(SettingsPageId.Overview, false);
            RefreshData();
            ApplyTheme();
            _refreshTimer.Start();
            ResumeLayout(true);
        }

        internal SettingsPageId ActivePageId
        {
            get { return _activePageId; }
        }

        internal int CreatedPageCount
        {
            get { return _pages.Count; }
        }

        internal CheckBox EnabledToggle
        {
            get { return _enabled; }
        }

        internal Label StatusLiveRegion
        {
            get { return _status; }
        }

        internal SettingsPageBase GetPage(SettingsPageId pageId)
        {
            SettingsPageBase page;
            return _pages.TryGetValue(pageId, out page) ? page : null;
        }

        internal RadioButton GetNavigationButton(SettingsPageId pageId)
        {
            RadioButton button;
            return _navigationButtons.TryGetValue(pageId, out button) ? button : null;
        }

        internal bool ProcessNavigationShortcut(Keys keyData)
        {
            Keys shortcut = keyData & (Keys.KeyCode | Keys.Modifiers);
            if (shortcut == (Keys.Alt | Keys.D1)) return NavigateFromShortcut(SettingsPageId.Overview);
            if (shortcut == (Keys.Alt | Keys.D2)) return NavigateFromShortcut(SettingsPageId.NormalDebounce);
            if (shortcut == (Keys.Alt | Keys.D3)) return NavigateFromShortcut(SettingsPageId.GameMode);
            if (shortcut == (Keys.Alt | Keys.D4)) return NavigateFromShortcut(SettingsPageId.KeyManagement);
            if (shortcut == (Keys.Alt | Keys.D5)) return NavigateFromShortcut(SettingsPageId.ApplicationSettings);
            return false;
        }

        public void RefreshData()
        {
            _settings.Normalize();
            _refreshing = true;
            try
            {
                _enabled.Checked = _settings.Enabled;
                UpdateEnabledText();
                foreach (SettingsPageBase page in _pages.Values)
                {
                    page.RefreshData();
                }
                _uiError = "";
            }
            catch (Exception error)
            {
                _uiError = "界面刷新失败：" + Describe(error);
            }
            finally
            {
                _refreshing = false;
            }

            UpdateStatus();
        }

        public void RefreshVisibleValuesOnly()
        {
            _refreshing = true;
            try
            {
                _enabled.Checked = _settings.Enabled;
                UpdateEnabledText();
                SettingsPageBase activePage;
                if (_pages.TryGetValue(_activePageId, out activePage))
                {
                    activePage.RefreshLiveState();
                }
                _uiError = "";
            }
            catch (Exception error)
            {
                _uiError = "界面状态刷新失败：" + Describe(error);
            }
            finally
            {
                _refreshing = false;
            }

            UpdateStatus();
        }

        protected override bool ProcessCmdKey(ref Message msg, Keys keyData)
        {
            if (ProcessNavigationShortcut(keyData)) return true;
            return base.ProcessCmdKey(ref msg, keyData);
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                _refreshTimer.Stop();
                _refreshTimer.Tick -= OnRefreshTimerTick;
                _refreshTimer.Dispose();
                HandleCreated -= OnHandleCreated;
                Shown -= OnShown;
                _themeService.ThemeChanged -= OnThemeChanged;
                _themeService.Dispose();
                _context.NavigateRequested = null;
                _context.RefreshRequested = null;
                _windowIcon.Dispose();
                _windowFont.Dispose();
                _pageTitleFont.Dispose();
                _brandFont.Dispose();
            }
            base.Dispose(disposing);
        }

        private Panel BuildNavigation()
        {
            var panel = new Panel
            {
                Dock = DockStyle.Fill,
                Margin = new Padding(0),
                Padding = new Padding(12),
                Name = "SettingsNavigation",
                AccessibleName = "设置页面导航",
                AccessibleRole = AccessibleRole.Grouping,
                Tag = "Settings.CardSurface"
            };
            var layout = new TableLayoutPanel
            {
                Dock = DockStyle.Fill,
                ColumnCount = 1,
                RowCount = 7,
                Margin = new Padding(0),
                Tag = "Settings.CardSurface"
            };
            layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100F));
            layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 68F));
            for (int index = 0; index < 5; index++)
            {
                layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 48F));
            }
            layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100F));

            var brand = new Label
            {
                Dock = DockStyle.Fill,
                Font = _brandFont,
                Text = "KeyboardDebounce",
                TextAlign = ContentAlignment.MiddleLeft,
                AccessibleName = "Keyboard Debounce 设置"
            };
            layout.Controls.Add(brand, 0, 0);

            AddNavigationButton(layout, SettingsPageId.Overview, "概览", "Alt+1", 1);
            AddNavigationButton(layout, SettingsPageId.NormalDebounce, "普通防抖", "Alt+2", 2);
            AddNavigationButton(layout, SettingsPageId.GameMode, "游戏模式", "Alt+3", 3);
            AddNavigationButton(layout, SettingsPageId.KeyManagement, "按键管理", "Alt+4", 4);
            AddNavigationButton(layout, SettingsPageId.ApplicationSettings, "应用设置", "Alt+5", 5);
            panel.Controls.Add(layout);
            return panel;
        }

        private void AddNavigationButton(
            TableLayoutPanel layout,
            SettingsPageId pageId,
            string text,
            string shortcut,
            int row)
        {
            var button = new RadioButton
            {
                Dock = DockStyle.Fill,
                Margin = new Padding(0, 2, 0, 2),
                Padding = new Padding(10, 0, 6, 0),
                Text = text,
                TextAlign = ContentAlignment.MiddleLeft,
                FlatStyle = FlatStyle.Flat,
                Appearance = Appearance.Button,
                AutoCheck = true,
                UseVisualStyleBackColor = false,
                Tag = pageId,
                Name = "Nav" + pageId,
                AccessibleName = text,
                AccessibleDescription = "切换到" + text + "页面，快捷键 " + shortcut,
                AccessibleRole = AccessibleRole.RadioButton,
                TabStop = true,
                TabIndex = row - 1
            };
            button.CheckedChanged += OnNavigationCheckedChanged;
            button.KeyDown += OnNavigationKeyDown;
            _navigationButtons.Add(pageId, button);
            layout.Controls.Add(button, 0, row);
        }

        private void AddPage(SettingsPageBase page)
        {
            page.Name = page.PageId + "Page";
            page.AccessibleName = page.PageTitle + "设置页";
            page.AccessibleRole = AccessibleRole.Pane;
            page.Visible = false;
            _pages.Add(page.PageId, page);
            _pageHost.Controls.Add(page);
        }

        private void NavigateTo(SettingsPageId pageId, bool focusPage)
        {
            SettingsPageBase selectedPage;
            if (!_pages.TryGetValue(pageId, out selectedPage))
            {
                throw new ArgumentOutOfRangeException("pageId");
            }

            _activePageId = pageId;
            foreach (KeyValuePair<SettingsPageId, SettingsPageBase> pair in _pages)
            {
                pair.Value.Visible = pair.Key == pageId;
            }
            selectedPage.BringToFront();
            _pageTitle.Text = selectedPage.PageTitle;
            selectedPage.RefreshData();
            ApplyNavigationTheme(_themeService.CurrentPalette);

            if (focusPage && selectedPage.InitialFocusControl != null)
            {
                selectedPage.InitialFocusControl.Focus();
            }
        }

        private bool NavigateFromShortcut(SettingsPageId pageId)
        {
            NavigateTo(pageId, true);
            return true;
        }

        private void OnNavigationCheckedChanged(object sender, EventArgs e)
        {
            if (_updatingNavigation) return;
            var button = sender as RadioButton;
            if (button == null || !button.Checked || !(button.Tag is SettingsPageId)) return;
            NavigateTo((SettingsPageId)button.Tag, true);
        }

        private void OnNavigationKeyDown(object sender, KeyEventArgs e)
        {
            var button = sender as RadioButton;
            if (button == null || !(button.Tag is SettingsPageId)) return;

            int current = _navigationOrder.IndexOf((SettingsPageId)button.Tag);
            int next = current;
            if (e.KeyCode == Keys.Down || e.KeyCode == Keys.Right)
            {
                next = (current + 1) % _navigationOrder.Count;
            }
            else if (e.KeyCode == Keys.Up || e.KeyCode == Keys.Left)
            {
                next = (current - 1 + _navigationOrder.Count) % _navigationOrder.Count;
            }
            else if (e.KeyCode == Keys.Home)
            {
                next = 0;
            }
            else if (e.KeyCode == Keys.End)
            {
                next = _navigationOrder.Count - 1;
            }
            else if (e.KeyCode == Keys.Enter || e.KeyCode == Keys.Space)
            {
                NavigateTo((SettingsPageId)button.Tag, true);
                e.Handled = true;
                e.SuppressKeyPress = true;
                return;
            }
            else
            {
                return;
            }

            SettingsPageId nextId = _navigationOrder[next];
            NavigateTo(nextId, false);
            _navigationButtons[nextId].Focus();
            e.Handled = true;
            e.SuppressKeyPress = true;
        }

        private void OnEnabledChanged(object sender, EventArgs e)
        {
            if (_refreshing) return;
            _settings.Enabled = _enabled.Checked;
            UpdateEnabledText();
            _context.SaveSettingsAndRefresh();
        }

        private void UpdateEnabledText()
        {
            _enabled.Text = _enabled.Checked ? "防抖已启用" : "防抖已暂停";
        }

        private void OnRefreshTimerTick(object sender, EventArgs e)
        {
            RefreshVisibleValuesOnly();
        }

        private void OnThemeChanged(object sender, EventArgs e)
        {
            if (IsDisposed || Disposing) return;
            if (InvokeRequired)
            {
                if (!IsHandleCreated)
                {
                    _themeUpdatePending = true;
                    return;
                }

                try
                {
                    BeginInvoke(new Action(ApplyTheme));
                }
                catch (InvalidOperationException)
                {
                    _themeUpdatePending = true;
                }
                return;
            }

            ApplyTheme();
        }

        private void OnHandleCreated(object sender, EventArgs e)
        {
            if (_themeUpdatePending)
            {
                _themeUpdatePending = false;
                ApplyTheme();
            }
            else
            {
                _themeService.ApplyTitleBar(this);
            }
        }

        private void OnShown(object sender, EventArgs e)
        {
            ApplyTheme();
        }

        private void ApplyTheme()
        {
            if (IsDisposed || Disposing) return;
            SettingsThemePalette palette = _themeService.CurrentPalette
                ?? SettingsThemePalette.Create(SettingsThemeKind.Light);
            SettingsThemeStyler.Apply(this, palette);
            _status.ForeColor = HasVisibleError() ? palette.Error : palette.MutedText;
            ApplyNavigationTheme(palette);
            _themeService.ApplyTitleBar(this);
            UpdateStatus();
            Invalidate(true);
        }

        private void ApplyNavigationTheme(SettingsThemePalette palette)
        {
            if (palette == null) return;
            _updatingNavigation = true;
            try
            {
                foreach (KeyValuePair<SettingsPageId, RadioButton> pair in _navigationButtons)
                {
                    bool selected = pair.Key == _activePageId;
                    RadioButton button = pair.Value;
                    button.Checked = selected;
                    bool highContrastSelection = selected
                        && palette.Kind == SettingsThemeKind.HighContrast;
                    button.BackColor = selected ? palette.Selection : palette.Card;
                    button.ForeColor = highContrastSelection
                        ? SettingsThemeStyler.GetAccentTextColor(palette)
                        : palette.Text;
                    button.FlatAppearance.BorderColor = selected ? palette.Accent : palette.Card;
                    button.FlatAppearance.CheckedBackColor = palette.Selection;
                    button.FlatAppearance.MouseOverBackColor = palette.Selection;
                    button.FlatAppearance.MouseDownBackColor = palette.Accent;
                    string pageTitle = GetPageTitle(pair.Key);
                    string shortcut = "Alt+" + (int)pair.Key;
                    button.AccessibleName = selected ? pageTitle + "，当前页" : pageTitle;
                    button.AccessibleDescription = selected
                        ? "当前显示的设置页面。快捷键 " + shortcut
                        : "切换到" + pageTitle + "页面，快捷键 " + shortcut;
                }
            }
            finally
            {
                _updatingNavigation = false;
            }
        }

        private string GetPageTitle(SettingsPageId pageId)
        {
            SettingsPageBase page;
            return _pages.TryGetValue(pageId, out page) ? page.PageTitle : pageId.ToString();
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
                recent = "状态读取失败：" + Describe(error);
            }

            string text = "最近事件：" + (String.IsNullOrWhiteSpace(recent) ? "等待按键事件。" : recent);
            if (!String.IsNullOrWhiteSpace(_themeService.LastError))
            {
                text += "  |  主题：" + _themeService.LastError;
            }
            if (!String.IsNullOrWhiteSpace(_uiError))
            {
                text += "  |  " + _uiError;
            }
            _status.Text = text;
            _status.AccessibleName = text;

            SettingsThemePalette palette = _themeService.CurrentPalette;
            if (palette != null)
            {
                _status.ForeColor = HasVisibleError() ? palette.Error : palette.MutedText;
            }
        }

        private bool HasVisibleError()
        {
            return !String.IsNullOrWhiteSpace(_themeService.LastError)
                || !String.IsNullOrWhiteSpace(_uiError);
        }

        private static string Describe(Exception error)
        {
            return error.GetType().Name + " - " + error.Message;
        }
    }
}
