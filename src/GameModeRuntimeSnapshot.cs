using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;

namespace KeyboardDebounce
{
    internal enum GameModeDetectionHealth
    {
        Healthy,
        Partial,
        Failed
    }

    internal sealed class GameModeRuntimeSnapshot : IEquatable<GameModeRuntimeSnapshot>
    {
        public static readonly GameModeRuntimeSnapshot Inactive =
            new GameModeRuntimeSnapshot(
                false,
                "",
                Array.Empty<string>(),
                GameModeDetectionHealth.Healthy,
                "");

        public GameModeRuntimeSnapshot(
            bool isActive,
            string activeExecutable,
            IEnumerable<string> runningConfiguredExecutables,
            GameModeDetectionHealth detectionHealth,
            string errorMessage)
            : this(
                isActive,
                activeExecutable,
                runningConfiguredExecutables,
                detectionHealth,
                errorMessage,
                Array.Empty<string>())
        {
        }

        public GameModeRuntimeSnapshot(
            bool isActive,
            string activeExecutable,
            IEnumerable<string> runningConfiguredExecutables,
            GameModeDetectionHealth detectionHealth,
            string errorMessage,
            IEnumerable<string> unknownConfiguredExecutables)
        {
            string normalizedActive = GameProcessDetector.NormalizeProcessName(
                activeExecutable);
            IsActive = isActive && normalizedActive.Length > 0;
            ActiveExecutable = IsActive ? normalizedActive : "";

            var running = new List<string>();
            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            if (runningConfiguredExecutables != null)
            {
                foreach (string executable in runningConfiguredExecutables)
                {
                    string normalized = GameProcessDetector.NormalizeProcessName(executable);
                    if (normalized.Length > 0 && seen.Add(normalized))
                    {
                        running.Add(normalized);
                    }
                }
            }

            RunningConfiguredExecutables = new ReadOnlyCollection<string>(running);

            var unknown = new List<string>();
            var seenUnknown = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            if (unknownConfiguredExecutables != null)
            {
                foreach (string executable in unknownConfiguredExecutables)
                {
                    string normalized = GameProcessDetector.NormalizeProcessName(executable);
                    if (normalized.Length > 0
                        && !seen.Contains(normalized)
                        && seenUnknown.Add(normalized))
                    {
                        unknown.Add(normalized);
                    }
                }
            }

            UnknownConfiguredExecutables = new ReadOnlyCollection<string>(unknown);
            DetectionHealth = detectionHealth;
            ErrorMessage = errorMessage ?? "";
        }

        public bool IsActive { get; private set; }
        public string ActiveExecutable { get; private set; }
        public IReadOnlyList<string> RunningConfiguredExecutables { get; private set; }
        public IReadOnlyList<string> UnknownConfiguredExecutables { get; private set; }
        public GameModeDetectionHealth DetectionHealth { get; private set; }
        public string ErrorMessage { get; private set; }

        public bool Equals(GameModeRuntimeSnapshot other)
        {
            if (ReferenceEquals(null, other)) return false;
            if (ReferenceEquals(this, other)) return true;
            if (IsActive != other.IsActive
                || !String.Equals(
                    ActiveExecutable,
                    other.ActiveExecutable,
                    StringComparison.OrdinalIgnoreCase)
                || DetectionHealth != other.DetectionHealth
                || !String.Equals(ErrorMessage, other.ErrorMessage, StringComparison.Ordinal)
                || RunningConfiguredExecutables.Count
                    != other.RunningConfiguredExecutables.Count
                || UnknownConfiguredExecutables.Count
                    != other.UnknownConfiguredExecutables.Count)
            {
                return false;
            }

            for (int index = 0; index < RunningConfiguredExecutables.Count; index++)
            {
                if (!String.Equals(
                    RunningConfiguredExecutables[index],
                    other.RunningConfiguredExecutables[index],
                    StringComparison.OrdinalIgnoreCase))
                {
                    return false;
                }
            }

            for (int index = 0; index < UnknownConfiguredExecutables.Count; index++)
            {
                if (!String.Equals(
                    UnknownConfiguredExecutables[index],
                    other.UnknownConfiguredExecutables[index],
                    StringComparison.OrdinalIgnoreCase))
                {
                    return false;
                }
            }

            return true;
        }

        public override bool Equals(object obj)
        {
            return Equals(obj as GameModeRuntimeSnapshot);
        }

        public override int GetHashCode()
        {
            var hash = new HashCode();
            hash.Add(IsActive);
            hash.Add(ActiveExecutable, StringComparer.OrdinalIgnoreCase);
            hash.Add(DetectionHealth);
            hash.Add(ErrorMessage, StringComparer.Ordinal);
            foreach (string executable in RunningConfiguredExecutables)
            {
                hash.Add(executable, StringComparer.OrdinalIgnoreCase);
            }
            foreach (string executable in UnknownConfiguredExecutables)
            {
                hash.Add(executable, StringComparer.OrdinalIgnoreCase);
            }
            return hash.ToHashCode();
        }
    }
}
