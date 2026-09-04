using System;
using System.Collections.Generic;

namespace KeyboardDebounce
{
    public enum DebounceMode
    {
        Normal = 0,
        Game = 1
    }

    public sealed class KeyEventSample
    {
        public int VirtualKeyCode { get; set; }
        public uint ScanCode { get; set; }
        public bool IsExtendedKey { get; set; }
        public bool IsKeyDown { get; set; }
        public long TimestampMs { get; set; }
    }

    public sealed class DebounceDecision
    {
        public bool Suppress { get; set; }
        public string Reason { get; set; }
        public int EffectiveThresholdMs { get; set; }
        public long IntervalMs { get; set; }
        public int LearningAdjustmentMs { get; set; }
        public string LearningReason { get; set; }
        public bool LearningStateChanged { get; set; }
    }

    internal readonly struct PhysicalKeyIdentity : IEquatable<PhysicalKeyIdentity>
    {
        public PhysicalKeyIdentity(int virtualKeyCode, uint scanCode, bool isExtendedKey)
        {
            VirtualKeyCode = virtualKeyCode;
            ScanCode = scanCode;
            IsExtendedKey = isExtendedKey;
        }

        public int VirtualKeyCode { get; }
        public uint ScanCode { get; }
        public bool IsExtendedKey { get; }

        public bool Equals(PhysicalKeyIdentity other)
        {
            return VirtualKeyCode == other.VirtualKeyCode
                && ScanCode == other.ScanCode
                && IsExtendedKey == other.IsExtendedKey;
        }

        public override bool Equals(object obj)
        {
            return obj is PhysicalKeyIdentity && Equals((PhysicalKeyIdentity)obj);
        }

        public override int GetHashCode()
        {
            unchecked
            {
                int hash = VirtualKeyCode;
                hash = (hash * 397) ^ (int)ScanCode;
                hash = (hash * 397) ^ (IsExtendedKey ? 1 : 0);
                return hash;
            }
        }
    }

    internal sealed class RuntimeKeyState
    {
        public DebounceMode Mode;
        public bool HasMode;
        public bool IsDown;
        public long FirstDownMs = -1;
        public long FirstSuppressedDownMs = -1;
        public long LastObservedDownMs = -1;
        public long LastAcceptedDownMs = -1;
        public bool HasPhysicalKeyUpSinceLastDown;
        public int ConsecutiveNearBoundarySuppressions;
        public int StableAcceptedCount;
    }

    public sealed class DebounceEngine
    {
        private const int MinThresholdMs = 20;
        private const int MaxThresholdMs = 250;
        private readonly AppSettings _settings;
        private readonly LearningState _learning;
        private readonly Dictionary<PhysicalKeyIdentity, RuntimeKeyState> _runtime;

        public DebounceEngine(AppSettings settings, LearningState learning)
        {
            if (settings == null) throw new ArgumentNullException("settings");
            if (learning == null) throw new ArgumentNullException("learning");
            _settings = settings;
            _settings.Normalize();
            _learning = learning;
            _learning.Normalize(_settings.DefaultThresholdMs);
            _runtime = new Dictionary<PhysicalKeyIdentity, RuntimeKeyState>();
        }

        public DebounceDecision Process(KeyEventSample sample)
        {
            return Process(sample, DebounceMode.Normal);
        }

        public DebounceDecision Process(KeyEventSample sample, DebounceMode mode)
        {
            if (sample == null) throw new ArgumentNullException("sample");
            if (mode != DebounceMode.Normal && mode != DebounceMode.Game)
            {
                throw new ArgumentOutOfRangeException("mode");
            }

            if (IsIgnored(sample.VirtualKeyCode))
            {
                return NewDecision(false, "ignored", 0, 0, 0, "", false);
            }

            bool freezeLearning = mode == DebounceMode.Game;
            KeyLearningState learning = freezeLearning
                ? FindLearningState(sample.VirtualKeyCode)
                : GetLearningState(sample.VirtualKeyCode);
            PhysicalKeyIdentity identity = GetPhysicalKeyIdentity(sample);
            RuntimeKeyState runtime = GetRuntimeState(identity, mode);
            int threshold = GetEffectiveThreshold(learning, mode);
            int longHoldBypassMs = mode == DebounceMode.Game
                ? _settings.GameModeLongHoldBypassMs
                : _settings.LongHoldBypassMs;

            if (!sample.IsKeyDown)
            {
                CompleteRuntimeKeyUpWithoutLearning(identity);
                if (!freezeLearning)
                {
                    learning.LastSeenUtc = DateTime.UtcNow;
                }
                return NewDecision(
                    false,
                    "key-up",
                    threshold,
                    0,
                    0,
                    "",
                    !freezeLearning);
            }

            long observedInterval = GetInterval(sample.TimestampMs, runtime.LastObservedDownMs);
            long acceptedInterval = GetInterval(sample.TimestampMs, runtime.LastAcceptedDownMs);
            bool releaseSeparated = runtime.HasPhysicalKeyUpSinceLastDown;
            runtime.HasPhysicalKeyUpSinceLastDown = false;
            runtime.LastObservedDownMs = sample.TimestampMs;

            bool acceptedHoldLongEnough = runtime.IsDown &&
                runtime.FirstDownMs >= 0 &&
                GetInterval(sample.TimestampMs, runtime.FirstDownMs) >= longHoldBypassMs;
            bool suppressedHoldLongEnough = !runtime.IsDown &&
                runtime.FirstSuppressedDownMs >= 0 &&
                GetInterval(sample.TimestampMs, runtime.FirstSuppressedDownMs) >= longHoldBypassMs;
            bool heldLongEnough = acceptedHoldLongEnough || suppressedHoldLongEnough;

            if (!heldLongEnough && observedInterval <= threshold)
            {
                if (!runtime.IsDown && runtime.FirstSuppressedDownMs < 0)
                {
                    runtime.FirstSuppressedDownMs = sample.TimestampMs;
                }
                runtime.StableAcceptedCount = 0;

                int adjustment = 0;
                string learningReason = "";
                if (!freezeLearning)
                {
                    learning.SuppressedCount++;
                    learning.LastIntervalMs = observedInterval;
                    learning.LastSeenUtc = DateTime.UtcNow;
                    adjustment = LearnFromSuppression(
                        learning,
                        runtime,
                        observedInterval,
                        threshold,
                        releaseSeparated);
                    learningReason = adjustment == 0 ? "" : learning.LastAdjustmentReason;
                }

                return NewDecision(
                    true,
                    "short-repeat",
                    threshold,
                    observedInterval,
                    adjustment,
                    learningReason,
                    !freezeLearning);
            }

            if (!runtime.IsDown)
            {
                runtime.FirstDownMs = suppressedHoldLongEnough
                    ? runtime.FirstSuppressedDownMs
                    : sample.TimestampMs;
            }

            runtime.IsDown = true;
            runtime.FirstSuppressedDownMs = -1;
            runtime.LastAcceptedDownMs = sample.TimestampMs;

            int acceptedAdjustment = 0;
            string acceptedLearningReason = "";
            if (freezeLearning)
            {
                runtime.ConsecutiveNearBoundarySuppressions = 0;
                runtime.StableAcceptedCount = 0;
            }
            else
            {
                learning.AcceptedCount++;
                learning.LastIntervalMs = observedInterval == Int64.MaxValue ? 0 : observedInterval;
                learning.LastSeenUtc = DateTime.UtcNow;
                acceptedAdjustment = LearnFromAcceptance(
                    learning,
                    runtime,
                    acceptedInterval,
                    threshold,
                    heldLongEnough);
                acceptedLearningReason = acceptedAdjustment == 0
                    ? ""
                    : learning.LastAdjustmentReason;
            }

            return NewDecision(
                false,
                heldLongEnough ? "long-hold" : "accepted",
                threshold,
                observedInterval,
                acceptedAdjustment,
                acceptedLearningReason,
                !freezeLearning);
        }

        public KeyLearningState GetLearningState(int virtualKeyCode)
        {
            KeyLearningState learning;
            if (!_learning.Keys.TryGetValue(virtualKeyCode, out learning) || learning == null)
            {
                learning = new KeyLearningState { ThresholdMs = _settings.DefaultThresholdMs };
                _learning.Keys[virtualKeyCode] = learning;
            }

            learning.Normalize(_settings.DefaultThresholdMs);
            return learning;
        }

        private KeyLearningState FindLearningState(int virtualKeyCode)
        {
            KeyLearningState learning;
            if (!_learning.Keys.TryGetValue(virtualKeyCode, out learning))
            {
                return null;
            }
            return learning;
        }

        public void MakeKeyLessSensitive(int virtualKeyCode)
        {
            if (IsIgnored(virtualKeyCode)) return;
            KeyLearningState learning = GetLearningState(virtualKeyCode);
            learning.ThresholdMs = Clamp(learning.ThresholdMs - 5, MinThresholdMs, MaxThresholdMs);
        }

        public void MakeKeyMoreSensitive(int virtualKeyCode)
        {
            if (IsIgnored(virtualKeyCode)) return;
            KeyLearningState learning = GetLearningState(virtualKeyCode);
            learning.ThresholdMs = Clamp(learning.ThresholdMs + 5, MinThresholdMs, MaxThresholdMs);
        }

        public void ResetLearning()
        {
            _learning.Keys.Clear();
            _runtime.Clear();
        }

        public void ResetRuntimeState()
        {
            _runtime.Clear();
        }

        internal void ResetRuntimeStateExcept(ICollection<PhysicalKeyIdentity> preservedKeys)
        {
            if (preservedKeys == null || preservedKeys.Count == 0)
            {
                ResetRuntimeState();
                return;
            }

            var keysToRemove = new List<PhysicalKeyIdentity>();
            foreach (PhysicalKeyIdentity identity in _runtime.Keys)
            {
                if (!preservedKeys.Contains(identity))
                {
                    keysToRemove.Add(identity);
                }
                else
                {
                    RuntimeKeyState runtime = _runtime[identity];
                    runtime.ConsecutiveNearBoundarySuppressions = 0;
                    runtime.StableAcceptedCount = 0;
                }
            }

            foreach (PhysicalKeyIdentity identity in keysToRemove)
            {
                _runtime.Remove(identity);
            }
        }

        internal void CompleteRuntimeKeyUpWithoutLearning(PhysicalKeyIdentity identity)
        {
            RuntimeKeyState runtime;
            if (!_runtime.TryGetValue(identity, out runtime)) return;

            runtime.IsDown = false;
            runtime.FirstDownMs = -1;
            runtime.FirstSuppressedDownMs = -1;
            runtime.HasPhysicalKeyUpSinceLastDown = true;
        }

        private void ResetRuntimeForIgnoredKey(int virtualKeyCode)
        {
            foreach (var pair in _runtime)
            {
                if (pair.Key.VirtualKeyCode != virtualKeyCode) continue;

                RuntimeKeyState runtime = pair.Value;
                runtime.IsDown = false;
                runtime.FirstDownMs = -1;
                runtime.FirstSuppressedDownMs = -1;
                runtime.LastAcceptedDownMs = -1;
                runtime.HasPhysicalKeyUpSinceLastDown = false;
                runtime.ConsecutiveNearBoundarySuppressions = 0;
                runtime.StableAcceptedCount = 0;
            }
        }

        public bool IsIgnored(int virtualKeyCode)
        {
            return _settings.IgnoredKeys != null && _settings.IgnoredKeys.Contains(virtualKeyCode);
        }

        public bool IsGameModeFilteredKey(int virtualKeyCode)
        {
            return _settings.GameModeFilteredKeys != null
                && _settings.GameModeFilteredKeys.Contains(virtualKeyCode);
        }

        public void IgnoreKey(int virtualKeyCode)
        {
            if (_settings.IgnoredKeys == null)
            {
                _settings.IgnoredKeys = new List<int>();
            }

            if (!_settings.IgnoredKeys.Contains(virtualKeyCode))
            {
                _settings.IgnoredKeys.Add(virtualKeyCode);
                _settings.IgnoredKeys.Sort();
            }

            _learning.Keys.Remove(virtualKeyCode);
            ResetRuntimeForIgnoredKey(virtualKeyCode);
        }

        public void UnignoreKey(int virtualKeyCode)
        {
            if (_settings.IgnoredKeys == null) return;
            _settings.IgnoredKeys.Remove(virtualKeyCode);
        }

        public void ClearIgnoredKeys()
        {
            if (_settings.IgnoredKeys != null)
            {
                _settings.IgnoredKeys.Clear();
            }
        }

        private RuntimeKeyState GetRuntimeState(
            PhysicalKeyIdentity identity,
            DebounceMode mode)
        {
            RuntimeKeyState state;
            if (!_runtime.TryGetValue(identity, out state))
            {
                state = new RuntimeKeyState();
                _runtime[identity] = state;
            }
            if (!state.HasMode || state.Mode != mode)
            {
                ResetRuntimeForMode(state, mode);
            }
            return state;
        }

        private int GetEffectiveThreshold(
            KeyLearningState learning,
            DebounceMode mode)
        {
            int learnedThreshold = learning == null || learning.ThresholdMs == 0
                ? _settings.DefaultThresholdMs
                : learning.ThresholdMs;
            learnedThreshold = Clamp(learnedThreshold, MinThresholdMs, MaxThresholdMs);
            int threshold = (int)Math.Round(learnedThreshold * _settings.GlobalSensitivity);
            threshold = Clamp(threshold, MinThresholdMs, MaxThresholdMs);
            if (mode == DebounceMode.Game)
            {
                int gameThreshold = Clamp(
                    _settings.GameModeThresholdMs,
                    MinThresholdMs,
                    MaxThresholdMs);
                threshold = Math.Min(threshold, gameThreshold);
            }
            return threshold;
        }

        private static PhysicalKeyIdentity GetPhysicalKeyIdentity(KeyEventSample sample)
        {
            return new PhysicalKeyIdentity(
                sample.VirtualKeyCode,
                sample.ScanCode,
                sample.IsExtendedKey);
        }

        private static void ResetRuntimeForMode(
            RuntimeKeyState runtime,
            DebounceMode mode)
        {
            runtime.Mode = mode;
            runtime.HasMode = true;
            runtime.IsDown = false;
            runtime.FirstDownMs = -1;
            runtime.FirstSuppressedDownMs = -1;
            runtime.LastObservedDownMs = -1;
            runtime.LastAcceptedDownMs = -1;
            runtime.HasPhysicalKeyUpSinceLastDown = false;
            runtime.ConsecutiveNearBoundarySuppressions = 0;
            runtime.StableAcceptedCount = 0;
        }

        private static DebounceDecision NewDecision(
            bool suppress,
            string reason,
            int threshold,
            long interval,
            int adjustment,
            string learningReason,
            bool learningStateChanged)
        {
            return new DebounceDecision
            {
                Suppress = suppress,
                Reason = reason,
                EffectiveThresholdMs = threshold,
                IntervalMs = interval == Int64.MaxValue ? 0 : interval,
                LearningAdjustmentMs = adjustment,
                LearningReason = learningReason ?? "",
                LearningStateChanged = learningStateChanged
            };
        }

        private int LearnFromSuppression(
            KeyLearningState learning,
            RuntimeKeyState runtime,
            long interval,
            int threshold,
            bool releaseSeparated)
        {
            if (!releaseSeparated || interval <= 0 || interval < threshold - 5)
            {
                runtime.ConsecutiveNearBoundarySuppressions = 0;
                return 0;
            }

            runtime.ConsecutiveNearBoundarySuppressions++;
            if (runtime.ConsecutiveNearBoundarySuppressions < 2)
            {
                return 0;
            }
            runtime.ConsecutiveNearBoundarySuppressions = 0;

            int measuredThreshold = (int)Math.Ceiling(
                (interval + 5.0) / _settings.GlobalSensitivity);
            int automaticUpperBound = Math.Min(
                MaxThresholdMs,
                _settings.DefaultThresholdMs + 40);
            int targetThreshold = Math.Min(
                measuredThreshold,
                automaticUpperBound);
            if (targetThreshold <= learning.ThresholdMs)
            {
                return 0;
            }
            return ApplyAdjustment(
                learning,
                targetThreshold - learning.ThresholdMs,
                "suppressed-near-boundary");
        }

        private int LearnFromAcceptance(
            KeyLearningState learning,
            RuntimeKeyState runtime,
            long interval,
            int threshold,
            bool heldLongEnough)
        {
            if (heldLongEnough || interval <= 0 || interval == Int64.MaxValue)
            {
                runtime.ConsecutiveNearBoundarySuppressions = 0;
                runtime.StableAcceptedCount = 0;
                return 0;
            }

            runtime.ConsecutiveNearBoundarySuppressions = 0;
            if (learning.ThresholdMs <= _settings.DefaultThresholdMs)
            {
                runtime.StableAcceptedCount = 0;
                return 0;
            }

            long stableIntervalMs = Math.Max(300L, threshold * 2L);
            if (interval >= stableIntervalMs)
            {
                runtime.StableAcceptedCount++;
                if (runtime.StableAcceptedCount >= 12)
                {
                    runtime.StableAcceptedCount = 0;
                    int targetThreshold = Math.Max(
                        _settings.DefaultThresholdMs,
                        learning.ThresholdMs - 5);
                    return ApplyAdjustment(
                        learning,
                        targetThreshold - learning.ThresholdMs,
                        "stable-decay");
                }
            }
            else
            {
                runtime.StableAcceptedCount = 0;
            }

            return 0;
        }

        private static int ApplyAdjustment(KeyLearningState learning, int delta, string reason)
        {
            if (delta == 0)
            {
                return 0;
            }

            int before = learning.ThresholdMs;
            int after = Clamp(before + delta, MinThresholdMs, MaxThresholdMs);
            int actualDelta = after - before;
            learning.ThresholdMs = after;
            if (actualDelta != 0)
            {
                RecordAdjustment(learning, actualDelta, reason);
            }
            return actualDelta;
        }

        private static void RecordAdjustment(KeyLearningState learning, int delta, string reason)
        {
            if (delta == 0) return;
            learning.LastAdjustmentMs = delta;
            learning.LastAdjustmentReason = reason ?? "";
            learning.LastAdjustedUtc = DateTime.UtcNow;
        }

        private static long GetInterval(long timestampMs, long previousTimestampMs)
        {
            if (previousTimestampMs < 0)
            {
                return Int64.MaxValue;
            }
            if (timestampMs >= previousTimestampMs)
            {
                return timestampMs - previousTimestampMs;
            }
            if (timestampMs >= 0
                && timestampMs <= UInt32.MaxValue
                && previousTimestampMs <= UInt32.MaxValue)
            {
                return unchecked((uint)timestampMs - (uint)previousTimestampMs);
            }
            return Int64.MaxValue;
        }

        private static int Clamp(int value, int min, int max)
        {
            if (value < min) return min;
            if (value > max) return max;
            return value;
        }
    }
}
