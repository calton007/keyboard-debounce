using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.Windows.Forms;
using Microsoft.Win32;
using Windows.UI.ViewManagement;

namespace KeyboardDebounce
{
    internal sealed class SystemSettingsThemeService : ISettingsThemeService
    {
        private readonly object _sync = new object();
        private UISettings _uiSettings;
        private bool _uiSettingsSubscribed;
        private bool _systemEventsSubscribed;
        private bool _disposed;
        private SettingsThemeKind _currentKind;
        private SettingsThemePalette _currentPalette;
        private string _detectionError;
        private string _titleBarError;

        public SystemSettingsThemeService()
        {
            var initializationErrors = new List<string>();
            try
            {
                _uiSettings = new UISettings();
                _uiSettings.ColorValuesChanged += OnColorValuesChanged;
                _uiSettingsSubscribed = true;
            }
            catch (Exception error)
            {
                initializationErrors.Add("无法监听系统浅深色主题：" + Describe(error));
            }

            try
            {
                SystemEvents.UserPreferenceChanged += OnUserPreferenceChanged;
                _systemEventsSubscribed = true;
            }
            catch (Exception error)
            {
                initializationErrors.Add("无法监听系统高对比度设置：" + Describe(error));
            }

            RefreshTheme(initializationErrors, false);
        }

        public event EventHandler ThemeChanged;

        public SettingsThemeKind CurrentKind
        {
            get
            {
                lock (_sync) return _currentKind;
            }
        }

        public SettingsThemePalette CurrentPalette
        {
            get
            {
                lock (_sync) return _currentPalette;
            }
        }

        public string LastError
        {
            get
            {
                lock (_sync)
                {
                    if (String.IsNullOrEmpty(_detectionError)) return _titleBarError ?? "";
                    if (String.IsNullOrEmpty(_titleBarError)) return _detectionError;
                    return _detectionError + " " + _titleBarError;
                }
            }
        }

        internal static bool IsForegroundLight(byte red, byte green, byte blue)
        {
            return ((5 * green) + (2 * red) + blue) > (8 * 128);
        }

        public void ApplyTitleBar(Form form)
        {
            if (form == null || form.IsDisposed || !form.IsHandleCreated) return;
            if (!OperatingSystem.IsWindowsVersionAtLeast(10, 0, 22000)) return;

            int dark = CurrentKind == SettingsThemeKind.Dark ? 1 : 0;
            try
            {
                int result = NativeMethods.DwmSetWindowAttribute(
                    form.Handle,
                    NativeMethods.DWMWA_USE_IMMERSIVE_DARK_MODE,
                    ref dark,
                    Marshal.SizeOf(typeof(int)));
                SetTitleBarError(
                    result < 0
                        ? "Windows 11 标题栏主题同步失败：HRESULT 0x" + ((uint)result).ToString("X8") + "。"
                        : "");
            }
            catch (DllNotFoundException error)
            {
                SetTitleBarError("Windows 11 标题栏主题同步失败：" + Describe(error));
            }
            catch (EntryPointNotFoundException error)
            {
                SetTitleBarError("Windows 11 标题栏主题同步失败：" + Describe(error));
            }
        }

        public void Dispose()
        {
            lock (_sync)
            {
                if (_disposed) return;
                _disposed = true;
            }

            if (_uiSettingsSubscribed && _uiSettings != null)
            {
                _uiSettings.ColorValuesChanged -= OnColorValuesChanged;
                _uiSettingsSubscribed = false;
            }
            if (_systemEventsSubscribed)
            {
                SystemEvents.UserPreferenceChanged -= OnUserPreferenceChanged;
                _systemEventsSubscribed = false;
            }
        }

        private void OnColorValuesChanged(UISettings sender, object args)
        {
            RefreshTheme(null, true);
        }

        private void OnUserPreferenceChanged(object sender, UserPreferenceChangedEventArgs args)
        {
            RefreshTheme(null, true);
        }

        private void RefreshTheme(ICollection<string> existingErrors, bool notify)
        {
            var errors = existingErrors == null
                ? new List<string>()
                : new List<string>(existingErrors);
            SettingsThemeKind nextKind = SettingsThemeKind.Light;

            try
            {
                if (SystemInformation.HighContrast)
                {
                    nextKind = SettingsThemeKind.HighContrast;
                }
                else if (_uiSettings != null)
                {
                    Windows.UI.Color foreground = _uiSettings.GetColorValue(UIColorType.Foreground);
                    nextKind = IsForegroundLight(foreground.R, foreground.G, foreground.B)
                        ? SettingsThemeKind.Dark
                        : SettingsThemeKind.Light;
                }
                else
                {
                    errors.Add("系统主题检测不可用，已回退浅色主题。");
                }
            }
            catch (Exception error)
            {
                nextKind = SettingsThemeKind.Light;
                errors.Add("系统主题检测失败，已回退浅色主题：" + Describe(error));
            }

            EventHandler handler = null;
            lock (_sync)
            {
                if (_disposed) return;
                _currentKind = nextKind;
                _currentPalette = SettingsThemePalette.Create(nextKind);
                _detectionError = errors.Count == 0 ? "" : String.Join(" ", errors);
                if (notify) handler = ThemeChanged;
            }

            if (handler != null) handler(this, EventArgs.Empty);
        }

        private static string Describe(Exception error)
        {
            return error.GetType().Name + " - " + error.Message;
        }

        private void SetTitleBarError(string error)
        {
            lock (_sync)
            {
                if (_disposed) return;
                _titleBarError = error ?? "";
            }
        }
    }
}
