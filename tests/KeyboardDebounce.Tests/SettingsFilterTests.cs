using System.Collections.Generic;
using System.Linq;
using Xunit;

namespace KeyboardDebounce.Tests
{
    public sealed class SettingsFilterTests
    {
        [Fact]
        public void ApplyFiltersByScopeAndSearch()
        {
            var rows = new List<SettingsGridRow>
            {
                new SettingsGridRow { Vk = 65, KeyName = "A", HasLearning = true },
                new SettingsGridRow { Vk = 66, KeyName = "B", GameFiltered = true },
                new SettingsGridRow { Vk = 67, KeyName = "Space", Ignored = true }
            };

            List<SettingsGridRow> learned = SettingsFilter.Apply(rows, "", KeyFilterScope.LearnedOnly);
            List<SettingsGridRow> game = SettingsFilter.Apply(rows, "66", KeyFilterScope.GameFilteredOnly);
            List<SettingsGridRow> ignored = SettingsFilter.Apply(rows, "spa", KeyFilterScope.IgnoredOnly);

            Assert.Single(learned);
            Assert.Equal(65, learned[0].Vk);
            Assert.Single(game);
            Assert.Equal(66, game[0].Vk);
            Assert.Single(ignored);
            Assert.Equal(67, ignored[0].Vk);
        }

        [Fact]
        public void SearchMatchesDecimalVirtualKeyAndKeyNameWithoutChangingRows()
        {
            var rows = new List<SettingsGridRow>
            {
                new SettingsGridRow { Vk = 13, KeyName = "Enter", HasLearning = true },
                new SettingsGridRow { Vk = 65, KeyName = "A", GameFiltered = true },
                new SettingsGridRow { Vk = 88, KeyName = "X", Ignored = true }
            };

            Assert.Equal(3, SettingsFilter.Apply(rows, "", KeyFilterScope.All).Count);
            Assert.Equal(65, SettingsFilter.Apply(rows, "65", KeyFilterScope.All)[0].Vk);
            Assert.Equal(13, SettingsFilter.Apply(rows, "ent", KeyFilterScope.All)[0].Vk);
            Assert.Equal(3, rows.Count);
        }

        [Fact]
        public void SortByVkKeepsIgnoresInNumericOrder()
        {
            var rows = new List<SettingsGridRow>
            {
                new SettingsGridRow { Vk = 70, KeyName = "F", Ignored = true },
                new SettingsGridRow { Vk = 65, KeyName = "A", Ignored = false },
                new SettingsGridRow { Vk = 80, KeyName = "E", Ignored = false },
                new SettingsGridRow { Vk = 67, KeyName = "C", Ignored = true },
            };

            SettingsGridRowSorter.Sort(rows, "Vk", true);
            int[] actual = rows.Select(r => r.Vk).ToArray();
            Assert.Equal(new[] { 65, 67, 70, 80 }, actual);
        }

        [Fact]
        public void SortByNonVkMovesIgnoredItemsToBottom()
        {
            var rows = new List<SettingsGridRow>
            {
                new SettingsGridRow { Vk = 66, KeyName = "C", Ignored = false },
                new SettingsGridRow { Vk = 65, KeyName = "A", Ignored = true },
                new SettingsGridRow { Vk = 67, KeyName = "B", Ignored = false },
                new SettingsGridRow { Vk = 68, KeyName = "Z", Ignored = true },
            };

            SettingsGridRowSorter.Sort(rows, "KeyName", true);
            int[] actual = rows.Select(r => r.Vk).ToArray();
            Assert.Equal(new[] { 67, 66, 65, 68 }, actual);
        }

        [Fact]
        public void SortByThresholdPrioritizesRowsWithLearning()
        {
            var rows = new List<SettingsGridRow>
            {
                new SettingsGridRow { Vk = 65, KeyName = "A", HasLearning = false, ThresholdMs = 250 },
                new SettingsGridRow { Vk = 66, KeyName = "B", HasLearning = true, ThresholdMs = 20 },
                new SettingsGridRow { Vk = 67, KeyName = "C", HasLearning = true, ThresholdMs = 100 },
            };

            SettingsGridRowSorter.Sort(rows, "Threshold", true);
            int[] actual = rows.Select(r => r.Vk).ToArray();
            Assert.Equal(new[] { 66, 67, 65 }, actual);
        }
    }
}
