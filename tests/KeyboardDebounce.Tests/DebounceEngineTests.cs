using System;
using System.Collections.Generic;
using System.IO;
using System.Runtime.Serialization.Json;
using KeyboardDebounce;
using Xunit;

namespace KeyboardDebounce.Tests
{
    public sealed class DebounceEngineTests
    {
        [Fact]
        public void SuppressesShortRepeat()
        {
            var settings = new AppSettings { DefaultThresholdMs = 45 };
            var engine = NewEngine(settings, out var learning);

            Assert.False(engine.Process(Down(65, 1000)).Suppress);
            Assert.False(engine.Process(Up(65, 1010)).Suppress);
            Assert.True(engine.Process(Down(65, 1025)).Suppress);
        }

        [Fact]
        public void AllowsLongHoldRepeat()
        {
            var settings = new AppSettings { DefaultThresholdMs = 45, LongHoldBypassMs = 220 };
            var engine = NewEngine(settings, out var learning);

            Assert.False(engine.Process(Down(8, 1000)).Suppress);
            Assert.True(engine.Process(Down(8, 1030)).Suppress);
            Assert.False(engine.Process(Down(8, 1230)).Suppress);
        }

        [Fact]
        public void ManualCalibrationChangesThreshold()
        {
            var settings = new AppSettings { DefaultThresholdMs = 45 };
            var engine = NewEngine(settings, out var learning);

            engine.GetLearningState(65);
            engine.MakeKeyMoreSensitive(65);
            Assert.Equal(50, learning.Keys[65].ThresholdMs);

            engine.MakeKeyLessSensitive(65);
            Assert.Equal(45, learning.Keys[65].ThresholdMs);
        }

        [Fact]
        public void DoesNotStoreTextLikeData()
        {
            var settings = new AppSettings();
            var engine = NewEngine(settings, out var learning);

            engine.Process(Down(65, 1000));

            Assert.True(learning.Keys.ContainsKey(65));
            Assert.Null(learning.Keys[65].GetType().GetProperty("Text"));
        }

        [Fact]
        public void IgnoredKeyNeverSuppressesOrLearns()
        {
            var settings = new AppSettings { DefaultThresholdMs = 160 };
            var engine = NewEngine(settings, out var learning);
            engine.IgnoreKey(65);

            DebounceDecision first = engine.Process(Down(65, 1000));
            DebounceDecision second = engine.Process(Down(65, 1010));

            Assert.False(first.Suppress);
            Assert.False(second.Suppress);
            Assert.Equal("ignored", first.Reason);
            Assert.Equal("ignored", second.Reason);
            Assert.False(learning.Keys.ContainsKey(65));

            Assert.False(engine.Process(Down(66, 2000)).Suppress);
            Assert.True(engine.Process(Down(66, 2010)).Suppress);
        }

        [Fact]
        public void ResetLearningKeepsIgnoredKeys()
        {
            var settings = new AppSettings();
            var engine = NewEngine(settings, out var learning);
            engine.IgnoreKey(65);
            engine.Process(Down(66, 1000));

            engine.ResetLearning();

            Assert.Contains(65, settings.IgnoredKeys);
            Assert.False(learning.Keys.ContainsKey(66));
        }

        [Fact]
        public void SortsMixedLearningAndIgnoredRows()
        {
            var rows = new List<SettingsGridRow>
            {
                new SettingsGridRow { Vk = 65, KeyName = "A", Ignored = false, HasLearning = true, ThresholdMs = 120, AcceptedCount = 3, SuppressedCount = 1 },
                new SettingsGridRow { Vk = 8, KeyName = "Back", Ignored = true, HasLearning = false },
                new SettingsGridRow { Vk = 66, KeyName = "B", Ignored = false, HasLearning = true, ThresholdMs = 60, AcceptedCount = 8, SuppressedCount = 0 }
            };

            SettingsGridRowSorter.Sort(rows, "Threshold", true);
            Assert.Equal(66, rows[0].Vk);
            Assert.Equal(8, rows[2].Vk);

            SettingsGridRowSorter.Sort(rows, "Ignored", true);
            Assert.False(rows[0].Ignored);
            Assert.True(rows[2].Ignored);

            SettingsGridRowSorter.Sort(rows, "Vk", false);
            Assert.Equal(66, rows[0].Vk);
            Assert.Equal(8, rows[2].Vk);

            SettingsGridRowSorter.Sort(rows, "Suppressed", true);
            Assert.True(rows[0].HasLearning);
            Assert.Equal(8, rows[2].Vk);
        }

        [Fact]
        public void SuppressionNearBoundaryRecordsLearning()
        {
            var settings = new AppSettings { DefaultThresholdMs = 90 };
            var engine = NewEngine(settings, out var learning);

            engine.Process(Down(65, 1000));
            DebounceDecision decision = engine.Process(Down(65, 1088));

            Assert.True(decision.Suppress);
            Assert.True(decision.LearningAdjustmentMs > 0);
            Assert.True(learning.Keys[65].ThresholdMs > 90);
            Assert.Equal("suppressed-near-boundary", learning.Keys[65].LastAdjustmentReason);
        }

        [Fact]
        public void AcceptedSuspectedBounceLearnsUpward()
        {
            var settings = new AppSettings { DefaultThresholdMs = 45, LongHoldBypassMs = 500 };
            var engine = NewEngine(settings, out var learning);

            engine.Process(Down(65, 1000));
            engine.Process(Up(65, 1010));
            DebounceDecision first = engine.Process(Down(65, 1070));
            engine.Process(Up(65, 1080));
            DebounceDecision second = engine.Process(Down(65, 1140));

            Assert.False(first.Suppress);
            Assert.False(second.Suppress);
            Assert.True(second.LearningAdjustmentMs > 0);
            Assert.Equal("accepted-suspected-bounce", learning.Keys[65].LastAdjustmentReason);
        }

        [Fact]
        public void StableInputLearnsDownward()
        {
            var settings = new AppSettings { DefaultThresholdMs = 120, LongHoldBypassMs = 500 };
            var engine = NewEngine(settings, out var learning);
            long ms = 1000;

            for (int i = 0; i < 26; i++)
            {
                engine.Process(Down(65, ms));
                engine.Process(Up(65, ms + 10));
                ms += 600;
            }

            Assert.True(learning.Keys[65].ThresholdMs < 120);
            Assert.Equal("stable-decay", learning.Keys[65].LastAdjustmentReason);
        }

        [Fact]
        public void SerializesUnadjustedLearningState()
        {
            var learning = new LearningState();
            learning.Keys[65] = new KeyLearningState
            {
                ThresholdMs = 90,
                AcceptedCount = 1,
                LastSeenUtc = DateTime.MinValue,
                LastAdjustedUtc = DateTime.MinValue
            };
            learning.Normalize(90);

            var serializer = new DataContractJsonSerializer(typeof(LearningState));
            using (var stream = new MemoryStream())
            {
                serializer.WriteObject(stream, learning);
                Assert.True(stream.Length > 0);
            }
        }

        [Fact]
        public void IgnoredRowsStayLastExceptVkSort()
        {
            var rows = new List<SettingsGridRow>
            {
                new SettingsGridRow { Vk = 8, KeyName = "Back", Ignored = true, HasLearning = false },
                new SettingsGridRow { Vk = 65, KeyName = "A", Ignored = false, HasLearning = true, AcceptedCount = 10 },
                new SettingsGridRow { Vk = 66, KeyName = "B", Ignored = false, HasLearning = true, AcceptedCount = 2 },
                new SettingsGridRow { Vk = 9, KeyName = "Tab", Ignored = true, HasLearning = false }
            };

            SettingsGridRowSorter.Sort(rows, "Vk", true);
            Assert.Equal(8, rows[0].Vk);

            SettingsGridRowSorter.Sort(rows, "Accepted", true);
            Assert.False(rows[0].Ignored);
            Assert.False(rows[1].Ignored);
            Assert.True(rows[2].Ignored);
            Assert.True(rows[3].Ignored);
            Assert.Equal(66, rows[0].Vk);

            SettingsGridRowSorter.Sort(rows, "Accepted", false);
            Assert.False(rows[0].Ignored);
            Assert.False(rows[1].Ignored);
            Assert.True(rows[2].Ignored);
            Assert.True(rows[3].Ignored);
            Assert.Equal(65, rows[0].Vk);
        }

        [Fact]
        public void StoreSeparatesSettingsAndLearningState()
        {
            string dir = NewTempDir();
            try
            {
                var store = new SettingsStore(dir);
                var settings = new AppSettings { Enabled = false, SilentRun = true, DefaultThresholdMs = 110 };
                var learning = new LearningState();
                learning.Keys[65] = new KeyLearningState { ThresholdMs = 120, AcceptedCount = 3 };

                store.Save(settings, learning);

                string settingsJson = File.ReadAllText(store.SettingsPath);
                string learningJson = File.ReadAllText(store.LearningStatePath);
                Assert.DoesNotContain("AcceptedCount", settingsJson);
                Assert.Contains("SilentRun", settingsJson);
                Assert.Contains("Keys", learningJson);

                AppData loaded = store.Load();
                Assert.False(loaded.Settings.Enabled);
                Assert.True(loaded.Settings.SilentRun);
                Assert.Equal(110, loaded.Settings.DefaultThresholdMs);
                Assert.Equal(120, loaded.Learning.Keys[65].ThresholdMs);
            }
            finally
            {
                Directory.Delete(dir, true);
            }
        }

        [Fact]
        public void StoreImportsLegacyLearningStateFromSettingsJson()
        {
            string dir = NewTempDir();
            try
            {
                var store = new SettingsStore(dir);
                string legacyJson = "{\"Enabled\":true,\"StartWithWindows\":false,\"GlobalSensitivity\":1,\"DefaultThresholdMs\":90,\"LongHoldBypassMs\":500,\"StartupDelayMs\":3000,\"PauseHotkey\":\"Ctrl+Alt+F12\",\"Keys\":[{\"Key\":65,\"Value\":{\"ThresholdMs\":130,\"AcceptedCount\":2,\"SuppressedCount\":1,\"LastIntervalMs\":40,\"LastAdjustmentMs\":5,\"LastAdjustmentReason\":\"legacy\"}}],\"IgnoredKeys\":[8]}";
                File.WriteAllText(store.SettingsPath, legacyJson);

                AppData loaded = store.Load();

                Assert.False(loaded.Settings.SilentRun);
                Assert.Contains(8, loaded.Settings.IgnoredKeys);
                Assert.True(loaded.Learning.Keys.ContainsKey(65));
                Assert.Equal(130, loaded.Learning.Keys[65].ThresholdMs);
                Assert.Equal("legacy", loaded.Learning.Keys[65].LastAdjustmentReason);
            }
            finally
            {
                Directory.Delete(dir, true);
            }
        }

        [Fact]
        public void NewSettingsShowWindowByDefault()
        {
            var settings = new AppSettings();

            Assert.False(settings.SilentRun);
        }

        private static DebounceEngine NewEngine(AppSettings settings, out LearningState learning)
        {
            learning = new LearningState();
            return new DebounceEngine(settings, learning);
        }

        private static string NewTempDir()
        {
            string dir = Path.Combine(Path.GetTempPath(), "KeyboardDebounce.Tests", Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(dir);
            return dir;
        }

        private static KeyEventSample Down(int vk, long ms)
        {
            return new KeyEventSample { VirtualKeyCode = vk, IsKeyDown = true, TimestampMs = ms };
        }

        private static KeyEventSample Up(int vk, long ms)
        {
            return new KeyEventSample { VirtualKeyCode = vk, IsKeyDown = false, TimestampMs = ms };
        }
    }
}
