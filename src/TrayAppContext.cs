using System;
using System.Drawing;
using System.Threading;
using System.Windows.Forms;

namespace KeyboardDebounce
{
    public sealed class TrayAppContext : ApplicationContext
    {
        private readonly SettingsStore _store;
        private readonly AppSettings _settings;
        private readonly LearningState _learning;
        private readonly DebounceEngine _engine;
        private readonly NotifyIcon _notifyIcon;
        private readonly KeyboardHook _hook;
        private readonly HotkeyWindow _hotkeyWindow;
        private readonly ToolStripMenuItem _toggleItem;
        private readonly System.Threading.Timer _startupTimer;
        private const string AppName = "Keyboard Debounce";
        private SettingsForm _mainWindow;
        private bool _inStartupDelay;
        private string _lastStatus;

        public TrayAppContext()
        {
            _store = new SettingsStore();
            AppData appData = _store.Load();
            _settings = appData.Settings;
            _learning = appData.Learning;
            _settings.StartWithWindows = StartupManager.IsEnabled();
            _engine = new DebounceEngine(_settings, _learning);
            _inStartupDelay = _settings.StartupDelayMs > 0;

            _notifyIcon = new NotifyIcon();
            _notifyIcon.Icon = AppIcon.Load();
            _notifyIcon.Text = AppName;
            _notifyIcon.Visible = true;
            _notifyIcon.ContextMenuStrip = BuildMenu(out _toggleItem);
            _notifyIcon.DoubleClick += delegate { ShowMainWindow(); };

            _hook = new KeyboardHook(_engine, IsSuppressionEnabled, OnDecision);
            _hook.Start();
            _lastStatus = "键盘钩子已启动，等待按键事件。";

            _hotkeyWindow = new HotkeyWindow(ToggleEnabled);

            if (_inStartupDelay)
            {
                _startupTimer = new System.Threading.Timer(delegate
                {
                    _inStartupDelay = false;
                    UpdateTrayText();
                }, null, _settings.StartupDelayMs, Timeout.Infinite);
            }

            UpdateTrayText();
            if (!_settings.SilentRun)
            {
                ShowMainWindow();
            }
        }

        private ContextMenuStrip BuildMenu(out ToolStripMenuItem toggle)
        {
            var menu = new ContextMenuStrip();

            var toggleItem = new ToolStripMenuItem("启用防抖");
            toggle = toggleItem;
            toggleItem.Checked = _settings.Enabled;
            toggleItem.CheckOnClick = true;
            toggleItem.Click += delegate
            {
                _settings.Enabled = toggleItem.Checked;
                SaveSettings();
                UpdateTrayText();
            };
            menu.Items.Add(toggleItem);

            var mainWindowItem = new ToolStripMenuItem("显示主界面");
            mainWindowItem.Click += delegate { ShowMainWindow(); };
            menu.Items.Add(mainWindowItem);

            menu.Items.Add(new ToolStripSeparator());

            var exitItem = new ToolStripMenuItem("退出");
            exitItem.Click += delegate { ExitThread(); };
            menu.Items.Add(exitItem);

            return menu;
        }

        private bool IsSuppressionEnabled()
        {
            return _settings.Enabled && !_inStartupDelay;
        }

        private void ToggleEnabled()
        {
            _settings.Enabled = !_settings.Enabled;
            _toggleItem.Checked = _settings.Enabled;
            SaveSettings();
            UpdateTrayText();
            RefreshMainWindow();
        }

        private void OnDecision(DebounceDecision decision, int virtualKeyCode)
        {
            _lastStatus = DateTime.Now.ToString("HH:mm:ss.fff") +
                " VK " + virtualKeyCode +
                " " + decision.Reason +
                " interval=" + decision.IntervalMs +
                "ms threshold=" + decision.EffectiveThresholdMs +
                "ms" +
                " learn=" + FormatLearningAdjustment(decision.LearningAdjustmentMs) +
                (String.IsNullOrEmpty(decision.LearningReason) ? "" : " " + decision.LearningReason) +
                (decision.Suppress ? " suppressed" : " accepted");

            if (decision.LearningAdjustmentMs != 0)
            {
                SaveLearningState();
            }

            if (decision.Suppress)
            {
                _notifyIcon.Text = AppName;
            }
        }

        private string GetLastStatus()
        {
            return _lastStatus;
        }

        private static string FormatLearningAdjustment(int adjustment)
        {
            if (adjustment > 0) return "+" + adjustment + "ms";
            if (adjustment < 0) return adjustment + "ms";
            return "0ms";
        }

        private void ShowMainWindow()
        {
            if (_mainWindow == null || _mainWindow.IsDisposed)
            {
                _mainWindow = new SettingsForm(_settings, _learning, _engine, _store, SaveSettings, SaveLearningState, GetLastStatus);
            }
            _mainWindow.Show();
            _mainWindow.Activate();
            _mainWindow.RefreshData();
        }

        private void RefreshMainWindow()
        {
            if (_mainWindow != null && !_mainWindow.IsDisposed)
            {
                _mainWindow.RefreshData();
            }
        }

        private void SaveSettings()
        {
            StartupManager.SetEnabled(_settings.StartWithWindows);
            _store.SaveSettings(_settings);
        }

        private void SaveLearningState()
        {
            _store.SaveLearningState(_learning, _settings.DefaultThresholdMs);
        }

        private void UpdateTrayText()
        {
            _notifyIcon.Text = AppName;
        }

        protected override void ExitThreadCore()
        {
            SaveSettings();
            SaveLearningState();
            if (_hook != null) _hook.Dispose();
            if (_hotkeyWindow != null) _hotkeyWindow.Dispose();
            if (_startupTimer != null) _startupTimer.Dispose();
            if (_notifyIcon != null)
            {
                _notifyIcon.Visible = false;
                _notifyIcon.Dispose();
            }
            base.ExitThreadCore();
        }
    }
}
