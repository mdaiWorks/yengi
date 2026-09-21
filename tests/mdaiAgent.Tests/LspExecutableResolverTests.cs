using System;
using System.IO;
using Xunit;

namespace mdaiAgent.Tests;

public class LspExecutableResolverTests
{
    public LspExecutableResolverTests()
    {
        LocalizationManager.Instance.SetLanguage("tr");
    }

    [Fact]
    public void Resolve_PrefersProjectNodeModulesCommandShim()
    {
        var root = CreateTemporaryDirectory();
        try
        {
            var projectDirectory = Path.Combine(root, "project", "src");
            var binDirectory = Path.Combine(root, "project", "node_modules", ".bin");
            Directory.CreateDirectory(projectDirectory);
            Directory.CreateDirectory(binDirectory);

            var commandPath = Path.Combine(binDirectory, "html-languageserver.cmd");
            File.WriteAllText(commandPath, "@echo off");

            var result = LspExecutableResolver.Resolve(projectDirectory, "vscode-html-language-server", "html-languageserver");

            Assert.NotNull(result);
            Assert.Equal(commandPath, result!.ExecutablePath);
            Assert.Equal("Proje node_modules/.bin", result.Source);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public void Resolve_DoesNotTreatPowerShellOnlyShimAsLaunchable()
    {
        var root = CreateTemporaryDirectory();
        try
        {
            var projectDirectory = Path.Combine(root, "project");
            var binDirectory = Path.Combine(projectDirectory, "node_modules", ".bin");
            Directory.CreateDirectory(binDirectory);
            File.WriteAllText(Path.Combine(binDirectory, "mdai-test-html-language-server.ps1"), "Write-Output test");

            var result = LspExecutableResolver.Resolve(projectDirectory, "mdai-test-html-language-server");

            Assert.Null(result);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    private static string CreateTemporaryDirectory()
    {
        var path = Path.Combine(Path.GetTempPath(), "mdaiAgent-lsp-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(path);
        return path;
    }
}
