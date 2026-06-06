using System;
using System.Collections.Generic;
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

    public sealed class SettingsStore
    {
        private readonly string _settingsPath;
        private readonly string _learningStatePath;

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
        }

        public string SettingsPath
        {
            get { return _settingsPath; }
        }

        public string LearningStatePath
        {
            get { return _learningStatePath; }
        }

        public AppData Load()
        {
            AppSettings settings = LoadSettings();
            LearningState learning = LoadLearningState(settings.DefaultThresholdMs);

            settings.Normalize();
            learning.Normalize(settings.DefaultThresholdMs);
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
            WriteJson(_learningStatePath, learning, typeof(LearningState));
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
            try
            {
                if (!File.Exists(_settingsPath))
                {
                    return new AppSettings();
                }

                var settings = (AppSettings)ReadJson(_settingsPath, typeof(AppSettings));
                if (settings == null) return new AppSettings();
                settings.Normalize();
                return settings;
            }
            catch
            {
                return new AppSettings();
            }
        }

        private LearningState LoadLearningState(int fallbackThreshold)
        {
            try
            {
                if (File.Exists(_learningStatePath))
                {
                    var learning = (LearningState)ReadJson(_learningStatePath, typeof(LearningState));
                    if (learning == null) return new LearningState();
                    learning.Normalize(fallbackThreshold);
                    return learning;
                }

                return LoadLegacyLearningState(fallbackThreshold);
            }
            catch
            {
                return new LearningState();
            }
        }

        private LearningState LoadLegacyLearningState(int fallbackThreshold)
        {
            if (!File.Exists(_settingsPath)) return new LearningState();

            var legacy = (LegacyAppSettings)ReadJson(_settingsPath, typeof(LegacyAppSettings));
            var learning = new LearningState { Keys = legacy == null ? null : legacy.Keys };
            learning.Normalize(fallbackThreshold);
            return learning;
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
            string tempPath = path + ".tmp";
            using (FileStream stream = File.Create(tempPath))
            {
                var serializer = new DataContractJsonSerializer(type);
                serializer.WriteObject(stream, value);
            }

            if (File.Exists(path))
            {
                File.Delete(path);
            }
            File.Move(tempPath, path);
        }

        [DataContract]
        private sealed class LegacyAppSettings
        {
            [DataMember(Order = 8)]
            public Dictionary<int, KeyLearningState> Keys { get; set; }
        }
    }
}
