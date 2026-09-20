using System;
using System.IO;
using mdaiAgent;
using Xunit;

namespace mdaiAgent.Tests
{
    public class ProjectTypeDetectorTests
    {
        [Fact]
        public void DetectProjectType_FlutterProject_ReturnsFlutter()
        {
            var temp = Path.Combine(Path.GetTempPath(), "mdai_flutter_test_" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(temp);
            File.WriteAllText(Path.Combine(temp, "pubspec.yaml"), "name: demo\n");
            try
            {
                var detected = ProjectTypeDetector.DetectProjectType(temp);
                Assert.Equal("Flutter", detected);
            }
            finally
            {
                try { Directory.Delete(temp, true); } catch { }
            }
        }

        [Fact]
        public void DetectProjectType_PythonProject_ReturnsPython()
        {
            var temp = Path.Combine(Path.GetTempPath(), "mdai_python_test_" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(temp);
            File.WriteAllText(Path.Combine(temp, "requirements.txt"), "requests\n");
            try
            {
                var detected = ProjectTypeDetector.DetectProjectType(temp);
                Assert.Equal("Python", detected);
            }
            finally
            {
                try { Directory.Delete(temp, true); } catch { }
            }
        }
    }
}
