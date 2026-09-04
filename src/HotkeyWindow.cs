using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Windows.Forms;

namespace KeyboardDebounce
{
    public sealed class HotkeyWindow : NativeWindow, IDisposable
    {
        private const int FirstHotkeyId = 100;
        private const int LastHotkeyId = 0xBFFF;
        private const int WmHotkey = 0x0312;
        private readonly Action _onHotkey;
        private readonly HashSet<int> _registeredIds = new HashSet<int>();
        private bool _disposed;
        private int _currentHotkeyId;
        private int _nextHotkeyId = FirstHotkeyId;
        private HotkeyBinding _currentBinding;

        public HotkeyWindow(Action onHotkey, string hotkeyText)
        {
            _onHotkey = onHotkey;
            CreateHandle(new CreateParams());
            TryRegister(hotkeyText);
        }

        public bool IsRegistered
        {
            get { return _currentHotkeyId != 0; }
        }

        public string RegisteredHotkeyText { get; private set; }

        public string RegistrationError { get; private set; }

        public bool TryRegister(string hotkeyText)
        {
            return Rebind(hotkeyText);
        }

        public bool Rebind(string hotkeyText)
        {
            if (_disposed)
            {
                RegistrationError = "热键窗口已释放，无法注册热键。";
                return false;
            }

            HotkeyBinding binding;
            string parseError;
            if (!HotkeyBinding.TryParse(hotkeyText, out binding, out parseError))
            {
                RegistrationError = parseError;
                return false;
            }

            string normalizedText = hotkeyText.Trim();
            if (_currentBinding != null && _currentBinding.IsEquivalentTo(binding))
            {
                RegisteredHotkeyText = normalizedText;
                RegistrationError = null;
                return true;
            }

            int candidateId;
            if (!TryGetAvailableHotkeyId(out candidateId))
            {
                RegistrationError = "没有可用的全局热键标识，无法注册热键。";
                return false;
            }

            if (!NativeMethods.RegisterHotKey(
                Handle,
                candidateId,
                binding.Modifiers,
                binding.VirtualKeyCode))
            {
                RegistrationError = GetLastWin32ErrorMessage("无法注册全局热键");
                return false;
            }
            _registeredIds.Add(candidateId);

            if (_currentHotkeyId != 0)
            {
                int previousId = _currentHotkeyId;
                if (!NativeMethods.UnregisterHotKey(Handle, previousId))
                {
                    string previousError = GetLastWin32ErrorMessage("无法注销原全局热键");
                    if (NativeMethods.UnregisterHotKey(Handle, candidateId))
                    {
                        _registeredIds.Remove(candidateId);
                        RegistrationError = previousError + "。已保留原热键。";
                    }
                    else
                    {
                        string rollbackError = GetLastWin32ErrorMessage("无法回滚新全局热键");
                        RegistrationError = previousError
                            + "；"
                            + rollbackError
                            + "。原热键仍作为当前热键，退出程序时会再次清理新注册。";
                    }
                    return false;
                }
                _registeredIds.Remove(previousId);
            }

            _currentHotkeyId = candidateId;
            _currentBinding = binding;
            RegisteredHotkeyText = normalizedText;
            RegistrationError = null;
            return true;
        }

        protected override void WndProc(ref Message m)
        {
            if (m.Msg == WmHotkey
                && _currentHotkeyId != 0
                && m.WParam.ToInt32() == _currentHotkeyId
                && _onHotkey != null)
            {
                _onHotkey();
            }
            base.WndProc(ref m);
        }

        public void Dispose()
        {
            if (_disposed) return;

            var errors = new List<Exception>();
            foreach (int id in new List<int>(_registeredIds))
            {
                if (!NativeMethods.UnregisterHotKey(Handle, id))
                {
                    int errorCode = Marshal.GetLastWin32Error();
                    errors.Add(new Win32Exception(
                        errorCode,
                        "无法注销全局热键（ID " + id + "）。"));
                }
            }

            _registeredIds.Clear();
            _currentHotkeyId = 0;
            _currentBinding = null;
            RegisteredHotkeyText = null;

            try
            {
                DestroyHandle();
            }
            catch (Exception error)
            {
                errors.Add(error);
            }
            finally
            {
                _disposed = true;
            }

            if (errors.Count == 1)
            {
                throw errors[0];
            }
            if (errors.Count > 1)
            {
                throw new AggregateException(errors);
            }
        }

        private bool TryGetAvailableHotkeyId(out int hotkeyId)
        {
            int capacity = LastHotkeyId - FirstHotkeyId + 1;
            for (int attempt = 0; attempt < capacity; attempt++)
            {
                int candidate = _nextHotkeyId;
                _nextHotkeyId = candidate == LastHotkeyId
                    ? FirstHotkeyId
                    : candidate + 1;
                if (!_registeredIds.Contains(candidate))
                {
                    hotkeyId = candidate;
                    return true;
                }
            }

            hotkeyId = 0;
            return false;
        }

        private static string GetLastWin32ErrorMessage(string action)
        {
            int errorCode = Marshal.GetLastWin32Error();
            return action + "：" + new Win32Exception(errorCode).Message;
        }
    }
}
