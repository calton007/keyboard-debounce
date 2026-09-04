using System;
using System.IO;
using System.Threading;
using KeyboardDebounce;
using Xunit;

namespace KeyboardDebounce.Tests
{
    public sealed class SettingsFormSmokeTests
    {
        [Fact]
        public void SettingsFormConstructsWithProcessGameModeConfiguration()
        {
            Exception failure = null;
            var thread = new Thread(new ThreadStart(delegate
            {
                string directory = Path.Combine(
                    Path.GetTempPath(),
                    "KeyboardDebounce.Tests",
                    Guid.NewGuid().ToString("N"));
                try
                {
                    var settings = new AppSettings
                    {
                        ProcessGameModeEnabled = true,
                        GameProcesses = new System.Collections.Generic.List<string>
                        {
                            "example-game.exe"
                        },
                        GameModeFilteredKeys = new System.Collections.Generic.List<int>
                        {
                            65
                        }
                    };
                    var learning = new LearningState();
                    learning.Keys[65] = new KeyLearningState { ThresholdMs = 90 };
                    var engine = new DebounceEngine(settings, learning);
                    var store = new SettingsStore(directory);

                    using (var form = new SettingsForm(
                        settings,
                        learning,
                        engine,
                        store,
                        delegate { },
                        delegate { },
                        delegate { return "ok"; },
                        delegate { return "已激活（example-game）"; }))
                    {
                        form.RefreshData();
                    }
                }
                catch (Exception error)
                {
                    failure = error;
                }
                finally
                {
                    if (Directory.Exists(directory))
                    {
                        Directory.Delete(directory, true);
                    }
                }
            }));
            thread.SetApartmentState(ApartmentState.STA);
            thread.Start();

            Assert.True(thread.Join(TimeSpan.FromSeconds(15)), "Settings form smoke test timed out.");
            Assert.Null(failure);
        }
    }
}
