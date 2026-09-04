using System;
using System.IO;
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
                if (key == null) return false;

                string command = key.GetValue(AppName) as string;
                return IsStartupCommandForExecutable(command, Environment.ProcessPath);
            }
        }

        internal static bool IsStartupCommandForExecutable(
            string command,
            string executablePath)
        {
            if (String.IsNullOrWhiteSpace(command)
                || String.IsNullOrWhiteSpace(executablePath))
            {
                return false;
            }

            string trimmedCommand = command.Trim();
            string commandPath;
            if (trimmedCommand[0] == '"')
            {
                int closingQuote = trimmedCommand.IndexOf('"', 1);
                if (closingQuote != trimmedCommand.Length - 1)
                {
                    return false;
                }
                commandPath = trimmedCommand.Substring(1, closingQuote - 1);
            }
            else
            {
                if (trimmedCommand.IndexOf('"') >= 0)
                {
                    return false;
                }
                commandPath = trimmedCommand;
            }

            string normalizedCommandPath;
            string normalizedExecutablePath;
            if (!TryNormalizeAbsolutePath(commandPath, out normalizedCommandPath)
                || !TryNormalizeAbsolutePath(executablePath, out normalizedExecutablePath))
            {
                return false;
            }

            return String.Equals(
                normalizedCommandPath,
                normalizedExecutablePath,
                StringComparison.OrdinalIgnoreCase);
        }

        public static void SetEnabled(bool enabled)
        {
            using (RegistryKey key = Registry.CurrentUser.CreateSubKey(RunKey, true))
            {
                if (key == null)
                {
                    throw new InvalidOperationException("无法打开当前用户的开机启动注册表项。");
                }
                if (enabled)
                {
                    string exePath = Environment.ProcessPath;
                    if (String.IsNullOrEmpty(exePath))
                    {
                        throw new InvalidOperationException("无法确定当前程序路径，未写入开机启动项。");
                    }
                    key.SetValue(AppName, "\"" + exePath + "\"");
                }
                else
                {
                    key.DeleteValue(AppName, false);
                }
            }
        }

        private static bool TryNormalizeAbsolutePath(string value, out string normalized)
        {
            normalized = null;
            if (String.IsNullOrWhiteSpace(value)
                || value.IndexOf('\0') >= 0
                || !Path.IsPathFullyQualified(value))
            {
                return false;
            }

            try
            {
                normalized = Path.GetFullPath(value);
                return true;
            }
            catch (ArgumentException)
            {
                return false;
            }
            catch (NotSupportedException)
            {
                return false;
            }
            catch (PathTooLongException)
            {
                return false;
            }
        }
    }
}
