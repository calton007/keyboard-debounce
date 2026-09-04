using System;
using System.Collections.Generic;
using KeyboardDebounce;
using Xunit;

namespace KeyboardDebounce.Tests
{
    public sealed class TrayGameProcessStateTests
    {
        [Fact]
        public void RequestsDuringActiveCheckCoalesceIntoOneReservedRerun()
        {
            var gate = new GameProcessCheckGate();

            Assert.True(gate.TryBegin());
            Assert.False(gate.TryBegin());
            Assert.False(gate.TryBegin());
            Assert.True(gate.IsRunning);
            Assert.True(gate.HasPendingRequest);

            Assert.True(gate.Complete(true));
            Assert.True(gate.IsRunning);
            Assert.False(gate.HasPendingRequest);

            Assert.False(gate.Complete(true));
            Assert.False(gate.IsRunning);
            Assert.False(gate.HasPendingRequest);
        }

        [Fact]
        public void PendingRerunIsDiscardedWhenApplicationIsExiting()
        {
            var gate = new GameProcessCheckGate();

            Assert.True(gate.TryBegin());
            Assert.False(gate.TryBegin());

            Assert.False(gate.Complete(false));
            Assert.False(gate.IsRunning);
            Assert.False(gate.HasPendingRequest);
        }

        [Fact]
        public void EffectiveModeImmediatelyHonorsCurrentToggleAndList()
        {
            Assert.True(GameProcessConfigurationState.IsEffectivelyActive(
                true,
                true,
                new[] { "game" }));
            Assert.False(GameProcessConfigurationState.IsEffectivelyActive(
                true,
                false,
                new[] { "game" }));
            Assert.False(GameProcessConfigurationState.IsEffectivelyActive(
                true,
                true,
                Array.Empty<string>()));
            Assert.False(GameProcessConfigurationState.IsEffectivelyActive(
                true,
                true,
                null));
            Assert.False(GameProcessConfigurationState.IsEffectivelyActive(
                false,
                true,
                new[] { "game" }));
        }

        [Fact]
        public void CachedMatchSurvivesOnlyWhileSameExecutableRemainsConfigured()
        {
            IReadOnlyList<string> configured = new[]
            {
                "other.exe",
                "C:\\Games\\Client.EXE"
            };

            Assert.True(GameProcessConfigurationState.KeepsCachedMatch(
                true,
                configured,
                "client"));
            Assert.False(GameProcessConfigurationState.KeepsCachedMatch(
                false,
                configured,
                "client"));
            Assert.False(GameProcessConfigurationState.KeepsCachedMatch(
                true,
                new[] { "other" },
                "client"));
            Assert.False(GameProcessConfigurationState.KeepsCachedMatch(
                true,
                Array.Empty<string>(),
                "client"));
        }

        [Fact]
        public void CheckResultIsAcceptedOnlyForCurrentConfigurationVersion()
        {
            Assert.True(GameProcessConfigurationState.ShouldAcceptCheckResult(
                false,
                7,
                7));
            Assert.False(GameProcessConfigurationState.ShouldAcceptCheckResult(
                false,
                7,
                8));
            Assert.False(GameProcessConfigurationState.ShouldAcceptCheckResult(
                true,
                7,
                7));
        }

        [Fact]
        public void PartialDetectionKeepsActualModeButPublishesConfirmedMatches()
        {
            var previous = new GameModeRuntimeSnapshot(
                true,
                "primary.exe",
                new[] { "primary", "secondary" },
                GameModeDetectionHealth.Healthy,
                "");
            var partial = new GameProcessDetectionResult(
                true,
                "secondary",
                new[] { "secondary" },
                GameModeDetectionHealth.Partial,
                "Win32Exception - Access denied");

            GameModeRuntimeSnapshot next =
                GameProcessConfigurationState.ApplyDetection(
                    previous,
                    true,
                    new[] { "primary", "secondary" },
                    partial);

            Assert.True(next.IsActive);
            Assert.Equal("primary", next.ActiveExecutable);
            Assert.Equal(new[] { "secondary" }, next.RunningConfiguredExecutables);
            Assert.Equal(new[] { "primary" }, next.UnknownConfiguredExecutables);
            Assert.Equal(GameModeDetectionHealth.Partial, next.DetectionHealth);
            Assert.Equal("Win32Exception - Access denied", next.ErrorMessage);
        }

        [Fact]
        public void FailedDetectionKeepsPreviousModeAndRowState()
        {
            var previous = new GameModeRuntimeSnapshot(
                true,
                "primary",
                new[] { "primary" },
                GameModeDetectionHealth.Healthy,
                "");
            var failed = new GameProcessDetectionResult(
                false,
                "",
                Array.Empty<string>(),
                GameModeDetectionHealth.Failed,
                "InvalidOperationException - enumeration failed");

            GameModeRuntimeSnapshot next =
                GameProcessConfigurationState.ApplyDetection(
                    previous,
                    true,
                    new[] { "primary", "secondary" },
                    failed);

            Assert.True(next.IsActive);
            Assert.Equal("primary", next.ActiveExecutable);
            Assert.Equal(new[] { "primary" }, next.RunningConfiguredExecutables);
            Assert.Empty(next.UnknownConfiguredExecutables);
            Assert.Equal(GameModeDetectionHealth.Failed, next.DetectionHealth);
        }

        [Fact]
        public void FailedDetectionAfterPartialKeepsUnknownRowState()
        {
            var partialSnapshot = new GameModeRuntimeSnapshot(
                true,
                "primary",
                new[] { "secondary" },
                GameModeDetectionHealth.Partial,
                "Win32Exception - Access denied",
                new[] { "primary", "tertiary" });
            var failed = new GameProcessDetectionResult(
                false,
                "",
                Array.Empty<string>(),
                GameModeDetectionHealth.Failed,
                "InvalidOperationException - enumeration failed");

            GameModeRuntimeSnapshot next =
                GameProcessConfigurationState.ApplyDetection(
                    partialSnapshot,
                    true,
                    new[] { "primary", "secondary", "tertiary" },
                    failed);

            Assert.True(next.IsActive);
            Assert.Equal("primary", next.ActiveExecutable);
            Assert.Equal(new[] { "secondary" }, next.RunningConfiguredExecutables);
            Assert.Equal(
                new[] { "primary", "tertiary" },
                next.UnknownConfiguredExecutables);
            Assert.Equal(GameModeDetectionHealth.Failed, next.DetectionHealth);
        }

        [Fact]
        public void HealthyDetectionAfterFailureReplacesRetainedStateAndClearsError()
        {
            var failedSnapshot = new GameModeRuntimeSnapshot(
                true,
                "primary",
                new[] { "primary" },
                GameModeDetectionHealth.Failed,
                "InvalidOperationException - enumeration failed");
            var healthy = new GameProcessDetectionResult(
                false,
                "",
                Array.Empty<string>(),
                GameModeDetectionHealth.Healthy,
                "");

            GameModeRuntimeSnapshot recovered =
                GameProcessConfigurationState.ApplyDetection(
                    failedSnapshot,
                    true,
                    new[] { "primary", "secondary" },
                    healthy);

            Assert.False(recovered.IsActive);
            Assert.Equal("", recovered.ActiveExecutable);
            Assert.Empty(recovered.RunningConfiguredExecutables);
            Assert.Equal(GameModeDetectionHealth.Healthy, recovered.DetectionHealth);
            Assert.Equal("", recovered.ErrorMessage);
        }

        [Fact]
        public void HealthyDetectionAfterPartialActivatesConfirmedMatchAndClearsUnknownState()
        {
            var partialSnapshot = new GameModeRuntimeSnapshot(
                false,
                "",
                new[] { "primary" },
                GameModeDetectionHealth.Partial,
                "Win32Exception - Access denied",
                new[] { "secondary" });
            var healthy = new GameProcessDetectionResult(
                true,
                "primary",
                new[] { "primary" },
                GameModeDetectionHealth.Healthy,
                "");

            GameModeRuntimeSnapshot recovered =
                GameProcessConfigurationState.ApplyDetection(
                    partialSnapshot,
                    true,
                    new[] { "primary", "secondary" },
                    healthy);

            Assert.True(recovered.IsActive);
            Assert.Equal("primary", recovered.ActiveExecutable);
            Assert.Equal(new[] { "primary" }, recovered.RunningConfiguredExecutables);
            Assert.Empty(recovered.UnknownConfiguredExecutables);
            Assert.Equal(GameModeDetectionHealth.Healthy, recovered.DetectionHealth);
            Assert.Equal("", recovered.ErrorMessage);
        }

        [Fact]
        public void RemovingActiveMatchKeepsRemainingRowsAcrossNextFailedDetection()
        {
            var previous = new GameModeRuntimeSnapshot(
                true,
                "primary",
                new[] { "primary", "secondary" },
                GameModeDetectionHealth.Healthy,
                "");

            GameModeRuntimeSnapshot reconciled =
                GameProcessConfigurationState.ReconcileConfiguration(
                    previous,
                    true,
                    new[] { "secondary" });

            Assert.False(reconciled.IsActive);
            Assert.Equal("", reconciled.ActiveExecutable);
            Assert.Equal(new[] { "secondary" }, reconciled.RunningConfiguredExecutables);
            Assert.Equal(GameModeDetectionHealth.Healthy, reconciled.DetectionHealth);

            var failed = new GameProcessDetectionResult(
                false,
                "",
                Array.Empty<string>(),
                GameModeDetectionHealth.Failed,
                "InvalidOperationException - enumeration failed");
            GameModeRuntimeSnapshot afterFailure =
                GameProcessConfigurationState.ApplyDetection(
                    reconciled,
                    true,
                    new[] { "secondary" },
                    failed);

            Assert.False(afterFailure.IsActive);
            Assert.Equal(new[] { "secondary" }, afterFailure.RunningConfiguredExecutables);
            Assert.Equal(GameModeDetectionHealth.Failed, afterFailure.DetectionHealth);
        }

        [Theory]
        [InlineData(false, "primary")]
        [InlineData(true, "secondary")]
        [InlineData(true, "")]
        public void ConfigurationChangesImmediatelyDeactivateInvalidCachedMatch(
            bool enabled,
            string remainingExecutable)
        {
            var previous = new GameModeRuntimeSnapshot(
                true,
                "primary",
                new[] { "primary", "secondary" },
                GameModeDetectionHealth.Failed,
                "stale error");
            IReadOnlyList<string> configured = String.IsNullOrEmpty(remainingExecutable)
                ? Array.Empty<string>()
                : new[] { remainingExecutable };

            GameModeRuntimeSnapshot next =
                GameProcessConfigurationState.ReconcileConfiguration(
                    previous,
                    enabled,
                    configured);

            Assert.False(next.IsActive);
            Assert.Equal("", next.ActiveExecutable);
            Assert.Equal(GameModeDetectionHealth.Healthy, next.DetectionHealth);
            Assert.Equal("", next.ErrorMessage);
        }

        [Fact]
        public void RunningSetChangeIsACompleteRuntimeSnapshotChange()
        {
            var first = new GameModeRuntimeSnapshot(
                true,
                "primary",
                new[] { "primary" },
                GameModeDetectionHealth.Healthy,
                "");
            var second = new GameModeRuntimeSnapshot(
                true,
                "primary",
                new[] { "primary", "secondary" },
                GameModeDetectionHealth.Healthy,
                "");

            Assert.False(first.Equals(second));
        }
    }
}
