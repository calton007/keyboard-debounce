using System;
using System.Diagnostics;
using System.IO;
using System.Threading;

namespace KeyboardDebounce
{
    [Flags]
    internal enum SettingsUiChangeKind
    {
        None = 0,
        Settings = 1,
        Learning = 2,
        Runtime = 4,
        Navigation = 8
    }

    internal sealed class SettingsUiStateChangedEventArgs : EventArgs
    {
        public SettingsUiStateChangedEventArgs(
            SettingsUiChangeKind changeKind,
            int settingsVersion,
            int learningVersion,
            int runtimeVersion)
        {
            ChangeKind = changeKind;
            SettingsVersion = settingsVersion;
            LearningVersion = learningVersion;
            RuntimeVersion = runtimeVersion;
        }

        public SettingsUiChangeKind ChangeKind { get; private set; }
        public int SettingsVersion { get; private set; }
        public int LearningVersion { get; private set; }
        public int RuntimeVersion { get; private set; }
    }

    internal sealed class SettingsUiContext
    {
        private int _settingsVersion;
        private int _learningVersion;
        private int _runtimeVersion;
        private readonly Func<GameModeRuntimeSnapshot> _getGameModeRuntimeSnapshot;
        private readonly Action _requestGameModeRuntimeRefresh;

        public SettingsUiContext(
            AppSettings settings,
            LearningState learning,
            DebounceEngine engine,
            SettingsStore store,
            Action saveSettings,
            Action saveLearning,
            Func<string> getStatus,
            Func<string> getGameModeStatus)
            : this(
                settings,
                learning,
                engine,
                store,
                saveSettings,
                saveLearning,
                getStatus,
                getGameModeStatus,
                null,
                null)
        {
        }

        public SettingsUiContext(
            AppSettings settings,
            LearningState learning,
            DebounceEngine engine,
            SettingsStore store,
            Action saveSettings,
            Action saveLearning,
            Func<string> getStatus,
            Func<GameModeRuntimeSnapshot> getGameModeRuntimeSnapshot,
            Action requestGameModeRuntimeRefresh)
            : this(
                settings,
                learning,
                engine,
                store,
                saveSettings,
                saveLearning,
                getStatus,
                null,
                getGameModeRuntimeSnapshot,
                requestGameModeRuntimeRefresh)
        {
        }

        private SettingsUiContext(
            AppSettings settings,
            LearningState learning,
            DebounceEngine engine,
            SettingsStore store,
            Action saveSettings,
            Action saveLearning,
            Func<string> getStatus,
            Func<string> getGameModeStatus,
            Func<GameModeRuntimeSnapshot> getGameModeRuntimeSnapshot,
            Action requestGameModeRuntimeRefresh)
        {
            if (settings == null) throw new ArgumentNullException("settings");
            if (learning == null) throw new ArgumentNullException("learning");
            if (engine == null) throw new ArgumentNullException("engine");
            if (store == null) throw new ArgumentNullException("store");

            Settings = settings;
            Learning = learning;
            Engine = engine;
            Store = store;
            SaveSettings = saveSettings;
            SaveLearning = saveLearning;
            GetStatus = getStatus;
            GetGameModeStatus = getGameModeStatus;
            _getGameModeRuntimeSnapshot = getGameModeRuntimeSnapshot;
            _requestGameModeRuntimeRefresh = requestGameModeRuntimeRefresh;
            _settingsVersion = 1;
            _learningVersion = 1;
            _runtimeVersion = 1;
        }

        public AppSettings Settings { get; private set; }
        public LearningState Learning { get; private set; }
        public DebounceEngine Engine { get; private set; }
        public SettingsStore Store { get; private set; }
        public Action SaveSettings { get; private set; }
        public Action SaveLearning { get; private set; }
        public Func<string> GetStatus { get; private set; }
        public Func<string> GetGameModeStatus { get; private set; }
        public Action<SettingsPageId> NavigateRequested { get; set; }
        public Action RefreshRequested { get; set; }
        public event EventHandler<SettingsUiStateChangedEventArgs> StateChanged;

        public int SettingsVersion
        {
            get { return Volatile.Read(ref _settingsVersion); }
        }

        public int LearningVersion
        {
            get { return Volatile.Read(ref _learningVersion); }
        }

        public int RuntimeVersion
        {
            get { return Volatile.Read(ref _runtimeVersion); }
        }

        public void SaveSettingsAndRefresh()
        {
            if (SaveSettings != null) SaveSettings();
            Interlocked.Increment(ref _settingsVersion);
            Interlocked.Increment(ref _runtimeVersion);
            RaiseStateChanged(SettingsUiChangeKind.Settings | SettingsUiChangeKind.Runtime);
            if (RefreshRequested != null) RefreshRequested();
        }

        public void SaveLearningAndRefresh()
        {
            if (SaveLearning != null) SaveLearning();
            Interlocked.Increment(ref _learningVersion);
            Interlocked.Increment(ref _runtimeVersion);
            RaiseStateChanged(SettingsUiChangeKind.Learning | SettingsUiChangeKind.Runtime);
            if (RefreshRequested != null) RefreshRequested();
        }

        public void SaveAllAndRefresh()
        {
            if (SaveSettings != null) SaveSettings();
            if (SaveLearning != null) SaveLearning();
            Interlocked.Increment(ref _settingsVersion);
            Interlocked.Increment(ref _learningVersion);
            Interlocked.Increment(ref _runtimeVersion);
            RaiseStateChanged(
                SettingsUiChangeKind.Settings
                | SettingsUiChangeKind.Learning
                | SettingsUiChangeKind.Runtime);
            if (RefreshRequested != null) RefreshRequested();
        }

        public string GetCurrentStatus()
        {
            return GetStatus == null ? "" : GetStatus();
        }

        public string GetCurrentGameModeStatus()
        {
            if (GetGameModeStatus != null) return GetGameModeStatus();
            if (!Settings.ProcessGameModeEnabled) return "自动切换已关闭";
            if (Settings.GameProcesses == null || Settings.GameProcesses.Count == 0)
            {
                return "请先添加游戏 EXE";
            }

            GameModeRuntimeSnapshot snapshot = GetCurrentGameModeSnapshot();
            if (snapshot.DetectionHealth != GameModeDetectionHealth.Healthy)
            {
                string mode = snapshot.IsActive ? "游戏模式" : "普通模式";
                string error = String.IsNullOrWhiteSpace(snapshot.ErrorMessage)
                    ? "进程状态不可用"
                    : snapshot.ErrorMessage.Trim();
                return "检测状态未知，继续保持" + mode + " · " + error;
            }

            return snapshot.IsActive
                ? "游戏模式已开启 · "
                    + GameProcessDetector.FormatExecutableName(snapshot.ActiveExecutable)
                    + " 正在运行"
                : "自动检测已开启，等待游戏启动";
        }

        public GameModeRuntimeSnapshot GetCurrentGameModeSnapshot()
        {
            GameModeRuntimeSnapshot snapshot = _getGameModeRuntimeSnapshot == null
                ? null
                : _getGameModeRuntimeSnapshot();
            return snapshot ?? GameModeRuntimeSnapshot.Inactive;
        }

        public void RequestGameModeRuntimeRefresh()
        {
            if (_requestGameModeRuntimeRefresh != null)
            {
                _requestGameModeRuntimeRefresh();
            }
        }

        public void RequestNavigation(SettingsPageId pageId)
        {
            RaiseStateChanged(SettingsUiChangeKind.Navigation);
            if (NavigateRequested != null) NavigateRequested(pageId);
        }

        public void NotifyRuntimeStateChanged()
        {
            Interlocked.Increment(ref _runtimeVersion);
            RaiseStateChanged(SettingsUiChangeKind.Runtime);
        }

        public void NotifyExternalStateChanged(
            bool settingsChanged,
            bool learningChanged,
            bool runtimeChanged)
        {
            SettingsUiChangeKind changeKind = SettingsUiChangeKind.None;
            if (settingsChanged)
            {
                Interlocked.Increment(ref _settingsVersion);
                changeKind |= SettingsUiChangeKind.Settings;
            }
            if (learningChanged)
            {
                Interlocked.Increment(ref _learningVersion);
                changeKind |= SettingsUiChangeKind.Learning;
            }
            if (runtimeChanged)
            {
                Interlocked.Increment(ref _runtimeVersion);
                changeKind |= SettingsUiChangeKind.Runtime;
            }
            if (changeKind != SettingsUiChangeKind.None)
            {
                RaiseStateChanged(changeKind);
            }
        }

        public string GetSettingsDirectory()
        {
            return Path.GetDirectoryName(Store.SettingsPath) ?? "";
        }

        public void OpenSettingsDirectory()
        {
            string directory = GetSettingsDirectory();
            if (directory.Length == 0 || !Directory.Exists(directory))
            {
                throw new DirectoryNotFoundException("配置目录不存在：" + directory);
            }

            Process.Start(new ProcessStartInfo
            {
                FileName = directory,
                UseShellExecute = true
            });
        }

        private void RaiseStateChanged(SettingsUiChangeKind changeKind)
        {
            EventHandler<SettingsUiStateChangedEventArgs> handler = StateChanged;
            if (handler == null) return;

            handler(
                this,
                new SettingsUiStateChangedEventArgs(
                    changeKind,
                    SettingsVersion,
                    LearningVersion,
                    RuntimeVersion));
        }
    }
}
