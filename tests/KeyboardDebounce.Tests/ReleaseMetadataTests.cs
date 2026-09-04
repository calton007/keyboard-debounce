using System;
using KeyboardDebounce;
using Xunit;

namespace KeyboardDebounce.Tests
{
    public sealed class ReleaseMetadataTests
    {
        [Fact]
        public void ApplicationAssemblyEmbedsLicenseNotice()
        {
            string[] resources = typeof(AppSettings).Assembly.GetManifestResourceNames();

            Assert.Contains("KeyboardDebounce.LICENSE", resources);
        }

        [Fact]
        public void SingleFileStartupConfiguresWindowsAppRuntimeBaseDirectory()
        {
            const string variableName = "MICROSOFT_WINDOWSAPPRUNTIME_BASE_DIRECTORY";
            string originalValue = Environment.GetEnvironmentVariable(variableName);

            try
            {
                Environment.SetEnvironmentVariable(variableName, "stale-value");

                Program.ConfigureWindowsAppRuntimeBaseDirectory();

                Assert.Equal(
                    AppContext.BaseDirectory,
                    Environment.GetEnvironmentVariable(variableName));
            }
            finally
            {
                Environment.SetEnvironmentVariable(variableName, originalValue);
            }
        }
    }
}
