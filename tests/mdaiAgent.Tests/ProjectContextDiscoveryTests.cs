using System;
using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;
using Xunit;

namespace mdaiAgent.Tests;

public class ProjectContextDiscoveryServiceTests
{
    [Fact]
    public async Task DiscoverContext_ExtractsKeywordsFromMessage()
    {
        var tempDir = Path.Combine(Path.GetTempPath(), "mdai_discovery_test");
        if (Directory.Exists(tempDir)) Directory.Delete(tempDir, true);
        Directory.CreateDirectory(tempDir);

        var service = new ProjectContextDiscoveryService(tempDir, s => { });
        var result = await service.DiscoverContextFromMessageAsync("ToolExecutor method bug fixed");

        Assert.NotEmpty(result.ExtractedKeywords);
        Assert.Contains("ToolExecutor", result.ExtractedKeywords);

        Directory.Delete(tempDir, true);
    }

    [Fact]
    public async Task DiscoverContext_FindsRelevantFiles()
    {
        var tempDir = Path.Combine(Path.GetTempPath(), "mdai_discovery_test");
        if (Directory.Exists(tempDir)) Directory.Delete(tempDir, true);
        Directory.CreateDirectory(Path.Combine(tempDir, "BasucuIDE"));

        // Create mock files
        var toolExecutorFile = Path.Combine(tempDir, "BasucuIDE", "ToolExecutor.cs");
        File.WriteAllText(toolExecutorFile, "public class ToolExecutor { public async Task Execute() { } }");

        var chatFlowFile = Path.Combine(tempDir, "BasucuIDE", "ChatFlowService.cs");
        File.WriteAllText(chatFlowFile, "public class ChatFlowService { }");

        var service = new ProjectContextDiscoveryService(tempDir, s => { });
        var result = await service.DiscoverContextFromMessageAsync("ToolExecutor execute method");

        Assert.NotEmpty(result.RelevantFiles);
        Assert.Contains(toolExecutorFile, result.RelevantFiles);

        Directory.Delete(tempDir, true);
    }

    [Fact]
    public async Task DiscoverContext_ExtractsCodeSnippets()
    {
        var tempDir = Path.Combine(Path.GetTempPath(), "mdai_discovery_test");
        if (Directory.Exists(tempDir)) Directory.Delete(tempDir, true);
        Directory.CreateDirectory(Path.Combine(tempDir, "BasucuIDE"));

        var testFile = Path.Combine(tempDir, "BasucuIDE", "TestClass.cs");
        var code = @"public class TestClass
{
    public async Task Execute()
    {
        await Task.Delay(100);
    }
}";
        File.WriteAllText(testFile, code);

        var service = new ProjectContextDiscoveryService(tempDir, s => { });
        var result = await service.DiscoverContextFromMessageAsync("Execute method implementation");

        Assert.NotEmpty(result.CodeSnippets);
        Assert.NotEmpty(result.ContextMarkdown);
        Assert.Contains("Execute", result.ContextMarkdown);

        Directory.Delete(tempDir, true);
    }

    [Fact]
    public async Task DiscoverContext_GeneratesMarkdown()
    {
        var tempDir = Path.Combine(Path.GetTempPath(), "mdai_discovery_test");
        if (Directory.Exists(tempDir)) Directory.Delete(tempDir, true);
        Directory.CreateDirectory(Path.Combine(tempDir, "BasucuIDE"));

        File.WriteAllText(Path.Combine(tempDir, "BasucuIDE", "Test.cs"), "public class Test { }");

        var service = new ProjectContextDiscoveryService(tempDir, s => { });
        var result = await service.DiscoverContextFromMessageAsync("Test class");

        Assert.NotNull(result.ContextMarkdown);
        Assert.Contains("Otomatik Keşfedilen Context", result.ContextMarkdown);
        Assert.Contains("Keywords", result.ContextMarkdown);

        Directory.Delete(tempDir, true);
    }

    [Fact]
    public async Task DiscoverContext_PrioritizesTestsAndIncludesSnippets()
    {
        var tempDir = Path.Combine(Path.GetTempPath(), "mdai_discovery_test");
        if (Directory.Exists(tempDir)) Directory.Delete(tempDir, true);

        Directory.CreateDirectory(Path.Combine(tempDir, "BasucuIDE"));
        Directory.CreateDirectory(Path.Combine(tempDir, "tests", "mdaiAgent.Tests"));

        var sourceFile = Path.Combine(tempDir, "BasucuIDE", "BuildService.cs");
        File.WriteAllText(sourceFile, @"
public class BuildService
{
    public bool RunBuild() {
        return true;
    }
}");

        var testFile = Path.Combine(tempDir, "tests", "mdaiAgent.Tests", "BuildServiceTests.cs");
        File.WriteAllText(testFile, @"
public class BuildServiceTests
{
    [Fact]
    public void RunBuild_ReturnsTrue()
    {
        var service = new BuildService();
        Assert.True(service.RunBuild());
    }
}");

        var service = new ProjectContextDiscoveryService(tempDir, s => { });
        var result = await service.DiscoverContextFromMessageAsync("BuildService timeout bug fix verification");

        Assert.Contains(sourceFile, result.RelevantFiles);
        Assert.Contains(testFile, result.RelevantFiles);
        Assert.Contains("RunBuild", result.ContextMarkdown);
        Assert.Contains("BuildServiceTests", result.ContextMarkdown);

        Directory.Delete(tempDir, true);
    }

    [Fact]
    public async Task DiscoverContext_PrioritizesFileMentionedByPath()
    {
        var tempDir = Path.Combine(Path.GetTempPath(), "mdai_discovery_file_hint_test");
        if (Directory.Exists(tempDir)) Directory.Delete(tempDir, true);

        Directory.CreateDirectory(Path.Combine(tempDir, "src"));
        var sourceFile = Path.Combine(tempDir, "src", "OrderService.cs");
        File.WriteAllText(sourceFile, "public class OrderService { }");
        File.WriteAllText(Path.Combine(tempDir, "src", "OtherService.cs"), "public class OtherService { }");

        var service = new ProjectContextDiscoveryService(tempDir, _ => { });
        var result = await service.DiscoverContextFromMessageAsync("Review src/OrderService.cs and its tests");

        Assert.Contains(Path.GetFullPath(sourceFile), result.PrioritizedFiles);
        Assert.Contains(Path.GetFullPath(sourceFile), result.RelevantFiles);
        Assert.Contains("Öncelikli Dosyalar", result.ContextMarkdown);

        Directory.Delete(tempDir, true);
    }

    [Fact]
    public async Task DiscoverContext_IncludesConventionalTestPairForFileHint()
    {
        var tempDir = Path.Combine(Path.GetTempPath(), "mdai_discovery_pair_test");
        if (Directory.Exists(tempDir)) Directory.Delete(tempDir, true);

        Directory.CreateDirectory(Path.Combine(tempDir, "src"));
        var sourceFile = Path.Combine(tempDir, "src", "OrderService.cs");
        var testFile = Path.Combine(tempDir, "src", "OrderServiceTests.cs");
        File.WriteAllText(sourceFile, "public class OrderService { }");
        File.WriteAllText(testFile, "public class UnrelatedTestFixture { }");

        var service = new ProjectContextDiscoveryService(tempDir, _ => { });
        var result = await service.DiscoverContextFromMessageAsync("Review src/OrderService.cs");

        Assert.Contains(Path.GetFullPath(sourceFile), result.RelevantFiles);
        Assert.Contains(Path.GetFullPath(testFile), result.RelevantFiles);

        Directory.Delete(tempDir, true);
    }

    [Fact]
    public async Task DiscoverContext_IncludesTaskTextFileMentionedInMessage()
    {
        var tempDir = Path.Combine(Path.GetTempPath(), "mdai_discovery_task_text_test");
        if (Directory.Exists(tempDir)) Directory.Delete(tempDir, true);
        Directory.CreateDirectory(tempDir);

        var taskFile = Path.Combine(tempDir, "gorev1.txt");
        File.WriteAllText(taskFile, "Create a .NET 8 Minimal API named TaskBoard.");

        var service = new ProjectContextDiscoveryService(tempDir, _ => { });
        var result = await service.DiscoverContextFromMessageAsync("Önce gorev1.txt dosyasını oku ve uygula.");

        Assert.Contains(Path.GetFullPath(taskFile), result.PrioritizedFiles);
        Assert.Contains(Path.GetFullPath(taskFile), result.RelevantFiles);
        Assert.Contains("TaskBoard", result.ContextMarkdown);

        Directory.Delete(tempDir, true);
    }
}
