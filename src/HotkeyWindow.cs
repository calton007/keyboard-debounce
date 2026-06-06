using System;
using System.Windows.Forms;

namespace KeyboardDebounce
{
    public sealed class HotkeyWindow : NativeWindow, IDisposable
    {
        private const int HotkeyId = 100;
        private const int WmHotkey = 0x0312;
        private readonly Action _onHotkey;

        public HotkeyWindow(Action onHotkey)
        {
            _onHotkey = onHotkey;
            CreateHandle(new CreateParams());
            NativeMethods.RegisterHotKey(
                Handle,
                HotkeyId,
                NativeMethods.MOD_CONTROL | NativeMethods.MOD_ALT,
                NativeMethods.VK_F12);
        }

        protected override void WndProc(ref Message m)
        {
            if (m.Msg == WmHotkey && _onHotkey != null)
            {
                _onHotkey();
            }
            base.WndProc(ref m);
        }

        public void Dispose()
        {
            NativeMethods.UnregisterHotKey(Handle, HotkeyId);
            DestroyHandle();
        }
    }
}
