using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;

namespace KeyboardDebounce
{
    internal sealed class GameProcessDetectionResult
    {
        public static readonly GameProcessDetectionResult Inactive =
            new GameProcessDetectionResult(
                false,
                "",
                Array.Empty<string>(),
                GameModeDetectionHealth.Healthy,
                "");

        public GameProcessDetectionResult(bool isActive, string matchedProcessName)
            : this(
                isActive,
                matchedProcessName,
                isActive && !String.IsNullOrWhiteSpace(matchedProcessName)
                    ? new[] { matchedProcessName }
                    : Array.Empty<string>())
        {
        }

        public GameProcessDetectionResult(
            bool isActive,
            string matchedProcessName,
            IEnumerable<string> runningConfiguredExecutables)
            : this(
                isActive,
                matchedProcessName,
                runningConfiguredExecutables,
                GameModeDetectionHealth.Healthy,
                "")
        {
        }

        public GameProcessDetectionResult(
            bool isActive,
            string matchedProcessName,
            IEnumerable<string> runningConfiguredExecutables,
            GameModeDetectionHealth detectionHealth,
            string errorMessage)
        {
            IsActive = isActive;
            MatchedProcessName = matchedProcessName ?? "";
            RunningConfiguredExecutables = new ReadOnlyCollection<string>(
                new List<string>(runningConfiguredExecutables ?? Array.Empty<string>()));
            DetectionHealth = detectionHealth;
            ErrorMessage = errorMessage ?? "";
        }

        public bool IsActive { get; private set; }
        public string MatchedProcessName { get; private set; }
        public IReadOnlyList<string> RunningConfiguredExecutables { get; private set; }
        public GameModeDetectionHealth DetectionHealth { get; private set; }
        public string ErrorMessage { get; private set; }
    }

    internal static class GameProcessDetector
    {
        internal sealed class RunningExecutableScanResult
        {
            public RunningExecutableScanResult(
                IReadOnlyList<string> executableNames,
                GameModeDetectionHealth detectionHealth,
                string errorMessage,
                Exception originalError)
            {
                ExecutableNames = executableNames ?? Array.Empty<string>();
                DetectionHealth = detectionHealth;
                ErrorMessage = errorMessage ?? "";
                OriginalError = originalError;
            }

            public IReadOnlyList<string> ExecutableNames { get; private set; }
            public GameModeDetectionHealth DetectionHealth { get; private set; }
            public string ErrorMessage { get; private set; }
            public Exception OriginalError { get; private set; }
        }

        internal sealed class RunningProcessSnapshot
        {
            public RunningProcessSnapshot(int processId, string processName)
                : this(processId, processName, null)
            {
            }

            public RunningProcessSnapshot(Exception error)
                : this(-1, "", error)
            {
            }

            private RunningProcessSnapshot(
                int processId,
                string processName,
                Exception error)
            {
                ProcessId = processId;
                ProcessName = processName ?? "";
                Error = error;
            }

            public int ProcessId { get; private set; }
            public string ProcessName { get; private set; }
            public Exception Error { get; private set; }
        }

        public static string NormalizeProcessName(string value)
        {
            if (String.IsNullOrWhiteSpace(value)) return "";

            string candidate = value.Trim().Trim('"');
            try
            {
                string fileName = Path.GetFileName(candidate);
                if (!String.IsNullOrWhiteSpace(fileName))
                {
                    candidate = fileName;
                }
            }
            catch (ArgumentException)
            {
                return "";
            }

            if (candidate.EndsWith(".exe", StringComparison.OrdinalIgnoreCase))
            {
                candidate = candidate.Substring(0, candidate.Length - 4);
            }

            candidate = candidate.Trim();
            if (candidate.Length == 0 || candidate.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0)
            {
                return "";
            }
            return candidate;
        }

        public static string FormatExecutableName(string value)
        {
            string normalized = NormalizeProcessName(value);
            return normalized.Length == 0 ? "" : normalized + ".exe";
        }

        public static GameProcessDetectionResult Detect(
            IEnumerable<string> configuredProcessNames,
            IEnumerable<string> runningProcessNames)
        {
            return Detect(
                configuredProcessNames,
                runningProcessNames,
                GameModeDetectionHealth.Healthy,
                "");
        }

        internal static GameProcessDetectionResult Detect(
            IEnumerable<string> configuredProcessNames,
            IEnumerable<string> runningProcessNames,
            GameModeDetectionHealth detectionHealth,
            string errorMessage)
        {
            var configured = new List<string>();
            var configuredSet = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            if (configuredProcessNames != null)
            {
                foreach (string processName in configuredProcessNames)
                {
                    string normalized = NormalizeProcessName(processName);
                    if (normalized.Length > 0 && configuredSet.Add(normalized))
                    {
                        configured.Add(normalized);
                    }
                }
            }

            if (configured.Count == 0 || runningProcessNames == null)
            {
                return detectionHealth == GameModeDetectionHealth.Healthy
                    ? GameProcessDetectionResult.Inactive
                    : new GameProcessDetectionResult(
                        false,
                        "",
                        Array.Empty<string>(),
                        detectionHealth,
                        errorMessage);
            }

            var running = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (string runningProcessName in runningProcessNames)
            {
                string normalized = NormalizeProcessName(runningProcessName);
                if (normalized.Length > 0)
                {
                    running.Add(normalized);
                }
            }

            var matches = new List<string>();
            foreach (string configuredProcessName in configured)
            {
                if (running.Contains(configuredProcessName))
                {
                    matches.Add(configuredProcessName);
                }
            }
            return matches.Count == 0
                ? new GameProcessDetectionResult(
                    false,
                    "",
                    Array.Empty<string>(),
                    detectionHealth,
                    detectionHealth == GameModeDetectionHealth.Healthy
                        ? ""
                        : errorMessage)
                : new GameProcessDetectionResult(
                    true,
                    matches[0],
                    matches,
                    detectionHealth,
                    detectionHealth == GameModeDetectionHealth.Healthy
                        ? ""
                        : errorMessage);
        }

        public static IReadOnlyList<string> GetRunningExecutableNames()
        {
            RunningExecutableScanResult result = ScanRunningExecutableNames();
            if (result.DetectionHealth == GameModeDetectionHealth.Failed)
            {
                throw new InvalidOperationException(
                    result.ErrorMessage,
                    result.OriginalError);
            }
            return result.ExecutableNames;
        }

        private static RunningExecutableScanResult ScanRunningExecutableNames()
        {
            Process[] processes;
            try
            {
                processes = Process.GetProcesses();
            }
            catch (Exception error)
            {
                return new RunningExecutableScanResult(
                    Array.Empty<string>(),
                    GameModeDetectionHealth.Failed,
                    DescribeProcessError(error),
                    error);
            }

            var snapshots = new List<RunningProcessSnapshot>();
            try
            {
                int currentProcessId = Environment.ProcessId;
                foreach (Process process in processes)
                {
                    try
                    {
                        int processId = process.Id;
                        if (processId == 0
                            || processId == 4
                            || processId == currentProcessId)
                        {
                            continue;
                        }

                        snapshots.Add(new RunningProcessSnapshot(
                            processId,
                            process.ProcessName));
                    }
                    catch (InvalidOperationException error)
                    {
                        snapshots.Add(new RunningProcessSnapshot(error));
                    }
                    catch (Win32Exception error)
                    {
                        snapshots.Add(new RunningProcessSnapshot(error));
                    }
                    catch (NotSupportedException error)
                    {
                        snapshots.Add(new RunningProcessSnapshot(error));
                    }
                }

                return CreateRunningExecutableScanResult(
                    snapshots,
                    currentProcessId);
            }
            catch (Exception error)
            {
                return new RunningExecutableScanResult(
                    Array.Empty<string>(),
                    GameModeDetectionHealth.Failed,
                    DescribeProcessError(error),
                    error);
            }
            finally
            {
                foreach (Process process in processes)
                {
                    process.Dispose();
                }
            }
        }

        internal static RunningExecutableScanResult CreateRunningExecutableScanResult(
            IEnumerable<RunningProcessSnapshot> processes,
            int currentProcessId)
        {
            var snapshots = processes == null
                ? new List<RunningProcessSnapshot>()
                : new List<RunningProcessSnapshot>(processes);
            int unreadableCount = 0;
            Exception firstError = null;
            foreach (RunningProcessSnapshot process in snapshots)
            {
                if (process == null || process.Error == null) continue;
                unreadableCount++;
                if (firstError == null) firstError = process.Error;
            }

            IReadOnlyList<string> names = NormalizeRunningExecutableNames(
                snapshots,
                currentProcessId);
            if (unreadableCount == 0)
            {
                return new RunningExecutableScanResult(
                    names,
                    GameModeDetectionHealth.Healthy,
                    "",
                    null);
            }

            return new RunningExecutableScanResult(
                names,
                GameModeDetectionHealth.Partial,
                unreadableCount + " 个进程无法读取；首个错误："
                    + DescribeProcessError(firstError),
                firstError);
        }

        internal static IReadOnlyList<string> NormalizeRunningExecutableNames(
            IEnumerable<RunningProcessSnapshot> processes,
            int currentProcessId)
        {
            var names = new SortedSet<string>(StringComparer.OrdinalIgnoreCase);
            if (processes == null)
            {
                return new List<string>();
            }

            foreach (RunningProcessSnapshot process in processes)
            {
                if (process == null
                    || process.ProcessId == 0
                    || process.ProcessId == 4
                    || process.ProcessId == currentProcessId)
                {
                    continue;
                }

                string normalized = NormalizeProcessName(process.ProcessName);
                if (normalized.Length > 0)
                {
                    names.Add(normalized);
                }
            }

            return new List<string>(names);
        }

        public static GameProcessDetectionResult DetectRunningProcesses(
            IEnumerable<string> configuredProcessNames)
        {
            RunningExecutableScanResult scan = ScanRunningExecutableNames();
            return Detect(configuredProcessNames, scan);
        }

        internal static GameProcessDetectionResult Detect(
            IEnumerable<string> configuredProcessNames,
            RunningExecutableScanResult scan)
        {
            if (scan == null)
            {
                return Detect(
                    configuredProcessNames,
                    Array.Empty<string>(),
                    GameModeDetectionHealth.Failed,
                    "进程枚举未返回结果");
            }
            return Detect(
                configuredProcessNames,
                scan.ExecutableNames,
                scan.DetectionHealth,
                scan.ErrorMessage);
        }

        private static string DescribeProcessError(Exception error)
        {
            return error == null
                ? "未知进程访问错误"
                : error.GetType().Name + " - " + error.Message;
        }
    }
}
