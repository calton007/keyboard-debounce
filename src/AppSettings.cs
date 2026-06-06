using System;
using System.Collections.Generic;
using System.Runtime.Serialization;

namespace KeyboardDebounce
{
    [DataContract]
    public sealed class AppSettings
    {
        public AppSettings()
        {
            Enabled = true;
            StartWithWindows = false;
            SilentRun = false;
            GlobalSensitivity = 1.0;
            DefaultThresholdMs = 90;
            LongHoldBypassMs = 500;
            StartupDelayMs = 3000;
            PauseHotkey = "Ctrl+Alt+F12";
            IgnoredKeys = new List<int>();
        }

        [DataMember(Order = 1)]
        public bool Enabled { get; set; }

        [DataMember(Order = 2)]
        public bool StartWithWindows { get; set; }

        [DataMember(Order = 3)]
        public bool SilentRun { get; set; }

        [DataMember(Order = 4)]
        public double GlobalSensitivity { get; set; }

        [DataMember(Order = 5)]
        public int DefaultThresholdMs { get; set; }

        [DataMember(Order = 6)]
        public int LongHoldBypassMs { get; set; }

        [DataMember(Order = 7)]
        public int StartupDelayMs { get; set; }

        [DataMember(Order = 8)]
        public string PauseHotkey { get; set; }

        [DataMember(Order = 9)]
        public List<int> IgnoredKeys { get; set; }

        public void Normalize()
        {
            if (GlobalSensitivity < 0.5) GlobalSensitivity = 0.5;
            if (GlobalSensitivity > 3.0) GlobalSensitivity = 3.0;
            if (DefaultThresholdMs < 20) DefaultThresholdMs = 20;
            if (DefaultThresholdMs > 250) DefaultThresholdMs = 250;
            if (LongHoldBypassMs < 250) LongHoldBypassMs = 250;
            if (LongHoldBypassMs > 1000) LongHoldBypassMs = 1000;
            if (StartupDelayMs < 0) StartupDelayMs = 0;
            if (StartupDelayMs > 30000) StartupDelayMs = 30000;
            if (String.IsNullOrWhiteSpace(PauseHotkey)) PauseHotkey = "Ctrl+Alt+F12";
            if (IgnoredKeys == null) IgnoredKeys = new List<int>();
        }
    }

    [DataContract]
    public sealed class LearningState
    {
        public LearningState()
        {
            Keys = new Dictionary<int, KeyLearningState>();
        }

        [DataMember(Order = 1)]
        public Dictionary<int, KeyLearningState> Keys { get; set; }

        public void Normalize(int fallbackThreshold)
        {
            if (Keys == null) Keys = new Dictionary<int, KeyLearningState>();
            foreach (var pair in Keys)
            {
                if (pair.Value != null)
                {
                    pair.Value.Normalize(fallbackThreshold);
                }
            }
        }
    }

    [DataContract]
    public sealed class KeyLearningState
    {
        public KeyLearningState()
        {
            ThresholdMs = 90;
        }

        [DataMember(Order = 1)]
        public int ThresholdMs { get; set; }

        [DataMember(Order = 2)]
        public long AcceptedCount { get; set; }

        [DataMember(Order = 3)]
        public long SuppressedCount { get; set; }

        [DataMember(Order = 4)]
        public long LastIntervalMs { get; set; }

        [DataMember(Order = 5, EmitDefaultValue = false)]
        public DateTime LastSeenUtc { get; set; }

        [DataMember(Order = 6)]
        public int LastAdjustmentMs { get; set; }

        [DataMember(Order = 7)]
        public string LastAdjustmentReason { get; set; }

        [DataMember(Order = 8, EmitDefaultValue = false)]
        public DateTime LastAdjustedUtc { get; set; }

        public void Normalize(int fallbackThreshold)
        {
            if (ThresholdMs < 20) ThresholdMs = 20;
            if (ThresholdMs > 250) ThresholdMs = 250;
            if (ThresholdMs == 0) ThresholdMs = fallbackThreshold;
            if (LastAdjustmentReason == null) LastAdjustmentReason = "";
            LastSeenUtc = NormalizeStoredUtc(LastSeenUtc);
            LastAdjustedUtc = NormalizeStoredUtc(LastAdjustedUtc);
        }

        private static DateTime NormalizeStoredUtc(DateTime value)
        {
            if (value == DateTime.MinValue) return DateTime.MinValue;
            if (value.Year <= 1900) return DateTime.MinValue;
            if (value.Kind == DateTimeKind.Utc) return value;
            return value.ToUniversalTime();
        }
    }
}
