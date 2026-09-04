using System;
using System.IO;
using System.Runtime.Serialization.Json;
using System.Text;
using KeyboardDebounce;
using Xunit;

namespace KeyboardDebounce.Tests
{
    public sealed class DebounceEngineRegressionTests
    {
        [Fact]
        public void ContinuousChatterResetsSilenceWindow()
        {
            var settings = new AppSettings
            {
                DefaultThresholdMs = 90,
                LongHoldBypassMs = 1000
            };
            DebounceEngine engine = NewEngine(settings, out LearningState learning);

            DebounceDecision first = engine.Process(Down(65, 1000));
            DebounceDecision second = engine.Process(Down(65, 1060));
            DebounceDecision third = engine.Process(Down(65, 1120));

            Assert.False(first.Suppress);
            Assert.True(second.Suppress);
            Assert.True(third.Suppress);
            Assert.Equal(1, learning.Keys[65].AcceptedCount);
            Assert.Equal(2, learning.Keys[65].SuppressedCount);
        }

        [Fact]
        public void LongHoldKeepsAllowingShortIntervalRepeats()
        {
            var settings = new AppSettings
            {
                DefaultThresholdMs = 90,
                LongHoldBypassMs = 250
            };
            DebounceEngine engine = NewEngine(settings, out LearningState learning);

            Assert.False(engine.Process(Down(8, 1000)).Suppress);
            Assert.True(engine.Process(Down(8, 1060)).Suppress);

            DebounceDecision firstLongHoldRepeat = engine.Process(Down(8, 1260));
            DebounceDecision nextShortRepeat = engine.Process(Down(8, 1280));

            Assert.False(firstLongHoldRepeat.Suppress);
            Assert.Equal("long-hold", firstLongHoldRepeat.Reason);
            Assert.False(nextShortRepeat.Suppress);
            Assert.Equal("long-hold", nextShortRepeat.Reason);
            Assert.Equal(3, learning.Keys[8].AcceptedCount);
        }

        [Fact]
        public void OrdinaryReleaseSeparatedClicksNeverDriftUpward()
        {
            var settings = new AppSettings
            {
                DefaultThresholdMs = 160,
                LongHoldBypassMs = 500
            };
            DebounceEngine engine = NewEngine(settings, out LearningState learning);
            long timestampMs = 1000;

            for (int index = 0; index < 40; index++)
            {
                DebounceDecision down = engine.Process(Down(65, timestampMs));
                engine.Process(Up(65, timestampMs + 10));

                Assert.False(down.Suppress);
                Assert.Equal(0, down.LearningAdjustmentMs);
                timestampMs += 220;
            }

            Assert.Equal(160, learning.Keys[65].ThresholdMs);
            Assert.Equal(40, learning.Keys[65].AcceptedCount);
        }

        [Fact]
        public void HeldNearBoundaryRepeatsDoNotBecomeLearningEvidence()
        {
            var settings = new AppSettings
            {
                DefaultThresholdMs = 90,
                LongHoldBypassMs = 500
            };
            DebounceEngine engine = NewEngine(settings, out LearningState learning);

            Assert.False(engine.Process(Down(65, 1000)).Suppress);
            Assert.True(engine.Process(Down(65, 1088)).Suppress);
            Assert.True(engine.Process(Down(65, 1176)).Suppress);

            Assert.Equal(90, learning.Keys[65].ThresholdMs);
            Assert.Equal(0, learning.Keys[65].LastAdjustmentMs);
        }

        [Fact]
        public void NearBoundaryLearningConvertsEffectiveIntervalToBaseThreshold()
        {
            var settings = new AppSettings
            {
                DefaultThresholdMs = 90,
                GlobalSensitivity = 1.5
            };
            DebounceEngine engine = NewEngine(settings, out LearningState learning);

            Assert.False(engine.Process(Down(65, 1000)).Suppress);
            engine.Process(Up(65, 1010));
            Assert.True(engine.Process(Down(65, 1133)).Suppress);
            engine.Process(Up(65, 1143));
            DebounceDecision learned = engine.Process(Down(65, 1266));

            Assert.True(learned.Suppress);
            Assert.Equal(135, learned.EffectiveThresholdMs);
            Assert.Equal(2, learned.LearningAdjustmentMs);
            Assert.Equal(92, learning.Keys[65].ThresholdMs);
        }

        [Fact]
        public void AutomaticLearningStopsAtDefaultThresholdPlusForty()
        {
            var settings = new AppSettings
            {
                DefaultThresholdMs = 90,
                GlobalSensitivity = 0.5
            };
            DebounceEngine engine = NewEngine(settings, out LearningState learning);
            long timestampMs = 1000;
            Assert.False(engine.Process(Down(65, timestampMs)).Suppress);
            engine.Process(Up(65, timestampMs + 1));

            DebounceDecision last = null;
            int[] nearBoundaryIntervals = { 45, 45, 50, 50, 55, 55, 60, 60, 65, 65 };
            foreach (int interval in nearBoundaryIntervals)
            {
                timestampMs += interval;
                last = engine.Process(Down(65, timestampMs));
                Assert.True(last.Suppress);
                engine.Process(Up(65, timestampMs + 1));
            }

            Assert.NotNull(last);
            Assert.Equal(0, last.LearningAdjustmentMs);
            Assert.Equal(130, learning.Keys[65].ThresholdMs);
        }

        [Fact]
        public void SuppressionLearningDoesNotLowerThresholdAlreadyAboveAutomaticCap()
        {
            var settings = new AppSettings
            {
                DefaultThresholdMs = 90,
                GlobalSensitivity = 0.5
            };
            var learning = new LearningState();
            learning.Keys[65] = new KeyLearningState { ThresholdMs = 150 };
            var engine = new DebounceEngine(settings, learning);

            engine.Process(Down(65, 1000));
            engine.Process(Up(65, 1010));
            Assert.True(engine.Process(Down(65, 1075)).Suppress);
            engine.Process(Up(65, 1085));
            DebounceDecision second = engine.Process(Down(65, 1150));

            Assert.True(second.Suppress);
            Assert.Equal(0, second.LearningAdjustmentMs);
            Assert.Equal(150, learning.Keys[65].ThresholdMs);
        }

        [Fact]
        public void NonBoundarySuppressionBreaksNearBoundaryEvidenceRun()
        {
            var settings = new AppSettings { DefaultThresholdMs = 90 };
            DebounceEngine engine = NewEngine(settings, out LearningState learning);

            engine.Process(Down(65, 1000));
            engine.Process(Up(65, 1010));
            Assert.True(engine.Process(Down(65, 1088)).Suppress);
            engine.Process(Up(65, 1098));
            Assert.True(engine.Process(Down(65, 1120)).Suppress);
            engine.Process(Up(65, 1130));
            DebounceDecision afterInterruptedRun = engine.Process(Down(65, 1208));

            Assert.True(afterInterruptedRun.Suppress);
            Assert.Equal(0, afterInterruptedRun.LearningAdjustmentMs);
            Assert.Equal(90, learning.Keys[65].ThresholdMs);
        }

        [Fact]
        public void AcceptedEventBreaksNearBoundaryEvidenceRun()
        {
            var settings = new AppSettings { DefaultThresholdMs = 90 };
            DebounceEngine engine = NewEngine(settings, out LearningState learning);

            engine.Process(Down(65, 1000));
            engine.Process(Up(65, 1010));
            Assert.True(engine.Process(Down(65, 1088)).Suppress);
            engine.Process(Up(65, 1098));
            Assert.False(engine.Process(Down(65, 1188)).Suppress);
            engine.Process(Up(65, 1198));
            DebounceDecision afterInterruptedRun = engine.Process(Down(65, 1276));

            Assert.True(afterInterruptedRun.Suppress);
            Assert.Equal(0, afterInterruptedRun.LearningAdjustmentMs);
            Assert.Equal(90, learning.Keys[65].ThresholdMs);
        }

        [Fact]
        public void LongHoldBreaksNearBoundaryEvidenceRun()
        {
            var settings = new AppSettings
            {
                DefaultThresholdMs = 90,
                LongHoldBypassMs = 500
            };
            DebounceEngine engine = NewEngine(settings, out LearningState learning);

            engine.Process(Down(65, 1000));
            engine.Process(Up(65, 1010));
            Assert.True(engine.Process(Down(65, 1088)).Suppress);
            DebounceDecision longHold = engine.Process(Down(65, 1588));
            engine.Process(Up(65, 1598));
            DebounceDecision afterInterruptedRun = engine.Process(Down(65, 1676));

            Assert.False(longHold.Suppress);
            Assert.Equal("long-hold", longHold.Reason);
            Assert.True(afterInterruptedRun.Suppress);
            Assert.Equal(0, afterInterruptedRun.LearningAdjustmentMs);
            Assert.Equal(90, learning.Keys[65].ThresholdMs);
        }

        [Fact]
        public void ModeSwitchBreaksNearBoundaryEvidenceRun()
        {
            var settings = new AppSettings
            {
                DefaultThresholdMs = 90,
                GameModeThresholdMs = 45
            };
            DebounceEngine engine = NewEngine(settings, out LearningState learning);

            engine.Process(Down(65, 1000));
            engine.Process(Up(65, 1010));
            Assert.True(engine.Process(Down(65, 1088)).Suppress);

            Assert.False(engine.Process(Down(65, 2000), DebounceMode.Game).Suppress);
            engine.Process(Up(65, 2010), DebounceMode.Game);
            Assert.False(engine.Process(Down(65, 3000), DebounceMode.Normal).Suppress);
            engine.Process(Up(65, 3010), DebounceMode.Normal);
            DebounceDecision afterModeSwitch = engine.Process(
                Down(65, 3088),
                DebounceMode.Normal);

            Assert.True(afterModeSwitch.Suppress);
            Assert.Equal(0, afterModeSwitch.LearningAdjustmentMs);
            Assert.Equal(90, learning.Keys[65].ThresholdMs);
        }

        [Fact]
        public void StableInputDecaysAfterPreviousSuppression()
        {
            var settings = new AppSettings
            {
                DefaultThresholdMs = 120,
                LongHoldBypassMs = 500
            };
            var learning = new LearningState();
            learning.Keys[65] = new KeyLearningState { ThresholdMs = 140 };
            var engine = new DebounceEngine(settings, learning);

            engine.Process(Down(65, 1000));
            engine.Process(Up(65, 1010));
            Assert.True(engine.Process(Down(65, 1138)).Suppress);
            engine.Process(Up(65, 1148));

            long timestampMs = 2000;
            for (int i = 0; i < 12; i++)
            {
                Assert.False(engine.Process(Down(65, timestampMs)).Suppress);
                engine.Process(Up(65, timestampMs + 10));
                timestampMs += 600;
            }

            KeyLearningState key = learning.Keys[65];
            Assert.Equal(135, key.ThresholdMs);
            Assert.Equal("stable-decay", key.LastAdjustmentReason);
            Assert.Equal(1, key.SuppressedCount);
            Assert.Equal(13, key.AcceptedCount);
        }

        [Fact]
        public void OrdinaryEventDoesNotEraseLastRealAdjustment()
        {
            var settings = new AppSettings
            {
                DefaultThresholdMs = 90,
                LongHoldBypassMs = 500
            };
            DebounceEngine engine = NewEngine(settings, out LearningState learning);

            engine.Process(Down(65, 1000));
            engine.Process(Up(65, 1010));
            engine.Process(Down(65, 1088));
            engine.Process(Up(65, 1098));
            DebounceDecision adjusted = engine.Process(Down(65, 1176));
            KeyLearningState key = learning.Keys[65];
            int adjustmentMs = key.LastAdjustmentMs;
            string adjustmentReason = key.LastAdjustmentReason;
            DateTime adjustedUtc = key.LastAdjustedUtc;

            engine.Process(Up(65, 1186));
            DebounceDecision ordinary = engine.Process(Down(65, 2000));

            Assert.True(adjusted.LearningAdjustmentMs > 0);
            Assert.Equal(0, ordinary.LearningAdjustmentMs);
            Assert.Equal("", ordinary.LearningReason);
            Assert.Equal(adjustmentMs, key.LastAdjustmentMs);
            Assert.Equal(adjustmentReason, key.LastAdjustmentReason);
            Assert.Equal(adjustedUtc, key.LastAdjustedUtc);
        }

        [Fact]
        public void ResetRuntimeStatePreservesLearningAndIgnoredKeys()
        {
            var settings = new AppSettings
            {
                DefaultThresholdMs = 90,
                IgnoredKeys = { 8 }
            };
            DebounceEngine engine = NewEngine(settings, out LearningState learning);
            engine.MakeKeyMoreSensitive(65);
            Assert.False(engine.Process(Down(65, 1000)).Suppress);
            KeyLearningState keyBeforeReset = learning.Keys[65];

            engine.ResetRuntimeState();

            Assert.False(engine.Process(Down(65, 1010)).Suppress);
            Assert.Same(keyBeforeReset, learning.Keys[65]);
            Assert.Equal(95, learning.Keys[65].ThresholdMs);
            Assert.Equal(2, learning.Keys[65].AcceptedCount);
            Assert.Contains(8, settings.IgnoredKeys);
        }

        [Fact]
        public void HookTimestampWrapStillSuppressesShortRepeat()
        {
            var settings = new AppSettings { DefaultThresholdMs = 90 };
            DebounceEngine engine = NewEngine(settings, out LearningState learning);

            Assert.False(engine.Process(Down(65, UInt32.MaxValue - 15L)).Suppress);
            DebounceDecision wrapped = engine.Process(Down(65, 20));

            Assert.True(wrapped.Suppress);
            Assert.Equal(36, wrapped.IntervalMs);
        }

        [Fact]
        public void DecisionsReportEveryPersistedLearningMutation()
        {
            var settings = new AppSettings
            {
                DefaultThresholdMs = 90,
                IgnoredKeys = { 8 }
            };
            DebounceEngine engine = NewEngine(settings, out LearningState learning);

            DebounceDecision accepted = engine.Process(Down(65, 1000));
            DebounceDecision suppressed = engine.Process(Down(65, 1050));
            DebounceDecision keyUp = engine.Process(Up(65, 1060));
            DebounceDecision ignored = engine.Process(Down(8, 1100));

            Assert.True(accepted.LearningStateChanged);
            Assert.True(suppressed.LearningStateChanged);
            Assert.True(keyUp.LearningStateChanged);
            Assert.False(ignored.LearningStateChanged);
            Assert.Equal(1, learning.Keys[65].AcceptedCount);
            Assert.Equal(1, learning.Keys[65].SuppressedCount);
        }

        [Fact]
        public void StableDecayRequiresConsecutiveStableAcceptances()
        {
            var settings = new AppSettings { DefaultThresholdMs = 90 };
            var learning = new LearningState();
            learning.Keys[65] = new KeyLearningState { ThresholdMs = 100 };
            var engine = new DebounceEngine(settings, learning);
            long timestampMs = 1000;
            engine.Process(Down(65, timestampMs));
            engine.Process(Up(65, timestampMs + 10));

            for (int index = 0; index < 11; index++)
            {
                timestampMs += 400;
                Assert.Equal(0, engine.Process(Down(65, timestampMs)).LearningAdjustmentMs);
                engine.Process(Up(65, timestampMs + 10));
            }

            timestampMs += 200;
            Assert.Equal(0, engine.Process(Down(65, timestampMs)).LearningAdjustmentMs);
            engine.Process(Up(65, timestampMs + 10));

            DebounceDecision afterInterruptedRun = null;
            for (int index = 0; index < 11; index++)
            {
                timestampMs += 400;
                afterInterruptedRun = engine.Process(Down(65, timestampMs));
                Assert.Equal(0, afterInterruptedRun.LearningAdjustmentMs);
                engine.Process(Up(65, timestampMs + 10));
            }

            Assert.NotNull(afterInterruptedRun);
            Assert.Equal(0, afterInterruptedRun.LearningAdjustmentMs);
            Assert.Equal(100, learning.Keys[65].ThresholdMs);
        }

        [Fact]
        public void StableInputReturnsHighLearnedThresholdToDefaultWithinNinetySixSamples()
        {
            var settings = new AppSettings { DefaultThresholdMs = 160 };
            var learning = new LearningState();
            learning.Keys[65] = new KeyLearningState { ThresholdMs = 200 };
            var engine = new DebounceEngine(settings, learning);
            long timestampMs = 1000;

            Assert.False(engine.Process(Down(65, timestampMs)).Suppress);
            engine.Process(Up(65, timestampMs + 10));

            for (int index = 0; index < 96; index++)
            {
                timestampMs += 1000;
                DebounceDecision stable = engine.Process(Down(65, timestampMs));
                engine.Process(Up(65, timestampMs + 10));

                Assert.False(stable.Suppress);
                Assert.True(learning.Keys[65].ThresholdMs >= 160);
            }

            Assert.Equal(160, learning.Keys[65].ThresholdMs);
            Assert.Equal(97, learning.Keys[65].AcceptedCount);

            for (int index = 0; index < 12; index++)
            {
                timestampMs += 1000;
                engine.Process(Down(65, timestampMs));
                engine.Process(Up(65, timestampMs + 10));
            }

            Assert.Equal(160, learning.Keys[65].ThresholdMs);
        }

        [Fact]
        public void IgnoreThenUnignoreDoesNotInheritNearBoundarySuppressionProgress()
        {
            var settings = new AppSettings { DefaultThresholdMs = 90 };
            DebounceEngine engine = NewEngine(settings, out LearningState learning);
            Assert.False(engine.Process(Down(65, 1000)).Suppress);
            engine.Process(Up(65, 1010));
            Assert.True(engine.Process(Down(65, 1088)).Suppress);
            engine.Process(Up(65, 1098));

            engine.IgnoreKey(65);
            engine.UnignoreKey(65);
            Assert.False(engine.Process(Down(65, 1200)).Suppress);
            engine.Process(Up(65, 1210));
            DebounceDecision afterUnignore = engine.Process(Down(65, 1288));

            Assert.True(afterUnignore.Suppress);
            Assert.Equal(0, afterUnignore.LearningAdjustmentMs);
            Assert.Equal(90, learning.Keys[65].ThresholdMs);
        }

        [Fact]
        public void IgnoreThenUnignoreDoesNotInheritStableDecayProgress()
        {
            var settings = new AppSettings { DefaultThresholdMs = 90 };
            var learning = new LearningState();
            learning.Keys[65] = new KeyLearningState { ThresholdMs = 100 };
            var engine = new DebounceEngine(settings, learning);
            long timestampMs = 1000;
            Assert.False(engine.Process(Down(65, timestampMs)).Suppress);
            engine.Process(Up(65, timestampMs + 10));
            for (int index = 0; index < 11; index++)
            {
                timestampMs += 400;
                Assert.Equal(0, engine.Process(Down(65, timestampMs)).LearningAdjustmentMs);
                engine.Process(Up(65, timestampMs + 10));
            }

            engine.IgnoreKey(65);
            engine.UnignoreKey(65);
            engine.MakeKeyMoreSensitive(65);
            engine.MakeKeyMoreSensitive(65);

            timestampMs += 400;
            Assert.False(engine.Process(Down(65, timestampMs)).Suppress);
            engine.Process(Up(65, timestampMs + 10));
            for (int index = 0; index < 11; index++)
            {
                timestampMs += 400;
                DebounceDecision stable = engine.Process(Down(65, timestampMs));
                Assert.Equal(0, stable.LearningAdjustmentMs);
                engine.Process(Up(65, timestampMs + 10));
            }

            timestampMs += 400;
            DebounceDecision afterUnignore = engine.Process(Down(65, timestampMs));

            Assert.False(afterUnignore.Suppress);
            Assert.Equal(-5, afterUnignore.LearningAdjustmentMs);
            Assert.Equal(95, learning.Keys[65].ThresholdMs);
        }

        [Fact]
        public void GameModeHighFrequencyTapsStayAcceptedWithoutChangingLearning()
        {
            var settings = new AppSettings
            {
                DefaultThresholdMs = 90,
                GameModeThresholdMs = 45,
                GameModeLongHoldBypassMs = 250
            };
            var learning = new LearningState();
            var lastSeenUtc = new DateTime(2026, 1, 2, 3, 4, 5, DateTimeKind.Utc);
            learning.Keys[65] = new KeyLearningState
            {
                ThresholdMs = 90,
                AcceptedCount = 7,
                SuppressedCount = 3,
                LastIntervalMs = 88,
                LastSeenUtc = lastSeenUtc,
                LastAdjustmentMs = 5,
                LastAdjustmentReason = "before-game",
                LastAdjustedUtc = lastSeenUtc
            };
            var engine = new DebounceEngine(settings, learning);
            long timestampMs = 1000;

            for (int index = 0; index < 20; index++)
            {
                DebounceDecision down = engine.Process(
                    Down(65, timestampMs),
                    DebounceMode.Game);
                DebounceDecision up = engine.Process(
                    Up(65, timestampMs + 10),
                    DebounceMode.Game);

                Assert.False(down.Suppress);
                Assert.Equal(45, down.EffectiveThresholdMs);
                Assert.False(down.LearningStateChanged);
                Assert.False(up.LearningStateChanged);
                timestampMs += 100;
            }

            KeyLearningState after = learning.Keys[65];
            Assert.Equal(90, after.ThresholdMs);
            Assert.Equal(7, after.AcceptedCount);
            Assert.Equal(3, after.SuppressedCount);
            Assert.Equal(88, after.LastIntervalMs);
            Assert.Equal(lastSeenUtc, after.LastSeenUtc);
            Assert.Equal(5, after.LastAdjustmentMs);
            Assert.Equal("before-game", after.LastAdjustmentReason);
            Assert.Equal(lastSeenUtc, after.LastAdjustedUtc);
        }

        [Fact]
        public void GameModeDoesNotCreatePersistentLearningState()
        {
            var learning = new LearningState();
            var engine = new DebounceEngine(
                new AppSettings { GameModeThresholdMs = 45 },
                learning);

            DebounceDecision first = engine.Process(Down(65, 1000), DebounceMode.Game);
            DebounceDecision repeat = engine.Process(Down(65, 1020), DebounceMode.Game);
            DebounceDecision keyUp = engine.Process(Up(65, 1030), DebounceMode.Game);

            Assert.False(first.Suppress);
            Assert.True(repeat.Suppress);
            Assert.False(first.LearningStateChanged);
            Assert.False(repeat.LearningStateChanged);
            Assert.False(keyUp.LearningStateChanged);
            Assert.Empty(learning.Keys);
        }

        [Fact]
        public void GameModeUsesItsIndependentLongHoldBypass()
        {
            var settings = new AppSettings
            {
                DefaultThresholdMs = 90,
                LongHoldBypassMs = 500,
                GameModeThresholdMs = 90,
                GameModeLongHoldBypassMs = 50
            };
            var engine = new DebounceEngine(settings, new LearningState());

            Assert.False(engine.Process(Down(8, 1000), DebounceMode.Game).Suppress);
            Assert.True(engine.Process(Down(8, 1030), DebounceMode.Game).Suppress);
            DebounceDecision heldRepeat = engine.Process(
                Down(8, 1050),
                DebounceMode.Game);

            Assert.False(heldRepeat.Suppress);
            Assert.Equal("long-hold", heldRepeat.Reason);
        }

        [Fact]
        public void RuntimeTimingSeparatesSameVirtualKeyWithDifferentScanCodes()
        {
            var learning = new LearningState();
            var engine = new DebounceEngine(
                new AppSettings { DefaultThresholdMs = 90 },
                learning);

            DebounceDecision firstPhysicalKey = engine.Process(new KeyEventSample
            {
                VirtualKeyCode = 13,
                ScanCode = 28,
                IsKeyDown = true,
                TimestampMs = 1000
            });
            DebounceDecision secondPhysicalKey = engine.Process(new KeyEventSample
            {
                VirtualKeyCode = 13,
                ScanCode = 29,
                IsKeyDown = true,
                TimestampMs = 1010
            });
            DebounceDecision extendedPhysicalKey = engine.Process(new KeyEventSample
            {
                VirtualKeyCode = 13,
                ScanCode = 28,
                IsExtendedKey = true,
                IsKeyDown = true,
                TimestampMs = 1015
            });
            DebounceDecision firstPhysicalRepeat = engine.Process(new KeyEventSample
            {
                VirtualKeyCode = 13,
                ScanCode = 28,
                IsKeyDown = true,
                TimestampMs = 1020
            });

            Assert.False(firstPhysicalKey.Suppress);
            Assert.False(secondPhysicalKey.Suppress);
            Assert.False(extendedPhysicalKey.Suppress);
            Assert.True(firstPhysicalRepeat.Suppress);
            Assert.Equal(3, learning.Keys[13].AcceptedCount);
            Assert.Equal(1, learning.Keys[13].SuppressedCount);
            Assert.Single(learning.Keys);
        }

        [Fact]
        public void LegacySettingsWithoutGameFieldsReceiveSafeGameDefaults()
        {
            const string legacyJson = "{\"Enabled\":true,\"DefaultThresholdMs\":90}";
            var serializer = new DataContractJsonSerializer(typeof(AppSettings));
            AppSettings settings;
            using (var stream = new MemoryStream(Encoding.UTF8.GetBytes(legacyJson)))
            {
                settings = (AppSettings)serializer.ReadObject(stream);
            }

            settings.Normalize();

            Assert.False(settings.ProcessGameModeEnabled);
            Assert.Empty(settings.GameProcesses);
            Assert.Equal(45, settings.GameModeThresholdMs);
            Assert.Equal(250, settings.GameModeLongHoldBypassMs);
            Assert.Empty(settings.GameModeFilteredKeys);
        }

        [Fact]
        public void GameSettingsNormalizeProcessesRangesAndFilteredKeys()
        {
            var settings = new AppSettings
            {
                GameModeThresholdMs = -1,
                GameModeLongHoldBypassMs = 1
            };
            settings.GameProcesses.Add(" game.exe ");
            settings.GameProcesses.Add("C:\\Games\\GAME.EXE");
            settings.GameProcesses.Add("   ");
            settings.GameModeFilteredKeys.Add(65);
            settings.GameModeFilteredKeys.Add(65);
            settings.GameModeFilteredKeys.Add(0);
            settings.GameModeFilteredKeys.Add(256);

            settings.Normalize();

            Assert.Equal(new[] { "game" }, settings.GameProcesses);
            Assert.Equal(20, settings.GameModeThresholdMs);
            Assert.Equal(50, settings.GameModeLongHoldBypassMs);
            Assert.Equal(new[] { 65 }, settings.GameModeFilteredKeys);
        }

        [Fact]
        public void IgnoredKeysNormalizeDuplicatesInvalidValuesAndSortOrder()
        {
            var settings = new AppSettings();
            settings.IgnoredKeys.Add(66);
            settings.IgnoredKeys.Add(65);
            settings.IgnoredKeys.Add(66);
            settings.IgnoredKeys.Add(0);
            settings.IgnoredKeys.Add(-1);
            settings.IgnoredKeys.Add(256);

            settings.Normalize();

            Assert.Equal(new[] { 65, 66 }, settings.IgnoredKeys);
        }

        private static DebounceEngine NewEngine(AppSettings settings, out LearningState learning)
        {
            learning = new LearningState();
            return new DebounceEngine(settings, learning);
        }

        private static KeyEventSample Down(int virtualKeyCode, long timestampMs)
        {
            return new KeyEventSample
            {
                VirtualKeyCode = virtualKeyCode,
                IsKeyDown = true,
                TimestampMs = timestampMs
            };
        }

        private static KeyEventSample Up(int virtualKeyCode, long timestampMs)
        {
            return new KeyEventSample
            {
                VirtualKeyCode = virtualKeyCode,
                IsKeyDown = false,
                TimestampMs = timestampMs
            };
        }
    }
}
