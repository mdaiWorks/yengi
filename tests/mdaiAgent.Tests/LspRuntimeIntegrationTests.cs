using System;
using System.IO;
using System.Threading.Tasks;
using mdaiAgent;
using Xunit;

namespace mdaiAgent.Tests;

public class LspRuntimeIntegrationTests
{
    [Fact]
    public async Task HtmlServer_InitializesWhenAvailable()
    {
        var projectDirectory = CreateTemporaryDirectory();
        try
        {
            var resolution = LspExecutableResolver.Resolve(
                projectDirectory,
                "vscode-html-language-server");
            if (resolution == null)
            {
                Console.WriteLine("Modern vscode-html-language-server is not installed; integration test skipped.");
                return;
            }

            using var service = new LanguageServerService();
            var logs = new System.Collections.Generic.List<string>();
            service.LogReceived += (_, log) => logs.Add(log);
            var started = await service.StartServerAsync("html", projectDirectory);

            Assert.True(started, $"HTML LSP bulundu ancak initialize edilemedi. {string.Join(" | ", logs)}");
            Assert.True(service.IsServingLanguage("html"));

            var filePath = Path.Combine(projectDirectory, "index.html");
            await service.OpenDocumentAsync(filePath, "<!doctype html><html lang=\"tr\"><body>Merhaba</body></html>", "html");
        }
        finally
        {
            Directory.Delete(projectDirectory, recursive: true);
        }
    }

    [Fact]
    public async Task TypeScriptServer_InitializesWhenAvailable()
    {
        var projectDirectory = CreateTemporaryDirectory();
        try
        {
            var resolution = LspExecutableResolver.Resolve(projectDirectory, "typescript-language-server");
            if (resolution == null)
            {
                Console.WriteLine("typescript-language-server is not installed; integration test skipped.");
                return;
            }

            var globalTsserver = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                "npm",
                "node_modules",
                "typescript",
                "lib",
                "tsserver.js");
            if (!File.Exists(globalTsserver))
            {
                Console.WriteLine("A compatible tsserver.js is not installed; integration test skipped.");
                return;
            }

            using var service = new LanguageServerService();
            var logs = new System.Collections.Generic.List<string>();
            service.LogReceived += (_, log) => logs.Add(log);
            var started = await service.StartServerAsync("ts", projectDirectory);

            Assert.True(started, $"TypeScript LSP bulundu ancak initialize edilemedi. {string.Join(" | ", logs)}");
            Assert.True(service.IsServingLanguage("ts"));

            var filePath = Path.Combine(projectDirectory, "app.ts");
            await service.OpenDocumentAsync(filePath, "const message: string = 'Merhaba';", "typescript");
        }
        finally
        {
            Directory.Delete(projectDirectory, recursive: true);
        }
    }

    private static string CreateTemporaryDirectory()
    {
        var path = Path.Combine(Path.GetTempPath(), "mdaiAgent-lsp-runtime-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(path);
        return path;
    }
}
