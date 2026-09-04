using System;
using System.Collections.Generic;

namespace KeyboardDebounce
{
    internal enum SettingsPageId
    {
        Overview = 1,
        NormalDebounce = 2,
        GameMode = 3,
        KeyManagement = 4,
        ApplicationSettings = 5
    }

    internal enum KeyFilterScope
    {
        All,
        LearnedOnly,
        GameFilteredOnly,
        IgnoredOnly
    }

    internal static class SettingsFilter
    {
        public static List<SettingsGridRow> Apply(
            IEnumerable<SettingsGridRow> rows,
            string searchText,
            KeyFilterScope scope)
        {
            var result = new List<SettingsGridRow>();
            if (rows == null) return result;

            string normalizedSearch = NormalizeSearch(searchText);
            foreach (SettingsGridRow row in rows)
            {
                if (!MatchesScope(row, scope)) continue;
                if (!MatchesSearch(row, normalizedSearch)) continue;
                result.Add(row);
            }

            return result;
        }

        public static string NormalizeSearch(string searchText)
        {
            return searchText == null ? "" : searchText.Trim();
        }

        public static bool MatchesScope(SettingsGridRow row, KeyFilterScope scope)
        {
            if (row == null) return false;

            switch (scope)
            {
                case KeyFilterScope.LearnedOnly:
                    return row.HasLearning;
                case KeyFilterScope.GameFilteredOnly:
                    return row.GameFiltered;
                case KeyFilterScope.IgnoredOnly:
                    return row.Ignored;
                default:
                    return true;
            }
        }

        public static bool MatchesSearch(SettingsGridRow row, string normalizedSearch)
        {
            if (row == null) return false;
            if (String.IsNullOrEmpty(normalizedSearch)) return true;

            string vk = row.Vk.ToString();
            if (vk.IndexOf(normalizedSearch, StringComparison.OrdinalIgnoreCase) >= 0)
            {
                return true;
            }

            string keyName = row.KeyName ?? "";
            return keyName.IndexOf(normalizedSearch, StringComparison.OrdinalIgnoreCase) >= 0;
        }
    }
}
