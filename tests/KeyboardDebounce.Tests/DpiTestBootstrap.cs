using System;
using System.Runtime.CompilerServices;
using System.Windows.Forms;

namespace KeyboardDebounce.Tests
{
    internal static class DpiTestBootstrap
    {
        public static bool IsInitialized { get; private set; }
        public static bool IsHighDpiPerMonitorV2Enabled { get; private set; }

        [ModuleInitializer]
        internal static void InitializeModule()
        {
            Ensure();
        }

        public static void Ensure()
        {
            if (IsInitialized) return;
            IsInitialized = true;

            try
            {
                bool applied = Application.SetHighDpiMode(HighDpiMode.PerMonitorV2);
                IsHighDpiPerMonitorV2Enabled = applied
                    || Application.HighDpiMode == HighDpiMode.PerMonitorV2;
            }
            catch (PlatformNotSupportedException)
            {
                // Some test hosts/runtime combinations may not support per-monitor v2.
                IsHighDpiPerMonitorV2Enabled = false;
            }
        }
    }
}
