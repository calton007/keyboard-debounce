using System;
using System.Drawing;
using System.IO;

namespace KeyboardDebounce
{
    internal static class AppIcon
    {
        public static Icon Load()
        {
            string exePath = Environment.ProcessPath;
            Icon associated = TryExtractAssociatedIcon(exePath);
            if (associated != null)
            {
                return associated;
            }

            string exeDirectory = AppContext.BaseDirectory;
            string localIcon = String.IsNullOrEmpty(exeDirectory)
                ? "keyboard-debounce.ico"
                : Path.Combine(exeDirectory, "keyboard-debounce.ico");
            if (File.Exists(localIcon))
            {
                try
                {
                    return new Icon(localIcon);
                }
                catch
                {
                }
            }

            return SystemIcons.Shield;
        }

        private static Icon TryExtractAssociatedIcon(string exePath)
        {
            try
            {
                if (String.IsNullOrEmpty(exePath) || !File.Exists(exePath))
                {
                    return null;
                }

                return Icon.ExtractAssociatedIcon(exePath);
            }
            catch
            {
                return null;
            }
        }
    }
}
