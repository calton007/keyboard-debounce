using System;
using System.Collections.Generic;

namespace KeyboardDebounce
{
    public sealed class SettingsGridRow
    {
        public int Vk { get; set; }
        public string KeyName { get; set; }
        public bool Ignored { get; set; }
        public bool HasLearning { get; set; }
        public int ThresholdMs { get; set; }
        public long AcceptedCount { get; set; }
        public long SuppressedCount { get; set; }
        public string LastSeen { get; set; }
    }

    public static class SettingsGridRowSorter
    {
        public static void Sort(List<SettingsGridRow> rows, string columnName, bool ascending)
        {
            if (rows == null) throw new ArgumentNullException("rows");
            if (String.IsNullOrEmpty(columnName)) columnName = "Vk";

            rows.Sort(delegate(SettingsGridRow left, SettingsGridRow right)
            {
                int ignoredGroupResult = CompareIgnoredGroup(left, right, columnName);
                if (ignoredGroupResult != 0)
                {
                    return ignoredGroupResult;
                }

                int result = Compare(left, right, columnName);
                return ascending ? result : -result;
            });
        }

        private static int CompareIgnoredGroup(SettingsGridRow left, SettingsGridRow right, string columnName)
        {
            if (columnName == "Vk") return 0;
            if (left == null || right == null) return 0;
            if (left.Ignored == right.Ignored) return 0;
            return left.Ignored ? 1 : -1;
        }

        public static int Compare(SettingsGridRow left, SettingsGridRow right, string columnName)
        {
            if (left == null && right == null) return 0;
            if (left == null) return 1;
            if (right == null) return -1;

            switch (columnName)
            {
                case "Vk":
                    return left.Vk.CompareTo(right.Vk);
                case "Threshold":
                    return CompareNullableNumber(left.HasLearning, left.ThresholdMs, right.HasLearning, right.ThresholdMs);
                case "Accepted":
                    return CompareNullableNumber(left.HasLearning, left.AcceptedCount, right.HasLearning, right.AcceptedCount);
                case "Suppressed":
                    return CompareNullableNumber(left.HasLearning, left.SuppressedCount, right.HasLearning, right.SuppressedCount);
                case "Ignored":
                    return left.Ignored.CompareTo(right.Ignored);
                case "LastSeen":
                    return String.Compare(left.LastSeen, right.LastSeen, StringComparison.Ordinal);
                case "KeyName":
                    return String.Compare(left.KeyName, right.KeyName, StringComparison.Ordinal);
                default:
                    return left.Vk.CompareTo(right.Vk);
            }
        }

        private static int CompareNullableNumber(bool leftHasValue, long leftValue, bool rightHasValue, long rightValue)
        {
            if (!leftHasValue && !rightHasValue) return 0;
            if (!leftHasValue) return 1;
            if (!rightHasValue) return -1;
            return leftValue.CompareTo(rightValue);
        }
    }
}
