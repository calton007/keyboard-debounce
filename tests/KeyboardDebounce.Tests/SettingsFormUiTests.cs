using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Runtime.ExceptionServices;
using System.Threading;
using System.Windows.Forms;
using System.Windows.Forms.Automation;
using KeyboardDebounce;
using Xunit;

namespace KeyboardDebounce.Tests
{
    public sealed class SettingsFormUiTests
    {
        [Fact]
        public void FormCreatesFivePagesOnceAndSupportsNavigationShortcuts()
        {
            RunOnStaThread(delegate
            {
                using (var fixture = new SettingsFormFixture())
                {
                    fixture.Show();
                    Assert.Equal(5, fixture.Form.CreatedPageCount);
                    Assert.Equal(SettingsPageId.Overview, fixture.Form.ActivePageId);
                    Assert.Equal(AutomationLiveSetting.Polite, fixture.Form.StatusLiveRegion.LiveSetting);

                    SettingsPageBase[] originalPages = GetPages(fixture.Form);
                    for (int index = 0; index < originalPages.Length; index++)
                    {
                        Keys shortcut = Keys.Alt | (Keys)((int)Keys.D1 + index);
                        Assert.True(fixture.Form.ProcessNavigationShortcut(shortcut));
                        Assert.Equal((SettingsPageId)(index + 1), fixture.Form.ActivePageId);
                        Assert.True(fixture.Form.GetNavigationButton((SettingsPageId)(index + 1)).Checked);
                        Assert.Contains("当前页", fixture.Form.GetNavigationButton((SettingsPageId)(index + 1)).AccessibleName);
                        PumpEvents();
                        Assert.Equal((SettingsPageId)(index + 1), fixture.Form.ActivePageId);
                        Assert.Single(
                            originalPages,
                            delegate(SettingsPageBase page) { return page.Visible; });
                    }

                    fixture.Theme.SetTheme(SettingsThemeKind.Dark);
                    fixture.Form.RefreshData();
                    SettingsPageBase[] refreshedPages = GetPages(fixture.Form);
                    for (int index = 0; index < originalPages.Length; index++)
                    {
                        Assert.Same(originalPages[index], refreshedPages[index]);
                    }
                }
            });
        }

        [Fact]
        public void GlobalEnabledToggleSavesImmediately()
        {
            RunOnStaThread(delegate
            {
                using (var fixture = new SettingsFormFixture())
                {
                    fixture.Show();
                    int before = fixture.SaveCount;
                    fixture.Form.EnabledToggle.Checked = false;
                    PumpEvents();

                    Assert.False(fixture.Settings.Enabled);
                    Assert.True(fixture.SaveCount > before);
                }
            });
        }

        [Fact]
        public void SelectingFirstExecutableEnablesGameModeAndSavesOnce()
        {
            RunOnStaThread(delegate
            {
                using (var fixture = new SettingsFormFixture())
                {
                    fixture.Show();
                    fixture.Form.ProcessNavigationShortcut(Keys.Alt | Keys.D3);
                    var page = (GameModeSettingsPage)fixture.Form.GetPage(SettingsPageId.GameMode);
                    int before = fixture.SaveCount;

                    page.BrowseExecutableProvider = delegate
                    {
                        return new[]
                        {
                            "  C:\\Games\\sample-game.exe  ",
                            "C:\\Games\\SAMPLE-GAME.EXE",
                            "C:\\Games\\second-game.exe"
                        };
                    };
                    page.BrowseExecutableButton.PerformClick();
                    PumpEvents();

                    Assert.True(fixture.Settings.ProcessGameModeEnabled);
                    Assert.True(page.ProcessModeToggle.Checked);
                    Assert.Contains("sample-game", fixture.Settings.GameProcesses);
                    Assert.Contains("second-game", fixture.Settings.GameProcesses);
                    Assert.Equal(2, fixture.Settings.GameProcesses.Count);
                    Assert.Equal(before + 1, fixture.SaveCount);
                    Assert.Equal(2, page.ExecutableGrid.Rows.Count);
                }
            });
        }

        [Fact]
        public void RunningExecutablePickerAddsSelectionsWithoutManualInputAndCancelDoesNotSave()
        {
            RunOnStaThread(delegate
            {
                using (var fixture = new SettingsFormFixture())
                {
                    fixture.Show();
                    fixture.Form.ProcessNavigationShortcut(Keys.Alt | Keys.D3);
                    var page = (GameModeSettingsPage)fixture.Form.GetPage(SettingsPageId.GameMode);
                    int before = fixture.SaveCount;

                    page.RunningExecutableProvider = delegate
                    {
                        return new[] { "client.exe", "launcher" };
                    };
                    page.BrowseRunningExecutableButton.PerformClick();
                    PumpEvents();

                    Assert.Equal(before + 1, fixture.SaveCount);
                    Assert.Equal(new[] { "client", "launcher" }, fixture.Settings.GameProcesses);
                    Assert.Empty(page.Controls.Find("GameProcessInput", true));

                    int afterAdd = fixture.SaveCount;
                    page.RunningExecutableProvider = delegate { return Array.Empty<string>(); };
                    page.BrowseRunningExecutableButton.PerformClick();
                    PumpEvents();

                    Assert.Equal(afterAdd, fixture.SaveCount);
                    Assert.Equal(new[] { "client", "launcher" }, fixture.Settings.GameProcesses);
                }
            });
        }

        [Fact]
        public void RunningExecutableSearchMatchesExeNameCaseInsensitively()
        {
            IReadOnlyList<string> result = RunningExecutablePickerDialog.FilterExecutableNames(
                new[] { "CLIENT", "launcher.exe", "Client.exe" },
                "cli");

            Assert.Equal(new[] { "CLIENT" }, result);
        }

        [Fact]
        public void RunningExecutableDialogSupportsSearchableMultiSelectionWithoutAutoAdding()
        {
            RunOnStaThread(delegate
            {
                using (var fixture = new SettingsFormFixture())
                using (var dialog = new RunningExecutablePickerDialog(
                    new[] { "client" },
                    delegate { return new[] { "client", "launcher", "sample-game" }; }))
                {
                    fixture.Show();
                    fixture.Theme.SetTheme(SettingsThemeKind.Dark);
                    dialog.Show(fixture.Form);
                    WaitUntil(delegate { return !dialog.IsLoading; });

                    var search = (TextBox)FindByName(dialog, "RunningExecutableSearch");
                    var list = (CheckedListBox)FindByName(dialog, "RunningExecutableList");
                    var add = (Button)FindByName(dialog, "AddSelectedRunningExecutablesButton");
                    Assert.Equal(2, list.Items.Count);
                    Assert.False(add.Enabled);

                    search.Text = "launch";
                    PumpEvents();
                    Assert.Single(list.Items);
                    Assert.Equal("launcher.exe", list.Items[0]);
                    list.SetItemChecked(0, true);
                    PumpEvents();
                    Assert.True(add.Enabled);
                    Assert.Empty(fixture.Settings.GameProcesses);

                    add.PerformClick();
                    PumpEvents();
                    Assert.Equal(DialogResult.OK, dialog.DialogResult);
                    Assert.Equal(new[] { "launcher" }, dialog.SelectedExecutableNames);
                    Assert.Empty(fixture.Settings.GameProcesses);
                }
            });
        }

        [Fact]
        public void RunningExecutableDialogExposesEnumerationErrorsWithoutChangingSettings()
        {
            RunOnStaThread(delegate
            {
                using (var fixture = new SettingsFormFixture())
                using (var dialog = new RunningExecutablePickerDialog(
                    Array.Empty<string>(),
                    delegate { throw new InvalidOperationException("enumeration failed"); }))
                {
                    fixture.Show();
                    dialog.Show(fixture.Form);
                    WaitUntil(delegate { return !dialog.IsLoading; });

                    var status = (Label)FindByName(dialog, "RunningExecutableStatus");
                    var add = (Button)FindByName(dialog, "AddSelectedRunningExecutablesButton");
                    Assert.Contains("InvalidOperationException - enumeration failed", status.Text);
                    Assert.False(add.Enabled);
                    Assert.Empty(fixture.Settings.GameProcesses);
                }
            });
        }

        [Fact]
        public void FailedExecutableSelectionEnumerationDoesNotPartiallyMutateSettings()
        {
            RunOnStaThread(delegate
            {
                using (var fixture = new SettingsFormFixture())
                {
                    fixture.Show();
                    fixture.Form.ProcessNavigationShortcut(Keys.Alt | Keys.D3);
                    var page = (GameModeSettingsPage)fixture.Form.GetPage(SettingsPageId.GameMode);
                    int before = fixture.SaveCount;

                    Assert.Throws<InvalidOperationException>(
                        delegate { page.AddExecutableSelections(ThrowAfterFirstExecutable()); });

                    Assert.Empty(fixture.Settings.GameProcesses);
                    Assert.False(fixture.Settings.ProcessGameModeEnabled);
                    Assert.Equal(before, fixture.SaveCount);
                }
            });
        }

        [Fact]
        public void NormalAndGameParameterEditorsAreVisibleAdjustableAndSaveImmediately()
        {
            RunOnStaThread(delegate
            {
                using (var fixture = new SettingsFormFixture())
                {
                    fixture.Show();
                    fixture.Form.ProcessNavigationShortcut(Keys.Alt | Keys.D2);
                    Control normalPage = fixture.Form.GetPage(SettingsPageId.NormalDebounce);
                    TrackBar sensitivity = GetDescendants(normalPage)
                        .OfType<TrackBar>()
                        .Single(control => control.AccessibleName == "全局敏感度");
                    NumericUpDown defaultThreshold = GetDescendants(normalPage)
                        .OfType<NumericUpDown>()
                        .Single(control => control.AccessibleName == "默认阈值(ms)");
                    int beforeNormal = fixture.SaveCount;

                    sensitivity.Value = 125;
                    defaultThreshold.Value = 140;
                    PumpEvents();

                    float deviceScale = fixture.Form.DeviceDpi / 96F;
                    Assert.True(sensitivity.Width >= (int)Math.Floor(220 * deviceScale) - 2);
                    Assert.True(defaultThreshold.Width >= (int)Math.Floor(90 * deviceScale) - 2);
                    Assert.Equal(1.25, fixture.Settings.GlobalSensitivity, 2);
                    Assert.Equal(140, fixture.Settings.DefaultThresholdMs);
                    Assert.True(fixture.SaveCount >= beforeNormal + 2);

                    fixture.Form.ProcessNavigationShortcut(Keys.Alt | Keys.D3);
                    Control gamePage = fixture.Form.GetPage(SettingsPageId.GameMode);
                    TrackBar gameThreshold = GetDescendants(gamePage)
                        .OfType<TrackBar>()
                        .Single(control => control.AccessibleName == "游戏阈值上限滑块");
                    NumericUpDown gameThresholdValue = GetDescendants(gamePage)
                        .OfType<NumericUpDown>()
                        .Single(control => control.AccessibleName == "游戏阈值上限");
                    int beforeGame = fixture.SaveCount;

                    gameThreshold.Value = 80;
                    PumpEvents();

                    Assert.Equal(80, gameThresholdValue.Value);
                    Assert.Equal(80, fixture.Settings.GameModeThresholdMs);
                    Assert.Equal(beforeGame + 1, fixture.SaveCount);
                }
            });
        }

        [Fact]
        public void GameModeLiveRefreshUpdatesInPlaceWithoutRebuildingExecutableRows()
        {
            RunOnStaThread(delegate
            {
                using (var fixture = new SettingsFormFixture())
                {
                    fixture.Show();
                    fixture.Form.ProcessNavigationShortcut(Keys.Alt | Keys.D3);
                    var page = (GameModeSettingsPage)fixture.Form.GetPage(SettingsPageId.GameMode);
                    page.AddExecutableSelections(new[] { "sample-game.exe" });
                    PumpEvents();

                    DataGridViewRow originalRow = page.ExecutableGrid.Rows[0];
                    page.ExecutableGrid.CurrentCell = originalRow.Cells[0];
                    fixture.Form.RefreshVisibleValuesOnly();
                    PumpEvents();

                    Assert.Same(originalRow, page.ExecutableGrid.Rows[0]);
                    Assert.Same(originalRow, page.ExecutableGrid.CurrentRow);
                }
            });
        }

        [Fact]
        public void GameModePresentationUsesAuthoritativeTrayStatusInsteadOfPageSnapshot()
        {
            RunOnStaThread(delegate
            {
                using (var fixture = new SettingsFormFixture())
                {
                    fixture.Show();
                    fixture.Settings.ProcessGameModeEnabled = true;
                    fixture.Settings.GameProcesses = new List<string> { "sample-game" };
                    fixture.GameModeStatusText = "游戏模式已开启 · sample-game.exe 正在运行";
                    var page = (GameModeSettingsPage)fixture.Form.GetPage(SettingsPageId.GameMode);
                    page.RunningExecutableSnapshotProvider = delegate
                    {
                        return Array.Empty<string>();
                    };

                    fixture.Form.ProcessNavigationShortcut(Keys.Alt | Keys.D3);
                    WaitUntil(delegate { return !page.IsRunningRefreshInProgress; });
                    fixture.Form.RefreshVisibleValuesOnly();

                    var status = (Label)FindByName(page, "GameModeStatusValue");
                    Assert.Equal("游戏模式已开启", status.Text);
                    Assert.Contains("sample-game.exe", GetDescendants(page)
                        .OfType<Label>()
                        .Single(label => label.AccessibleDescription == "当前运行的游戏 EXE")
                        .Text);
                    Assert.Equal("未运行", page.ExecutableGrid.Rows[0]
                        .Cells["ExecutableStatus"].Value);
                }
            });
        }

        [Fact]
        public void GameModeRunningSnapshotDoesNotBlockUiAndErrorsRemainVisibleUntilSuccess()
        {
            RunOnStaThread(delegate
            {
                using (var fixture = new SettingsFormFixture())
                using (var release = new ManualResetEventSlim(false))
                using (var providerEntered = new ManualResetEventSlim(false))
                using (var watchdog = new System.Threading.Timer(
                    delegate(object state) { release.Set(); },
                    null,
                    TimeSpan.FromSeconds(5),
                    Timeout.InfiniteTimeSpan))
                {
                    try
                    {
                        fixture.Show();
                        fixture.Settings.ProcessGameModeEnabled = true;
                        fixture.Settings.GameProcesses = new List<string> { "sample-game" };
                        var page = (GameModeSettingsPage)fixture.Form.GetPage(SettingsPageId.GameMode);
                        page.RunningExecutableSnapshotProvider = delegate
                        {
                            providerEntered.Set();
                            release.Wait();
                            throw new InvalidOperationException("snapshot failed");
                        };

                        fixture.Form.ProcessNavigationShortcut(Keys.Alt | Keys.D3);
                        Assert.False(
                            release.IsSet,
                            "页面导航等待了后台 EXE 枚举完成。");
                        WaitUntil(delegate { return providerEntered.IsSet; });
                        Assert.True(page.IsRunningRefreshInProgress);

                        NumericUpDown threshold = GetDescendants(page)
                            .OfType<NumericUpDown>()
                            .Single(control => control.AccessibleName == "游戏阈值上限");
                        threshold.Value = Math.Min(threshold.Maximum, threshold.Value + 5);
                        PumpEvents();
                        Assert.False(
                            release.IsSet,
                            "即时保存等待了后台 EXE 枚举完成。");

                        release.Set();
                        WaitUntil(delegate { return !page.IsRunningRefreshInProgress; });
                        var error = (Label)FindByName(page, "GameExecutableRefreshError");
                        Assert.True(error.Visible);
                        Assert.Contains("InvalidOperationException - snapshot failed", error.Text);
                        Assert.Equal("状态未知", page.ExecutableGrid.Rows[0]
                            .Cells["ExecutableStatus"].Value);

                        fixture.Form.RefreshVisibleValuesOnly();
                        PumpEvents();
                        Assert.True(error.Visible);
                        Assert.Equal(error.Text, error.AccessibilityObject.Name);
                        Assert.Equal(AutomationLiveSetting.Assertive, error.LiveSetting);
                    }
                    finally
                    {
                        release.Set();
                    }
                }
            });
        }

        [Fact]
        public void DynamicStatusAccessibilityAndRunningColorMeetRequirements()
        {
            RunOnStaThread(delegate
            {
                using (var fixture = new SettingsFormFixture())
                {
                    fixture.Show();
                    Label footer = (Label)FindByName(fixture.Form, "StatusLiveRegion");
                    Assert.Equal(footer.Text, footer.AccessibilityObject.Name);
                    Assert.Equal(AutomationLiveSetting.Polite, footer.LiveSetting);

                    fixture.Settings.ProcessGameModeEnabled = true;
                    fixture.Settings.GameProcesses = new List<string> { "sample-game" };
                    fixture.GameModeStatusText = "游戏模式已开启 · sample-game.exe 正在运行";
                    fixture.Form.ProcessNavigationShortcut(Keys.Alt | Keys.D3);
                    fixture.Form.RefreshVisibleValuesOnly();
                    var status = (Label)FindByName(fixture.Form, "GameModeStatusValue");

                    Assert.Equal(status.Text, status.AccessibilityObject.Name);
                    Assert.Equal(AutomationLiveSetting.Polite, status.LiveSetting);
                    Assert.True(ContrastRatio(status.ForeColor, status.Parent.BackColor) >= 4.5);
                }
            });
        }

        [Fact]
        public void KeyPageRefreshPreservesSearchScopeSortSelectionAndFocus()
        {
            RunOnStaThread(delegate
            {
                using (var fixture = new SettingsFormFixture())
                {
                    fixture.Show();
                    fixture.Form.ProcessNavigationShortcut(Keys.Alt | Keys.D4);
                    var page = (KeyManagementSettingsPage)fixture.Form.GetPage(SettingsPageId.KeyManagement);

                    page.FilterScope = KeyFilterScope.GameFilteredOnly;
                    page.SearchTextBox.Text = "B";
                    page.ApplySearchImmediately();
                    page.SortByColumn("KeyName");
                    page.SelectVirtualKey(66);
                    page.SearchTextBox.Focus();
                    PumpEvents();
                    Assert.True(page.SearchTextBox.Focused);

                    fixture.Form.RefreshVisibleValuesOnly();
                    fixture.Form.RefreshData();
                    PumpEvents();

                    Assert.Equal("B", page.SearchTextBox.Text);
                    Assert.Equal(KeyFilterScope.GameFilteredOnly, page.FilterScope);
                    Assert.Equal("KeyName", page.CurrentSortColumn);
                    Assert.True(page.SortAscending);
                    Assert.Equal(66, page.SelectedVirtualKey);
                    Assert.Equal(new[] { 66 }, page.GetDisplayedVirtualKeys());
                    Assert.True(page.SearchTextBox.Focused);
                }
            });
        }

        [Fact]
        public void KeyTableCheckboxesUpdateSettingsAndSaveImmediately()
        {
            RunOnStaThread(delegate
            {
                using (var fixture = new SettingsFormFixture())
                {
                    fixture.Show();
                    fixture.Form.ProcessNavigationShortcut(Keys.Alt | Keys.D4);
                    var page = (KeyManagementSettingsPage)fixture.Form.GetPage(SettingsPageId.KeyManagement);
                    page.FilterScope = KeyFilterScope.All;
                    page.SearchTextBox.Clear();
                    page.ApplySearchImmediately();

                    DataGridViewRow row = FindGridRow(page.Grid, 65);
                    int settingsBefore = fixture.SaveCount;
                    row.Cells["GameFiltered"].Value = true;
                    PumpEvents();

                    Assert.Contains(65, fixture.Settings.GameModeFilteredKeys);
                    Assert.True(fixture.SaveCount > settingsBefore);

                    row = FindGridRow(page.Grid, 65);
                    int learningBefore = fixture.SaveLearningCount;
                    row.Cells["Ignored"].Value = true;
                    PumpEvents();

                    Assert.Contains(65, fixture.Settings.IgnoredKeys);
                    Assert.True(fixture.SaveLearningCount > learningBefore);
                }
            });
        }

        [Fact]
        public void NavigatingToKeyPageIncludesKeysLearnedWhilePageWasHidden()
        {
            RunOnStaThread(delegate
            {
                using (var fixture = new SettingsFormFixture())
                {
                    fixture.Show();
                    fixture.Learning.Keys[70] = new KeyLearningState { ThresholdMs = 80 };

                    fixture.Form.ProcessNavigationShortcut(Keys.Alt | Keys.D4);
                    var page = (KeyManagementSettingsPage)fixture.Form.GetPage(SettingsPageId.KeyManagement);

                    Assert.Contains(70, page.GetDisplayedVirtualKeys());
                }
            });
        }

        [Fact]
        public void SearchUsesOneHundredFiftyMillisecondDebounceAndDoesNotSave()
        {
            RunOnStaThread(delegate
            {
                using (var fixture = new SettingsFormFixture())
                {
                    fixture.Show();
                    fixture.Form.ProcessNavigationShortcut(Keys.Alt | Keys.D4);
                    var page = (KeyManagementSettingsPage)fixture.Form.GetPage(SettingsPageId.KeyManagement);
                    int settingsBefore = fixture.SaveCount;
                    int learningBefore = fixture.SaveLearningCount;
                    Assert.Equal(150, page.SearchDebounceInterval);

                    page.SearchTextBox.Text = "65";
                    var wait = Stopwatch.StartNew();
                    while (wait.ElapsedMilliseconds < 250)
                    {
                        PumpEvents();
                        Thread.Sleep(10);
                    }

                    Assert.Equal(new[] { 65 }, page.GetDisplayedVirtualKeys());
                    Assert.Equal(settingsBefore, fixture.SaveCount);
                    Assert.Equal(learningBefore, fixture.SaveLearningCount);
                }
            });
        }

        [Fact]
        public void ThemeChangeRestylesWithoutRecreatingPagesAndDisposeUnsubscribes()
        {
            RunOnStaThread(delegate
            {
                var fixture = new SettingsFormFixture();
                SettingsPageBase[] pages = GetPages(fixture.Form);
                fixture.Show();
                Assert.Equal(1, fixture.Theme.SubscriptionCount);

                fixture.Theme.SetTheme(SettingsThemeKind.Dark);
                PumpEvents();

                Assert.Equal(SettingsThemePalette.Create(SettingsThemeKind.Dark).Page, fixture.Form.BackColor);
                Assert.Same(pages[0], fixture.Form.GetPage(SettingsPageId.Overview));

                fixture.Dispose();
                Assert.Equal(0, fixture.Theme.SubscriptionCount);
                Assert.True(fixture.Theme.IsDisposed);
                fixture.Theme.SetTheme(SettingsThemeKind.Light);
            });
        }

        [Fact]
        public void ShellLayoutFitsApprovedSizesAcrossPagesAndThemes()
        {
            RunOnStaThread(delegate
            {
                using (var fixture = new SettingsFormFixture())
                {
                    fixture.Show();
                    Size[] sizes = { new Size(960, 640), new Size(1160, 760) };
                    SettingsThemeKind[] themes =
                    {
                        SettingsThemeKind.Light,
                        SettingsThemeKind.Dark,
                        SettingsThemeKind.HighContrast
                    };

                    foreach (Size size in sizes)
                    {
                        fixture.Form.Size = size;
                        foreach (SettingsThemeKind theme in themes)
                        {
                            fixture.Theme.SetTheme(theme);
                            for (int pageIndex = 0; pageIndex < 5; pageIndex++)
                            {
                                fixture.Form.ProcessNavigationShortcut(
                                    Keys.Alt | (Keys)((int)Keys.D1 + pageIndex));
                                fixture.Form.PerformLayout();
                                PumpEvents();
                                AssertShellLayout(fixture.Form);
                                AssertCardsContainButtonsAndLabels(fixture.Form);
                            }
                        }
                    }
                }
            });
        }

        [Fact]
        public void RepresentativeThemeViewsRenderToBitmaps()
        {
            RunOnStaThread(delegate
            {
                using (var fixture = new SettingsFormFixture())
                {
                    fixture.Show();
                    RenderView(fixture, new Size(1160, 760), SettingsThemeKind.Light, SettingsPageId.Overview, "light-overview");
                    RenderView(fixture, new Size(1160, 760), SettingsThemeKind.Light, SettingsPageId.NormalDebounce, "light-normal");
                    fixture.Settings.ProcessGameModeEnabled = true;
                    fixture.Settings.GameProcesses = new List<string>
                    {
                        "explorer",
                        "notepad",
                        "client",
                        "launcher",
                        "sample-game"
                    };
                    fixture.GameModeStatusText = "游戏模式已开启 · explorer.exe 正在运行";
                    fixture.RecentStatusText = "W 按下 · 间隔 18ms · 已放行";
                    fixture.Form.RefreshData();
                    RenderView(fixture, new Size(1160, 760), SettingsThemeKind.Dark, SettingsPageId.GameMode, "dark-game");
                    RenderView(fixture, new Size(1488, 1058), SettingsThemeKind.Dark, SettingsPageId.GameMode, "dark-game-design-qa");
                    RenderView(fixture, new Size(1160, 760), SettingsThemeKind.Dark, SettingsPageId.KeyManagement, "dark-keys");
                    RenderView(fixture, new Size(960, 640), SettingsThemeKind.HighContrast, SettingsPageId.ApplicationSettings, "high-contrast-app");
                }
            });
        }

        [Theory]
        [InlineData(1.5F)]
        [InlineData(2.0F)]
        public void FixedWidthEditorsSurviveInitialDpiScaleBeforeShow(float scaleFactor)
        {
            RunOnStaThread(delegate
            {
                using (var fixture = new SettingsFormFixture())
                {
                    fixture.Form.Scale(new SizeF(scaleFactor, scaleFactor));
                    AssertEditorWidths(fixture.Form, scaleFactor);

                    fixture.Show();
                    for (int pageIndex = 0; pageIndex < 5; pageIndex++)
                    {
                        fixture.Form.ProcessNavigationShortcut(
                            Keys.Alt | (Keys)((int)Keys.D1 + pageIndex));
                        fixture.Form.PerformLayout();
                        PumpEvents();
                    }

                    AssertEditorWidths(fixture.Form, scaleFactor);
                }
            });
        }

        [Theory]
        [InlineData(1.5F)]
        [InlineData(2.0F)]
        public void ShellKeepsItsStructureWhenScaledForHigherDpi(float scaleFactor)
        {
            RunOnStaThread(delegate
            {
                using (var fixture = new SettingsFormFixture())
                {
                    fixture.Show();
                    fixture.Form.Size = new Size(960, 640);
                    fixture.Form.Scale(new SizeF(scaleFactor, scaleFactor));
                    for (int pageIndex = 0; pageIndex < 5; pageIndex++)
                    {
                        fixture.Form.ProcessNavigationShortcut(
                            Keys.Alt | (Keys)((int)Keys.D1 + pageIndex));
                        fixture.Form.PerformLayout();
                        PumpEvents();
                        float expectedScale = fixture.Form.DeviceDpi / 96.0f * scaleFactor;
                        AssertShellLayout(fixture.Form, expectedScale);
                        AssertCardsContainButtonsAndLabels(fixture.Form);
                        AssertHeaderDoesNotClip(fixture.Form);
                    }
                }
            });
        }

        [Fact]
        public void HeaderControlsDoNotClipAtCurrentDeviceDpi()
        {
            RunOnStaThread(delegate
            {
                using (var fixture = new SettingsFormFixture())
                {
                    fixture.Show();
                    fixture.Form.PerformLayout();
                    PumpEvents();
                    AssertHeaderDoesNotClip(fixture.Form);
                }
            });
        }

        [Fact]
        public void RealDpiLayoutUsesExpectedScaledShellRegions()
        {
            RunOnStaThread(delegate
            {
                using (var fixture = new SettingsFormFixture())
                {
                    fixture.Show();
                    var scale = fixture.Form.DeviceDpi / 96.0f;
                    for (int pageIndex = 0; pageIndex < 5; pageIndex++)
                    {
                        fixture.Form.ProcessNavigationShortcut(
                            Keys.Alt | (Keys)((int)Keys.D1 + pageIndex));
                        fixture.Form.PerformLayout();
                        PumpEvents();
                        AssertShellLayout(fixture.Form, scale);
                        AssertHeaderDoesNotClip(fixture.Form);
                        AssertCardsContainButtonsAndLabels(fixture.Form);
                    }
                }
            });
        }

        [Fact]
        public void PerMonitorV2DpiBootstrapShouldBeEnabledWhenTestsCreateUi()
        {
            RunOnStaThread(delegate
            {
                Assert.True(DpiTestBootstrap.IsHighDpiPerMonitorV2Enabled);
                using (var fixture = new SettingsFormFixture())
                {
                    fixture.Show();
                }
            });
        }

        private static SettingsPageBase[] GetPages(SettingsForm form)
        {
            return new[]
            {
                form.GetPage(SettingsPageId.Overview),
                form.GetPage(SettingsPageId.NormalDebounce),
                form.GetPage(SettingsPageId.GameMode),
                form.GetPage(SettingsPageId.KeyManagement),
                form.GetPage(SettingsPageId.ApplicationSettings)
            };
        }

        private static IEnumerable<string> ThrowAfterFirstExecutable()
        {
            yield return "partial.exe";
            throw new InvalidOperationException("selection failed");
        }

        private static DataGridViewRow FindGridRow(DataGridView grid, int virtualKeyCode)
        {
            foreach (DataGridViewRow row in grid.Rows)
            {
                var model = row.Tag as SettingsGridRow;
                if (model != null && model.Vk == virtualKeyCode) return row;
            }
            throw new InvalidOperationException("未找到 VK " + virtualKeyCode + " 的表格行。");
        }

        private static void AssertShellLayout(SettingsForm form, float? scaleFactor = null)
        {
            Control navigation = FindByName(form, "SettingsNavigation");
            Control header = FindByName(form, "SettingsHeader");
            Control pageHost = FindByName(form, "SettingsPageHost");
            Control footer = FindByName(form, "SettingsStatusBar");
            Rectangle client = new Rectangle(form.PointToScreen(Point.Empty), form.ClientSize);
            Rectangle navigationBounds = ToScreenBounds(navigation);
            Rectangle headerBounds = ToScreenBounds(header);
            Rectangle pageBounds = ToScreenBounds(pageHost);
            Rectangle footerBounds = ToScreenBounds(footer);

            Assert.True(client.Contains(navigationBounds));
            Assert.True(client.Contains(headerBounds));
            Assert.True(client.Contains(pageBounds));
            Assert.True(client.Contains(footerBounds));
            Assert.True(navigationBounds.Right <= headerBounds.Left);
            Assert.True(headerBounds.Bottom <= pageBounds.Top);
            Assert.True(pageBounds.Bottom <= footerBounds.Top);
            float expectedScale = scaleFactor.GetValueOrDefault(form.DeviceDpi / 96f);
            if (expectedScale <= 0F) expectedScale = form.DeviceDpi / 96f;
            Assert.InRange(
                navigationBounds.Width,
                (int)(188 * expectedScale) - 2,
                (int)(188 * expectedScale) + 2);
            Assert.InRange(
                headerBounds.Height,
                (int)(68 * expectedScale) - 2,
                (int)(68 * expectedScale) + 2);
            Assert.InRange(
                footerBounds.Height,
                (int)(36 * expectedScale) - 2,
                (int)(36 * expectedScale) + 2);

            SettingsPageBase active = form.GetPage(form.ActivePageId);
            Assert.True(active.Visible);
            Assert.True(active.ClientSize.Width > 0);
            Assert.True(active.ClientSize.Height > 0);
            foreach (SettingsCard card in GetDescendants(active).OfType<SettingsCard>())
            {
                Assert.True(card.Width > 0);
                Assert.True(card.Height > 0);
                Assert.True(card.Right <= active.DisplayRectangle.Right);
            }
        }

        private static void AssertHeaderDoesNotClip(SettingsForm form)
        {
            Label title = (Label)FindByName(form, "PageTitleLabel");
            Assert.True(title.Height >= title.PreferredHeight);
        }

        private static void AssertEditorWidths(SettingsForm form, float scaleFactor)
        {
            Control normalPage = form.GetPage(SettingsPageId.NormalDebounce);
            AssertEditorWidth<TrackBar>(normalPage, "全局敏感度", 220, scaleFactor);
            AssertEditorWidth<NumericUpDown>(normalPage, "默认阈值(ms)", 90, scaleFactor);
            AssertEditorWidth<NumericUpDown>(normalPage, "长按放行(ms)", 90, scaleFactor);

            Control gamePage = form.GetPage(SettingsPageId.GameMode);
            AssertEditorWidth<NumericUpDown>(gamePage, "游戏阈值上限", 90, scaleFactor);
            AssertEditorWidth<TrackBar>(gamePage, "游戏阈值上限滑块", 220, scaleFactor);
            AssertEditorWidth<NumericUpDown>(gamePage, "游戏长按放行", 90, scaleFactor);
            AssertEditorWidth<TrackBar>(gamePage, "游戏长按放行滑块", 220, scaleFactor);

            Control keyPage = form.GetPage(SettingsPageId.KeyManagement);
            AssertEditorWidth<TextBox>(keyPage, "搜索", 220, scaleFactor);
            AssertEditorWidth<ComboBox>(keyPage, "范围", 160, scaleFactor);
            AssertEditorWidth<NumericUpDown>(keyPage, "手动添加 VK", 90, scaleFactor);

            Control applicationPage = form.GetPage(SettingsPageId.ApplicationSettings);
            AssertEditorWidth<NumericUpDown>(applicationPage, "启动延迟(ms)", 100, scaleFactor);
            AssertEditorWidth<TextBox>(applicationPage, "热键", 180, scaleFactor);
        }

        private static void AssertEditorWidth<TEditor>(
            Control root,
            string accessibleName,
            int logicalWidth,
            float scaleFactor) where TEditor : Control
        {
            Control editor = GetDescendants(root).Single(
                delegate(Control control)
                {
                    return control is TEditor && String.Equals(
                        control.AccessibleName,
                        accessibleName,
                        StringComparison.Ordinal);
                });
            int expectedWidth = (int)Math.Round(logicalWidth * scaleFactor);
            Assert.True(
                editor.Width >= expectedWidth,
                $"输入控件 {accessibleName} 的宽度应至少为 {expectedWidth}px，实际为 {editor.Width}px。");
        }

        private static void AssertCardsContainButtonsAndLabels(SettingsForm form)
        {
            SettingsPageBase active = form.GetPage(form.ActivePageId);
            var contentControls = GetDescendants(active)
                .Where(control => control is Button || control is Label || control is CheckBox)
                .ToArray();
            foreach (Control control in contentControls)
            {
                SettingsCard parentCard = FindAncestor<SettingsCard>(control);
                if (parentCard == null) continue;

                Rectangle controlScreenBounds = ToScreenBounds(control);
                Rectangle cardScreenBounds = ToScreenBounds(parentCard);
                Assert.True(
                    cardScreenBounds.Contains(controlScreenBounds),
                    $"页面 {form.ActivePageId} 的控件 {control.Name}/{control.Text}({control.GetType().Name}) "
                    + $"边界 {controlScreenBounds} 在卡片 {parentCard.Name} 边界 {cardScreenBounds} 中溢出。");
            }
        }

        private static Control FindAncestor(Control control, Type ancestorType)
        {
            var current = control.Parent;
            while (current != null)
            {
                if (ancestorType.IsInstanceOfType(current)) return current;
                current = current.Parent;
            }
            return null;
        }

        private static T FindAncestor<T>(Control control) where T : Control
        {
            return FindAncestor(control, typeof(T)) as T;
        }

        private static Control FindByName(Control root, string name)
        {
            foreach (Control control in GetDescendants(root))
            {
                if (String.Equals(control.Name, name, StringComparison.Ordinal)) return control;
            }
            throw new InvalidOperationException("未找到控件：" + name);
        }

        private static IEnumerable<Control> GetDescendants(Control root)
        {
            foreach (Control child in root.Controls)
            {
                yield return child;
                foreach (Control descendant in GetDescendants(child)) yield return descendant;
            }
        }

        private static Rectangle ToScreenBounds(Control control)
        {
            return new Rectangle(control.PointToScreen(Point.Empty), control.ClientSize);
        }

        private static void RenderView(
            SettingsFormFixture fixture,
            Size size,
            SettingsThemeKind theme,
            SettingsPageId pageId,
            string fileName)
        {
            fixture.Form.Size = size;
            fixture.Theme.SetTheme(theme);
            fixture.Form.ProcessNavigationShortcut(
                Keys.Alt | (Keys)((int)Keys.D1 + ((int)pageId - 1)));
            fixture.Form.PerformLayout();
            PumpEvents();
            if (pageId == SettingsPageId.GameMode)
            {
                var gamePage = (GameModeSettingsPage)fixture.Form.GetPage(pageId);
                WaitUntil(delegate { return !gamePage.IsRunningRefreshInProgress; });
            }

            using (var bitmap = new Bitmap(fixture.Form.Width, fixture.Form.Height))
            {
                fixture.Form.DrawToBitmap(bitmap, new Rectangle(Point.Empty, bitmap.Size));
                Assert.Equal(fixture.Form.Size, bitmap.Size);

                string outputDirectory = Environment.GetEnvironmentVariable("KEYBOARDDEBOUNCE_UI_SNAPSHOT_DIR");
                if (!String.IsNullOrWhiteSpace(outputDirectory))
                {
                    Directory.CreateDirectory(outputDirectory);
                    bitmap.Save(Path.Combine(outputDirectory, fileName + ".png"));
                }
            }
        }

        private static void PumpEvents()
        {
            Application.DoEvents();
        }

        private static void WaitUntil(Func<bool> condition)
        {
            var stopwatch = Stopwatch.StartNew();
            while (!condition())
            {
                if (stopwatch.Elapsed > TimeSpan.FromSeconds(5))
                {
                    throw new TimeoutException("等待界面异步操作完成超时。");
                }
                Application.DoEvents();
                Thread.Sleep(10);
            }
            Application.DoEvents();
        }

        private static double ContrastRatio(Color foreground, Color background)
        {
            double light = RelativeLuminance(foreground);
            double dark = RelativeLuminance(background);
            if (light < dark)
            {
                double swap = light;
                light = dark;
                dark = swap;
            }
            return (light + 0.05) / (dark + 0.05);
        }

        private static double RelativeLuminance(Color color)
        {
            double red = LinearColor(color.R / 255.0);
            double green = LinearColor(color.G / 255.0);
            double blue = LinearColor(color.B / 255.0);
            return (0.2126 * red) + (0.7152 * green) + (0.0722 * blue);
        }

        private static double LinearColor(double value)
        {
            return value <= 0.04045
                ? value / 12.92
                : Math.Pow((value + 0.055) / 1.055, 2.4);
        }

        private static void RunOnStaThread(Action action)
        {
            Exception failure = null;
            var thread = new Thread(new ThreadStart(delegate
            {
                try
                {
                    DpiTestBootstrap.Ensure();
                    action();
                }
                catch (Exception error)
                {
                    failure = error;
                }
            }));
            thread.SetApartmentState(ApartmentState.STA);
            thread.Start();
            Assert.True(thread.Join(TimeSpan.FromSeconds(20)), "STA UI test timed out.");
            if (failure != null) ExceptionDispatchInfo.Capture(failure).Throw();
        }

        private sealed class SettingsFormFixture : IDisposable
        {
            private readonly string _directory;
            private bool _disposed;

            public SettingsFormFixture()
            {
                _directory = Path.Combine(
                    Path.GetTempPath(),
                    "KeyboardDebounce.SettingsFormUiTests",
                    Guid.NewGuid().ToString("N"));
                Settings = new AppSettings
                {
                    Enabled = true,
                    ProcessGameModeEnabled = false,
                    GameProcesses = new List<string>(),
                    GameModeFilteredKeys = new List<int> { 66 },
                    IgnoredKeys = new List<int> { 88 }
                };
                Learning = new LearningState();
                Learning.Keys[65] = new KeyLearningState { ThresholdMs = 90 };
                Learning.Keys[66] = new KeyLearningState { ThresholdMs = 100 };
                var engine = new DebounceEngine(Settings, Learning);
                var store = new SettingsStore(_directory);
                Theme = new FakeThemeService();
                GameModeStatusText = "自动切换已关闭";
                RecentStatusText = "ok";
                Form = new SettingsForm(
                    Settings,
                    Learning,
                    engine,
                    store,
                    delegate { SaveCount++; },
                    delegate { SaveLearningCount++; },
                    delegate { return RecentStatusText; },
                    delegate { return GameModeStatusText; },
                    Theme);
            }

            public AppSettings Settings { get; private set; }
            public LearningState Learning { get; private set; }
            public SettingsForm Form { get; private set; }
            public FakeThemeService Theme { get; private set; }
            public int SaveCount { get; private set; }
            public int SaveLearningCount { get; private set; }
            public string GameModeStatusText { get; set; }
            public string RecentStatusText { get; set; }

            public void Show()
            {
                Form.Show();
                PumpEvents();
            }

            public void Dispose()
            {
                if (_disposed) return;
                _disposed = true;
                Form.Dispose();
                if (Directory.Exists(_directory)) Directory.Delete(_directory, true);
            }
        }

        private sealed class FakeThemeService : ISettingsThemeService
        {
            private EventHandler _themeChanged;

            public FakeThemeService()
            {
                CurrentKind = SettingsThemeKind.Light;
                CurrentPalette = SettingsThemePalette.Create(CurrentKind);
            }

            public event EventHandler ThemeChanged
            {
                add
                {
                    _themeChanged += value;
                    SubscriptionCount++;
                }
                remove
                {
                    _themeChanged -= value;
                    SubscriptionCount--;
                }
            }

            public SettingsThemeKind CurrentKind { get; private set; }
            public SettingsThemePalette CurrentPalette { get; private set; }
            public string LastError { get; private set; }
            public int SubscriptionCount { get; private set; }
            public bool IsDisposed { get; private set; }

            public void SetTheme(SettingsThemeKind kind)
            {
                CurrentKind = kind;
                CurrentPalette = SettingsThemePalette.Create(kind);
                EventHandler handler = _themeChanged;
                if (handler != null) handler(this, EventArgs.Empty);
            }

            public void ApplyTitleBar(Form form)
            {
            }

            public void Dispose()
            {
                IsDisposed = true;
            }
        }
    }

    public sealed class SettingsThemeTests
    {
        [Fact]
        public void PalettesUseApprovedColorsAndSystemHighContrastColors()
        {
            SettingsThemePalette light = SettingsThemePalette.Create(SettingsThemeKind.Light);
            SettingsThemePalette dark = SettingsThemePalette.Create(SettingsThemeKind.Dark);
            SettingsThemePalette highContrast = SettingsThemePalette.Create(SettingsThemeKind.HighContrast);

            Assert.Equal(Color.FromArgb(0xF3, 0xF4, 0xF6), light.Page);
            Assert.Equal(Color.White, light.Card);
            Assert.Equal(Color.FromArgb(0x11, 0x18, 0x27), light.Text);
            Assert.Equal(Color.FromArgb(0xD1, 0xD5, 0xDB), light.Border);
            Assert.Equal(Color.FromArgb(0x25, 0x63, 0xEB), light.Accent);

            Assert.Equal(Color.FromArgb(0x11, 0x18, 0x27), dark.Page);
            Assert.Equal(Color.FromArgb(0x1F, 0x29, 0x37), dark.Card);
            Assert.Equal(Color.FromArgb(0xF9, 0xFA, 0xFB), dark.Text);
            Assert.Equal(Color.FromArgb(0x4B, 0x55, 0x63), dark.Border);
            Assert.Equal(Color.FromArgb(0x60, 0xA5, 0xFA), dark.Accent);

            Assert.Equal(SystemColors.Window, highContrast.Page);
            Assert.Equal(SystemColors.Window, highContrast.Card);
            Assert.Equal(SystemColors.WindowText, highContrast.Text);
            Assert.Equal(SystemColors.Highlight, highContrast.Accent);
        }

        [Theory]
        [InlineData(255, 255, 255, true)]
        [InlineData(0, 0, 0, false)]
        [InlineData(240, 240, 240, true)]
        [InlineData(32, 32, 32, false)]
        public void ForegroundBrightnessClassifiesTheme(
            byte red,
            byte green,
            byte blue,
            bool expectedLight)
        {
            Assert.Equal(
                expectedLight,
                SystemSettingsThemeService.IsForegroundLight(red, green, blue));
        }
    }
}
