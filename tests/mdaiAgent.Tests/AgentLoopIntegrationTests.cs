using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using System.Threading.Tasks;
using Xunit;

namespace mdaiAgent.Tests;

public class AgentLoopIntegrationTests : IDisposable
{
    private readonly string _tempDir;
    private readonly string _settingsDir;

    public AgentLoopIntegrationTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), $"agent_test_{Guid.NewGuid()}");
        _settingsDir = Path.Combine(_tempDir, ".mdai");
        Directory.CreateDirectory(_tempDir);
        Directory.CreateDirectory(_settingsDir);
    }

    public void Dispose()
    {
        try
        {
            if (Directory.Exists(_tempDir))
                Directory.Delete(_tempDir, true);
        }
        catch { }
    }

    private ToolExecutor CreateExecutor()
    {
        return new ToolExecutor(
            projectFolder: _tempDir,
            terminalLog: _ => { },
            confirmCommand: _ => Task.FromResult(true),
            confirmFileChange: (_, _, _) => Task.FromResult(true),
            safeAutomationEnabled: false
        );
    }

    [Fact]
    public async Task ToolExecution_CreatesAndReadsFile()
    {
        // Test: Create a file, then read it
        var executor = CreateExecutor();

        // Step 1: Create file
        var createArgs = JsonSerializer.Serialize(new
        {
            filePath = Path.Combine(_tempDir, "test.txt"),
            content = "Hello World"
        });
        var createResult = await executor.ExecuteAsync("CreateOrUpdateFile", createArgs);
        Assert.True(createResult.Success);

        // Step 2: Read file
        var readArgs = JsonSerializer.Serialize(new
        {
            filePath = Path.Combine(_tempDir, "test.txt")
        });
        var readResult = await executor.ExecuteAsync("ReadFile", readArgs);
        Assert.True(readResult.Success);
        Assert.Contains("Hello World", readResult.Output);
    }

    [Fact]
    public async Task CheckpointFlow_SaveAndRestore()
    {
        // Test: Full checkpoint create -> modify -> restore flow
        var executor = CreateExecutor();

        // Create initial file
        var initialFile = Path.Combine(_tempDir, "state.txt");
        File.WriteAllText(initialFile, "v1");

        // Create checkpoint
        var checkpointArgs = JsonSerializer.Serialize(new
        {
            projectPath = _tempDir,
            label = "version1",
            description = "Initial state"
        });
        var checkpointResult = await executor.ExecuteAsync("CreateCheckpoint", checkpointArgs);
        Assert.True(checkpointResult.Success);

        // Modify file
        File.WriteAllText(initialFile, "v2_modified");
        Assert.Equal("v2_modified", File.ReadAllText(initialFile));

        // Rollback
        var rollbackArgs = JsonSerializer.Serialize(new
        {
            projectPath = _tempDir,
            label = "version1"
        });
        var rollbackResult = await executor.ExecuteAsync("RollbackToCheckpoint", rollbackArgs);
        Assert.True(rollbackResult.Success);

        // Verify restoration
        Assert.Equal("v1", File.ReadAllText(initialFile));
    }

    [Fact]
    public async Task SubAgentTaskFlow_DelegateAndTrack()
    {
        // Test: Delegate task and verify structured result
        var executor = CreateExecutor();

        var taskArgs = JsonSerializer.Serialize(new
        {
            role = "CodeReviewer",
            prompt = "Review the following code",
            context = "function add(a, b) { return a + b; }",
            waitForCompletion = false
        });

        var result = await executor.ExecuteAsync("DelegateTask", taskArgs);

        Assert.True(result.Success);
        
        // Parse JSON response
        var json = JsonDocument.Parse(result.Output).RootElement;
        Assert.True(json.TryGetProperty("taskId", out var taskIdProp));
        Assert.True(json.TryGetProperty("status", out var statusProp));
        
        var taskId = taskIdProp.GetString();
        var status = statusProp.GetString();

        Assert.NotEmpty(taskId);
        Assert.Equal("Started", status);  // Status is "Started" when task initiated

        // Verify coordinator has task
        var coordinator = executor.SubAgentCoordinator;
        var task = coordinator.GetTask(taskId);
        Assert.NotNull(task);
        Assert.Equal("CodeReviewer", task.Role);
    }

    [Fact]
    public async Task ContextDiscovery_FindsRelevantFiles()
    {
        // Test: Project context discovery
        var executor = CreateExecutor();

        // Create test files with keywords
        File.WriteAllText(
            Path.Combine(_tempDir, "UserService.cs"),
            "public class UserService { public async Task ExecuteAsync() { } }"
        );

        File.WriteAllText(
            Path.Combine(_tempDir, "OrderProcessor.cs"),
            "public class OrderProcessor { }"
        );

        var args = JsonSerializer.Serialize(new
        {
            message = "I need to review the ExecuteAsync method in services"
        });

        var result = await executor.ExecuteAsync("DiscoverProjectContext", args);

        Assert.True(result.Success);
        // Should find UserService since it has ExecuteAsync
        Assert.Contains("UserService", result.Output, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task FileOperations_PreserveData()
    {
        // Test: File create, read, modify, read flow
        var executor = CreateExecutor();
        var filePath = Path.Combine(_tempDir, "data.json");

        var json = @"{ ""name"": ""test"", ""value"": 42 }";

        // Create
        var createArgs = JsonSerializer.Serialize(new { filePath, content = json });
        var createResult = await executor.ExecuteAsync("CreateOrUpdateFile", createArgs);
        Assert.True(createResult.Success);

        // Read 1
        var readArgs1 = JsonSerializer.Serialize(new { filePath });
        var readResult1 = await executor.ExecuteAsync("ReadFile", readArgs1);
        Assert.True(readResult1.Success);
        Assert.Contains("test", readResult1.Output);

        // Modify
        var modified = @"{ ""name"": ""modified"", ""value"": 99 }";
        var updateArgs = JsonSerializer.Serialize(new { filePath, content = modified });
        var updateResult = await executor.ExecuteAsync("CreateOrUpdateFile", updateArgs);
        Assert.True(updateResult.Success);

        // Read 2
        var readArgs2 = JsonSerializer.Serialize(new { filePath });
        var readResult2 = await executor.ExecuteAsync("ReadFile", readArgs2);
        Assert.True(readResult2.Success);
        Assert.Contains("99", readResult2.Output);
        Assert.DoesNotContain("42", readResult2.Output);
    }

    [Fact]
    public async Task ErrorRecovery_SmartRecoveryAnalyzes()
    {
        // Test: Smart recovery can analyze compilation errors
        var executor = CreateExecutor();

        var errorOutput = "error CS0103: The name 'undefined_var' does not exist in the current context. Did you forget a using directive or an assembly reference?";

        var args = JsonSerializer.Serialize(new { lastError = errorOutput });
        var result = await executor.ExecuteAsync("SmartRecovery", args);

        Assert.True(result.Success);
        Assert.NotEmpty(result.Output);
        // Should provide recovery suggestions
        Assert.True(result.Output.Length > 20);
    }

    [Fact]
    public async Task QuickCommandFlow_CreateAndExecute()
    {
        // Test: Create quick command, then execute it
        var executor = CreateExecutor();

        // Create
        var createArgs = JsonSerializer.Serialize(new
        {
            name = "echo_test",
            command = "echo Test",
            description = "Test echo command"
        });
        var createResult = await executor.ExecuteAsync("CreateQuickCommand", createArgs);
        Assert.True(createResult.Success);

        // Execute
        var execArgs = JsonSerializer.Serialize(new { name = "echo_test" });
        var execResult = await executor.ExecuteAsync("ExecuteQuickCommand", execArgs);
        Assert.True(execResult.Success);
    }

    [Fact]
    public async Task DirectoryListing_ShowsStructure()
    {
        // Test: List directory structure
        var executor = CreateExecutor();

        // Create structure
        File.WriteAllText(Path.Combine(_tempDir, "file1.txt"), "");
        File.WriteAllText(Path.Combine(_tempDir, "file2.cs"), "");
        Directory.CreateDirectory(Path.Combine(_tempDir, "subfolder"));

        var args = JsonSerializer.Serialize(new { path = _tempDir });
        var result = await executor.ExecuteAsync("ListDirectory", args);

        Assert.True(result.Success);
        Assert.Contains("file1.txt", result.Output);
        Assert.Contains("file2.cs", result.Output);
        Assert.Contains("subfolder", result.Output);
    }

    [Fact]
    public async Task CodeSearch_FindsPatterns()
    {
        // Test: Search code for patterns
        var executor = CreateExecutor();

        var csFile = Path.Combine(_tempDir, "Logic.cs");
        File.WriteAllText(csFile, @"
public class Calculator 
{ 
    public int Add(int a, int b) { return a + b; }
    public int Multiply(int a, int b) { return a * b; }
}");

        var args = JsonSerializer.Serialize(new
        {
            rootPath = _tempDir,
            query = "public int",
            includePattern = "*.cs"
        });
        var result = await executor.ExecuteAsync("SearchCode", args);

        Assert.True(result.Success);
        // Result should contain at least some match information
        Assert.NotEmpty(result.Output);
    }

    [Fact]
    public async Task FileSearch_FindsByPattern()
    {
        // Test: Find files matching pattern
        var executor = CreateExecutor();

        File.WriteAllText(Path.Combine(_tempDir, "service1.cs"), "");
        File.WriteAllText(Path.Combine(_tempDir, "service2.cs"), "");
        File.WriteAllText(Path.Combine(_tempDir, "config.json"), "");

        var args = JsonSerializer.Serialize(new
        {
            rootPath = _tempDir,
            pattern = "*.cs"
        });
        var result = await executor.ExecuteAsync("FindFiles", args);

        Assert.True(result.Success);
        Assert.Contains("service1.cs", result.Output);
        Assert.Contains("service2.cs", result.Output);
        Assert.DoesNotContain("config.json", result.Output);
    }

    [Fact]
    public async Task PathBoundary_PreventsEscape()
    {
        // Test: Security - path boundary enforcement
        var executor = CreateExecutor();

        var dangerous = Path.Combine(_tempDir, "..", "..", "system.txt");
        var args = JsonSerializer.Serialize(new { filePath = dangerous });

        var result = await executor.ExecuteAsync("ReadFile", args);

        // Should fail due to boundary check
        Assert.False(result.Success);
        Assert.NotNull(result.Error);
    }

    [Fact]
    public async Task MultipleCheckpoints_Tracked()
    {
        // Test: Can create multiple checkpoints
        var executor = CreateExecutor();

        File.WriteAllText(Path.Combine(_tempDir, "data.txt"), "v1");

        // Create checkpoint 1
        var cp1Args = JsonSerializer.Serialize(new
        {
            projectPath = _tempDir,
            label = "checkpoint_1",
            description = "First"
        });
        var cp1Result = await executor.ExecuteAsync("CreateCheckpoint", cp1Args);
        Assert.True(cp1Result.Success);

        // Modify and create checkpoint 2
        File.WriteAllText(Path.Combine(_tempDir, "data.txt"), "v2");
        var cp2Args = JsonSerializer.Serialize(new
        {
            projectPath = _tempDir,
            label = "checkpoint_2",
            description = "Second"
        });
        var cp2Result = await executor.ExecuteAsync("CreateCheckpoint", cp2Args);
        Assert.True(cp2Result.Success);

        // Verify both exist
        var checkpointBase = Path.Combine(_tempDir, ".mdai", "checkpoints");
        Assert.True(Directory.Exists(Path.Combine(checkpointBase, "checkpoint_1")));
        Assert.True(Directory.Exists(Path.Combine(checkpointBase, "checkpoint_2")));
    }
}
