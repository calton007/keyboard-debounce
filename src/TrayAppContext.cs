using System;
using System.Collections.Generic;
using System.Drawing;
using System.Text;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace KeyboardDebounce
{
    internal sealed class GameProcessCheckGate
    {
        private readonly object _sync = new object();
        private bool _running;
        private bool _pending;

        public bool IsRunning
        {
            get
            {
                lock (_sync) return _running;
            }
        }

        public bool HasPendingRequest
        {
            get
            {
                lock (_sync) return _pending;
            }
        }

        public bool TryBegin()
        {
            lock (_sync)
            {
                if (_running)
                {
                    _pending = true;
                    return false;
                }

                _running = true;
                return true;
            }
        }

        public bool Complete(bool continuePendingRequest)
        {
            lock (_sync)
            {
                if (!_running)
                {
                    throw new InvalidOperationException(
                        "Cannot complete a game process check that is not running.");
                }

                if (_pending && continuePendingRequest)
                {
                    _pending = false;
                    return true;
                }

                _running = false;
                _pending = false;
                return false;
            }
        }
    }

    internal static class GameProcessConfigurationState
    {
        public static GameModeRuntimeSnapshot ApplyDetection(
            GameModeRuntimeSnapshot previous,
            bool processModeEnabled,
            IReadOnlyList<string> configuredExecutables,
            GameProcessDetectionResult result)
        {
            previous = previous ?? GameModeRuntimeSnapshot.Inactive;
            if (!processModeEnabled
                || configuredExecutables == null
                || configuredExecutables.Count == 0)
            {
                return GameModeRuntimeSnapshot.Inactive;
            }

            result = result ?? new GameProcessDetectionResult(
                false,
                "",
                Array.Empty<string>(),
                GameModeDetectionHealth.Failed,
                "进程检测未返回结果");

            IReadOnlyList<string> confirmedRunning = IntersectConfiguredExecutables(
                configuredExecutables,
                result.RunningConfiguredExecutables);
            if (result.DetectionHealth == GameModeDetectionHealth.Healthy)
            {
                string active = confirmedRunning.Count == 0
                    ? ""
                    : confirmedRunning[0];
                return new GameModeRuntimeSnapshot(
                    active.Length > 0,
                    active,
                    confirmedRunning,
                    GameModeDetectionHealth.Healthy,
                    "");
            }

            bool keepPreviousMode = previous.IsActive
                && KeepsCachedMatch(
                    true,
                    configuredExecutables,
                    previous.ActiveExecutable);
            IReadOnlyList<string> running = result.DetectionHealth
                == GameModeDetectionHealth.Failed
                    ? IntersectConfiguredExecutables(
                        configuredExecutables,
                        previous.RunningConfiguredExecutables)
                    : confirmedRunning;
            IReadOnlyList<string> unknown = result.DetectionHealth
                == GameModeDetectionHealth.Failed
                    ? IntersectConfiguredExecutables(
                        configuredExecutables,
                        previous.UnknownConfiguredExecutables)
                    : ExceptConfiguredExecutables(
                        configuredExecutables,
                        confirmedRunning);
            return new GameModeRuntimeSnapshot(
                keepPreviousMode,
                keepPreviousMode ? previous.ActiveExecutable : "",
                running,
                result.DetectionHealth,
                result.ErrorMessage,
                unknown);
        }

        public static GameModeRuntimeSnapshot ReconcileConfiguration(
            GameModeRuntimeSnapshot previous,
            bool processModeEnabled,
            IReadOnlyList<string> configuredExecutables)
        {
            previous = previous ?? GameModeRuntimeSnapshot.Inactive;
            if (!processModeEnabled
                || configuredExecutables == null
                || configuredExecutables.Count == 0)
            {
                return GameModeRuntimeSnapshot.Inactive;
            }

            if (previous.IsActive
                && !KeepsCachedMatch(
                    true,
                    configuredExecutables,
                    previous.ActiveExecutable))
            {
                return new GameModeRuntimeSnapshot(
                    false,
                    "",
                    IntersectConfiguredExecutables(
                        configuredExecutables,
                        previous.RunningConfiguredExecutables),
                    GameModeDetectionHealth.Healthy,
                    "",
                    IntersectConfiguredExecutables(
                        configuredExecutables,
                        previous.UnknownConfiguredExecutables));
            }

            return new GameModeRuntimeSnapshot(
                previous.IsActive,
                previous.ActiveExecutable,
                IntersectConfiguredExecutables(
                    configuredExecutables,
                    previous.RunningConfiguredExecutables),
                previous.DetectionHealth,
                previous.ErrorMessage,
                IntersectConfiguredExecutables(
                    configuredExecutables,
                    previous.UnknownConfiguredExecutables));
        }

        public static bool ShouldAcceptCheckResult(
            bool exiting,
            int startedConfigurationVersion,
            int currentConfigurationVersion)
        {
            return !exiting
                && startedConfigurationVersion == currentConfigurationVersion;
        }

        public static bool IsEffectivelyActive(
            bool cachedActive,
            bool processModeEnabled,
            IReadOnlyList<string> configuredExecutables)
        {
            return cachedActive
                && processModeEnabled
                && configuredExecutables != null
                && configuredExecutables.Count > 0;
        }

        public static bool KeepsCachedMatch(
            bool processModeEnabled,
            IReadOnlyList<string> configuredExecutables,
            string activeExecutable)
        {
            if (!processModeEnabled
                || configuredExecutables == null
                || configuredExecutables.Count == 0)
            {
                return false;
            }

            string normalizedActive = GameProcessDetector.NormalizeProcessName(activeExecutable);
            if (normalizedActive.Length == 0) return false;

            for (int index = 0; index < configuredExecutables.Count; index++)
            {
                string configured = GameProcessDetector.NormalizeProcessName(
                    configuredExecutables[index]);
                if (String.Equals(
                    configured,
                    normalizedActive,
                    StringComparison.OrdinalIgnoreCase))
                {
                    return true;
                }
            }
            return false;
        }

        private static IReadOnlyList<string> IntersectConfiguredExecutables(
            IReadOnlyList<string> configuredExecutables,
            IReadOnlyList<string> candidates)
        {
            var candidateSet = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            if (candidates != null)
            {
                foreach (string candidate in candidates)
                {
                    string normalized = GameProcessDetector.NormalizeProcessName(candidate);
                    if (normalized.Length > 0)
                    {
                        candidateSet.Add(normalized);
                    }
                }
            }

            var result = new List<string>();
            var added = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (string configured in configuredExecutables)
            {
                string normalized = GameProcessDetector.NormalizeProcessName(configured);
                if (normalized.Length > 0
                    && candidateSet.Contains(normalized)
                    && added.Add(normalized))
                {
                    result.Add(normalized);
                }
            }
            return result;
        }

        private static IReadOnlyList<string> ExceptConfiguredExecutables(
            IReadOnlyList<string> configuredExecutables,
            IReadOnlyList<string> excluded)
        {
            var excludedSet = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            if (excluded != null)
            {
                foreach (string executable in excluded)
                {
                    string normalized = GameProcessDetector.NormalizeProcessName(executable);
                    if (normalized.Length > 0)
                    {
                        excludedSet.Add(normalized);
                    }
                }
            }

            var result = new List<string>();
            var added = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (string configured in configuredExecutables)
            {
                string normalized = GameProcessDetector.NormalizeProcessName(configured);
                if (normalized.Length > 0
                    && !excludedSet.Contains(normalized)
                    && added.Add(normalized))
                {
                    result.Add(normalized);
                }
            }
            return result;
        }
    }

    public sealed class TrayAppContext : ApplicationContext
    {
        private const string AppName = "Keyboard Debounce";
        private const long LearningCheckpointMs = 5000;
        private const long InitialSaveRetryMs = 1000;
        private const long MaximumSaveRetryMs = 60000;
        private const int GameProcessPollMs = 1000;
        private readonly SettingsStore _store;
        private readonly AppSettings _settings;
        private readonly LearningState _learning;
        private readonly DebounceEngine _engine;
        private readonly NotifyIcon _notifyIcon;
        private readonly Icon _trayIcon;
        private readonly KeyboardHook _hook;
        private readonly HotkeyWindow _hotkeyWindow;
        private readonly ToolStripMenuItem _toggleItem;
        private readonly ToolStripMenuItem _gameModeItem;
        private readonly Timer _decisionTimer;
        private readonly Timer _startupTimer;
        private readonly Timer _gameProcessTimer;
        private readonly SaveTaskSlot _learningSaveSlot;
        private readonly SaveErrorStatus _saveErrorStatus;
        private readonly SaveTaskSlot _settingsSaveSlot;
        private readonly GameProcessCheckGate _gameProcessCheckGate =
            new GameProcessCheckGate();
        private bool _inStartupDelay;
        private bool _lastAppliedEnabled;
        private bool _lastAppliedStartWithWindows;
        private GameModeRuntimeSnapshot _gameModeRuntimeSnapshot =
            GameModeRuntimeSnapshot.Inactive;
        private int _gameProcessConfigurationVersion;
        private bool _exiting;
        private DebounceDecision _pendingDecision;
        private int _pendingVirtualKeyCode;
        private string _lastStatus;

        internal event Action ShowSettingsWindowRequested;
        internal event Action SettingsDataRefreshRequested;
        internal event Action ExitRequested;

        internal SettingsUiContext UiContext { get; }

        internal bool ShouldShowSettingsWindowOnStartup
        {
            get { return !_settings.SilentRun; }
        }

        public TrayAppContext()
        {
            _store = new SettingsStore();
            AppData appData = _store.Load();
            _settings = appData.Settings;
            _learning = appData.Learning;
            _settings.StartWithWindows = StartupManager.IsEnabled();
            _lastAppliedStartWithWindows = _settings.StartWithWindows;
            _engine = new DebounceEngine(_settings, _learning);
            _settingsSaveSlot = new SaveTaskSlot(
                0,
                InitialSaveRetryMs,
                MaximumSaveRetryMs);
            _learningSaveSlot = new SaveTaskSlot(
                LearningCheckpointMs,
                InitialSaveRetryMs,
                MaximumSaveRetryMs);
            _saveErrorStatus = new SaveErrorStatus();
            LearningStateMigrationStatus migration = _store.LearningMigrationStatus;
            if (migration != null && migration.IsSavePending)
            {
                _learningSaveSlot.MarkDirty(Environment.TickCount64, true);
                if (migration.Error != null)
                {
                    _saveErrorStatus.SetError(
                        false,
                        "学习状态迁移保存失败：" + DescribeException(migration.Error));
                }
            }
            UiContext = new SettingsUiContext(
                _settings,
                _learning,
                _engine,
                _store,
                SaveSettings,
                SaveLearningStateInteractive,
                GetLastStatus,
                GetGameModeRuntimeSnapshot,
                BeginGameProcessCheck);
            _inStartupDelay = _settings.StartupDelayMs > 0;

            try
            {
                _trayIcon = AppIcon.Load();
                _notifyIcon = new NotifyIcon();
                _notifyIcon.Icon = _trayIcon;
                _notifyIcon.Text = BuildTrayTooltip(UiContext.GetCurrentGameModeStatus());
                _notifyIcon.ContextMenuStrip = BuildMenu(out _toggleItem, out _gameModeItem);
                _notifyIcon.Visible = true;
                _notifyIcon.DoubleClick += delegate
                {
                    RunInteractive("显示主界面失败", ShowMainWindow);
                };

                string startupWarning = BuildLoadIssueMessage(_store.LoadIssues);
                try
                {
                    ApplyGameProcessDetection(
                        DetectConfiguredGameProcesses(),
                        false);
                }
                catch (Exception error)
                {
                    startupWarning = AppendWarning(
                        startupWarning,
                        "游戏 EXE 检测启动失败；已保持普通模式。" + Environment.NewLine
                        + "原因：" + DescribeException(error));
                    _gameModeRuntimeSnapshot = new GameModeRuntimeSnapshot(
                        false,
                        "",
                        Array.Empty<string>(),
                        GameModeDetectionHealth.Failed,
                        DescribeException(error));
                }
                _hotkeyWindow = new HotkeyWindow(ToggleEnabled, _settings.PauseHotkey);
                if (!_hotkeyWindow.IsRegistered)
                {
                    _settings.Enabled = false;
                    _toggleItem.Checked = false;
                    startupWarning = AppendWarning(
                        startupWarning,
                        "暂停热键注册失败，防抖已安全停用。" + Environment.NewLine
                        + "热键：" + _settings.PauseHotkey + Environment.NewLine
                        + "原因：" + _hotkeyWindow.RegistrationError);
                }

                _hook = new KeyboardHook(
                    _engine,
                    IsSuppressionEnabled,
                    IsGameModeActive,
                    QueueDecision);
                _hook.Start();
                _lastAppliedEnabled = _settings.Enabled;
                _lastStatus = "键盘钩子已启动，等待按键事件。";

                _decisionTimer = new Timer();
                _decisionTimer.Interval = 50;
                _decisionTimer.Tick += delegate { FlushPendingWork(); };
                _decisionTimer.Start();

                StartPendingSaves();

                _gameProcessTimer = new Timer();
                _gameProcessTimer.Interval = GameProcessPollMs;
                _gameProcessTimer.Tick += delegate { BeginGameProcessCheck(); };
                _gameProcessTimer.Start();

                if (!_hotkeyWindow.IsRegistered)
                {
                    SaveSettings();
                }

                if (_inStartupDelay)
                {
                    _startupTimer = new Timer();
                    _startupTimer.Interval = Math.Max(1, _settings.StartupDelayMs);
                    _startupTimer.Tick += delegate
                    {
                        _startupTimer.Stop();
                        _inStartupDelay = false;
                        _hook.ResetRuntimeState();
                        UpdateTrayText();
                        RequestSettingsRuntimeRefresh();
                    };
                    _startupTimer.Start();
                }

                UpdateTrayText();
                if (!String.IsNullOrEmpty(startupWarning))
                {
                    MessageBox.Show(
                        startupWarning,
                        AppName,
                        MessageBoxButtons.OK,
                        MessageBoxIcon.Warning);
                }
            }
            catch (Exception startupError)
            {
                var cleanupErrors = new List<Exception> { startupError };
                CaptureError(cleanupErrors, delegate
                {
                    if (_decisionTimer != null) _decisionTimer.Dispose();
                });
                CaptureError(cleanupErrors, delegate
                {
                    if (_startupTimer != null) _startupTimer.Dispose();
                });
                CaptureError(cleanupErrors, delegate
                {
                    if (_gameProcessTimer != null) _gameProcessTimer.Dispose();
                });
                CaptureError(cleanupErrors, delegate
                {
                    if (_hook != null) _hook.Dispose();
                });
                CaptureError(cleanupErrors, delegate
                {
                    if (_hotkeyWindow != null) _hotkeyWindow.Dispose();
                });
                CaptureError(cleanupErrors, delegate
                {
                    UiContext.RefreshRequested = null;
                    UiContext.NavigateRequested = null;
                });
                CaptureError(cleanupErrors, delegate
                {
                    if (_notifyIcon == null) return;
                    _notifyIcon.Visible = false;
                    if (_notifyIcon.ContextMenuStrip != null)
                    {
                        _notifyIcon.ContextMenuStrip.Dispose();
                    }
                    _notifyIcon.Dispose();
                });
                CaptureError(cleanupErrors, delegate
                {
                    if (_trayIcon != null) _trayIcon.Dispose();
                });

                if (cleanupErrors.Count == 1) throw;
                throw new AggregateException(
                    "Keyboard Debounce 启动失败，且部分已创建资源未能清理。",
                    cleanupErrors);
            }
        }

        private ContextMenuStrip BuildMenu(
            out ToolStripMenuItem toggle,
            out ToolStripMenuItem gameModeStatus)
        {
            var menu = new ContextMenuStrip();

            var toggleItem = new ToolStripMenuItem("启用防抖");
            toggle = toggleItem;
            toggleItem.Checked = _settings.Enabled;
            toggleItem.CheckOnClick = true;
            toggleItem.Click += delegate
            {
                RunInteractive("切换防抖失败", delegate
                {
                    ApplyEnabledState(toggleItem.Checked);
                    SaveSettings();
                    UpdateTrayText();
                    RequestSettingsDataRefresh();
                });
            };
            menu.Items.Add(toggleItem);

            var gameModeItem = new ToolStripMenuItem(
                UiContext.GetCurrentGameModeStatus());
            gameModeStatus = gameModeItem;
            gameModeItem.Enabled = false;
            menu.Items.Add(gameModeItem);

            var mainWindowItem = new ToolStripMenuItem("显示主界面");
            mainWindowItem.Click += delegate
            {
                RunInteractive("显示主界面失败", ShowMainWindow);
            };
            menu.Items.Add(mainWindowItem);

            menu.Items.Add(new ToolStripSeparator());

            var exitItem = new ToolStripMenuItem("退出");
            exitItem.Click += delegate { RequestExit(); };
            menu.Items.Add(exitItem);

            return menu;
        }

        private bool IsSuppressionEnabled()
        {
            return _settings.Enabled && !_inStartupDelay;
        }

        private bool IsGameModeActive()
        {
            GameModeRuntimeSnapshot snapshot = GetGameModeRuntimeSnapshot();
            return GameProcessConfigurationState.IsEffectivelyActive(
                snapshot.IsActive,
                _settings.ProcessGameModeEnabled,
                _settings.GameProcesses);
        }

        private GameModeRuntimeSnapshot GetGameModeRuntimeSnapshot()
        {
            return System.Threading.Volatile.Read(ref _gameModeRuntimeSnapshot)
                ?? GameModeRuntimeSnapshot.Inactive;
        }

        private GameProcessDetectionResult DetectConfiguredGameProcesses()
        {
            if (!_settings.ProcessGameModeEnabled
                || _settings.GameProcesses == null
                || _settings.GameProcesses.Count == 0)
            {
                return GameProcessDetectionResult.Inactive;
            }
            return GameProcessDetector.DetectRunningProcesses(
                new List<string>(_settings.GameProcesses));
        }

        private void BeginGameProcessCheck()
        {
            if (_exiting || !_gameProcessCheckGate.TryBegin()) return;
            _ = RunGameProcessCheckAsync();
        }

        private async Task RunGameProcessCheckAsync()
        {
            int configurationVersion = _gameProcessConfigurationVersion;
            try
            {
                bool enabled = _settings.ProcessGameModeEnabled;
                var configured = _settings.GameProcesses == null
                    ? new List<string>()
                    : new List<string>(_settings.GameProcesses);
                GameProcessDetectionResult result = enabled && configured.Count > 0
                    ? await Task.Run(
                        delegate
                        {
                            return GameProcessDetector.DetectRunningProcesses(configured);
                        })
                    : GameProcessDetectionResult.Inactive;
                if (!GameProcessConfigurationState.ShouldAcceptCheckResult(
                    _exiting,
                    configurationVersion,
                    _gameProcessConfigurationVersion)) return;

                ApplyGameProcessDetection(result, true);
            }
            catch (Exception error)
            {
                if (!GameProcessConfigurationState.ShouldAcceptCheckResult(
                    _exiting,
                    configurationVersion,
                    _gameProcessConfigurationVersion)) return;
                ApplyGameProcessDetection(
                    new GameProcessDetectionResult(
                        false,
                        "",
                        Array.Empty<string>(),
                        GameModeDetectionHealth.Failed,
                        DescribeException(error)),
                    true);
            }
            finally
            {
                if (_gameProcessCheckGate.Complete(!_exiting))
                {
                    _ = RunGameProcessCheckAsync();
                }
            }
        }

        private void ApplyGameProcessDetection(
            GameProcessDetectionResult result,
            bool notifyStateChange)
        {
            GameModeRuntimeSnapshot next =
                GameProcessConfigurationState.ApplyDetection(
                    GetGameModeRuntimeSnapshot(),
                    _settings.ProcessGameModeEnabled,
                    _settings.GameProcesses,
                    result);
            ApplyGameModeRuntimeSnapshot(next, notifyStateChange);
        }

        private void ApplyGameModeRuntimeSnapshot(
            GameModeRuntimeSnapshot next,
            bool notifyStateChange)
        {
            next = next ?? GameModeRuntimeSnapshot.Inactive;
            GameModeRuntimeSnapshot previous = GetGameModeRuntimeSnapshot();
            if (previous.Equals(next)) return;

            System.Threading.Volatile.Write(ref _gameModeRuntimeSnapshot, next);
            bool modeChanged = previous.IsActive != next.IsActive
                || !String.Equals(
                    previous.ActiveExecutable,
                    next.ActiveExecutable,
                    StringComparison.OrdinalIgnoreCase);
            if (modeChanged && _hook != null)
            {
                _hook.ResetRuntimeState();
            }
            if (notifyStateChange)
            {
                if (next.DetectionHealth != GameModeDetectionHealth.Healthy)
                {
                    string mode = next.IsActive ? "游戏模式" : "普通模式";
                    _lastStatus = "游戏 EXE 检测状态未知；继续保持"
                        + mode + "：" + next.ErrorMessage;
                }
                else if (modeChanged)
                {
                    _lastStatus = next.IsActive
                        ? "检测到 EXE "
                            + GameProcessDetector.FormatExecutableName(
                                next.ActiveExecutable)
                            + "，已进入游戏低干预模式。"
                        : "已添加的游戏 EXE 均未运行，已恢复普通模式。";
                }
            }
            UpdateTrayText();
            RequestSettingsRuntimeRefresh();
        }

        private void ToggleEnabled()
        {
            RunInteractive("暂停热键处理失败", delegate
            {
                ApplyEnabledState(!_settings.Enabled);
                _toggleItem.Checked = _settings.Enabled;
                SaveSettings();
                UpdateTrayText();
                RequestSettingsDataRefresh();
            });
        }

        private bool ApplyEnabledState(bool enabled)
        {
            if (enabled && _hotkeyWindow != null && !_hotkeyWindow.IsRegistered)
            {
                if (!_hotkeyWindow.TryRegister(_settings.PauseHotkey))
                {
                    _settings.Enabled = false;
                    _lastAppliedEnabled = false;
                    if (_toggleItem != null) _toggleItem.Checked = false;
                    ReportRuntimeError(
                        "无法启用防抖",
                        new InvalidOperationException(
                            "暂停热键未注册：" + _settings.PauseHotkey + "。"
                            + _hotkeyWindow.RegistrationError));
                    return false;
                }
                _settings.PauseHotkey = _hotkeyWindow.RegisteredHotkeyText;
            }

            if (_hook != null && (_lastAppliedEnabled != enabled || _settings.Enabled != enabled))
            {
                _hook.ResetRuntimeState();
            }
            _settings.Enabled = enabled;
            _lastAppliedEnabled = enabled;
            return true;
        }

        private void QueueDecision(DebounceDecision decision, int virtualKeyCode)
        {
            _pendingDecision = decision;
            _pendingVirtualKeyCode = virtualKeyCode;
            if (decision != null && decision.LearningStateChanged)
            {
                _learningSaveSlot.MarkDirty(
                    Environment.TickCount64,
                    decision.LearningAdjustmentMs != 0);
            }
        }

        private void FlushPendingWork()
        {
            DebounceDecision decision = _pendingDecision;
            if (decision != null)
            {
                _pendingDecision = null;
                _lastStatus = DateTime.Now.ToString("HH:mm:ss.fff")
                    + " VK " + _pendingVirtualKeyCode
                    + " " + decision.Reason
                    + " interval=" + decision.IntervalMs
                    + "ms threshold=" + decision.EffectiveThresholdMs
                    + "ms"
                    + " learn=" + FormatLearningAdjustment(decision.LearningAdjustmentMs)
                    + (String.IsNullOrEmpty(decision.LearningReason) ? "" : " " + decision.LearningReason)
                    + (decision.Suppress ? " suppressed" : " accepted");
                RequestSettingsRuntimeRefresh();
            }

            ObserveCompletedSaves();
            try
            {
                StartPendingSaves();
            }
            catch (Exception error)
            {
                ReportRuntimeError("状态保存任务启动失败", error);
            }

            Exception hookError = _hook.TakeLastError();
            if (hookError != null)
            {
                ReportRuntimeError("键盘钩子运行失败", hookError);
            }
        }

        private void ReportRuntimeError(string operation, Exception error)
        {
            _lastStatus = operation + "：" + DescribeException(error);
            ShowErrorBalloon(_lastStatus);
            RequestSettingsRuntimeRefresh();
        }

        private void ShowErrorBalloon(string message)
        {
            try
            {
                _notifyIcon.ShowBalloonTip(
                    5000,
                    AppName,
                    message,
                    ToolTipIcon.Error);
            }
            catch
            {
                // The status remains visible in the main window even if Explorer rejects the balloon.
            }
        }

        private void RunInteractive(string operation, Action action)
        {
            try
            {
                action();
            }
            catch (Exception error)
            {
                ReportRuntimeError(operation, error);
            }
        }

        private string GetLastStatus()
        {
            return _saveErrorStatus.GetVisibleStatus(_lastStatus);
        }

        private static string FormatLearningAdjustment(int adjustment)
        {
            if (adjustment > 0) return "+" + adjustment + "ms";
            if (adjustment < 0) return adjustment + "ms";
            return "0ms";
        }

        private void ShowMainWindow()
        {
            RequestShowSettingsWindow();
        }

        private void SaveSettings()
        {
            _gameProcessConfigurationVersion++;
            var applyErrors = new List<Exception>();
            CaptureError(applyErrors, ReconcileCachedGameProcessState);
            CaptureError(applyErrors, ApplyStartupSetting);
            CaptureError(applyErrors, ApplyPauseHotkeySetting);
            CaptureError(applyErrors, delegate { ApplyEnabledState(_settings.Enabled); });
            CaptureError(applyErrors, BeginGameProcessCheck);
            if (applyErrors.Count > 0)
            {
                ReportRuntimeError(
                    "部分设置未能应用；失败项已恢复到实际状态",
                    new AggregateException(applyErrors));
            }

            try
            {
                _settingsSaveSlot.MarkDirty(Environment.TickCount64, true);
                StartPendingSaves();
            }
            catch (Exception error)
            {
                ReportRuntimeError("设置保存失败；当前会话状态仍然生效", error);
            }

            RequestSettingsDataRefresh();
        }

        private void ReconcileCachedGameProcessState()
        {
            GameModeRuntimeSnapshot next =
                GameProcessConfigurationState.ReconcileConfiguration(
                    GetGameModeRuntimeSnapshot(),
                    _settings.ProcessGameModeEnabled,
                    _settings.GameProcesses);
            ApplyGameModeRuntimeSnapshot(next, true);
        }

        private void ApplyStartupSetting()
        {
            if (_settings.StartWithWindows == _lastAppliedStartWithWindows) return;

            bool requested = _settings.StartWithWindows;
            try
            {
                StartupManager.SetEnabled(requested);
                _lastAppliedStartWithWindows = requested;
            }
            catch
            {
                _settings.StartWithWindows = _lastAppliedStartWithWindows;
                throw;
            }
        }

        private void ApplyPauseHotkeySetting()
        {
            if (_hotkeyWindow == null) return;
            if (_hotkeyWindow.Rebind(_settings.PauseHotkey))
            {
                _settings.PauseHotkey = _hotkeyWindow.RegisteredHotkeyText;
                return;
            }

            string requested = _settings.PauseHotkey;
            if (_hotkeyWindow.IsRegistered)
            {
                _settings.PauseHotkey = _hotkeyWindow.RegisteredHotkeyText;
            }
            else
            {
                _settings.Enabled = false;
                _lastAppliedEnabled = false;
                if (_toggleItem != null) _toggleItem.Checked = false;
            }
            throw new InvalidOperationException(
                "暂停热键未能应用：" + requested + "。"
                + _hotkeyWindow.RegistrationError);
        }

        private void SaveSettingsSnapshot(AppSettings snapshot)
        {
            _store.SaveSettings(snapshot);
        }

        private void SaveLearningStateInteractive()
        {
            try
            {
                _learningSaveSlot.MarkDirty(Environment.TickCount64, true);
                StartPendingSaves();
            }
            catch (Exception error)
            {
                ReportRuntimeError("学习状态保存失败；当前会话状态仍然生效", error);
            }

            RequestSettingsRuntimeRefresh();
        }

        private void SaveLearningStateSnapshot(
            LearningState snapshot,
            int fallbackThreshold)
        {
            _store.SaveLearningState(snapshot, fallbackThreshold);
        }

        private void StartPendingSaves()
        {
            long nowMs = Environment.TickCount64;
            SaveTaskResult settingsResult = _settingsSaveSlot.TryStart(
                nowMs,
                delegate
                {
                    AppSettings snapshot = CloneSettings(_settings);
                    return Task.Run(delegate { SaveSettingsSnapshot(snapshot); });
                });
            HandleSaveTaskResult(settingsResult, true);

            SaveTaskResult learningResult = _learningSaveSlot.TryStart(
                nowMs,
                delegate
                {
                    LearningState snapshot = CloneLearningState(_learning);
                    int fallbackThreshold = _settings.DefaultThresholdMs;
                    return Task.Run(
                        delegate { SaveLearningStateSnapshot(snapshot, fallbackThreshold); });
                });
            HandleSaveTaskResult(learningResult, false);
        }

        private void ObserveCompletedSaves()
        {
            long nowMs = Environment.TickCount64;
            HandleSaveTaskResult(_settingsSaveSlot.Observe(nowMs), true);
            HandleSaveTaskResult(_learningSaveSlot.Observe(nowMs), false);
        }

        private void HandleSaveTaskResult(SaveTaskResult result, bool settings)
        {
            if (result == null) return;
            bool statusChanged = false;
            if (result.Kind == SaveTaskResultKind.Failed)
            {
                string operation = settings ? "设置保存失败" : "学习状态保存失败";
                string message = operation + "：" + DescribeException(result.Error);
                statusChanged = _saveErrorStatus.SetError(settings, message);
                ShowErrorBalloon(message);
            }
            else if (result.Kind == SaveTaskResultKind.Succeeded)
            {
                statusChanged = _saveErrorStatus.ClearError(settings);
            }

            if (statusChanged)
            {
                RequestSettingsRuntimeRefresh();
            }
        }

        private void WaitForBackgroundSaves()
        {
            var errors = new List<Exception>();
            long nowMs = Environment.TickCount64;
            SaveTaskResult settingsResult = _settingsSaveSlot.Wait(nowMs);
            HandleSaveTaskResult(settingsResult, true);
            if (settingsResult.Kind == SaveTaskResultKind.Failed)
            {
                errors.Add(settingsResult.Error);
            }
            SaveTaskResult learningResult = _learningSaveSlot.Wait(nowMs);
            HandleSaveTaskResult(learningResult, false);
            if (learningResult.Kind == SaveTaskResultKind.Failed)
            {
                errors.Add(learningResult.Error);
            }
            if (errors.Count > 0)
            {
                throw new AggregateException("一个或多个后台保存操作失败。", errors);
            }
        }

        private void UpdateTrayText()
        {
            string status = UiContext.GetCurrentGameModeStatus();
            if (_notifyIcon != null)
            {
                _notifyIcon.Text = BuildTrayTooltip(status);
            }
            if (_toggleItem != null)
            {
                _toggleItem.Checked = _settings.Enabled;
            }
            if (_gameModeItem != null)
            {
                _gameModeItem.Text = status;
            }
        }

        private static string BuildTrayTooltip(string status)
        {
            string text = AppName + " - " + (status ?? "");
            return text.Length <= 63 ? text : text.Substring(0, 63);
        }

        protected override void ExitThreadCore()
        {
            if (_exiting) return;
            _exiting = true;
            var errors = new List<Exception>();

            UiContext.RefreshRequested = null;
            UiContext.NavigateRequested = null;

            CaptureError(errors, delegate
            {
                if (_decisionTimer != null)
                {
                    _decisionTimer.Stop();
                    _decisionTimer.Dispose();
                }
            });
            CaptureError(errors, delegate
            {
                if (_startupTimer != null)
                {
                    _startupTimer.Stop();
                    _startupTimer.Dispose();
                }
            });
            CaptureError(errors, delegate
            {
                if (_gameProcessTimer != null)
                {
                    _gameProcessTimer.Stop();
                    _gameProcessTimer.Dispose();
                }
            });
            CaptureError(errors, delegate { if (_hook != null) _hook.Dispose(); });
            CaptureError(errors, delegate
            {
                if (_hook == null) return;
                Exception hookError = _hook.TakeLastError();
                if (hookError != null) throw hookError;
            });
            CaptureError(errors, delegate { if (_hotkeyWindow != null) _hotkeyWindow.Dispose(); });
            CaptureError(errors, WaitForBackgroundSaves);
            CaptureError(errors, delegate { StartupManager.SetEnabled(_settings.StartWithWindows); });
            CaptureError(errors, delegate { _store.SaveSettings(_settings); });
            CaptureError(errors, delegate
            {
                _store.SaveLearningState(_learning, _settings.DefaultThresholdMs);
            });
            CaptureError(errors, delegate
            {
                if (_notifyIcon != null)
                {
                    _notifyIcon.Visible = false;
                    if (_notifyIcon.ContextMenuStrip != null)
                    {
                        _notifyIcon.ContextMenuStrip.Dispose();
                    }
                    _notifyIcon.Dispose();
                }
            });
            CaptureError(errors, delegate
            {
                if (_trayIcon != null) _trayIcon.Dispose();
            });

            if (errors.Count > 0)
            {
                try
                {
                    MessageBox.Show(
                        BuildErrorMessage("退出清理未完全成功", errors),
                        AppName,
                        MessageBoxButtons.OK,
                        MessageBoxIcon.Error);
                }
                catch
                {
                }
            }
            base.ExitThreadCore();
        }

        internal void CloseForExit()
        {
            ExitThreadCore();
        }

        private static void CaptureError(List<Exception> errors, Action action)
        {
            try
            {
                action();
            }
            catch (Exception error)
            {
                errors.Add(error);
            }
        }

        private void RequestShowSettingsWindow()
        {
            Action callback = ShowSettingsWindowRequested;
            if (callback != null)
            {
                callback();
            }
        }

        private void RequestSettingsDataRefresh()
        {
            Action callback = SettingsDataRefreshRequested;
            if (callback != null)
            {
                callback();
            }
        }

        private void RequestSettingsRuntimeRefresh()
        {
            UiContext.NotifyRuntimeStateChanged();
        }

        private void RequestExit()
        {
            Action callback = ExitRequested;
            if (callback != null)
            {
                callback();
            }
        }

        private static string BuildLoadIssueMessage(IReadOnlyList<SettingsLoadIssue> issues)
        {
            if (issues == null || issues.Count == 0) return null;
            var text = new StringBuilder();
            text.AppendLine("检测到配置恢复事件：");
            foreach (SettingsLoadIssue issue in issues)
            {
                text.AppendLine();
                text.AppendLine("源文件：" + issue.SourcePath);
                if (!String.IsNullOrEmpty(issue.QuarantinedPath))
                {
                    text.AppendLine("隔离副本：" + issue.QuarantinedPath);
                }
                text.AppendLine("错误：" + DescribeException(issue.Error));
                if (issue.RecoveredFromBackup)
                {
                    text.AppendLine("结果：已从上一次有效备份恢复。");
                }
                else if (String.Equals(
                    System.IO.Path.GetFileName(issue.SourcePath),
                    "learning-state.json",
                    StringComparison.OrdinalIgnoreCase)
                    || System.IO.Path.GetFileName(issue.SourcePath).StartsWith(
                        "learning-state.json.",
                        StringComparison.OrdinalIgnoreCase))
                {
                    text.AppendLine("结果：未找到有效备份；学习状态已重置为空。");
                }
                else
                {
                    text.AppendLine("结果：未找到有效备份；防抖已保持安全停用。");
                }
            }
            return text.ToString().TrimEnd();
        }

        private static string AppendWarning(string current, string next)
        {
            if (String.IsNullOrEmpty(current)) return next;
            return current + Environment.NewLine + Environment.NewLine + next;
        }

        private static string BuildErrorMessage(string heading, IReadOnlyList<Exception> errors)
        {
            var text = new StringBuilder(heading);
            foreach (Exception error in errors)
            {
                text.AppendLine();
                text.Append(DescribeException(error));
            }
            return text.ToString();
        }

        private static AppSettings CloneSettings(AppSettings source)
        {
            return new AppSettings
            {
                Enabled = source.Enabled,
                StartWithWindows = source.StartWithWindows,
                SilentRun = source.SilentRun,
                GlobalSensitivity = source.GlobalSensitivity,
                DefaultThresholdMs = source.DefaultThresholdMs,
                LongHoldBypassMs = source.LongHoldBypassMs,
                StartupDelayMs = source.StartupDelayMs,
                PauseHotkey = source.PauseHotkey,
                ProcessGameModeEnabled = source.ProcessGameModeEnabled,
                GameProcesses = source.GameProcesses == null
                    ? new List<string>()
                    : new List<string>(source.GameProcesses),
                GameModeThresholdMs = source.GameModeThresholdMs,
                GameModeLongHoldBypassMs = source.GameModeLongHoldBypassMs,
                GameModeFilteredKeys = source.GameModeFilteredKeys == null
                    ? new List<int>()
                    : new List<int>(source.GameModeFilteredKeys),
                IgnoredKeys = source.IgnoredKeys == null
                    ? new List<int>()
                    : new List<int>(source.IgnoredKeys)
            };
        }

        private static LearningState CloneLearningState(LearningState source)
        {
            var clone = new LearningState
            {
                SchemaVersion = source.SchemaVersion
            };
            foreach (var pair in source.Keys)
            {
                KeyLearningState value = pair.Value;
                clone.Keys[pair.Key] = value == null
                    ? null
                    : new KeyLearningState
                    {
                        ThresholdMs = value.ThresholdMs,
                        AcceptedCount = value.AcceptedCount,
                        SuppressedCount = value.SuppressedCount,
                        LastIntervalMs = value.LastIntervalMs,
                        LastSeenUtc = value.LastSeenUtc,
                        LastAdjustmentMs = value.LastAdjustmentMs,
                        LastAdjustmentReason = value.LastAdjustmentReason,
                        LastAdjustedUtc = value.LastAdjustedUtc
                    };
            }
            return clone;
        }

        private static string DescribeException(Exception error)
        {
            var aggregate = error as AggregateException;
            if (aggregate == null)
            {
                return error.GetType().Name + " - " + error.Message;
            }

            var text = new StringBuilder();
            foreach (Exception inner in aggregate.Flatten().InnerExceptions)
            {
                if (text.Length > 0) text.Append(" | ");
                text.Append(inner.GetType().Name).Append(" - ").Append(inner.Message);
            }
            return text.ToString();
        }
    }
}
