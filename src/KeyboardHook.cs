using System;
using System.Diagnostics;
using System.Runtime.InteropServices;

namespace KeyboardDebounce
{
    public sealed class KeyboardHook : IDisposable
    {
        private readonly DebounceEngine _engine;
        private readonly Func<bool> _isEnabled;
        private readonly Action<DebounceDecision, int> _onDecision;
        private readonly NativeMethods.LowLevelKeyboardProc _proc;
        private IntPtr _hookId;

        public KeyboardHook(DebounceEngine engine, Func<bool> isEnabled, Action<DebounceDecision, int> onDecision)
        {
            if (engine == null) throw new ArgumentNullException("engine");
            if (isEnabled == null) throw new ArgumentNullException("isEnabled");
            _engine = engine;
            _isEnabled = isEnabled;
            _onDecision = onDecision;
            _proc = HookCallback;
        }

        public void Start()
        {
            if (_hookId != IntPtr.Zero) return;
            using (Process currentProcess = Process.GetCurrentProcess())
            using (ProcessModule currentModule = currentProcess.MainModule)
            {
                IntPtr module = NativeMethods.GetModuleHandle(currentModule.ModuleName);
                _hookId = NativeMethods.SetWindowsHookEx(NativeMethods.WH_KEYBOARD_LL, _proc, module, 0);
            }
            if (_hookId == IntPtr.Zero)
            {
                throw new InvalidOperationException("Failed to install keyboard hook.");
            }
        }

        public void Stop()
        {
            if (_hookId == IntPtr.Zero) return;
            NativeMethods.UnhookWindowsHookEx(_hookId);
            _hookId = IntPtr.Zero;
        }

        public void Dispose()
        {
            Stop();
        }

        private IntPtr HookCallback(int nCode, IntPtr wParam, IntPtr lParam)
        {
            if (nCode >= 0)
            {
                int message = wParam.ToInt32();
                bool isDown = message == NativeMethods.WM_KEYDOWN || message == NativeMethods.WM_SYSKEYDOWN;
                bool isUp = message == NativeMethods.WM_KEYUP || message == NativeMethods.WM_SYSKEYUP;
                if (isDown || isUp)
                {
                    var info = (NativeMethods.KbdLlHookStruct)Marshal.PtrToStructure(
                        lParam,
                        typeof(NativeMethods.KbdLlHookStruct));

                    var decision = _engine.Process(new KeyEventSample
                    {
                        VirtualKeyCode = (int)info.vkCode,
                        IsKeyDown = isDown,
                        TimestampMs = unchecked((long)info.time)
                    });

                    if (_onDecision != null)
                    {
                        _onDecision(decision, (int)info.vkCode);
                    }

                    if (_isEnabled() && decision.Suppress)
                    {
                        return new IntPtr(1);
                    }
                }
            }

            return NativeMethods.CallNextHookEx(_hookId, nCode, wParam, lParam);
        }
    }
}
