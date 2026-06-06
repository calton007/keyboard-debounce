using System;
using System.Collections.Generic;

namespace KeyboardDebounce
{
    public sealed class KeyEventSample
    {
        public int VirtualKeyCode { get; set; }
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
    }

    internal sealed class RuntimeKeyState
    {
        public bool IsDown;
        public long FirstDownMs = -1;
        public long LastAcceptedDownMs = -1;
        public int ConsecutiveSuspectedAccepted;
        public int StableAcceptedCount;
    }

    public sealed class DebounceEngine
    {
        private const int MinThresholdMs = 20;
        private const int MaxThresholdMs = 250;
        private readonly AppSettings _settings;
        private readonly LearningState _learning;
        private readonly Dictionary<int, RuntimeKeyState> _runtime;

        public DebounceEngine(AppSettings settings, LearningState learning)
        {
            if (settings == null) throw new ArgumentNullException("settings");
            if (learning == null) throw new ArgumentNullException("learning");
            _settings = settings;
            _settings.Normalize();
            _learning = learning;
            _learning.Normalize(_settings.DefaultThresholdMs);
            _runtime = new Dictionary<int, RuntimeKeyState>();
        }

        public DebounceDecision Process(KeyEventSample sample)
        {
            if (sample == null) throw new ArgumentNullException("sample");

            if (IsIgnored(sample.VirtualKeyCode))
            {
                return NewDecision(false, "ignored", 0, 0, 0, "");
            }

            KeyLearningState learning = GetLearningState(sample.VirtualKeyCode);
            RuntimeKeyState runtime = GetRuntimeState(sample.VirtualKeyCode);
            int threshold = GetEffectiveThreshold(learning);

            if (!sample.IsKeyDown)
            {
                runtime.IsDown = false;
                runtime.FirstDownMs = -1;
                learning.LastSeenUtc = DateTime.UtcNow;
                return NewDecision(false, "key-up", threshold, 0, 0, "");
            }

            long interval = runtime.LastAcceptedDownMs >= 0
                ? sample.TimestampMs - runtime.LastAcceptedDownMs
                : Int64.MaxValue;
            if (interval < 0) interval = Int64.MaxValue;

            bool heldLongEnough = runtime.IsDown &&
                runtime.FirstDownMs >= 0 &&
                sample.TimestampMs - runtime.FirstDownMs >= _settings.LongHoldBypassMs;

            if (!heldLongEnough && interval <= threshold)
            {
                learning.SuppressedCount++;
                learning.LastIntervalMs = interval;
                learning.LastSeenUtc = DateTime.UtcNow;
                runtime.ConsecutiveSuspectedAccepted = 0;
                runtime.StableAcceptedCount = 0;
                int adjustment = LearnFromSuppression(learning, interval, threshold);
                return NewDecision(true, "short-repeat", threshold, interval, adjustment, learning.LastAdjustmentReason);
            }

            if (!runtime.IsDown)
            {
                runtime.FirstDownMs = sample.TimestampMs;
            }

            runtime.IsDown = true;
            runtime.LastAcceptedDownMs = sample.TimestampMs;
            learning.AcceptedCount++;
            learning.LastIntervalMs = interval == Int64.MaxValue ? 0 : interval;
            learning.LastSeenUtc = DateTime.UtcNow;
            int acceptedAdjustment = LearnFromAcceptance(learning, runtime, interval, threshold, heldLongEnough);
            return NewDecision(false, heldLongEnough ? "long-hold" : "accepted", threshold, interval, acceptedAdjustment, learning.LastAdjustmentReason);
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

        public bool IsIgnored(int virtualKeyCode)
        {
            return _settings.IgnoredKeys != null && _settings.IgnoredKeys.Contains(virtualKeyCode);
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
            _runtime.Remove(virtualKeyCode);
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

        private RuntimeKeyState GetRuntimeState(int virtualKeyCode)
        {
            RuntimeKeyState state;
            if (!_runtime.TryGetValue(virtualKeyCode, out state))
            {
                state = new RuntimeKeyState();
                _runtime[virtualKeyCode] = state;
            }
            return state;
        }

        private int GetEffectiveThreshold(KeyLearningState learning)
        {
            int threshold = (int)Math.Round(learning.ThresholdMs * _settings.GlobalSensitivity);
            return Clamp(threshold, MinThresholdMs, MaxThresholdMs);
        }

        private static DebounceDecision NewDecision(bool suppress, string reason, int threshold, long interval, int adjustment, string learningReason)
        {
            return new DebounceDecision
            {
                Suppress = suppress,
                Reason = reason,
                EffectiveThresholdMs = threshold,
                IntervalMs = interval == Int64.MaxValue ? 0 : interval,
                LearningAdjustmentMs = adjustment,
                LearningReason = learningReason ?? ""
            };
        }

        private static int LearnFromSuppression(KeyLearningState learning, long interval, int threshold)
        {
            if (interval <= 0)
            {
                RecordAdjustment(learning, 0, "");
                return 0;
            }

            int delta = 0;
            string reason = "";
            if (interval >= threshold - 5)
            {
                delta = 8;
                reason = "suppressed-near-boundary";
            }
            else if (interval >= threshold / 2)
            {
                delta = 3;
                reason = "suppressed-mid-window";
            }

            return ApplyAdjustment(learning, delta, reason);
        }

        private static int LearnFromAcceptance(KeyLearningState learning, RuntimeKeyState runtime, long interval, int threshold, bool heldLongEnough)
        {
            if (heldLongEnough || interval <= 0 || interval == Int64.MaxValue)
            {
                runtime.ConsecutiveSuspectedAccepted = 0;
                RecordAdjustment(learning, 0, "");
                return 0;
            }

            bool suspectedBounce = interval > threshold && interval <= Math.Min(MaxThresholdMs, threshold + 80);
            if (suspectedBounce)
            {
                runtime.ConsecutiveSuspectedAccepted++;
                runtime.StableAcceptedCount = 0;
                if (runtime.ConsecutiveSuspectedAccepted >= 2)
                {
                    runtime.ConsecutiveSuspectedAccepted = 0;
                    return ApplyAdjustment(learning, 5, "accepted-suspected-bounce");
                }

                RecordAdjustment(learning, 0, "");
                return 0;
            }

            runtime.ConsecutiveSuspectedAccepted = 0;
            if (interval > threshold * 4 && learning.SuppressedCount == 0)
            {
                runtime.StableAcceptedCount++;
                if (runtime.StableAcceptedCount >= 25)
                {
                    runtime.StableAcceptedCount = 0;
                    return ApplyAdjustment(learning, -1, "stable-decay");
                }
            }

            RecordAdjustment(learning, 0, "");
            return 0;
        }

        private static int ApplyAdjustment(KeyLearningState learning, int delta, string reason)
        {
            if (delta == 0)
            {
                RecordAdjustment(learning, 0, "");
                return 0;
            }

            int before = learning.ThresholdMs;
            int after = Clamp(before + delta, MinThresholdMs, MaxThresholdMs);
            int actualDelta = after - before;
            learning.ThresholdMs = after;
            RecordAdjustment(learning, actualDelta, actualDelta == 0 ? "" : reason);
            return actualDelta;
        }

        private static void RecordAdjustment(KeyLearningState learning, int delta, string reason)
        {
            learning.LastAdjustmentMs = delta;
            learning.LastAdjustmentReason = reason ?? "";
            if (delta != 0)
            {
                learning.LastAdjustedUtc = DateTime.UtcNow;
            }
        }

        private static int Clamp(int value, int min, int max)
        {
            if (value < min) return min;
            if (value > max) return max;
            return value;
        }
    }
}
