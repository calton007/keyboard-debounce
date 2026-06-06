using System;
using Microsoft.Win32;

namespace KeyboardDebounce
{
    public static class StartupManager
    {
        private const string RunKey = @"Software\Microsoft\Windows\CurrentVersion\Run";
        private const string AppName = "KeyboardDebounce";

        public static bool IsEnabled()
        {
            using (RegistryKey key = Registry.CurrentUser.OpenSubKey(RunKey, false))
            {
                return key != null && key.GetValue(AppName) != null;
            }
        }

        public static void SetEnabled(bool enabled)
        {
            using (RegistryKey key = Registry.CurrentUser.OpenSubKey(RunKey, true))
            {
                if (key == null) return;
                if (enabled)
                {
                    string exePath = Environment.ProcessPath;
                    if (String.IsNullOrEmpty(exePath))
                    {
                        return;
                    }
                    key.SetValue(AppName, "\"" + exePath + "\"");
                }
                else
                {
                    key.DeleteValue(AppName, false);
                }
            }
        }
    }
}
