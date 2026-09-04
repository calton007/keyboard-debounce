using System;
using System.ComponentModel;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Threading;

namespace KeyboardDebounce
{
    public sealed class KeyboardHook : IDisposable
    {
        private readonly Action<DebounceDecision, int> _onDecision;
        private readonly NativeMethods.LowLevelKeyboardProc _proc;
        private readonly KeyboardEventFilter _filter;
        private IntPtr _hookId;
        private Exception _lastCallbackError;
        private bool _disposed;

        public KeyboardHook(
            DebounceEngine engine,
            Func<bool> isEnabled,
            Action<DebounceDecision, int> onDecision)
            : this(
                engine,
                isEnabled,
                delegate { return false; },
                onDecision)
        {
        }

        public KeyboardHook(
            DebounceEngine engine,
            Func<bool> isEnabled,
            Func<bool> isGameModeActive,
            Action<DebounceDecision, int> onDecision)
        {
            if (engine == null) throw new ArgumentNullException("engine");
            if (isEnabled == null) throw new ArgumentNullException("isEnabled");
            if (isGameModeActive == null) throw new ArgumentNullException("isGameModeActive");
            _onDecision = onDecision;
            _filter = new KeyboardEventFilter(engine, isEnabled, isGameModeActive);
            _proc = HookCallback;
        }

        public void Start()
        {
            if (_disposed) throw new ObjectDisposedException("KeyboardHook");
            if (_hookId != IntPtr.Zero) return;
            using (Process currentProcess = Process.GetCurrentProcess())
            using (ProcessModule currentModule = currentProcess.MainModule)
            {
                IntPtr module = NativeMethods.GetModuleHandle(currentModule.ModuleName);
                _hookId = NativeMethods.SetWindowsHookEx(
                    NativeMethods.WH_KEYBOARD_LL,
                    _proc,
                    module,
                    0);
            }
            if (_hookId == IntPtr.Zero)
            {
                throw new Win32Exception(
                    Marshal.GetLastWin32Error(),
                    "Failed to install keyboard hook.");
            }
        }

        public void Stop()
        {
            if (_hookId == IntPtr.Zero) return;
            if (!NativeMethods.UnhookWindowsHookEx(_hookId))
            {
                throw new Win32Exception(
                    Marshal.GetLastWin32Error(),
                    "Failed to remove keyboard hook.");
            }
            _hookId = IntPtr.Zero;
            ResetRuntimeState();
        }

        public void ResetRuntimeState()
        {
            _filter.ResetRuntimeState();
        }

        public Exception TakeLastError()
        {
            return Interlocked.Exchange(ref _lastCallbackError, null);
        }

        public void Dispose()
        {
            if (_disposed) return;
            Stop();
            _disposed = true;
        }

        private IntPtr HookCallback(int nCode, IntPtr wParam, IntPtr lParam)
        {
            try
            {
                if (nCode >= 0)
                {
                    int message = wParam.ToInt32();
                    bool isDown = message == NativeMethods.WM_KEYDOWN
                        || message == NativeMethods.WM_SYSKEYDOWN;
                    bool isUp = message == NativeMethods.WM_KEYUP
                        || message == NativeMethods.WM_SYSKEYUP;
                    if (isDown || isUp)
                    {
                        var info = (NativeMethods.KbdLlHookStruct)Marshal.PtrToStructure(
                            lParam,
                            typeof(NativeMethods.KbdLlHookStruct));
                        KeyboardFilterResult result = _filter.Process(
                            (int)info.vkCode,
                            info.scanCode,
                            info.flags,
                            isDown,
                            unchecked((long)info.time));
                        NotifyDecision(result.Decision, result.VirtualKeyCode);
                        if (result.Suppress)
                        {
                            return new IntPtr(1);
                        }
                    }
                }
            }
            catch (Exception error)
            {
                RecordCallbackError(error);
            }

            return NativeMethods.CallNextHookEx(_hookId, nCode, wParam, lParam);
        }

        private void NotifyDecision(DebounceDecision decision, int virtualKeyCode)
        {
            if (decision == null || _onDecision == null) return;
            try
            {
                _onDecision(decision, virtualKeyCode);
            }
            catch (Exception error)
            {
                RecordCallbackError(error);
            }
        }

        private void RecordCallbackError(Exception error)
        {
            if (error != null)
            {
                Interlocked.Exchange(ref _lastCallbackError, error);
            }
        }
    }
}
