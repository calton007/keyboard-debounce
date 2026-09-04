using System;
using KeyboardDebounce.WinUI;
using Microsoft.UI.Xaml;

namespace KeyboardDebounce
{
    public partial class App : Application
    {
        private TrayAppContext _trayContext;
        private SettingsWindow _settingsWindow;
        private bool _isShuttingDown;

        public App()
        {
            InitializeComponent();
        }

        protected override void OnLaunched(LaunchActivatedEventArgs args)
        {
            base.OnLaunched(args);
            if (_trayContext != null) return;

            _trayContext = new TrayAppContext();
            _trayContext.ShowSettingsWindowRequested += HandleShowSettingsWindowRequested;
            _trayContext.SettingsDataRefreshRequested += HandleSettingsDataRefreshRequested;
            _trayContext.ExitRequested += HandleExitRequested;

            // Keep one native WinUI window alive for the entire process lifetime.
            // Silent startup leaves it hidden; without a window WinUI can tear down
            // the dispatcher even though the tray and keyboard hook are still active.
            _settingsWindow = new SettingsWindow(_trayContext.UiContext);
            if (_trayContext.ShouldShowSettingsWindowOnStartup)
            {
                ShowSettingsWindow();
            }
        }

        private void HandleShowSettingsWindowRequested()
        {
            ShowSettingsWindow();
        }

        private void HandleSettingsDataRefreshRequested()
        {
            if (_settingsWindow != null)
            {
                _settingsWindow.RefreshData();
            }
        }

        private void HandleExitRequested()
        {
            ShutdownApplication();
        }

        private void ShowSettingsWindow()
        {
            if (_trayContext == null)
            {
                throw new InvalidOperationException("托盘上下文尚未初始化。");
            }

            _settingsWindow.ShowWindow();
            _settingsWindow.RefreshData();
            _settingsWindow.RefreshRuntimeState();
        }

        private void ShutdownApplication()
        {
            if (_isShuttingDown) return;
            _isShuttingDown = true;

            _settingsWindow?.CloseForExit();
            _trayContext?.CloseForExit();
            Exit();
        }
    }
}
