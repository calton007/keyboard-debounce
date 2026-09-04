using System;
using System.Drawing;
using System.Windows.Forms;
using KeyboardDebounce;
using Xunit;

namespace KeyboardDebounce.Tests
{
    public sealed class WindowIconTests
    {
        [Fact]
        public void ApplySetsBigAndSmallNativeWindowIcons()
        {
            var window = new NativeWindow();
            try
            {
                window.CreateHandle(new CreateParams
                {
                    Caption = "KeyboardDebounce icon test"
                });
                using Icon icon = (Icon)SystemIcons.Application.Clone();

                WindowIconManager.Apply(window.Handle, icon);

                Assert.Equal(
                    icon.Handle,
                    NativeMethods.SendMessage(
                        window.Handle,
                        NativeMethods.WM_GETICON,
                        (IntPtr)NativeMethods.ICON_BIG,
                        IntPtr.Zero));
                Assert.Equal(
                    icon.Handle,
                    NativeMethods.SendMessage(
                        window.Handle,
                        NativeMethods.WM_GETICON,
                        (IntPtr)NativeMethods.ICON_SMALL,
                        IntPtr.Zero));
            }
            finally
            {
                window.DestroyHandle();
            }
        }
    }
}
