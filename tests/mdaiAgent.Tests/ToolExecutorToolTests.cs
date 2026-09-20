using System;
using System.IO;
using System.Text.Json;
using System.Threading.Tasks;
using Xunit;

namespace mdaiAgent.Tests;

public class ToolExecutorToolTests : IDisposable
{
    private readonly string _tempDir;
    private readonly ToolExecutor _executor;

    public ToolExecutorToolTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), $"mdai_test_{Guid.NewGuid()}");
        Directory.CreateDirectory(_tempDir);

        _executor = new ToolExecutor(
            projectFolder: _tempDir,
            terminalLog: _ => { },
            confirmCommand: _ => Task.FromResult(true),
            confirmFileChange: (_, _, _) => Task.FromResult(true),
            safeAutomationEnabled: false
        );
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

    [Fact]
    public async Task ReadFile_Success()
    {
        // Arrange
        var testFile = Path.Combine(_tempDir, "test.txt");
        var content = "Line 1\nLine 2\nLine 3";
        File.WriteAllText(testFile, content);

        var args = JsonSerializer.Serialize(new { filePath = testFile });

        // Act
        var result = await _executor.ExecuteAsync("ReadFile", args);

        // Assert
        Assert.True(result.Success);
        Assert.Contains("Line 1", result.Output);
        Assert.Contains("Line 3", result.Output);
    }

    [Fact]
    public async Task ReadFile_WithLineRange()
    {
        // Arrange
        var testFile = Path.Combine(_tempDir, "test.txt");
        File.WriteAllText(testFile, "Line 1\nLine 2\nLine 3\nLine 4");

        var args = JsonSerializer.Serialize(new { filePath = testFile, startLine = 2, endLine = 3 });

        // Act
        var result = await _executor.ExecuteAsync("ReadFile", args);

        // Assert
        Assert.True(result.Success);
        Assert.Contains("Line 2", result.Output);
        Assert.Contains("Line 3", result.Output);
        Assert.DoesNotContain("Line 1", result.Output);
    }

    [Fact]
    public async Task ReadFile_NotFound()
    {
        // Arrange
        var args = JsonSerializer.Serialize(new { filePath = Path.Combine(_tempDir, "nonexistent.txt") });

        // Act
        var result = await _executor.ExecuteAsync("ReadFile", args);

        // Assert
        Assert.False(result.Success);
        Assert.NotNull(result.Error);
        Assert.Contains("bulunamadı", result.Error, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task CreateOrUpdateFile_NewFile()
    {
        // Arrange
        var testFile = Path.Combine(_tempDir, "new.txt");
        var newContent = "New file content";

        var args = JsonSerializer.Serialize(new
        {
            filePath = testFile,
            content = newContent
        });

        // Act
        var result = await _executor.ExecuteAsync("CreateOrUpdateFile", args);

        // Assert
        Assert.True(result.Success);
        Assert.True(File.Exists(testFile));
        Assert.Equal(newContent, File.ReadAllText(testFile));
    }

    [Fact]
    public async Task CreateOrUpdateFile_UpdateExisting()
    {
        // Arrange
        var testFile = Path.Combine(_tempDir, "existing.txt");
        var oldContent = "Old content";
        File.WriteAllText(testFile, oldContent);

        var newContent = "New content";
        var args = JsonSerializer.Serialize(new
        {
            filePath = testFile,
            content = newContent
        });

        // Act
        var result = await _executor.ExecuteAsync("CreateOrUpdateFile", args);

        // Assert
        Assert.True(result.Success);
        Assert.Equal(newContent, File.ReadAllText(testFile));
    }

    [Fact]
    public async Task CreateOrUpdateFile_CreatesBackup()
    {
        // Arrange
        var testFile = Path.Combine(_tempDir, "backup_test.txt");
        File.WriteAllText(testFile, "Original");

        var backupDir = Path.Combine(_tempDir, ".mdai", "backup");
        Directory.CreateDirectory(backupDir);

        var args = JsonSerializer.Serialize(new
        {
            filePath = testFile,
            content = "Updated"
        });

        // Act
        var result = await _executor.ExecuteAsync("CreateOrUpdateFile", args);

        // Assert
        Assert.True(result.Success);
        // Backup should exist
        var backupFiles = Directory.GetFiles(backupDir);
        Assert.NotEmpty(backupFiles);
    }

    [Fact]
    public async Task ReplaceFileContent_Success()
    {
        // Arrange
        var testFile = Path.Combine(_tempDir, "replace.txt");
        File.WriteAllText(testFile, "Original content");

        var args = JsonSerializer.Serialize(new
        {
            filePath = testFile,
            targetContent = "Original",
            replacementContent = "Updated"
        });

        // Act
        var result = await _executor.ExecuteAsync("ReplaceFileContent", args);

        // Assert
        Assert.True(result.Success);
        Assert.Contains("Updated content", File.ReadAllText(testFile));
    }

    [Fact]
    public async Task ReplaceFileContent_EmptyTargetRejected()
    {
        // Arrange
        var testFile = Path.Combine(_tempDir, "replace-empty.txt");
        File.WriteAllText(testFile, "Original content");

        var args = JsonSerializer.Serialize(new
        {
            filePath = testFile,
            targetContent = "",
            replacementContent = "Updated"
        });

        // Act
        var result = await _executor.ExecuteAsync("ReplaceFileContent", args);

        // Assert
        Assert.False(result.Success);
        Assert.Equal("Original content", File.ReadAllText(testFile));
    }

    [Fact]
    public async Task ReplaceFileContent_WhitespaceTargetRejected()
    {
        // Arrange
        var testFile = Path.Combine(_tempDir, "replace-whitespace.txt");
        File.WriteAllText(testFile, "Original content");

        var args = JsonSerializer.Serialize(new
        {
            filePath = testFile,
            targetContent = "   \n  ",
            replacementContent = "Updated"
        });

        // Act
        var result = await _executor.ExecuteAsync("ReplaceFileContent", args);

        // Assert
        Assert.False(result.Success);
        Assert.Equal("Original content", File.ReadAllText(testFile));
    }

    [Fact]
    public async Task ReplaceFileContent_WhitespaceOldContentRejectedWithClearError()
    {
        // Arrange
        var testFile = Path.Combine(_tempDir, "replace-old-whitespace.txt");
        File.WriteAllText(testFile, "Original content");

        var args = JsonSerializer.Serialize(new
        {
            filePath = testFile,
            targetContent = "   \n  ",
            replacementContent = "Updated"
        });

        // Act
        var result = await _executor.ExecuteAsync("ReplaceFileContent", args);

        // Assert
        Assert.False(result.Success);
        Assert.Contains("boş", result.Error, StringComparison.OrdinalIgnoreCase);
        Assert.Equal("Original content", File.ReadAllText(testFile));
    }

    [Fact]
    public async Task ReplaceFileContent_MissingTargetReportsPatchConflict()
    {
        // Arrange
        var testFile = Path.Combine(_tempDir, "replace-conflict.txt");
        File.WriteAllText(testFile, "Current content");

        var args = JsonSerializer.Serialize(new
        {
            filePath = testFile,
            targetContent = "Stale content",
            replacementContent = "Updated content"
        });

        // Act
        var result = await _executor.ExecuteAsync("ReplaceFileContent", args);

        // Assert
        Assert.False(result.Success);
        Assert.Contains("PATCH_CONFLICT", result.Error);
        Assert.Contains("ReadFile", result.Error);
        Assert.Equal("Current content", File.ReadAllText(testFile));
    }

    [Fact]
    public async Task ListDirectory_Success()
    {
        // Arrange
        File.WriteAllText(Path.Combine(_tempDir, "file1.txt"), "");
        File.WriteAllText(Path.Combine(_tempDir, "file2.txt"), "");
        Directory.CreateDirectory(Path.Combine(_tempDir, "subdir"));

        var args = JsonSerializer.Serialize(new { path = _tempDir });

        // Act
        var result = await _executor.ExecuteAsync("ListDirectory", args);

        // Assert
        Assert.True(result.Success);
        Assert.Contains("file1.txt", result.Output);
        Assert.Contains("file2.txt", result.Output);
        Assert.Contains("subdir", result.Output);
    }

    [Fact]
    public async Task FindFiles_ByPattern()
    {
        // Arrange
        File.WriteAllText(Path.Combine(_tempDir, "test.cs"), "");
        File.WriteAllText(Path.Combine(_tempDir, "test.txt"), "");
        File.WriteAllText(Path.Combine(_tempDir, "other.cs"), "");

        var args = JsonSerializer.Serialize(new
        {
            rootPath = _tempDir,
            pattern = "*.cs"
        });

        // Act
        var result = await _executor.ExecuteAsync("FindFiles", args);

        // Assert
        Assert.True(result.Success);
        Assert.Contains("test.cs", result.Output);
        Assert.Contains("other.cs", result.Output);
        Assert.DoesNotContain("test.txt", result.Output);
    }

    [Fact]
    public async Task SearchCode_FindsPattern()
    {
        // Arrange
        var testFile = Path.Combine(_tempDir, "code.cs");
        File.WriteAllText(testFile, "public class MyClass\n{\n    public void MyMethod()\n    {\n    }\n}");

        var args = JsonSerializer.Serialize(new
        {
            rootPath = _tempDir,
            query = "MyClass",
            includePattern = "*.cs"
        });

        // Act
        var result = await _executor.ExecuteAsync("SearchCode", args);

        // Assert
        Assert.True(result.Success);
        Assert.Contains("MyClass", result.Output);
    }

    [Fact]
    public async Task CreateCheckpoint_Success()
    {
        // Arrange
        var testFile = Path.Combine(_tempDir, "data.txt");
        File.WriteAllText(testFile, "checkpoint data");

        var args = JsonSerializer.Serialize(new
        {
            projectPath = _tempDir,
            label = "test_checkpoint",
            description = "Test checkpoint"
        });

        // Act
        var result = await _executor.ExecuteAsync("CreateCheckpoint", args);

        // Assert
        Assert.True(result.Success);
        var checkpointDir = Path.Combine(_tempDir, ".mdai", "checkpoints", "test_checkpoint");
        Assert.True(Directory.Exists(checkpointDir));
    }

    [Fact]
    public async Task RollbackToCheckpoint_Success()
    {
        // Arrange - Create checkpoint
        var testFile = Path.Combine(_tempDir, "data.txt");
        File.WriteAllText(testFile, "original data");

        var checkpointArgs = JsonSerializer.Serialize(new
        {
            projectPath = _tempDir,
            label = "rollback_test",
            description = "Test"
        });
        await _executor.ExecuteAsync("CreateCheckpoint", checkpointArgs);

        // Modify file
        File.WriteAllText(testFile, "modified data");
        Assert.Equal("modified data", File.ReadAllText(testFile));

        // Act - Rollback
        var rollbackArgs = JsonSerializer.Serialize(new
        {
            projectPath = _tempDir,
            label = "rollback_test"
        });
        var result = await _executor.ExecuteAsync("RollbackToCheckpoint", rollbackArgs);

        // Assert
        Assert.True(result.Success);
        // File should be restored to original
        var restored = File.ReadAllText(testFile);
        Assert.Equal("original data", restored);
    }

    [Fact]
    public async Task ExecuteAsync_UnknownTool()
    {
        // Arrange
        var args = JsonSerializer.Serialize(new { });

        // Act
        var result = await _executor.ExecuteAsync("UnknownTool123", args);

        // Assert
        Assert.False(result.Success);
        Assert.NotNull(result.Error);
        Assert.Contains("Bilinmeyen", result.Error, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task DelegateTask_ReturnsStructuredResult()
    {
        // Arrange
        var taskArgs = JsonSerializer.Serialize(new
        {
            role = "Tester",
            prompt = "Run tests",
            context = "Test context",
            waitForCompletion = false
        });

        // Act
        var result = await _executor.ExecuteAsync("DelegateTask", taskArgs);

        // Assert
        Assert.True(result.Success);
        Assert.Contains("taskId", result.Output, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("status", result.Output, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task DiscoverProjectContext_ReturnsMarkdown()
    {
        // Arrange
        // Create a test C# file
        var testFile = Path.Combine(_tempDir, "TestClass.cs");
        File.WriteAllText(testFile, "public class TestClass { public async Task ExecuteAsync() { } }");

        var args = JsonSerializer.Serialize(new
        {
            message = "ExecuteAsync method needs review"
        });

        // Act
        var result = await _executor.ExecuteAsync("DiscoverProjectContext", args);

        // Assert
        Assert.True(result.Success);
        Assert.Contains("Proje", result.Output, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task CreateQuickCommand_Success()
    {
        // Arrange
        var args = JsonSerializer.Serialize(new
        {
            name = "test_cmd",
            command = "dotnet build",
            description = "Build project"
        });

        // Act
        var result = await _executor.ExecuteAsync("CreateQuickCommand", args);

        // Assert
        Assert.True(result.Success);
    }

    [Fact]
    public async Task ExecuteQuickCommand_Success()
    {
        // Arrange - First create a quick command
        var createArgs = JsonSerializer.Serialize(new
        {
            name = "echo_test",
            command = "echo Hello",
            description = "Test echo"
        });
        await _executor.ExecuteAsync("CreateQuickCommand", createArgs);

        // Act - Execute it
        var execArgs = JsonSerializer.Serialize(new { name = "echo_test" });
        var result = await _executor.ExecuteAsync("ExecuteQuickCommand", execArgs);

        // Assert
        Assert.True(result.Success);
    }

    [Fact]
    public async Task SmartRecovery_ReturnsRecoveryPlan()
    {
        // Arrange
        var errorOutput = "CS0103: The name 'undefined' does not exist in the current context";
        var args = JsonSerializer.Serialize(new { lastError = errorOutput });

        // Act
        var result = await _executor.ExecuteAsync("SmartRecovery", args);

        // Assert
        Assert.True(result.Success);
        Assert.NotEmpty(result.Output);
    }

    [Fact]
    public async Task ResolvePath_EnforcesProjectBoundary()
    {
        // Arrange - Try to escape project folder
        var testFile = Path.Combine(_tempDir, "..", "outside.txt");
        var args = JsonSerializer.Serialize(new { filePath = testFile });

        // Act & Assert
        // Should throw or fail safely
        var result = await _executor.ExecuteAsync("ReadFile", args);

        // Should fail due to path boundary check
        Assert.False(result.Success);
        Assert.NotNull(result.Error);
    }

    [Fact]
    public void ProjectFolder_Property_Readable()
    {
        // Assert
        Assert.Equal(_tempDir, _executor.ProjectFolder);
    }

    [Fact]
    public void SubAgentCoordinator_Property_Accessible()
    {
        // Assert
        Assert.NotNull(_executor.SubAgentCoordinator);
    }

    [Fact]
    public void ToolRegistry_ExposesSmartRecovery()
    {
        var smartRecovery = ToolRegistry.GetTools()
            .Find(tool => tool.Function.Name == "SmartRecovery");

        Assert.NotNull(smartRecovery);
        Assert.Contains("lastError", smartRecovery.Function.Parameters.Required);
    }

    [Fact]
    public async Task ReadFile_SetsTimelineEvents()
    {
        // Arrange
        var testFile = Path.Combine(_tempDir, "timeline.txt");
        File.WriteAllText(testFile, "test");

        var timelineEvents = new System.Collections.Generic.List<TimelineEvent>();
        EventBus.Subscribe<TimelineEvent>(e => timelineEvents.Add(e));

        var args = JsonSerializer.Serialize(new { filePath = testFile });

        // Act
        var result = await _executor.ExecuteAsync("ReadFile", args);

        // Assert
        Assert.True(result.Success);
        Assert.NotEmpty(timelineEvents);
    }
}
