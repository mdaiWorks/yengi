using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using System.Threading.Tasks;
using Xunit;

namespace mdaiAgent.Tests;

public class AgentVerificationLoopServiceTests
{
    [Fact]
    public async Task VerifyBuild_SucceedsWhenBuildPasses()
    {
        var tempDir = Path.Combine(Path.GetTempPath(), "mdai_verify_test");
        if (Directory.Exists(tempDir)) Directory.Delete(tempDir, true);
        Directory.CreateDirectory(tempDir);

        var toolExecutor = new ToolExecutor(tempDir, s => { }, (Func<string, Task<bool>>)(async cmd => true), 
            async (f, o, n) => true);
        var chatSessionService = new ChatSessionService(() => tempDir);
        var chatFlowService = new ChatFlowService(
            chatSessionService,
            s => { },
            (s, sev) => { },
            s => { }
        );

        var service = new AgentVerificationLoopService(toolExecutor, chatFlowService, s => { });
        var result = await service.VerifyChangesAsync(
            new List<string> { "test.cs" },
            "System prompt",
            "Test message"
        );

        Assert.NotNull(result);
        Assert.NotEmpty(result.BuildTestResults);

        Directory.Delete(tempDir, true);
    }

    [Fact]
    public void VerificationResult_TracksChangedFiles()
    {
        var result = new VerificationResult
        {
            ChangedFiles = new() { "file1.cs", "file2.cs" }
        };

        Assert.Equal(2, result.ChangedFiles.Count);
        Assert.Contains("file1.cs", result.ChangedFiles);
    }

    [Fact]
    public void VerificationResult_SummaryIncludesChecksAndFiles()
    {
        var result = new VerificationResult
        {
            Status = "All Checks Passed",
            ChangedFiles = new() { "file1.cs", "file1.cs", "file2.cs" },
            BuildTestResults = new()
            {
                ("Build", true, ""),
                ("Tests", true, "")
            }
        };

        Assert.Contains("Değişen dosya: 2", result.Summary);
        Assert.Contains("Kontroller: 2 başarılı, 0 başarısız", result.Summary);
    }

    [Fact]
    public void VerificationResult_SummarySeparatesWarningsFromFailures()
    {
        var result = new VerificationResult
        {
            Status = "All Checks Passed",
            ChangedFiles = new() { "file1.cs" },
            BuildTestResults = new()
            {
                ("Build", true, ""),
                ("Static Analysis", true, "CS0168: Uyarı: değişken tanımlı ancak kullanılmadı."),
                ("Tests", true, "")
            }
        };

        Assert.Contains("0 başarısız", result.Summary);
        Assert.Contains("1 uyarı", result.Summary);
    }

    [Fact]
    public async Task VerifyChanges_AllowsMissingChangedFileForValidDeletion()
    {
        var tempDir = Path.Combine(Path.GetTempPath(), "mdai_verify_deleted_file");
        if (Directory.Exists(tempDir)) Directory.Delete(tempDir, true);
        Directory.CreateDirectory(tempDir);

        var executor = new ToolExecutor(tempDir, _ => { }, (Func<string, Task<bool>>)(_ => Task.FromResult(true)), async (_, _, _) => true);
        var chatService = new ChatFlowService(new ChatSessionService(() => tempDir), _ => { }, (_, _) => { }, _ => { });
        var service = new AgentVerificationLoopService(executor, chatService, _ => { });

        var result = await service.VerifyChangesAsync(new List<string> { Path.Combine(tempDir, "deleted.cs") }, "", "");

        Assert.True(result.Success);
        Assert.True(result.ChangedFileReview.Success);
        Assert.Equal(1, result.ChangedFileReview.MissingFileCount);

        Directory.Delete(tempDir, true);
    }

    [Fact]
    public async Task VerifyChanges_RejectsInvalidChangedFileEntry()
    {
        var tempDir = Path.Combine(Path.GetTempPath(), "mdai_verify_invalid_file");
        if (Directory.Exists(tempDir)) Directory.Delete(tempDir, true);
        Directory.CreateDirectory(tempDir);

        var executor = new ToolExecutor(tempDir, _ => { }, (Func<string, Task<bool>>)(_ => Task.FromResult(true)), async (_, _, _) => true);
        var chatService = new ChatFlowService(new ChatSessionService(() => tempDir), _ => { }, (_, _) => { }, _ => { });
        var service = new AgentVerificationLoopService(executor, chatService, _ => { });

        var result = await service.VerifyChangesAsync(new List<string> { "" }, "", "");

        Assert.False(result.Success);
        Assert.Equal("Changed Files Review Failed", result.Status);
        Assert.Equal(1, result.ChangedFileReview.InvalidEntryCount);

        Directory.Delete(tempDir, true);
    }

    [Fact]
    public void VerificationResult_InitializesStatus()
    {
        var result = new VerificationResult();

        Assert.Equal("Verifying", result.Status);
        Assert.False(result.Success);
    }

    [Fact]
    public void BuildTestResult_CanIndicateSuccess()
    {
        var result = new BuildTestResult { Success = true };

        Assert.True(result.Success);
        Assert.Null(result.Error);
    }

    [Fact]
    public void BuildTestResult_CanIndicateFailure()
    {
        var result = new BuildTestResult 
        { 
            Success = false, 
            Error = "Test failed: method not found" 
        };

        Assert.False(result.Success);
        Assert.Equal("Test failed: method not found", result.Error);
    }
}
