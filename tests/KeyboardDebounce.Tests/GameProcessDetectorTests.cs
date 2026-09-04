using System.Collections.Generic;
using System.ComponentModel;
using KeyboardDebounce;
using Xunit;

namespace KeyboardDebounce.Tests
{
    public sealed class GameProcessDetectorTests
    {
        [Theory]
        [InlineData("game", "game")]
        [InlineData("GAME.EXE", "GAME")]
        [InlineData("  \"C:\\Games\\Example Game\\client.exe\"  ", "client")]
        public void NormalizesExecutableNamesAndPaths(string value, string expected)
        {
            Assert.Equal(expected, GameProcessDetector.NormalizeProcessName(value));
        }

        [Fact]
        public void DetectsOnlyConfiguredRunningProcessesCaseInsensitively()
        {
            GameProcessDetectionResult active = GameProcessDetector.Detect(
                new[] { "client.exe", "other" },
                new[] { "explorer", "CLIENT" });
            GameProcessDetectionResult inactive = GameProcessDetector.Detect(
                new[] { "client.exe" },
                new[] { "explorer", "other" });

            Assert.True(active.IsActive);
            Assert.Equal("CLIENT", active.MatchedProcessName, ignoreCase: true);
            Assert.False(inactive.IsActive);
        }

        [Fact]
        public void RunningExecutableNamesAreNormalizedDeduplicatedSortedAndFiltered()
        {
            var processes = new[]
            {
                new GameProcessDetector.RunningProcessSnapshot(30, "zeta.exe"),
                new GameProcessDetector.RunningProcessSnapshot(20, "Beta"),
                new GameProcessDetector.RunningProcessSnapshot(21, "beta.EXE"),
                new GameProcessDetector.RunningProcessSnapshot(10, "alpha"),
                new GameProcessDetector.RunningProcessSnapshot(0, "Idle"),
                new GameProcessDetector.RunningProcessSnapshot(4, "System"),
                new GameProcessDetector.RunningProcessSnapshot(99, "KeyboardDebounce"),
                new GameProcessDetector.RunningProcessSnapshot(40, "   ")
            };

            IReadOnlyList<string> result =
                GameProcessDetector.NormalizeRunningExecutableNames(processes, 99);

            Assert.Equal(new[] { "alpha", "Beta", "zeta" }, result);
        }

        [Fact]
        public void DetectUsesConfiguredOrderWhenMultipleProcessesMatch()
        {
            string[] configured = { "second.exe", "FIRST", "second" };

            GameProcessDetectionResult firstEnumeration = GameProcessDetector.Detect(
                configured,
                new[] { "first", "SECOND" });
            GameProcessDetectionResult reversedEnumeration = GameProcessDetector.Detect(
                configured,
                new[] { "SECOND", "first" });

            Assert.True(firstEnumeration.IsActive);
            Assert.Equal("second", firstEnumeration.MatchedProcessName);
            Assert.Equal(
                new[] { "second", "FIRST" },
                firstEnumeration.RunningConfiguredExecutables);
            Assert.True(reversedEnumeration.IsActive);
            Assert.Equal("second", reversedEnumeration.MatchedProcessName);
            Assert.Equal(
                new[] { "second", "FIRST" },
                reversedEnumeration.RunningConfiguredExecutables);
        }

        [Theory]
        [InlineData("")]
        [InlineData("   ")]
        [InlineData("\"\"")]
        public void RejectsEmptyProcessNames(string value)
        {
            Assert.Equal("", GameProcessDetector.NormalizeProcessName(value));
        }

        [Theory]
        [InlineData("client", "client.exe")]
        [InlineData("client.exe", "client.exe")]
        [InlineData("C:\\Games\\client.EXE", "client.exe")]
        public void FormatsExecutableSuffixExactlyOnce(string value, string expected)
        {
            Assert.Equal(expected, GameProcessDetector.FormatExecutableName(value));
        }

        [Fact]
        public void PartialScanStillReportsEveryConfirmedConfiguredMatch()
        {
            GameProcessDetectionResult result = GameProcessDetector.Detect(
                new[] { "primary", "secondary.exe", "missing" },
                new[] { "secondary", "PRIMARY.exe" },
                GameModeDetectionHealth.Partial,
                "Win32Exception - Access denied");

            Assert.True(result.IsActive);
            Assert.Equal("primary", result.MatchedProcessName);
            Assert.Equal(
                new[] { "primary", "secondary" },
                result.RunningConfiguredExecutables);
            Assert.Equal(GameModeDetectionHealth.Partial, result.DetectionHealth);
            Assert.Equal("Win32Exception - Access denied", result.ErrorMessage);
        }

        [Fact]
        public void UnreadableProcessObservationFlowsThroughProductionScanAsPartial()
        {
            var originalError = new Win32Exception(5, "Access denied");
            var observations = new[]
            {
                new GameProcessDetector.RunningProcessSnapshot(10, "primary.exe"),
                new GameProcessDetector.RunningProcessSnapshot(originalError),
                new GameProcessDetector.RunningProcessSnapshot(20, "secondary")
            };
            GameProcessDetector.RunningExecutableScanResult scan =
                GameProcessDetector.CreateRunningExecutableScanResult(
                    observations,
                    99);

            GameProcessDetectionResult result = GameProcessDetector.Detect(
                new[] { "secondary.exe", "primary" },
                scan);

            Assert.Equal(GameModeDetectionHealth.Partial, scan.DetectionHealth);
            Assert.Same(originalError, scan.OriginalError);
            Assert.Equal(GameModeDetectionHealth.Partial, result.DetectionHealth);
            Assert.Equal(
                new[] { "secondary", "primary" },
                result.RunningConfiguredExecutables);
            Assert.Contains("Access denied", result.ErrorMessage);
        }
    }
}
