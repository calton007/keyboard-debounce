using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using KeyboardDebounce;
using Xunit;

namespace KeyboardDebounce.Tests
{
    public sealed class SaveTaskSlotTests
    {
        [Fact]
        public void OrdinaryDirtyMarksCoalesceAtFirstCheckpoint()
        {
            var slot = NewSlot();
            int starts = 0;

            slot.MarkDirty(100, false);
            slot.MarkDirty(200, false);

            Assert.Equal(5100, slot.NextAttemptMs);
            Assert.Equal(
                SaveTaskResultKind.None,
                slot.TryStart(5099, delegate
                {
                    starts++;
                    return Task.CompletedTask;
                }).Kind);
            Assert.Equal(0, starts);

            Assert.Equal(
                SaveTaskResultKind.Started,
                slot.TryStart(5100, delegate
                {
                    starts++;
                    return Task.CompletedTask;
                }).Kind);
            Assert.Equal(SaveTaskResultKind.Succeeded, slot.Observe(5100).Kind);
            Assert.Equal(1, starts);
            Assert.False(slot.IsDirty);
        }

        [Fact]
        public void DirtyMarkedDuringActiveTaskStartsAnotherSave()
        {
            var slot = NewSlot();
            var firstSave = new TaskCompletionSource<object>(
                TaskCreationOptions.RunContinuationsAsynchronously);
            int starts = 0;

            slot.MarkDirty(0, true);
            slot.TryStart(0, delegate
            {
                starts++;
                return firstSave.Task;
            });
            slot.MarkDirty(10, true);

            firstSave.SetResult(null);
            Assert.Equal(SaveTaskResultKind.Succeeded, slot.Wait(10).Kind);
            Assert.True(slot.IsDirty);

            Assert.Equal(
                SaveTaskResultKind.Started,
                slot.TryStart(10, delegate
                {
                    starts++;
                    return Task.CompletedTask;
                }).Kind);
            Assert.Equal(SaveTaskResultKind.Succeeded, slot.Observe(10).Kind);
            Assert.Equal(2, starts);
        }

        [Fact]
        public void FailureRestoresDirtyAndRetriesLatestStateWithCappedBackoff()
        {
            var slot = new SaveTaskSlot(5000, 100, 400);
            var savedVersions = new List<int>();
            int modelVersion = 1;

            slot.MarkDirty(0, true);
            StartFailedSave(slot, 0, modelVersion, savedVersions);
            Assert.Equal(100, slot.NextAttemptMs);

            modelVersion = 2;
            slot.MarkDirty(1, true);
            Assert.Equal(100, slot.NextAttemptMs);
            Assert.Equal(SaveTaskResultKind.None, slot.TryStart(99, TaskFactory).Kind);

            StartFailedSave(slot, 100, modelVersion, savedVersions);
            Assert.Equal(300, slot.NextAttemptMs);
            StartFailedSave(slot, 300, modelVersion, savedVersions);
            Assert.Equal(700, slot.NextAttemptMs);
            StartFailedSave(slot, 700, modelVersion, savedVersions);
            Assert.Equal(1100, slot.NextAttemptMs);
            Assert.Equal(new[] { 1, 2, 2, 2 }, savedVersions);
            Assert.Equal(4, slot.ConsecutiveFailures);

            Task TaskFactory()
            {
                return Task.CompletedTask;
            }
        }

        [Fact]
        public void SuccessfulRetryClearsFailureState()
        {
            var slot = new SaveTaskSlot(5000, 100, 400);
            slot.MarkDirty(0, true);
            slot.TryStart(
                0,
                delegate { return Task.FromException(new InvalidOperationException("disk")); });
            SaveTaskResult failed = slot.Observe(0);

            Assert.Equal(SaveTaskResultKind.Failed, failed.Kind);
            Assert.NotNull(slot.LastError);

            Assert.Equal(
                SaveTaskResultKind.Started,
                slot.TryStart(100, delegate { return Task.CompletedTask; }).Kind);
            Assert.Equal(SaveTaskResultKind.Succeeded, slot.Observe(100).Kind);
            Assert.Null(slot.LastError);
            Assert.Equal(0, slot.ConsecutiveFailures);
            Assert.False(slot.IsDirty);
        }

        [Fact]
        public void SynchronousStartFailureDoesNotLoseDirtyState()
        {
            var slot = new SaveTaskSlot(5000, 100, 400);
            slot.MarkDirty(0, true);

            SaveTaskResult result = slot.TryStart(
                0,
                delegate { throw new InvalidOperationException("snapshot"); });

            Assert.Equal(SaveTaskResultKind.Failed, result.Kind);
            Assert.True(slot.IsDirty);
            Assert.False(slot.HasActiveTask);
            Assert.Equal(100, slot.NextAttemptMs);
            Assert.IsType<InvalidOperationException>(slot.LastError);
        }

        private static SaveTaskSlot NewSlot()
        {
            return new SaveTaskSlot(5000, 100, 400);
        }

        private static void StartFailedSave(
            SaveTaskSlot slot,
            long nowMs,
            int version,
            List<int> savedVersions)
        {
            Assert.Equal(
                SaveTaskResultKind.Started,
                slot.TryStart(
                    nowMs,
                    delegate
                    {
                        savedVersions.Add(version);
                        return Task.FromException(new InvalidOperationException("disk"));
                    }).Kind);
            Assert.Equal(SaveTaskResultKind.Failed, slot.Observe(nowMs).Kind);
            Assert.True(slot.IsDirty);
        }
    }
}
