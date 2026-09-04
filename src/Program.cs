using System;
using System.Security.Principal;
using System.Threading;
using System.Runtime.InteropServices;
using System.Windows.Forms;
using Microsoft.UI.Dispatching;

namespace KeyboardDebounce
{
    internal static class Program
    {
        private static App _application;

        [STAThread]
        private static void Main()
        {
            ConfigureWindowsAppRuntimeBaseDirectory();

            try
            {
                bool createdNew;
                using (var instance = new Mutex(true, GetMutexName(), out createdNew))
                {
                    if (!createdNew)
                    {
                        MessageBox.Show(
                            "Keyboard Debounce 已经在当前用户会话中运行。",
                            "Keyboard Debounce",
                            MessageBoxButtons.OK,
                            MessageBoxIcon.Information);
                        return;
                    }

                    Application.SetHighDpiMode(HighDpiMode.PerMonitorV2);
                    Application.EnableVisualStyles();
                    Application.SetCompatibleTextRenderingDefault(false);
                    XamlCheckProcessRequirements();
                    WinRT.ComWrappersSupport.InitializeComWrappers();
                    Microsoft.UI.Xaml.Application.Start(delegate
                    {
                        var context = new DispatcherQueueSynchronizationContext(
                            DispatcherQueue.GetForCurrentThread());
                        SynchronizationContext.SetSynchronizationContext(context);
                        _application = new App();
                    });
                    GC.KeepAlive(instance);
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show(
                    "Keyboard Debounce 启动失败：" + Environment.NewLine
                    + ex.GetType().Name + " - " + ex.Message,
                    "Keyboard Debounce",
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Error);
            }
        }

        internal static void ConfigureWindowsAppRuntimeBaseDirectory()
        {
            // The Windows App SDK auto-initializers are intentionally disabled because
            // this application owns Main. A self-extracting single-file build therefore
            // needs an explicit base directory before any WinUI API is touched.
            Environment.SetEnvironmentVariable(
                "MICROSOFT_WINDOWSAPPRUNTIME_BASE_DIRECTORY",
                AppContext.BaseDirectory,
                EnvironmentVariableTarget.Process);
        }

        private static string GetMutexName()
        {
            string userIdentity = Environment.UserName;
            using (WindowsIdentity identity = WindowsIdentity.GetCurrent())
            {
                if (identity.User != null)
                {
                    userIdentity = identity.User.Value;
                }
            }
            return "Local\\KeyboardDebounce-" + userIdentity.Replace('\\', '_');
        }

        [DllImport("Microsoft.ui.xaml.dll")]
        private static extern void XamlCheckProcessRequirements();
    }
}
