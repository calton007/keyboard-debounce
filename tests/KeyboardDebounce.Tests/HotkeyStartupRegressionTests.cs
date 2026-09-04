using KeyboardDebounce;
using Xunit;

namespace KeyboardDebounce.Tests
{
    public sealed class HotkeyStartupRegressionTests
    {
        [Fact]
        public void HotkeyRequiresAtLeastOneModifier()
        {
            HotkeyBinding binding;
            string error;

            Assert.False(HotkeyBinding.TryParse("F11", out binding, out error));

            Assert.Null(binding);
            Assert.Contains("修饰键", error);
        }

        [Fact]
        public void HotkeyRejectsModifierAsTheMainKey()
        {
            HotkeyBinding binding;
            string error;

            Assert.False(HotkeyBinding.TryParse(
                "Ctrl+ControlKey",
                out binding,
                out error));

            Assert.Null(binding);
            Assert.Contains("非修饰键", error);
        }

        [Fact]
        public void HotkeyRejectsDuplicateModifiers()
        {
            HotkeyBinding binding;
            string error;

            Assert.False(HotkeyBinding.TryParse(
                "Ctrl+Control+F11",
                out binding,
                out error));

            Assert.Null(binding);
            Assert.Contains("重复", error);
        }

        [Fact]
        public void HotkeyStillRejectsReservedF12WhenModified()
        {
            HotkeyBinding binding;
            string error;

            Assert.False(HotkeyBinding.TryParse(
                "Ctrl+Shift+F12",
                out binding,
                out error));

            Assert.Null(binding);
            Assert.Contains("F12", error);
        }

        [Theory]
        [InlineData("\"C:\\Program Files\\KeyboardDebounce\\KeyboardDebounce.exe\"")]
        [InlineData("  \"C:\\Program Files\\KeyboardDebounce\\KeyboardDebounce.exe\"  ")]
        [InlineData(@"C:\Program Files\KeyboardDebounce\KeyboardDebounce.exe")]
        [InlineData(@"c:\program files\keyboarddebounce\keyboarddebounce.exe")]
        public void StartupCommandAcceptsOnlyTheCurrentExecutablePath(string command)
        {
            const string currentPath =
                @"C:\Program Files\KeyboardDebounce\KeyboardDebounce.exe";

            Assert.True(StartupManager.IsStartupCommandForExecutable(
                command,
                currentPath));
        }

        [Theory]
        [InlineData("\"C:\\Old\\KeyboardDebounce.exe\"")]
        [InlineData("\"C:\\Program Files\\KeyboardDebounce\\KeyboardDebounce.exe\" --minimized")]
        [InlineData(@"C:\Program Files\KeyboardDebounce\KeyboardDebounce.exe --minimized")]
        [InlineData("\"C:\\Program Files\\KeyboardDebounce\\KeyboardDebounce.exe\" & calc.exe")]
        [InlineData("cmd.exe /c \"C:\\Program Files\\KeyboardDebounce\\KeyboardDebounce.exe\"")]
        [InlineData(@".\KeyboardDebounce.exe")]
        [InlineData("\"C:\\Program Files\\KeyboardDebounce\\KeyboardDebounce.exe")]
        public void StartupCommandRejectsOldPathsArgumentsAndWrappers(string command)
        {
            const string currentPath =
                @"C:\Program Files\KeyboardDebounce\KeyboardDebounce.exe";

            Assert.False(StartupManager.IsStartupCommandForExecutable(
                command,
                currentPath));
        }

        [Theory]
        [InlineData(null, @"C:\KeyboardDebounce.exe")]
        [InlineData("", @"C:\KeyboardDebounce.exe")]
        [InlineData(@"C:\KeyboardDebounce.exe", null)]
        [InlineData(@"C:\KeyboardDebounce.exe", "")]
        public void StartupCommandRejectsMissingValues(
            string command,
            string executablePath)
        {
            Assert.False(StartupManager.IsStartupCommandForExecutable(
                command,
                executablePath));
        }
    }
}
