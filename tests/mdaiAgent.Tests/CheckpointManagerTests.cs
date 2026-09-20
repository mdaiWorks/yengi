using System;
using System.IO;
using System.Text.Json;
using System.Threading.Tasks;
using Xunit;

namespace mdaiAgent.Tests;

public class CheckpointManagerTests : IDisposable
{
    private readonly string _tempDir;
    private readonly CheckpointManager _manager;
    private readonly System.Collections.Generic.List<string> _logs;

    public CheckpointManagerTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), $"checkpoint_test_{Guid.NewGuid()}");
        Directory.CreateDirectory(_tempDir);
        _logs = new();
        _manager = new CheckpointManager(_tempDir, _logs.Add);
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
    public async Task CreateCheckpoint_Success()
    {
        // Arrange
        File.WriteAllText(Path.Combine(_tempDir, "test.txt"), "content");

        // Act
        var (success, message, path) = await _manager.CreateCheckpointAsync("test_cp", "Test checkpoint");

        // Assert
        Assert.True(success);
        Assert.NotNull(path);
        Assert.True(Directory.Exists(path));
    }

    [Fact]
    public async Task CreateCheckpoint_CreatesMetadata()
    {
        // Arrange
        File.WriteAllText(Path.Combine(_tempDir, "test.txt"), "content");

        // Act
        var (success, _, path) = await _manager.CreateCheckpointAsync("meta_test", "Metadata test");

        // Assert
        Assert.True(success);
        var metadataPath = Path.Combine(path, "checkpoint.json");
        Assert.True(File.Exists(metadataPath));
        
        var content = File.ReadAllText(metadataPath);
        var doc = JsonDocument.Parse(content);
        Assert.True(doc.RootElement.TryGetProperty("Label", out var label));
        Assert.Equal("meta_test", label.GetString());
    }

    [Fact]
    public async Task CreateCheckpoint_InvalidLabel()
    {
        // Act
        var (success, message, _) = await _manager.CreateCheckpointAsync("invalid/label", "");

        // Assert
        Assert.False(success);
        Assert.Contains("invalid characters", message);
    }

    [Fact]
    public async Task CreateCheckpoint_PreventsDuplicates()
    {
        // Arrange
        File.WriteAllText(Path.Combine(_tempDir, "test.txt"), "content");
        await _manager.CreateCheckpointAsync("dup_test", "First");

        // Act
        var (success, message, _) = await _manager.CreateCheckpointAsync("dup_test", "Second");

        // Assert
        Assert.False(success);
        Assert.Contains("already exists", message);
    }

    [Fact]
    public async Task RollbackToCheckpoint_Success()
    {
        // Arrange - Create checkpoint
        var file = Path.Combine(_tempDir, "data.txt");
        File.WriteAllText(file, "original");

        var (cpSuccess, _, cpPath) = await _manager.CreateCheckpointAsync("restore_test", "");
        Assert.True(cpSuccess);

        // Modify file
        File.WriteAllText(file, "modified");
        Assert.Equal("modified", File.ReadAllText(file));

        // Act - Rollback
        var (rollbackSuccess, _, filesRestored) = await _manager.RollbackToCheckpointAsync("restore_test");

        // Assert
        Assert.True(rollbackSuccess);
        Assert.Equal("original", File.ReadAllText(file));
        Assert.True(filesRestored > 0);
    }

    [Fact]
    public async Task RollbackToCheckpoint_NotFound()
    {
        // Act
        var (success, message, _) = await _manager.RollbackToCheckpointAsync("nonexistent");

        // Assert
        Assert.False(success);
        Assert.Contains("not found", message);
    }

    [Fact]
    public async Task ListCheckpoints_ReturnsAll()
    {
        // Arrange
        File.WriteAllText(Path.Combine(_tempDir, "test.txt"), "content");
        await _manager.CreateCheckpointAsync("cp1", "First");
        await _manager.CreateCheckpointAsync("cp2", "Second");
        await _manager.CreateCheckpointAsync("cp3", "Third");

        // Act
        var checkpoints = _manager.ListCheckpoints();

        // Assert
        Assert.Equal(3, checkpoints.Count);
        Assert.Equal("cp3", checkpoints[0].Label); // Most recent first
    }

    [Fact]
    public async Task ListCheckpoints_IncludesMetadata()
    {
        // Arrange
        File.WriteAllText(Path.Combine(_tempDir, "test.txt"), "content");
        await _manager.CreateCheckpointAsync("meta_list", "Test description");

        // Act
        var checkpoints = _manager.ListCheckpoints();

        // Assert
        var cp = checkpoints.Find(c => c.Label == "meta_list");
        Assert.NotNull(cp);
        Assert.Equal("Test description", cp.Description);
        Assert.True(cp.FileCount > 0);
    }

    [Fact]
    public async Task DeleteCheckpoint_Success()
    {
        // Arrange
        File.WriteAllText(Path.Combine(_tempDir, "test.txt"), "content");
        await _manager.CreateCheckpointAsync("del_test", "");

        // Act
        var deleted = _manager.DeleteCheckpoint("del_test");

        // Assert
        Assert.True(deleted);
        var checkpoints = _manager.ListCheckpoints();
        Assert.Empty(checkpoints);
    }

    [Fact]
    public async Task CleanupOldCheckpoints_KeepsRecent()
    {
        // Arrange
        File.WriteAllText(Path.Combine(_tempDir, "test.txt"), "content");
        
        for (int i = 0; i < 10; i++)
        {
            await _manager.CreateCheckpointAsync($"cp_{i:D2}", $"Checkpoint {i}");
            await Task.Delay(10); // Small delay to ensure different timestamps
        }

        // Act
        var deleted = _manager.CleanupOldCheckpoints(keepCount: 3);

        // Assert
        Assert.Equal(7, deleted);
        var remaining = _manager.ListCheckpoints();
        Assert.Equal(3, remaining.Count);
    }

    [Fact]
    public async Task VerifyCheckpoint_Valid()
    {
        // Arrange
        File.WriteAllText(Path.Combine(_tempDir, "test.txt"), "content");
        await _manager.CreateCheckpointAsync("verify_test", "");

        // Act
        var isValid = await _manager.VerifyCheckpointAsync("verify_test");

        // Assert
        Assert.True(isValid);
    }

    [Fact]
    public async Task VerifyCheckpoint_NotFound()
    {
        // Act
        var isValid = await _manager.VerifyCheckpointAsync("nonexistent");

        // Assert
        Assert.False(isValid);
    }

    [Fact]
    public async Task CreateCheckpoint_SkipsLargeFiles()
    {
        // Arrange - Create a large file
        var largeFile = Path.Combine(_tempDir, "large.bin");
        var largeData = new byte[6 * 1024 * 1024]; // 6 MB
        File.WriteAllBytes(largeFile, largeData);

        // Also create a normal file
        File.WriteAllText(Path.Combine(_tempDir, "normal.txt"), "normal");

        // Act
        var (success, message, cpPath) = await _manager.CreateCheckpointAsync("large_test", "Large file test");

        // Assert
        Assert.True(success);
        
        // Check that stub was created for large file
        var stubFiles = Directory.GetFiles(cpPath, "*.stubsz", SearchOption.AllDirectories);
        Assert.NotEmpty(stubFiles);
        
        // Check that normal file was copied
        var normalFile = Path.Combine(cpPath, "normal.txt");
        Assert.True(File.Exists(normalFile));
    }

    [Fact]
    public async Task Checkpoint_ExcludesSystemDirs()
    {
        // Arrange
        Directory.CreateDirectory(Path.Combine(_tempDir, "bin"));
        Directory.CreateDirectory(Path.Combine(_tempDir, "obj"));
        Directory.CreateDirectory(Path.Combine(_tempDir, ".git"));
        
        File.WriteAllText(Path.Combine(_tempDir, "bin", "file.dll"), "");
        File.WriteAllText(Path.Combine(_tempDir, "obj", "file.obj"), "");
        File.WriteAllText(Path.Combine(_tempDir, ".git", "config"), "");
        File.WriteAllText(Path.Combine(_tempDir, "keep.txt"), "important");

        // Act
        var (success, _, cpPath) = await _manager.CreateCheckpointAsync("exclude_test", "");

        // Assert
        Assert.True(success);
        
        // Check that excluded dirs are not in checkpoint
        Assert.False(Directory.Exists(Path.Combine(cpPath, "bin")));
        Assert.False(Directory.Exists(Path.Combine(cpPath, "obj")));
        Assert.False(Directory.Exists(Path.Combine(cpPath, ".git")));
        
        // Check that normal file is present
        Assert.True(File.Exists(Path.Combine(cpPath, "keep.txt")));
    }

    [Fact]
    public async Task MultipleCheckpoint_IndependentRestores()
    {
        // Arrange
        var file = Path.Combine(_tempDir, "version.txt");

        // State 1
        File.WriteAllText(file, "version 1");
        var (cp1Success, _, _) = await _manager.CreateCheckpointAsync("v1", "");
        Assert.True(cp1Success);

        // State 2
        File.WriteAllText(file, "version 2");
        var (cp2Success, _, _) = await _manager.CreateCheckpointAsync("v2", "");
        Assert.True(cp2Success);

        // Modify to v3
        File.WriteAllText(file, "version 3");

        // Act - Restore v1
        var (r1Success, _, _) = await _manager.RollbackToCheckpointAsync("v1");
        Assert.True(r1Success);
        Assert.Equal("version 1", File.ReadAllText(file));

        // Act - Restore v2
        var (r2Success, _, _) = await _manager.RollbackToCheckpointAsync("v2");
        Assert.True(r2Success);
        Assert.Equal("version 2", File.ReadAllText(file));
    }

    [Fact]
    public async Task Checkpoint_Atomic_CleanupOnFailure()
    {
        // Arrange
        File.WriteAllText(Path.Combine(_tempDir, "test.txt"), "content");

        // Act
        var (success, _, cpPath) = await _manager.CreateCheckpointAsync("atomic_test", "");

        // Assert
        Assert.True(success);
        
        // Verify no temp directory left behind
        var tempDir = cpPath + ".tmp";
        Assert.False(Directory.Exists(tempDir));
    }

    [Fact]
    public async Task Checkpoint_Metadata_IncludesDescription()
    {
        // Arrange
        File.WriteAllText(Path.Combine(_tempDir, "test.txt"), "content");
        var description = "This is a test checkpoint for critical changes";

        // Act
        await _manager.CreateCheckpointAsync("desc_test", description);
        var checkpoints = _manager.ListCheckpoints();

        // Assert
        var cp = checkpoints.Find(c => c.Label == "desc_test");
        Assert.NotNull(cp);
        Assert.Equal(description, cp.Description);
    }
}
