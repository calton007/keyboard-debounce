using System;
using System.Collections.Generic;
using System.IO;
using Xunit;

namespace KeyboardDebounce.Tests
{
    public sealed class SettingsUiContextTests
    {
        private static SettingsUiContext CreateContext(
            string tempDirectory,
            out AppSettings settings,
            out LearningState learning,
            Action onRefresh = null,
            Action<SettingsPageId> onNavigate = null,
            Action saveSettings = null,
            Action saveLearning = null)
        {
            settings = new AppSettings();
            learning = new LearningState();
            var engine = new DebounceEngine(settings, learning);
            var store = new SettingsStore(tempDirectory);

            var context = new SettingsUiContext(
                settings,
                learning,
                engine,
                store,
                saveSettings ?? (() => { }),
                saveLearning ?? (() => { }),
                () => "status",
                () => "game mode status");

            context.RefreshRequested = onRefresh;
            context.NavigateRequested = onNavigate;
            return context;
        }

        private static SettingsUiContext CreateRuntimeContext(
            string tempDirectory,
            AppSettings settings,
            Func<GameModeRuntimeSnapshot> getSnapshot,
            Action requestRuntimeRefresh)
        {
            var learning = new LearningState();
            return new SettingsUiContext(
                settings,
                learning,
                new DebounceEngine(settings, learning),
                new SettingsStore(tempDirectory),
                () => { },
                () => { },
                () => "status",
                getSnapshot,
                requestRuntimeRefresh);
        }

        [Fact]
        public void InitialVersionsStartAtOne()
        {
            using (var fixture = new TempDirectory())
            {
                SettingsUiContext context = CreateContext(fixture.Path, out _, out _);
                Assert.Equal(1, context.SettingsVersion);
                Assert.Equal(1, context.LearningVersion);
                Assert.Equal(1, context.RuntimeVersion);
            }
        }

        [Fact]
        public void SaveSettingsAndRefreshIncrementsSettingsAndRuntime()
        {
            using (var fixture = new TempDirectory())
            {
                int saveSettingsCalls = 0;
                int refreshCalls = 0;
                var raisedKinds = new List<SettingsUiChangeKind>();
                SettingsUiContext context = CreateContext(
                    fixture.Path,
                    out _,
                    out _,
                    onRefresh: () => refreshCalls += 1,
                    saveSettings: () => saveSettingsCalls += 1,
                    saveLearning: () => Assert.Fail("SaveLearning should not be called for settings refresh."));

                context.StateChanged += (_, args) => raisedKinds.Add(args.ChangeKind);
                context.SaveSettingsAndRefresh();

                Assert.Equal(1, saveSettingsCalls);
                Assert.Equal(1, refreshCalls);
                Assert.Equal(2, context.SettingsVersion);
                Assert.Equal(1, context.LearningVersion);
                Assert.Equal(2, context.RuntimeVersion);
                Assert.Single(raisedKinds);
                Assert.Equal(
                    SettingsUiChangeKind.Settings | SettingsUiChangeKind.Runtime,
                    raisedKinds[0]);
            }
        }

        [Fact]
        public void SaveLearningAndRefreshIncrementsLearningAndRuntime()
        {
            using (var fixture = new TempDirectory())
            {
                int saveSettingsCalls = 0;
                int saveLearningCalls = 0;
                int refreshCalls = 0;

                SettingsUiContext context = CreateContext(
                    fixture.Path,
                    out _,
                    out _,
                    onRefresh: () => refreshCalls += 1,
                    saveSettings: () => saveSettingsCalls += 1,
                    saveLearning: () => saveLearningCalls += 1);

                context.SaveLearningAndRefresh();

                Assert.Equal(1, saveLearningCalls);
                Assert.Equal(0, saveSettingsCalls);
                Assert.Equal(1, refreshCalls);
                Assert.Equal(1, context.SettingsVersion);
                Assert.Equal(2, context.LearningVersion);
                Assert.Equal(2, context.RuntimeVersion);
            }
        }

        [Fact]
        public void SaveAllAndRefreshIncrementsAllVersions()
        {
            using (var fixture = new TempDirectory())
            {
                int saveSettingsCalls = 0;
                int saveLearningCalls = 0;
                int refreshCalls = 0;
                var raisedKinds = new List<SettingsUiChangeKind>();

                SettingsUiContext context = CreateContext(
                    fixture.Path,
                    out _,
                    out _,
                    onRefresh: () => refreshCalls += 1,
                    saveSettings: () => saveSettingsCalls += 1,
                    saveLearning: () => saveLearningCalls += 1);

                context.StateChanged += (_, args) => raisedKinds.Add(args.ChangeKind);
                context.SaveAllAndRefresh();

                Assert.Equal(1, saveSettingsCalls);
                Assert.Equal(1, saveLearningCalls);
                Assert.Equal(1, refreshCalls);
                Assert.Equal(2, context.SettingsVersion);
                Assert.Equal(2, context.LearningVersion);
                Assert.Equal(2, context.RuntimeVersion);
                Assert.Single(raisedKinds);
                Assert.Equal(
                    SettingsUiChangeKind.Settings
                    | SettingsUiChangeKind.Learning
                    | SettingsUiChangeKind.Runtime,
                    raisedKinds[0]);
            }
        }

        [Fact]
        public void NotifyExternalStateChangedSkipsEventWhenNoChanges()
        {
            using (var fixture = new TempDirectory())
            {
                SettingsUiContext context = CreateContext(fixture.Path, out _, out _, saveSettings: () => { }, saveLearning: () => { });
                int events = 0;
                context.StateChanged += (_, _) => events++;

                context.NotifyExternalStateChanged(false, false, false);

                Assert.Equal(0, events);
                Assert.Equal(1, context.SettingsVersion);
                Assert.Equal(1, context.LearningVersion);
                Assert.Equal(1, context.RuntimeVersion);
            }
        }

        [Fact]
        public void NotifyExternalStateChangedRaisesOnlyRequestedKinds()
        {
            using (var fixture = new TempDirectory())
            {
                var kinds = new List<SettingsUiChangeKind>();
                SettingsUiContext context = CreateContext(fixture.Path, out _, out _, saveSettings: () => { }, saveLearning: () => { });
                context.StateChanged += (_, args) => kinds.Add(args.ChangeKind);

                context.NotifyExternalStateChanged(true, false, true);

                Assert.Equal(2, context.SettingsVersion);
                Assert.Equal(1, context.LearningVersion);
                Assert.Equal(2, context.RuntimeVersion);
                Assert.Single(kinds);
                Assert.Equal(
                    SettingsUiChangeKind.Settings | SettingsUiChangeKind.Runtime,
                    kinds[0]);
            }
        }

        [Fact]
        public void RequestNavigationRaisesNavigationKindWithoutVersionChange()
        {
            using (var fixture = new TempDirectory())
            {
                SettingsPageId navigatedPage = SettingsPageId.Overview;
                int refreshCalls = 0;
                var kinds = new List<SettingsUiChangeKind>();
                SettingsUiContext context = CreateContext(
                    fixture.Path,
                    out _,
                    out _,
                    onRefresh: () => refreshCalls += 1,
                    onNavigate: pageId => navigatedPage = pageId,
                    saveSettings: () => { },
                    saveLearning: () => { });
                context.StateChanged += (_, args) => kinds.Add(args.ChangeKind);

                context.RequestNavigation(SettingsPageId.GameMode);

                Assert.Equal(SettingsPageId.GameMode, navigatedPage);
                Assert.Equal(0, refreshCalls);
                Assert.Equal(1, context.SettingsVersion);
                Assert.Equal(1, context.LearningVersion);
                Assert.Equal(1, context.RuntimeVersion);
                Assert.Single(kinds);
                Assert.Equal(SettingsUiChangeKind.Navigation, kinds[0]);
            }
        }

        [Fact]
        public void TypedGameModeSnapshotFormatsAuthoritativeStatus()
        {
            using (var fixture = new TempDirectory())
            {
                var settings = new AppSettings
                {
                    ProcessGameModeEnabled = true,
                    GameProcesses = new List<string> { "Wow.exe" }
                };
                var snapshot = new GameModeRuntimeSnapshot(
                    true,
                    "Wow.exe",
                    new[] { "Wow" },
                    GameModeDetectionHealth.Healthy,
                    "");
                SettingsUiContext context = CreateRuntimeContext(
                    fixture.Path,
                    settings,
                    () => snapshot,
                    () => { });

                Assert.Same(snapshot, context.GetCurrentGameModeSnapshot());
                Assert.Equal(
                    "游戏模式已开启 · Wow.exe 正在运行",
                    context.GetCurrentGameModeStatus());
            }
        }

        [Fact]
        public void FailedDetectionStatusSaysWhichActualModeIsBeingKept()
        {
            using (var fixture = new TempDirectory())
            {
                var settings = new AppSettings
                {
                    ProcessGameModeEnabled = true,
                    GameProcesses = new List<string> { "game" }
                };
                var snapshot = new GameModeRuntimeSnapshot(
                    true,
                    "game",
                    new[] { "game" },
                    GameModeDetectionHealth.Failed,
                    "Win32Exception - Access denied");
                SettingsUiContext context = CreateRuntimeContext(
                    fixture.Path,
                    settings,
                    () => snapshot,
                    () => { });

                Assert.Equal(
                    "检测状态未知，继续保持游戏模式 · Win32Exception - Access denied",
                    context.GetCurrentGameModeStatus());
            }
        }

        [Fact]
        public void ManualGameModeRefreshRequestsDetectionWithoutInventingStateChange()
        {
            using (var fixture = new TempDirectory())
            {
                int requests = 0;
                var settings = new AppSettings();
                SettingsUiContext context = CreateRuntimeContext(
                    fixture.Path,
                    settings,
                    () => GameModeRuntimeSnapshot.Inactive,
                    () => requests++);

                context.RequestGameModeRuntimeRefresh();

                Assert.Equal(1, requests);
                Assert.Equal(1, context.RuntimeVersion);
            }
        }

        [Fact]
        public void RuntimeNotificationAdvancesVersionForHiddenPages()
        {
            using (var fixture = new TempDirectory())
            {
                SettingsUiContext context = CreateContext(
                    fixture.Path,
                    out _,
                    out _);
                SettingsUiStateChangedEventArgs raised = null;
                context.StateChanged += (_, args) => raised = args;

                context.NotifyRuntimeStateChanged();

                Assert.Equal(2, context.RuntimeVersion);
                Assert.NotNull(raised);
                Assert.Equal(SettingsUiChangeKind.Runtime, raised.ChangeKind);
                Assert.Equal(2, raised.RuntimeVersion);
            }
        }
    }

    internal sealed class TempDirectory : IDisposable
    {
        public TempDirectory()
        {
            Path = System.IO.Path.Combine(
                System.IO.Path.GetTempPath(),
                "KeyboardDebounce.Tests."
                    + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(Path);
        }

        public string Path { get; private set; }

        public void Dispose()
        {
            if (Directory.Exists(Path))
            {
                Directory.Delete(Path, true);
            }
        }
    }
}
