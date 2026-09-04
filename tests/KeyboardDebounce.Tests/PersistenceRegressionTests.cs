using System;
using System.Collections.Generic;
using System.IO;
using System.Runtime.Serialization;
using System.Runtime.Serialization.Json;
using KeyboardDebounce;
using Xunit;

namespace KeyboardDebounce.Tests
{
    public sealed class PersistenceRegressionTests
    {
        [Fact]
        public void MissingSettingsFieldsUseApplicationDefaults()
        {
            string dir = NewTempDir();
            try
            {
                var store = new SettingsStore(dir);
                File.WriteAllText(store.SettingsPath, "{}");

                AppData loaded = store.Load();

                Assert.True(loaded.Settings.Enabled);
                Assert.Equal(1.0, loaded.Settings.GlobalSensitivity);
                Assert.Equal(90, loaded.Settings.DefaultThresholdMs);
                Assert.Equal(500, loaded.Settings.LongHoldBypassMs);
                Assert.Equal(3000, loaded.Settings.StartupDelayMs);
                Assert.Equal("Ctrl+Alt+F11", loaded.Settings.PauseHotkey);
            }
            finally
            {
                Directory.Delete(dir, true);
            }
        }

        [Fact]
        public void ZeroLearningThresholdUsesProvidedFallbackBeforeClamping()
        {
            var state = new KeyLearningState { ThresholdMs = 0 };

            state.Normalize(137);

            Assert.Equal(137, state.ThresholdMs);
        }

        [Fact]
        public void LegacyLearningStateMigratesOnceWithoutDiscardingLearningEvidence()
        {
            string dir = NewTempDir();
            try
            {
                var store = new SettingsStore(dir);
                store.SaveSettings(new AppSettings { DefaultThresholdMs = 160 });
                var lastSeenUtc = new DateTime(2025, 1, 2, 3, 4, 5, DateTimeKind.Utc);
                var previousAdjustmentUtc = new DateTime(2025, 1, 1, 2, 3, 4, DateTimeKind.Utc);
                WriteLegacyLearningState(
                    store.LearningStatePath,
                    new Dictionary<int, KeyLearningState>
                    {
                        [65] = new KeyLearningState
                        {
                            ThresholdMs = 250,
                            AcceptedCount = 17,
                            SuppressedCount = 9,
                            LastIntervalMs = 88,
                            LastSeenUtc = lastSeenUtc,
                            LastAdjustmentMs = 8,
                            LastAdjustmentReason = "legacy-growth",
                            LastAdjustedUtc = previousAdjustmentUtc
                        },
                        [66] = new KeyLearningState
                        {
                            ThresholdMs = 117,
                            AcceptedCount = 23,
                            SuppressedCount = 4,
                            LastIntervalMs = 310,
                            LastSeenUtc = lastSeenUtc,
                            LastAdjustmentMs = -1,
                            LastAdjustmentReason = "stable-decay",
                            LastAdjustedUtc = previousAdjustmentUtc
                        }
                    });
                string legacyJson = File.ReadAllText(store.LearningStatePath);
                DateTime migrationStartedUtc = DateTime.UtcNow;

                AppData loaded = store.Load();

                DateTime migrationFinishedUtc = DateTime.UtcNow;
                Assert.Equal(2, loaded.Learning.SchemaVersion);
                KeyLearningState reset = loaded.Learning.Keys[65];
                Assert.Equal(160, reset.ThresholdMs);
                Assert.Equal(17, reset.AcceptedCount);
                Assert.Equal(9, reset.SuppressedCount);
                Assert.Equal(88, reset.LastIntervalMs);
                Assert.Equal(lastSeenUtc, reset.LastSeenUtc);
                Assert.Equal(-90, reset.LastAdjustmentMs);
                Assert.Equal("schema-v2-high-threshold-reset", reset.LastAdjustmentReason);
                Assert.InRange(reset.LastAdjustedUtc, migrationStartedUtc, migrationFinishedUtc);

                KeyLearningState retained = loaded.Learning.Keys[66];
                Assert.Equal(117, retained.ThresholdMs);
                Assert.Equal(23, retained.AcceptedCount);
                Assert.Equal(4, retained.SuppressedCount);
                Assert.Equal(310, retained.LastIntervalMs);
                Assert.Equal(lastSeenUtc, retained.LastSeenUtc);
                Assert.Equal(-1, retained.LastAdjustmentMs);
                Assert.Equal("stable-decay", retained.LastAdjustmentReason);
                Assert.Equal(previousAdjustmentUtc, retained.LastAdjustedUtc);

                string preV2BackupPath = Path.Combine(dir, "learning-state.pre-v2.json");
                Assert.Equal(legacyJson, File.ReadAllText(preV2BackupPath));
                Assert.True(store.LearningMigrationStatus.WasApplied);
                Assert.False(store.LearningMigrationStatus.IsSavePending);
                Assert.Null(store.LearningMigrationStatus.Error);

                loaded.Learning.Keys[65].AcceptedCount++;
                store.SaveLearningState(loaded.Learning, loaded.Settings.DefaultThresholdMs);
                Assert.Equal(legacyJson, File.ReadAllText(preV2BackupPath));

                string migratedJson = File.ReadAllText(store.LearningStatePath);
                var restartedStore = new SettingsStore(dir);
                AppData restarted = restartedStore.Load();
                Assert.Equal(2, restarted.Learning.SchemaVersion);
                Assert.Equal(160, restarted.Learning.Keys[65].ThresholdMs);
                Assert.Equal(18, restarted.Learning.Keys[65].AcceptedCount);
                Assert.InRange(
                    (reset.LastAdjustedUtc - restarted.Learning.Keys[65].LastAdjustedUtc).Duration(),
                    TimeSpan.Zero,
                    TimeSpan.FromMilliseconds(1));
                Assert.False(restartedStore.LearningMigrationStatus.WasApplied);
                Assert.Equal(migratedJson, File.ReadAllText(store.LearningStatePath));
                Assert.Equal(legacyJson, File.ReadAllText(preV2BackupPath));
            }
            finally
            {
                Directory.Delete(dir, true);
            }
        }

        [Fact]
        public void FailedMigrationBackupKeepsRepairedStateAndTheNormalSavePathRetriesIt()
        {
            string dir = NewTempDir();
            try
            {
                var store = new SettingsStore(dir);
                store.SaveSettings(new AppSettings { DefaultThresholdMs = 160 });
                WriteLegacyLearningState(
                    store.LearningStatePath,
                    new Dictionary<int, KeyLearningState>
                    {
                        [65] = new KeyLearningState
                        {
                            ThresholdMs = 250,
                            AcceptedCount = 7,
                            SuppressedCount = 3,
                            LastIntervalMs = 88
                        }
                    });
                string legacyJson = File.ReadAllText(store.LearningStatePath);
                string preV2BackupPath = Path.Combine(dir, "learning-state.pre-v2.json");
                Directory.CreateDirectory(preV2BackupPath);

                AppData loaded = store.Load();

                Assert.Equal(2, loaded.Learning.SchemaVersion);
                Assert.Equal(160, loaded.Learning.Keys[65].ThresholdMs);
                Assert.Equal(7, loaded.Learning.Keys[65].AcceptedCount);
                Assert.Equal(3, loaded.Learning.Keys[65].SuppressedCount);
                Assert.Equal(legacyJson, File.ReadAllText(store.LearningStatePath));
                Assert.True(store.LearningMigrationStatus.WasApplied);
                Assert.True(store.LearningMigrationStatus.IsSavePending);
                Assert.NotNull(store.LearningMigrationStatus.Error);

                Directory.Delete(preV2BackupPath);
                store.SaveLearningState(loaded.Learning, loaded.Settings.DefaultThresholdMs);

                Assert.Equal(legacyJson, File.ReadAllText(preV2BackupPath));
                Assert.False(store.LearningMigrationStatus.IsSavePending);
                Assert.Null(store.LearningMigrationStatus.Error);
                AppData restarted = new SettingsStore(dir).Load();
                Assert.Equal(160, restarted.Learning.Keys[65].ThresholdMs);
                Assert.Equal(2, restarted.Learning.SchemaVersion);
            }
            finally
            {
                Directory.Delete(dir, true);
            }
        }

        [Fact]
        public void FailedMigrationWriteKeepsRepairedStateAndTheNormalSavePathRetriesIt()
        {
            string dir = NewTempDir();
            try
            {
                var store = new SettingsStore(dir);
                store.SaveSettings(new AppSettings { DefaultThresholdMs = 160 });
                WriteLegacyLearningState(
                    store.LearningStatePath,
                    new Dictionary<int, KeyLearningState>
                    {
                        [65] = new KeyLearningState { ThresholdMs = 250 }
                    });
                string legacyJson = File.ReadAllText(store.LearningStatePath);
                string atomicBackupPath = store.LearningStatePath + ".bak";
                Directory.CreateDirectory(atomicBackupPath);

                AppData loaded = store.Load();

                Assert.Equal(160, loaded.Learning.Keys[65].ThresholdMs);
                Assert.Equal(legacyJson, File.ReadAllText(store.LearningStatePath));
                Assert.Equal(
                    legacyJson,
                    File.ReadAllText(Path.Combine(dir, "learning-state.pre-v2.json")));
                Assert.True(store.LearningMigrationStatus.WasApplied);
                Assert.True(store.LearningMigrationStatus.IsSavePending);
                Assert.NotNull(store.LearningMigrationStatus.Error);

                Directory.Delete(atomicBackupPath);
                store.SaveLearningState(loaded.Learning, loaded.Settings.DefaultThresholdMs);

                Assert.False(store.LearningMigrationStatus.IsSavePending);
                Assert.Null(store.LearningMigrationStatus.Error);
                AppData restarted = new SettingsStore(dir).Load();
                Assert.Equal(2, restarted.Learning.SchemaVersion);
                Assert.Equal(160, restarted.Learning.Keys[65].ThresholdMs);
            }
            finally
            {
                Directory.Delete(dir, true);
            }
        }

        [Fact]
        public void LegacySettingsImportBacksUpItsOriginalDocumentBeforeCreatingV2State()
        {
            string dir = NewTempDir();
            try
            {
                var store = new SettingsStore(dir);
                string legacyJson = "{\"Enabled\":true,\"DefaultThresholdMs\":90,"
                    + "\"Keys\":[{\"Key\":65,\"Value\":{\"ThresholdMs\":130}}]}";
                File.WriteAllText(store.SettingsPath, legacyJson);

                AppData loaded = store.Load();

                Assert.Equal(2, loaded.Learning.SchemaVersion);
                Assert.Equal(90, loaded.Learning.Keys[65].ThresholdMs);
                Assert.True(File.Exists(store.LearningStatePath));
                Assert.Equal(
                    legacyJson,
                    File.ReadAllText(Path.Combine(dir, "learning-state.pre-v2.json")));
            }
            finally
            {
                Directory.Delete(dir, true);
            }
        }

        [Fact]
        public void BlankPauseHotkeyUsesTheSupportedDefault()
        {
            var settings = new AppSettings { PauseHotkey = "  " };

            settings.Normalize();

            Assert.Equal("Ctrl+Alt+F11", settings.PauseHotkey);
        }

        [Fact]
        public void ReservedLegacyPauseHotkeyMigratesToSupportedDefault()
        {
            var settings = new AppSettings { PauseHotkey = "Ctrl+Alt+F12" };

            settings.Normalize();

            Assert.Equal("Ctrl+Alt+F11", settings.PauseHotkey);
        }

        [Fact]
        public void CorruptSettingsAreQuarantinedAndLoadInSafeDisabledState()
        {
            string dir = NewTempDir();
            try
            {
                var store = new SettingsStore(dir);
                File.WriteAllText(store.SettingsPath, "{ definitely-not-json");

                AppData loaded = store.Load();

                Assert.False(loaded.Settings.Enabled);
                Assert.True(File.Exists(store.SettingsPath));
                string quarantined = Assert.Single(Directory.GetFiles(dir, "settings.json.corrupt-*"));
                Assert.Equal("{ definitely-not-json", File.ReadAllText(quarantined));
                SettingsLoadIssue issue = Assert.Single(store.LoadIssues);
                Assert.Equal(store.SettingsPath, issue.SourcePath);
                Assert.Equal(quarantined, issue.QuarantinedPath);
                Assert.IsType<SerializationException>(issue.Error);
                Assert.False(issue.RecoveredFromBackup);

                AppData restarted = new SettingsStore(dir).Load();
                Assert.False(restarted.Settings.Enabled);
            }
            finally
            {
                Directory.Delete(dir, true);
            }
        }

        [Fact]
        public void CorruptSettingsRecoverFromLastKnownGoodBackup()
        {
            string dir = NewTempDir();
            try
            {
                var store = new SettingsStore(dir);
                store.SaveSettings(new AppSettings { Enabled = false, DefaultThresholdMs = 111 });
                store.SaveSettings(new AppSettings { Enabled = true, DefaultThresholdMs = 222 });
                File.WriteAllText(store.SettingsPath, "{ definitely-not-json");

                AppData loaded = store.Load();

                Assert.False(loaded.Settings.Enabled);
                Assert.Equal(111, loaded.Settings.DefaultThresholdMs);
                Assert.True(File.Exists(store.SettingsPath));
                Assert.True(File.Exists(store.SettingsPath + ".bak"));
                Assert.Single(Directory.GetFiles(dir, "settings.json.corrupt-*"));
                Assert.True(Assert.Single(store.LoadIssues).RecoveredFromBackup);
            }
            finally
            {
                Directory.Delete(dir, true);
            }
        }

        [Fact]
        public void MissingPrimarySettingsRecoverFromLastKnownGoodBackup()
        {
            string dir = NewTempDir();
            try
            {
                var store = new SettingsStore(dir);
                store.SaveSettings(new AppSettings { Enabled = false, DefaultThresholdMs = 111 });
                store.SaveSettings(new AppSettings { Enabled = true, DefaultThresholdMs = 222 });
                File.Delete(store.SettingsPath);

                AppData loaded = store.Load();

                Assert.False(loaded.Settings.Enabled);
                Assert.Equal(111, loaded.Settings.DefaultThresholdMs);
                Assert.True(File.Exists(store.SettingsPath));
                SettingsLoadIssue issue = Assert.Single(store.LoadIssues);
                Assert.True(issue.RecoveredFromBackup);
                Assert.IsType<FileNotFoundException>(issue.Error);
            }
            finally
            {
                Directory.Delete(dir, true);
            }
        }

        [Fact]
        public void FailedCorruptSettingsRecoveryKeepsSourceForNextStartup()
        {
            string dir = NewTempDir();
            try
            {
                var store = new SettingsStore(dir);
                File.WriteAllText(store.SettingsPath, "{ definitely-not-json");

                using (var locked = new FileStream(
                    store.SettingsPath,
                    FileMode.Open,
                    FileAccess.Read,
                    FileShare.Read))
                {
                    Assert.ThrowsAny<IOException>(() => store.Load());
                    Assert.True(File.Exists(store.SettingsPath));
                    Assert.Equal("{ definitely-not-json", File.ReadAllText(store.SettingsPath));
                }

                AppData restarted = new SettingsStore(dir).Load();
                Assert.False(restarted.Settings.Enabled);
            }
            finally
            {
                Directory.Delete(dir, true);
            }
        }

        [Fact]
        public void FailedReplacementKeepsTheExistingSettingsFile()
        {
            string dir = NewTempDir();
            try
            {
                var store = new SettingsStore(dir);
                store.SaveSettings(new AppSettings { Enabled = false, DefaultThresholdMs = 111 });
                string originalJson = File.ReadAllText(store.SettingsPath);

                using (var locked = new FileStream(store.SettingsPath, FileMode.Open, FileAccess.Read, FileShare.Read))
                {
                    Exception error = Record.Exception(
                        () => store.SaveSettings(new AppSettings { Enabled = true, DefaultThresholdMs = 222 }));

                    Assert.NotNull(error);
                }

                Assert.True(File.Exists(store.SettingsPath));
                Assert.Equal(originalJson, File.ReadAllText(store.SettingsPath));
                Assert.Empty(Directory.GetFiles(dir, "*.tmp"));
            }
            finally
            {
                Directory.Delete(dir, true);
            }
        }

        private static string NewTempDir()
        {
            string dir = Path.Combine(
                Path.GetTempPath(),
                "KeyboardDebounce.Tests",
                Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(dir);
            return dir;
        }

        private static void WriteLegacyLearningState(
            string path,
            Dictionary<int, KeyLearningState> keys)
        {
            using (FileStream stream = File.Create(path))
            {
                var serializer = new DataContractJsonSerializer(typeof(LegacyLearningStateDocument));
                serializer.WriteObject(
                    stream,
                    new LegacyLearningStateDocument { Keys = keys });
            }
        }

        [DataContract]
        private sealed class LegacyLearningStateDocument
        {
            [DataMember(Order = 1)]
            public Dictionary<int, KeyLearningState> Keys { get; set; }
        }
    }
}
