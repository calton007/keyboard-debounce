using KeyboardDebounce;
using Xunit;

namespace KeyboardDebounce.Tests
{
    public sealed class SaveErrorStatusTests
    {
        [Fact]
        public void OrdinaryEventCannotHideSaveErrorAndSuccessClearsOnlyItsStream()
        {
            var status = new SaveErrorStatus();
            Assert.True(status.SetError(false, "学习状态保存失败：disk"));
            Assert.False(status.SetError(false, "学习状态保存失败：disk"));

            string afterOrdinaryEvent = status.GetVisibleStatus("ordinary key event");

            Assert.Contains("学习状态保存失败：disk", afterOrdinaryEvent);
            Assert.DoesNotContain("ordinary key event", afterOrdinaryEvent);

            Assert.True(status.SetError(true, "设置保存失败：registry"));
            Assert.True(status.ClearError(false));
            Assert.False(status.ClearError(false));
            string settingsStillFailed = status.GetVisibleStatus("newer key event");
            Assert.Contains("设置保存失败：registry", settingsStillFailed);
            Assert.DoesNotContain("学习状态保存失败", settingsStillFailed);

            Assert.True(status.ClearError(true));
            Assert.False(status.ClearError(true));
            Assert.Equal("newer key event", status.GetVisibleStatus("newer key event"));
        }
    }
}
