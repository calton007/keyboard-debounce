using System;
using System.Drawing;

namespace KeyboardDebounce
{
    internal static class WindowIconManager
    {
        public static void Apply(IntPtr hwnd, Icon icon)
        {
            if (hwnd == IntPtr.Zero) throw new ArgumentException("Window handle is required.", "hwnd");
            if (icon == null) throw new ArgumentNullException("icon");

            IntPtr iconHandle = icon.Handle;
            NativeMethods.SendMessage(
                hwnd,
                NativeMethods.WM_SETICON,
                (IntPtr)NativeMethods.ICON_BIG,
                iconHandle);
            NativeMethods.SendMessage(
                hwnd,
                NativeMethods.WM_SETICON,
                (IntPtr)NativeMethods.ICON_SMALL,
                iconHandle);
            NativeMethods.SendMessage(
                hwnd,
                NativeMethods.WM_SETICON,
                (IntPtr)NativeMethods.ICON_SMALL2,
                iconHandle);
        }
    }
}
