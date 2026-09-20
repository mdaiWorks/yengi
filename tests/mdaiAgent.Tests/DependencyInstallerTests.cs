using System;
using System.IO;
using System.Threading.Tasks;
using mdaiAgent;
using Xunit;

namespace mdaiAgent.Tests
{
    public class DependencyInstallerTests
    {
        [Fact]
        public async Task InstallForLanguageAsync_InvalidPath_ReturnsError()
        {
            var res = await DependencyInstaller.InstallForLanguageAsync("C:\\path\\that\\doesntexist", "typescript", null);
            Assert.False(string.IsNullOrEmpty(res));
            Assert.Contains("Geçersiz proje klasörü", res);
        }

        [Fact]
        public async Task InstallForLanguageAsync_UnsupportedLanguage_ReturnsGuideMessage()
        {
            var temp = Path.Combine(Path.GetTempPath(), "mdai_test_project_" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(temp);
            try
            {
                var res = await DependencyInstaller.InstallForLanguageAsync(temp, "ruby", null);
                Assert.False(string.IsNullOrEmpty(res));
                Assert.Contains("Otomatik yükleme desteklenmiyor", res);
            }
            finally
            {
                try { Directory.Delete(temp, true); } catch { }
            }
        }
    }
}
