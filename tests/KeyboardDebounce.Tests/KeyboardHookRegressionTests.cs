using KeyboardDebounce;
using Xunit;

namespace KeyboardDebounce.Tests
{
    public sealed class KeyboardHookRegressionTests
    {
        [Fact]
        public void DisabledFilterBypassesWithoutLearning()
        {
            var settings = new AppSettings { Enabled = false };
            var learning = new LearningState();
            var filter = new KeyboardEventFilter(
                new DebounceEngine(settings, learning),
                delegate { return false; });

            KeyboardFilterResult result = filter.Process(65, 30, 0, true, 1000);

            Assert.False(result.Suppress);
            Assert.Null(result.Decision);
            Assert.Empty(learning.Keys);
        }

        [Fact]
        public void InjectedInputBypassesWithoutLearning()
        {
            var learning = new LearningState();
            var filter = NewFilter(new AppSettings(), learning);

            Assert.False(filter.Process(76, 38, NativeMethods.LLKHF_INJECTED, true, 1000).Suppress);
            Assert.False(filter.Process(76, 38, NativeMethods.LLKHF_INJECTED, true, 1010).Suppress);
            Assert.Empty(learning.Keys);
        }

        [Fact]
        public void ModifierKeysAlwaysBypassWithoutLearning()
        {
            var learning = new LearningState();
            var filter = NewFilter(new AppSettings(), learning);

            Assert.False(filter.Process(0xA2, 29, 0, true, 1000).Suppress);
            Assert.False(filter.Process(0xA2, 29, 0, false, 1010).Suppress);
            Assert.False(filter.Process(0xA2, 29, 0, true, 1020).Suppress);
            Assert.False(filter.Process(67, 46, 0, true, 1030).Suppress);

            Assert.False(learning.Keys.ContainsKey(0xA2));
            Assert.True(learning.Keys.ContainsKey(67));
        }

        [Fact]
        public void SuppressedChatterDownAlsoSuppressesItsUnmatchedUp()
        {
            var filter = NewFilter(
                new AppSettings { DefaultThresholdMs = 90 },
                new LearningState());

            Assert.False(filter.Process(65, 30, 0, true, 1000).Suppress);
            Assert.False(filter.Process(65, 30, 0, false, 1010).Suppress);
            Assert.True(filter.Process(65, 30, 0, true, 1020).Suppress);
            KeyboardFilterResult chatterUp = filter.Process(65, 30, 0, false, 1030);

            Assert.True(chatterUp.Suppress);
            Assert.Equal("paired-key-up", chatterUp.Decision.Reason);
        }

        [Fact]
        public void SuppressedHeldRepeatDoesNotHideFinalKeyUp()
        {
            var filter = NewFilter(
                new AppSettings { DefaultThresholdMs = 90 },
                new LearningState());

            Assert.False(filter.Process(8, 14, 0, true, 1000).Suppress);
            Assert.True(filter.Process(8, 14, 0, true, 1030).Suppress);
            KeyboardFilterResult finalUp = filter.Process(8, 14, 0, false, 1040);

            Assert.False(finalUp.Suppress);
            Assert.Equal("key-up", finalUp.Decision.Reason);
        }

        [Fact]
        public void AcceptedDownAfterSuppressionOwnsTheFollowingKeyUp()
        {
            var filter = NewFilter(
                new AppSettings { DefaultThresholdMs = 90 },
                new LearningState());

            filter.Process(65, 30, 0, true, 1000);
            filter.Process(65, 30, 0, false, 1010);
            Assert.True(filter.Process(65, 30, 0, true, 1020).Suppress);
            Assert.False(filter.Process(65, 30, 0, true, 1200).Suppress);

            Assert.False(filter.Process(65, 30, 0, false, 1210).Suppress);
        }

        [Fact]
        public void RapidRepressThenHoldEventuallyAllowsRepeatsAndPairsFinalUp()
        {
            var filter = NewFilter(
                new AppSettings
                {
                    DefaultThresholdMs = 250,
                    LongHoldBypassMs = 250
                },
                new LearningState());

            Assert.False(filter.Process(8, 14, 0, true, 1000).Suppress);
            Assert.False(filter.Process(8, 14, 0, false, 1010).Suppress);

            Assert.True(filter.Process(8, 14, 0, true, 1020).Suppress);
            Assert.True(filter.Process(8, 14, 0, false, 1030).Suppress);

            Assert.True(filter.Process(8, 14, 0, true, 1040).Suppress);
            Assert.True(filter.Process(8, 14, 0, true, 1270).Suppress);

            KeyboardFilterResult firstHeldRepeat = filter.Process(8, 14, 0, true, 1290);
            KeyboardFilterResult nextHeldRepeat = filter.Process(8, 14, 0, true, 1310);
            KeyboardFilterResult finalUp = filter.Process(8, 14, 0, false, 1320);

            Assert.False(firstHeldRepeat.Suppress);
            Assert.Equal("long-hold", firstHeldRepeat.Decision.Reason);
            Assert.False(nextHeldRepeat.Suppress);
            Assert.Equal("long-hold", nextHeldRepeat.Decision.Reason);
            Assert.False(finalUp.Suppress);
            Assert.Equal("key-up", finalUp.Decision.Reason);
        }

        [Fact]
        public void ResetClearsRuntimeButKeepsLearning()
        {
            var learning = new LearningState();
            var filter = NewFilter(new AppSettings(), learning);
            filter.Process(65, 30, 0, true, 1000);

            filter.ResetRuntimeState();

            Assert.True(learning.Keys.ContainsKey(65));
            Assert.Equal(1, learning.Keys[65].AcceptedCount);
            Assert.False(filter.Process(65, 30, 0, false, 1010).Suppress);
        }

        [Fact]
        public void PendingPhysicalKeyUpSurvivesPauseResetAndIsConsumedOnceWithoutLearning()
        {
            bool enabled = true;
            var learning = new LearningState();
            var engine = new DebounceEngine(new AppSettings(), learning);
            var filter = new KeyboardEventFilter(engine, delegate { return enabled; });
            filter.Process(65, 30, 0, true, 1000);
            filter.Process(65, 30, 0, false, 1010);
            Assert.True(filter.Process(65, 30, 0, true, 1020).Suppress);
            KeyLearningState learned = learning.Keys[65];
            long acceptedBeforePause = learned.AcceptedCount;
            long suppressedBeforePause = learned.SuppressedCount;
            System.DateTime lastSeenBeforePause = learned.LastSeenUtc;

            enabled = false;
            filter.ResetRuntimeState();
            KeyboardFilterResult pairedUp = filter.Process(65, 30, 0, false, 1030);
            KeyboardFilterResult secondUp = filter.Process(65, 30, 0, false, 1040);

            Assert.True(pairedUp.Suppress);
            Assert.Equal("paired-key-up", pairedUp.Decision.Reason);
            Assert.False(pairedUp.Decision.LearningStateChanged);
            Assert.False(secondUp.Suppress);
            Assert.Equal(acceptedBeforePause, learned.AcceptedCount);
            Assert.Equal(suppressedBeforePause, learned.SuppressedCount);
            Assert.Equal(lastSeenBeforePause, learned.LastSeenUtc);
        }

        [Fact]
        public void PendingPairedKeyUpDoesNotCarryLearningEvidenceAcrossRuntimeReset()
        {
            var settings = new AppSettings { DefaultThresholdMs = 90 };
            var learning = new LearningState();
            var filter = NewFilter(settings, learning);

            Assert.False(filter.Process(65, 30, 0, true, 1000).Suppress);
            Assert.False(filter.Process(65, 30, 0, false, 1010).Suppress);
            Assert.True(filter.Process(65, 30, 0, true, 1088).Suppress);

            filter.ResetRuntimeState();
            filter.ResetRuntimeState();

            KeyboardFilterResult pairedUp = filter.Process(65, 30, 0, false, 1098);
            KeyboardFilterResult nextBoundaryPress =
                filter.Process(65, 30, 0, true, 1176);

            Assert.True(pairedUp.Suppress);
            Assert.Equal("paired-key-up", pairedUp.Decision.Reason);
            Assert.True(nextBoundaryPress.Suppress);
            Assert.Equal(0, nextBoundaryPress.Decision.LearningAdjustmentMs);
            Assert.Equal(90, learning.Keys[65].ThresholdMs);
        }

        [Fact]
        public void PendingPhysicalKeyUpSurvivesIgnoreAndPreservesRapidRepressTiming()
        {
            var settings = new AppSettings();
            var learning = new LearningState();
            var engine = new DebounceEngine(settings, learning);
            var filter = new KeyboardEventFilter(engine, delegate { return true; });
            filter.Process(65, 30, 0, true, 1000);
            filter.Process(65, 30, 0, false, 1010);
            Assert.True(filter.Process(65, 30, 0, true, 1020).Suppress);

            engine.IgnoreKey(65);
            KeyboardFilterResult pairedUp = filter.Process(65, 30, 0, false, 1030);
            Assert.Empty(learning.Keys);
            engine.UnignoreKey(65);
            KeyboardFilterResult rapidRepress = filter.Process(65, 30, 0, true, 1040);

            Assert.True(pairedUp.Suppress);
            Assert.False(pairedUp.Decision.LearningStateChanged);
            Assert.True(rapidRepress.Suppress);
            Assert.Equal(20, rapidRepress.Decision.IntervalMs);
        }

        [Fact]
        public void BypassedPhysicalDownClearsPendingUpSoItsFinalUpPasses()
        {
            bool enabled = true;
            var learning = new LearningState();
            var filter = new KeyboardEventFilter(
                new DebounceEngine(new AppSettings(), learning),
                delegate { return enabled; });
            filter.Process(65, 30, 0, true, 1000);
            filter.Process(65, 30, 0, false, 1010);
            Assert.True(filter.Process(65, 30, 0, true, 1020).Suppress);

            enabled = false;
            filter.ResetRuntimeState();
            Assert.False(filter.Process(65, 30, 0, true, 1030).Suppress);
            Assert.False(filter.Process(65, 30, 0, false, 1040).Suppress);
            Assert.Equal(1, learning.Keys[65].SuppressedCount);
        }

        [Fact]
        public void InjectedUpDoesNotConsumePendingPhysicalUp()
        {
            bool enabled = true;
            var filter = new KeyboardEventFilter(
                new DebounceEngine(new AppSettings(), new LearningState()),
                delegate { return enabled; });
            filter.Process(65, 30, 0, true, 1000);
            filter.Process(65, 30, 0, false, 1010);
            Assert.True(filter.Process(65, 30, 0, true, 1020).Suppress);

            enabled = false;
            filter.ResetRuntimeState();
            Assert.False(filter.Process(
                65,
                30,
                NativeMethods.LLKHF_INJECTED,
                false,
                1030).Suppress);
            Assert.True(filter.Process(65, 30, 0, false, 1040).Suppress);
        }

        [Fact]
        public void GameModeFiltersOnlyConfiguredKeysAndFreezesLearning()
        {
            var settings = new AppSettings
            {
                GameModeThresholdMs = 45,
                GameModeFilteredKeys = { 65 }
            };
            var learning = new LearningState();
            bool gameModeActive = true;
            var filter = new KeyboardEventFilter(
                new DebounceEngine(settings, learning),
                delegate { return true; },
                delegate { return gameModeActive; });

            KeyboardFilterResult bypassedFirst = filter.Process(66, 48, 0, true, 1000);
            KeyboardFilterResult bypassedRepeat = filter.Process(66, 48, 0, true, 1010);
            filter.Process(66, 48, 0, false, 1020);
            KeyboardFilterResult filteredFirst = filter.Process(65, 30, 0, true, 2000);
            KeyboardFilterResult filteredRepeat = filter.Process(65, 30, 0, true, 2020);

            Assert.False(bypassedFirst.Suppress);
            Assert.False(bypassedRepeat.Suppress);
            Assert.Null(bypassedFirst.Decision);
            Assert.Null(bypassedRepeat.Decision);
            Assert.False(filteredFirst.Suppress);
            Assert.True(filteredRepeat.Suppress);
            Assert.False(filteredFirst.Decision.LearningStateChanged);
            Assert.False(filteredRepeat.Decision.LearningStateChanged);
            Assert.Empty(learning.Keys);
        }

        [Fact]
        public void GameModeWithNoFilteredKeysBypassesEveryOrdinaryKeyWithoutLearning()
        {
            var learning = new LearningState();
            var filter = new KeyboardEventFilter(
                new DebounceEngine(new AppSettings(), learning),
                delegate { return true; },
                delegate { return true; });

            KeyboardFilterResult firstDown = filter.Process(65, 30, 0, true, 1000);
            KeyboardFilterResult repeatedDown = filter.Process(65, 30, 0, true, 1010);
            KeyboardFilterResult keyUp = filter.Process(65, 30, 0, false, 1020);

            Assert.False(firstDown.Suppress);
            Assert.False(repeatedDown.Suppress);
            Assert.False(keyUp.Suppress);
            Assert.Null(firstDown.Decision);
            Assert.Null(repeatedDown.Decision);
            Assert.Null(keyUp.Decision);
            Assert.Empty(learning.Keys);
        }

        [Fact]
        public void HeldKeyStaysBypassedUntilUpAcrossGameModeTransitions()
        {
            bool gameModeActive = false;
            var settings = new AppSettings
            {
                DefaultThresholdMs = 90,
                GameModeFilteredKeys = { 65 }
            };
            var learning = new LearningState();
            var filter = new KeyboardEventFilter(
                new DebounceEngine(settings, learning),
                delegate { return true; },
                delegate { return gameModeActive; });

            Assert.False(filter.Process(66, 48, 0, true, 1000).Suppress);
            gameModeActive = true;
            KeyboardFilterResult heldRepeat = filter.Process(66, 48, 0, true, 1010);
            gameModeActive = false;
            KeyboardFilterResult finalUp = filter.Process(66, 48, 0, false, 1020);
            KeyboardFilterResult nextPress = filter.Process(66, 48, 0, true, 1030);

            Assert.False(heldRepeat.Suppress);
            Assert.Null(heldRepeat.Decision);
            Assert.False(finalUp.Suppress);
            Assert.Null(finalUp.Decision);
            Assert.False(nextPress.Suppress);
            Assert.NotNull(nextPress.Decision);
            Assert.Equal(2, learning.Keys[66].AcceptedCount);
        }

        [Fact]
        public void KeyPressedDuringPauseStaysBypassedAfterResumeUntilItsUp()
        {
            bool enabled = false;
            var learning = new LearningState();
            var filter = new KeyboardEventFilter(
                new DebounceEngine(new AppSettings(), learning),
                delegate { return enabled; });

            KeyboardFilterResult pausedDown = filter.Process(65, 30, 0, true, 1000);
            enabled = true;
            KeyboardFilterResult heldRepeat = filter.Process(65, 30, 0, true, 1010);
            KeyboardFilterResult finalUp = filter.Process(65, 30, 0, false, 1020);
            KeyboardFilterResult nextPress = filter.Process(65, 30, 0, true, 1030);

            Assert.Null(pausedDown.Decision);
            Assert.Null(heldRepeat.Decision);
            Assert.Null(finalUp.Decision);
            Assert.False(pausedDown.Suppress);
            Assert.False(heldRepeat.Suppress);
            Assert.False(finalUp.Suppress);
            Assert.False(nextPress.Suppress);
            Assert.Equal(1, learning.Keys[65].AcceptedCount);
        }

        [Fact]
        public void SameVirtualKeyPhysicalIdentitiesKeepIndependentPairing()
        {
            var filter = NewFilter(
                new AppSettings { DefaultThresholdMs = 90 },
                new LearningState());

            Assert.False(filter.Process(13, 28, 0, true, 1000).Suppress);
            Assert.False(filter.Process(13, 28, 0, false, 1010).Suppress);
            Assert.True(filter.Process(13, 28, 0, true, 1020).Suppress);
            Assert.False(filter.Process(
                13,
                28,
                NativeMethods.LLKHF_EXTENDED,
                true,
                1025).Suppress);

            KeyboardFilterResult mainEnterUp = filter.Process(13, 28, 0, false, 1030);
            KeyboardFilterResult numpadEnterUp = filter.Process(
                13,
                28,
                NativeMethods.LLKHF_EXTENDED,
                false,
                1035);

            Assert.True(mainEnterUp.Suppress);
            Assert.Equal("paired-key-up", mainEnterUp.Decision.Reason);
            Assert.False(numpadEnterUp.Suppress);
            Assert.Equal("key-up", numpadEnterUp.Decision.Reason);
        }

        [Fact]
        public void ParsesSupportedHotkeyAndRejectsReservedF12()
        {
            HotkeyBinding binding;
            string error;

            Assert.True(HotkeyBinding.TryParse("Ctrl+Alt+F11", out binding, out error));
            Assert.Null(error);
            Assert.Equal(NativeMethods.VK_F11, binding.VirtualKeyCode);
            Assert.Equal(
                NativeMethods.MOD_CONTROL | NativeMethods.MOD_ALT | NativeMethods.MOD_NOREPEAT,
                binding.Modifiers);

            Assert.False(HotkeyBinding.TryParse("Ctrl+Alt+F12", out binding, out error));
            Assert.Contains("F12", error);
        }

        private static KeyboardEventFilter NewFilter(AppSettings settings, LearningState learning)
        {
            return new KeyboardEventFilter(
                new DebounceEngine(settings, learning),
                delegate { return true; });
        }
    }
}
