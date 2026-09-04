using System;
using System.Threading.Tasks;

namespace KeyboardDebounce
{
    internal enum SaveTaskResultKind
    {
        None,
        Started,
        Succeeded,
        Failed
    }

    internal sealed class SaveTaskResult
    {
        private static readonly SaveTaskResult NoneResult =
            new SaveTaskResult(SaveTaskResultKind.None, null);
        private static readonly SaveTaskResult StartedResult =
            new SaveTaskResult(SaveTaskResultKind.Started, null);
        private static readonly SaveTaskResult SucceededResult =
            new SaveTaskResult(SaveTaskResultKind.Succeeded, null);

        private SaveTaskResult(SaveTaskResultKind kind, Exception error)
        {
            Kind = kind;
            Error = error;
        }

        public SaveTaskResultKind Kind { get; private set; }
        public Exception Error { get; private set; }

        public static SaveTaskResult None()
        {
            return NoneResult;
        }

        public static SaveTaskResult Started()
        {
            return StartedResult;
        }

        public static SaveTaskResult Succeeded()
        {
            return SucceededResult;
        }

        public static SaveTaskResult Failed(Exception error)
        {
            return new SaveTaskResult(SaveTaskResultKind.Failed, error);
        }
    }

    /// <summary>
    /// Coordinates one serialized background save stream. All members are intended
    /// to be called by the owning UI thread; only the Task returned by taskFactory
    /// runs in the background.
    /// </summary>
    internal sealed class SaveTaskSlot
    {
        private readonly long _checkpointDelayMs;
        private readonly long _initialRetryDelayMs;
        private readonly long _maximumRetryDelayMs;
        private Task _activeTask;
        private bool _dirty;
        private long _dirtyDueMs = Int64.MaxValue;
        private long _retryNotBeforeMs = Int64.MinValue;
        private int _consecutiveFailures;
        private Exception _lastError;

        public SaveTaskSlot(
            long checkpointDelayMs,
            long initialRetryDelayMs,
            long maximumRetryDelayMs)
        {
            if (checkpointDelayMs < 0)
            {
                throw new ArgumentOutOfRangeException("checkpointDelayMs");
            }
            if (initialRetryDelayMs <= 0)
            {
                throw new ArgumentOutOfRangeException("initialRetryDelayMs");
            }
            if (maximumRetryDelayMs < initialRetryDelayMs)
            {
                throw new ArgumentOutOfRangeException("maximumRetryDelayMs");
            }

            _checkpointDelayMs = checkpointDelayMs;
            _initialRetryDelayMs = initialRetryDelayMs;
            _maximumRetryDelayMs = maximumRetryDelayMs;
        }

        public bool IsDirty { get { return _dirty; } }
        public bool HasActiveTask { get { return _activeTask != null; } }
        public int ConsecutiveFailures { get { return _consecutiveFailures; } }
        public Exception LastError { get { return _lastError; } }

        public long NextAttemptMs
        {
            get
            {
                if (!_dirty) return Int64.MaxValue;
                return Math.Max(_dirtyDueMs, _retryNotBeforeMs);
            }
        }

        public void MarkDirty(long nowMs, bool immediate)
        {
            long requestedDueMs = immediate
                ? nowMs
                : SaturatingAdd(nowMs, _checkpointDelayMs);
            _dirty = true;
            if (requestedDueMs < _dirtyDueMs)
            {
                _dirtyDueMs = requestedDueMs;
            }
        }

        public SaveTaskResult TryStart(long nowMs, Func<Task> taskFactory)
        {
            if (taskFactory == null) throw new ArgumentNullException("taskFactory");
            if (_activeTask != null || !_dirty || nowMs < NextAttemptMs)
            {
                return SaveTaskResult.None();
            }

            _dirty = false;
            _dirtyDueMs = Int64.MaxValue;
            try
            {
                Task task = taskFactory();
                if (task == null)
                {
                    throw new InvalidOperationException("The save task factory returned null.");
                }
                _activeTask = task;
                return SaveTaskResult.Started();
            }
            catch (Exception error)
            {
                RecordFailure(nowMs, error);
                return SaveTaskResult.Failed(error);
            }
        }

        public SaveTaskResult Observe(long nowMs)
        {
            if (_activeTask == null || !_activeTask.IsCompleted)
            {
                return SaveTaskResult.None();
            }
            return CompleteActiveTask(nowMs);
        }

        public SaveTaskResult Wait(long nowMs)
        {
            if (_activeTask == null)
            {
                return SaveTaskResult.None();
            }
            return CompleteActiveTask(nowMs);
        }

        private SaveTaskResult CompleteActiveTask(long nowMs)
        {
            Task completed = _activeTask;
            _activeTask = null;
            try
            {
                completed.GetAwaiter().GetResult();
                _consecutiveFailures = 0;
                _retryNotBeforeMs = Int64.MinValue;
                _lastError = null;
                return SaveTaskResult.Succeeded();
            }
            catch (Exception error)
            {
                RecordFailure(nowMs, error);
                return SaveTaskResult.Failed(error);
            }
        }

        private void RecordFailure(long nowMs, Exception error)
        {
            _dirty = true;
            if (nowMs < _dirtyDueMs)
            {
                _dirtyDueMs = nowMs;
            }
            _consecutiveFailures++;
            _lastError = error;
            _retryNotBeforeMs = SaturatingAdd(nowMs, GetRetryDelayMs());
        }

        private long GetRetryDelayMs()
        {
            long delay = _initialRetryDelayMs;
            for (int failure = 1;
                failure < _consecutiveFailures && delay < _maximumRetryDelayMs;
                failure++)
            {
                if (delay > _maximumRetryDelayMs / 2)
                {
                    return _maximumRetryDelayMs;
                }
                delay *= 2;
            }
            return Math.Min(delay, _maximumRetryDelayMs);
        }

        private static long SaturatingAdd(long value, long delta)
        {
            if (delta > 0 && value > Int64.MaxValue - delta)
            {
                return Int64.MaxValue;
            }
            return value + delta;
        }
    }
}
