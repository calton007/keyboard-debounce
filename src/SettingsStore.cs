using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Runtime.Serialization;
using System.Runtime.Serialization.Json;
using System.Text;

namespace KeyboardDebounce
{
    public sealed class AppData
    {
        public AppData(AppSettings settings, LearningState learning)
        {
            Settings = settings ?? new AppSettings();
            Learning = learning ?? new LearningState();
        }

        public AppSettings Settings { get; private set; }
        public LearningState Learning { get; private set; }
    }

    public sealed class SettingsLoadIssue
    {
        internal SettingsLoadIssue(
            string sourcePath,
            string quarantinedPath,
            Exception error,
            bool recoveredFromBackup)
        {
            SourcePath = sourcePath;
            QuarantinedPath = quarantinedPath;
            Error = error;
            RecoveredFromBackup = recoveredFromBackup;
        }

        public string SourcePath { get; private set; }
        public string QuarantinedPath { get; private set; }
        public Exception Error { get; private set; }
        public bool RecoveredFromBackup { get; private set; }
    }

    internal sealed class LearningStateMigrationStatus
    {
        public LearningStateMigrationStatus(
            bool wasApplied,
            bool isSavePending,
            Exception error)
        {
            WasApplied = wasApplied;
            IsSavePending = isSavePending;
            Error = error;
        }

        public bool WasApplied { get; private set; }
        public bool IsSavePending { get; private set; }
        public Exception Error { get; private set; }
    }

    public sealed class SettingsStore
    {
        private const string PreV2LearningStateFileName = "learning-state.pre-v2.json";
        private readonly string _settingsPath;
        private readonly string _learningStatePath;
        private readonly List<SettingsLoadIssue> _loadIssues;
        private byte[] _learningMigrationBackupBytes;
        private string _learningMigrationBackupSourcePath;
        private volatile LearningStateMigrationStatus _learningMigrationStatus;

        public SettingsStore()
            : this(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "KeyboardDebounce"))
        {
        }

        public SettingsStore(string dir)
        {
            if (String.IsNullOrWhiteSpace(dir)) throw new ArgumentException("Directory path is required.", "dir");
            Directory.CreateDirectory(dir);
            _settingsPath = Path.Combine(dir, "settings.json");
            _learningStatePath = Path.Combine(dir, "learning-state.json");
            _loadIssues = new List<SettingsLoadIssue>();
            _learningMigrationBackupSourcePath = _learningStatePath;
            _learningMigrationStatus = NewLearningMigrationStatus(false, false, null);
        }

        public string SettingsPath
        {
            get { return _settingsPath; }
        }

        public string LearningStatePath
        {
            get { return _learningStatePath; }
        }

        public IReadOnlyList<SettingsLoadIssue> LoadIssues
        {
            get { return _loadIssues.AsReadOnly(); }
        }

        internal LearningStateMigrationStatus LearningMigrationStatus
        {
            get { return _learningMigrationStatus; }
        }

        public AppData Load()
        {
            _loadIssues.Clear();
            _learningMigrationBackupBytes = null;
            _learningMigrationBackupSourcePath = _learningStatePath;
            _learningMigrationStatus = NewLearningMigrationStatus(false, false, null);
            AppSettings settings = LoadSettings();
            LearningState learning = LoadLearningState(settings.DefaultThresholdMs);

            settings.Normalize();
            learning.Normalize(settings.DefaultThresholdMs);
            MigrateLearningStateIfNeeded(learning, settings.DefaultThresholdMs);
            return new AppData(settings, learning);
        }

        public void SaveSettings(AppSettings settings)
        {
            if (settings == null) throw new ArgumentNullException("settings");
            settings.Normalize();
            WriteJson(_settingsPath, settings, typeof(AppSettings));
        }

        public void SaveLearningState(LearningState learning, int fallbackThreshold)
        {
            if (learning == null) throw new ArgumentNullException("learning");
            learning.Normalize(fallbackThreshold);
            if (learning.SchemaVersion < LearningState.CurrentSchemaVersion)
            {
                learning.SchemaVersion = LearningState.CurrentSchemaVersion;
            }

            LearningStateMigrationStatus migration = _learningMigrationStatus;
            try
            {
                if (migration.WasApplied && migration.IsSavePending)
                {
                    EnsurePreV2LearningStateBackup();
                }

                WriteJson(_learningStatePath, learning, typeof(LearningState));
                if (migration.WasApplied)
                {
                    _learningMigrationBackupBytes = null;
                    _learningMigrationStatus = NewLearningMigrationStatus(true, false, null);
                }
            }
            catch (Exception error)
            {
                if (migration.WasApplied && migration.IsSavePending)
                {
                    _learningMigrationStatus = NewLearningMigrationStatus(true, true, error);
                }
                throw;
            }
        }

        public void Save(AppSettings settings, LearningState learning)
        {
            SaveSettings(settings);
            SaveLearningState(learning, settings == null ? 90 : settings.DefaultThresholdMs);
        }

        public string ReadRawForPrivacyCheck()
        {
            if (!File.Exists(_settingsPath)) return String.Empty;
            return File.ReadAllText(_settingsPath, Encoding.UTF8);
        }

        public string ReadRawLearningStateForPrivacyCheck()
        {
            if (!File.Exists(_learningStatePath)) return String.Empty;
            return File.ReadAllText(_learningStatePath, Encoding.UTF8);
        }

        private AppSettings LoadSettings()
        {
            if (!File.Exists(_settingsPath))
            {
                string backupPath = GetBackupPath(_settingsPath);
                if (File.Exists(backupPath))
                {
                    return RecoverMissingSettingsFromBackup(backupPath);
                }
                return new AppSettings();
            }

            try
            {
                return ReadSettings(_settingsPath);
            }
            catch (SerializationException error)
            {
                return RecoverSettings(error);
            }
        }

        private LearningState LoadLearningState(int fallbackThreshold)
        {
            if (!File.Exists(_learningStatePath))
            {
                string backupPath = GetBackupPath(_learningStatePath);
                if (File.Exists(backupPath))
                {
                    return RecoverMissingLearningStateFromBackup(backupPath, fallbackThreshold);
                }
                return LoadLegacyLearningState(fallbackThreshold);
            }

            try
            {
                return ReadLearningState(_learningStatePath, fallbackThreshold);
            }
            catch (SerializationException error)
            {
                return RecoverLearningState(error, fallbackThreshold);
            }
        }

        private AppSettings ReadSettings(string path)
        {
            var settings = (AppSettings)ReadJson(path, typeof(AppSettings));
            if (settings == null)
            {
                throw new SerializationException("The settings file did not contain a settings object.");
            }

            settings.Normalize();
            return settings;
        }

        private LearningState ReadLearningState(string path, int fallbackThreshold)
        {
            var learning = (LearningState)ReadJson(path, typeof(LearningState));
            if (learning == null)
            {
                throw new SerializationException("The learning-state file did not contain a state object.");
            }

            learning.Normalize(fallbackThreshold);
            return learning;
        }

        private AppSettings RecoverSettings(SerializationException error)
        {
            string quarantinedPath = Quarantine(_settingsPath);
            string backupPath = GetBackupPath(_settingsPath);

            if (File.Exists(backupPath))
            {
                try
                {
                    AppSettings recovered = ReadSettings(backupPath);
                    WriteJson(_settingsPath, recovered, typeof(AppSettings));
                    File.Copy(_settingsPath, backupPath, true);
                    _loadIssues.Add(new SettingsLoadIssue(_settingsPath, quarantinedPath, error, true));
                    return recovered;
                }
                catch (SerializationException backupError)
                {
                    string quarantinedBackupPath = Quarantine(backupPath);
                    _loadIssues.Add(new SettingsLoadIssue(_settingsPath, quarantinedPath, error, false));
                    _loadIssues.Add(new SettingsLoadIssue(backupPath, quarantinedBackupPath, backupError, false));
                }
            }
            else
            {
                _loadIssues.Add(new SettingsLoadIssue(_settingsPath, quarantinedPath, error, false));
            }

            var safeSettings = new AppSettings { Enabled = false };
            safeSettings.Normalize();
            WriteJson(_settingsPath, safeSettings, typeof(AppSettings));
            File.Copy(_settingsPath, backupPath, true);
            return safeSettings;
        }

        private AppSettings RecoverMissingSettingsFromBackup(string backupPath)
        {
            var missingError = new FileNotFoundException(
                "The settings file was missing; recovery from backup was attempted.",
                _settingsPath);
            try
            {
                AppSettings recovered = ReadSettings(backupPath);
                WriteJson(_settingsPath, recovered, typeof(AppSettings));
                _loadIssues.Add(new SettingsLoadIssue(_settingsPath, null, missingError, true));
                return recovered;
            }
            catch (SerializationException backupError)
            {
                string quarantinedBackupPath = Quarantine(backupPath);
                _loadIssues.Add(new SettingsLoadIssue(
                    backupPath,
                    quarantinedBackupPath,
                    backupError,
                    false));

                var safeSettings = new AppSettings { Enabled = false };
                safeSettings.Normalize();
                WriteJson(_settingsPath, safeSettings, typeof(AppSettings));
                File.Copy(_settingsPath, backupPath, true);
                return safeSettings;
            }
        }

        private LearningState RecoverLearningState(SerializationException error, int fallbackThreshold)
        {
            string quarantinedPath = Quarantine(_learningStatePath);
            string backupPath = GetBackupPath(_learningStatePath);

            if (File.Exists(backupPath))
            {
                try
                {
                    LearningState recovered = ReadLearningState(backupPath, fallbackThreshold);
                    WriteJson(_learningStatePath, recovered, typeof(LearningState));
                    File.Copy(_learningStatePath, backupPath, true);
                    _loadIssues.Add(new SettingsLoadIssue(_learningStatePath, quarantinedPath, error, true));
                    return recovered;
                }
                catch (SerializationException backupError)
                {
                    string quarantinedBackupPath = Quarantine(backupPath);
                    _loadIssues.Add(new SettingsLoadIssue(_learningStatePath, quarantinedPath, error, false));
                    _loadIssues.Add(new SettingsLoadIssue(backupPath, quarantinedBackupPath, backupError, false));
                }
            }
            else
            {
                _loadIssues.Add(new SettingsLoadIssue(_learningStatePath, quarantinedPath, error, false));
            }

            var safeLearning = new LearningState();
            WriteJson(_learningStatePath, safeLearning, typeof(LearningState));
            File.Copy(_learningStatePath, backupPath, true);
            return safeLearning;
        }

        private LearningState RecoverMissingLearningStateFromBackup(
            string backupPath,
            int fallbackThreshold)
        {
            var missingError = new FileNotFoundException(
                "The learning-state file was missing; recovery from backup was attempted.",
                _learningStatePath);
            try
            {
                LearningState recovered = ReadLearningState(backupPath, fallbackThreshold);
                WriteJson(_learningStatePath, recovered, typeof(LearningState));
                _loadIssues.Add(new SettingsLoadIssue(
                    _learningStatePath,
                    null,
                    missingError,
                    true));
                return recovered;
            }
            catch (SerializationException backupError)
            {
                string quarantinedBackupPath = Quarantine(backupPath);
                _loadIssues.Add(new SettingsLoadIssue(
                    backupPath,
                    quarantinedBackupPath,
                    backupError,
                    false));

                var safeLearning = new LearningState();
                WriteJson(_learningStatePath, safeLearning, typeof(LearningState));
                File.Copy(_learningStatePath, backupPath, true);
                return safeLearning;
            }
        }

        private LearningState LoadLegacyLearningState(int fallbackThreshold)
        {
            if (!File.Exists(_settingsPath)) return new LearningState();

            var legacy = (LegacyAppSettings)ReadJson(_settingsPath, typeof(LegacyAppSettings));
            if (legacy == null || legacy.Keys == null || legacy.Keys.Count == 0)
            {
                return new LearningState();
            }

            _learningMigrationBackupSourcePath = _settingsPath;
            var learning = new LearningState
            {
                SchemaVersion = 0,
                Keys = legacy.Keys
            };
            learning.Normalize(fallbackThreshold);
            return learning;
        }

        private void MigrateLearningStateIfNeeded(
            LearningState learning,
            int fallbackThreshold)
        {
            if (learning.SchemaVersion >= LearningState.CurrentSchemaVersion)
            {
                return;
            }

            DateTime migratedUtc = DateTime.UtcNow;
            foreach (KeyValuePair<int, KeyLearningState> pair in learning.Keys)
            {
                KeyLearningState keyState = pair.Value;
                if (keyState == null || keyState.ThresholdMs <= fallbackThreshold)
                {
                    continue;
                }

                int previousThreshold = keyState.ThresholdMs;
                keyState.ThresholdMs = fallbackThreshold;
                keyState.LastAdjustmentMs = fallbackThreshold - previousThreshold;
                keyState.LastAdjustmentReason = "schema-v2-high-threshold-reset";
                keyState.LastAdjustedUtc = migratedUtc;
            }

            learning.SchemaVersion = LearningState.CurrentSchemaVersion;
            _learningMigrationStatus = NewLearningMigrationStatus(true, true, null);
            try
            {
                SaveLearningState(learning, fallbackThreshold);
            }
            catch (Exception error)
            {
                _learningMigrationStatus = NewLearningMigrationStatus(true, true, error);
            }
        }

        private void EnsurePreV2LearningStateBackup()
        {
            string directory = Path.GetDirectoryName(_learningStatePath);
            string backupPath = Path.Combine(directory, PreV2LearningStateFileName);
            if (File.Exists(backupPath))
            {
                return;
            }

            byte[] backupBytes = _learningMigrationBackupBytes;
            if (backupBytes == null)
            {
                backupBytes = File.ReadAllBytes(_learningMigrationBackupSourcePath);
                _learningMigrationBackupBytes = backupBytes;
            }

            string tempPath = Path.Combine(
                directory,
                "." + PreV2LearningStateFileName + "." + Guid.NewGuid().ToString("N") + ".tmp");
            try
            {
                using (var stream = new FileStream(
                    tempPath,
                    FileMode.CreateNew,
                    FileAccess.Write,
                    FileShare.None,
                    4096,
                    FileOptions.WriteThrough))
                {
                    stream.Write(backupBytes, 0, backupBytes.Length);
                    stream.Flush(true);
                }

                try
                {
                    File.Move(tempPath, backupPath);
                }
                catch (IOException)
                {
                    if (!File.Exists(backupPath))
                    {
                        throw;
                    }
                }
            }
            finally
            {
                if (File.Exists(tempPath))
                {
                    File.Delete(tempPath);
                }
            }
        }

        private static LearningStateMigrationStatus NewLearningMigrationStatus(
            bool wasApplied,
            bool isSavePending,
            Exception error)
        {
            return new LearningStateMigrationStatus(wasApplied, isSavePending, error);
        }

        private static object ReadJson(string path, Type type)
        {
            using (FileStream stream = File.OpenRead(path))
            {
                var serializer = new DataContractJsonSerializer(type);
                return serializer.ReadObject(stream);
            }
        }

        private static void WriteJson(string path, object value, Type type)
        {
            string directory = Path.GetDirectoryName(path);
            string tempPath = Path.Combine(
                directory,
                "." + Path.GetFileName(path) + "." + Guid.NewGuid().ToString("N") + ".tmp");

            try
            {
                using (var stream = new FileStream(
                    tempPath,
                    FileMode.CreateNew,
                    FileAccess.Write,
                    FileShare.None,
                    4096,
                    FileOptions.WriteThrough))
                {
                    var serializer = new DataContractJsonSerializer(type);
                    serializer.WriteObject(stream, value);
                    stream.Flush(true);
                }

                if (File.Exists(path))
                {
                    File.Replace(tempPath, path, GetBackupPath(path), true);
                }
                else
                {
                    File.Move(tempPath, path);
                }
            }
            finally
            {
                if (File.Exists(tempPath))
                {
                    File.Delete(tempPath);
                }
            }
        }

        private static string GetBackupPath(string path)
        {
            return path + ".bak";
        }

        private static string Quarantine(string path)
        {
            string quarantinedPath = path
                + ".corrupt-"
                + DateTime.UtcNow.ToString("yyyyMMddHHmmssfff", CultureInfo.InvariantCulture)
                + "-"
                + Guid.NewGuid().ToString("N");
            File.Copy(path, quarantinedPath, false);
            return quarantinedPath;
        }

        [DataContract]
        private sealed class LegacyAppSettings
        {
            [DataMember(Order = 8)]
            public Dictionary<int, KeyLearningState> Keys { get; set; }
        }
    }
}
