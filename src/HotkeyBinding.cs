using System;
using System.Windows.Forms;

namespace KeyboardDebounce
{
    internal sealed class HotkeyBinding
    {
        private HotkeyBinding(int modifiers, int virtualKeyCode)
        {
            Modifiers = modifiers;
            VirtualKeyCode = virtualKeyCode;
        }

        public int Modifiers { get; private set; }
        public int VirtualKeyCode { get; private set; }

        public bool IsEquivalentTo(HotkeyBinding other)
        {
            return other != null
                && Modifiers == other.Modifiers
                && VirtualKeyCode == other.VirtualKeyCode;
        }

        public static bool TryParse(string value, out HotkeyBinding binding, out string error)
        {
            binding = null;
            error = null;
            if (String.IsNullOrWhiteSpace(value))
            {
                error = "暂停热键不能为空。";
                return false;
            }

            int modifiers = NativeMethods.MOD_NOREPEAT;
            int userModifiers = 0;
            int virtualKeyCode = 0;
            string[] parts = value.Split('+');
            foreach (string rawPart in parts)
            {
                string part = rawPart.Trim();
                if (part.Length == 0)
                {
                    error = "暂停热键格式无效：" + value;
                    return false;
                }

                if (String.Equals(part, "Ctrl", StringComparison.OrdinalIgnoreCase)
                    || String.Equals(part, "Control", StringComparison.OrdinalIgnoreCase))
                {
                    if ((userModifiers & NativeMethods.MOD_CONTROL) != 0)
                    {
                        error = "暂停热键包含重复的 Ctrl 修饰键。";
                        return false;
                    }
                    modifiers |= NativeMethods.MOD_CONTROL;
                    userModifiers |= NativeMethods.MOD_CONTROL;
                    continue;
                }
                if (String.Equals(part, "Alt", StringComparison.OrdinalIgnoreCase))
                {
                    if ((userModifiers & NativeMethods.MOD_ALT) != 0)
                    {
                        error = "暂停热键包含重复的 Alt 修饰键。";
                        return false;
                    }
                    modifiers |= NativeMethods.MOD_ALT;
                    userModifiers |= NativeMethods.MOD_ALT;
                    continue;
                }
                if (String.Equals(part, "Shift", StringComparison.OrdinalIgnoreCase))
                {
                    if ((userModifiers & NativeMethods.MOD_SHIFT) != 0)
                    {
                        error = "暂停热键包含重复的 Shift 修饰键。";
                        return false;
                    }
                    modifiers |= NativeMethods.MOD_SHIFT;
                    userModifiers |= NativeMethods.MOD_SHIFT;
                    continue;
                }
                if (String.Equals(part, "Win", StringComparison.OrdinalIgnoreCase)
                    || String.Equals(part, "Windows", StringComparison.OrdinalIgnoreCase))
                {
                    if ((userModifiers & NativeMethods.MOD_WIN) != 0)
                    {
                        error = "暂停热键包含重复的 Win 修饰键。";
                        return false;
                    }
                    modifiers |= NativeMethods.MOD_WIN;
                    userModifiers |= NativeMethods.MOD_WIN;
                    continue;
                }

                Keys key;
                if (virtualKeyCode != 0
                    || !Enum.TryParse(part, true, out key)
                    || !Enum.IsDefined(typeof(Keys), key))
                {
                    error = "暂停热键包含无效按键：" + part;
                    return false;
                }

                virtualKeyCode = (int)(key & Keys.KeyCode);
                if (IsModifierKey((Keys)virtualKeyCode))
                {
                    error = "暂停热键必须包含一个非修饰键。";
                    return false;
                }
            }

            if (virtualKeyCode == 0)
            {
                error = "暂停热键必须包含一个非修饰键。";
                return false;
            }
            if (userModifiers == 0)
            {
                error = "暂停热键必须至少包含 Ctrl、Alt、Shift 或 Win 中的一个修饰键。";
                return false;
            }
            if (virtualKeyCode == NativeMethods.VK_F12)
            {
                error = "F12 由 Windows 调试器保留，不能注册为全局热键。";
                return false;
            }

            binding = new HotkeyBinding(modifiers, virtualKeyCode);
            return true;
        }

        private static bool IsModifierKey(Keys key)
        {
            return key == Keys.ControlKey
                || key == Keys.LControlKey
                || key == Keys.RControlKey
                || key == Keys.Menu
                || key == Keys.LMenu
                || key == Keys.RMenu
                || key == Keys.ShiftKey
                || key == Keys.LShiftKey
                || key == Keys.RShiftKey
                || key == Keys.LWin
                || key == Keys.RWin;
        }
    }
}
