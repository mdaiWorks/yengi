using System;
using System.Text.Json;
using System.Threading.Tasks;
using System.IO;
using Xunit;

namespace mdaiAgent.Tests;

public class ToolExecutorTests
{
    [Fact]
    public async Task ReadFile_ReturnsContent()
    {
        var temp = Path.Combine(Path.GetTempPath(), "mdai_test_read.txt");
        File.WriteAllText(temp, "hello world");

        var executor = new ToolExecutor(Path.GetDirectoryName(temp), s => { }, (Func<string, Task<bool>>)(async cmd => true), async (f, o, n) => true);
        var args = JsonSerializer.Serialize(new { filePath = temp });
        var res = await executor.ExecuteAsync("ReadFile", args);

        Assert.True(res.Success);
        Assert.Equal("hello world", res.Output);

        File.Delete(temp);
    }

    [Fact]
    public async Task ReadFile_AllowsApprovedExternalFolder()
    {
        var projectDir = Path.Combine(Path.GetTempPath(), "mdai_test_project_external_read");
        var externalDir = Path.Combine(Path.GetTempPath(), "mdai_test_external_read");
        Directory.CreateDirectory(projectDir);
        Directory.CreateDirectory(externalDir);
        var filePath = Path.Combine(externalDir, "backup.txt");
        File.WriteAllText(filePath, "backup content");

        var approvalCount = 0;
        var executor = new ToolExecutor(
            projectDir,
            _ => { },
            (Func<string, Task<bool>>)(async _ => true),
            async (_, _, _) => true,
            requestExternalFolderAccess: async _ =>
            {
                approvalCount++;
                return true;
            });

        var result = await executor.ExecuteAsync("ReadFile", JsonSerializer.Serialize(new { filePath }));

        Assert.True(result.Success);
        Assert.Equal("backup content", result.Output);
        Assert.Equal(1, approvalCount);

        Directory.Delete(projectDir, true);
        Directory.Delete(externalDir, true);
    }

    [Fact]
    public async Task ReadFile_RejectsUnapprovedExternalFolder()
    {
        var projectDir = Path.Combine(Path.GetTempPath(), "mdai_test_project_external_reject");
        var externalDir = Path.Combine(Path.GetTempPath(), "mdai_test_external_reject");
        Directory.CreateDirectory(projectDir);
        Directory.CreateDirectory(externalDir);
        var filePath = Path.Combine(externalDir, "backup.txt");
        File.WriteAllText(filePath, "backup content");

        var executor = new ToolExecutor(
            projectDir,
            _ => { },
            (Func<string, Task<bool>>)(async _ => true),
            async (_, _, _) => true,
            requestExternalFolderAccess: async _ => false);

        var result = await executor.ExecuteAsync("ReadFile", JsonSerializer.Serialize(new { filePath }));

        Assert.False(result.Success);
        Assert.Contains("onaylanmadı", result.Error ?? string.Empty, StringComparison.OrdinalIgnoreCase);

        Directory.Delete(projectDir, true);
        Directory.Delete(externalDir, true);
    }

    [Fact]
    public async Task CreateOrUpdateFile_ExternalFolderStillRequiresFileApproval()
    {
        var projectDir = Path.Combine(Path.GetTempPath(), "mdai_test_project_external_write");
        var externalDir = Path.Combine(Path.GetTempPath(), "mdai_test_external_write");
        Directory.CreateDirectory(projectDir);
        Directory.CreateDirectory(externalDir);
        var filePath = Path.Combine(externalDir, "backup.txt");
        File.WriteAllText(filePath, "old");

        var executor = new ToolExecutor(
            projectDir,
            _ => { },
            (Func<string, Task<bool>>)(async _ => true),
            async (_, _, _) => false,
            requestExternalFolderAccess: async _ => true);

        var result = await executor.ExecuteAsync("CreateOrUpdateFile", JsonSerializer.Serialize(new { filePath, content = "new" }));

        Assert.False(result.Success);
        Assert.Equal("old", File.ReadAllText(filePath));

        Directory.Delete(projectDir, true);
        Directory.Delete(externalDir, true);
    }

    [Fact]
    public async Task CreateOrUpdateFile_BackupsAndWrites()
    {
        var projDir = Path.Combine(Path.GetTempPath(), "mdai_test_proj");
        if (Directory.Exists(projDir)) Directory.Delete(projDir, true);
        Directory.CreateDirectory(projDir);

        var filePath = Path.Combine(projDir, "test.txt");
        File.WriteAllText(filePath, "old");

        var executor = new ToolExecutor(projDir, s => { }, (Func<string, Task<bool>>)(async cmd => true), async (f, o, n) => true);
        var args = JsonSerializer.Serialize(new { filePath = filePath, content = "new content" });
        var res = await executor.ExecuteAsync("CreateOrUpdateFile", args);

        Assert.True(res.Success);
        var written = File.ReadAllText(filePath);
        Assert.Equal("new content", written);

        var backupDir = Path.Combine(projDir, ".mdai", "backup");
        Assert.True(Directory.Exists(backupDir));
        var backups = Directory.GetFiles(backupDir);
        Assert.NotEmpty(backups);

        Directory.Delete(projDir, true);
    }

    [Fact]
    public async Task CreateOrUpdateFile_UserRejects_ReturnsFalse()
    {
        var projDir = Path.Combine(Path.GetTempPath(), "mdai_test_proj2");
        if (Directory.Exists(projDir)) Directory.Delete(projDir, true);
        Directory.CreateDirectory(projDir);

        var filePath = Path.Combine(projDir, "test.txt");
        File.WriteAllText(filePath, "old");

        var executor = new ToolExecutor(projDir, s => { }, (Func<string, Task<bool>>)(async cmd => true), async (f, o, n) => false, safeAutomationEnabled: true);
        var args = JsonSerializer.Serialize(new { filePath = filePath, content = "new content" });
        var res = await executor.ExecuteAsync("CreateOrUpdateFile", args);

        Assert.False(res.Success);
        var written = File.ReadAllText(filePath);
        Assert.Equal("old", written);

        Directory.Delete(projDir, true);
    }

    [Fact]
    public async Task BuildProject_RequiresApproval_WhenSafeAutomationEnabled()
    {
        var projDir = Path.Combine(Path.GetTempPath(), "mdai_test_proj3");
        if (Directory.Exists(projDir)) Directory.Delete(projDir, true);
        Directory.CreateDirectory(projDir);

        var csprojPath = Path.Combine(projDir, "dummy.csproj");
        File.WriteAllText(csprojPath, "<Project Sdk=\"Microsoft.NET.Sdk\" />");

        var executor = new ToolExecutor(projDir, s => { }, (Func<string, Task<bool>>)(async cmd => false), async (f, o, n) => true, safeAutomationEnabled: true);
        var args = JsonSerializer.Serialize(new { projectPath = csprojPath });
        var res = await executor.ExecuteAsync("BuildProject", args);

        Assert.False(res.Success);
        Assert.Contains("onay", res.Error ?? string.Empty, System.StringComparison.OrdinalIgnoreCase);

        Directory.Delete(projDir, true);
    }

    [Fact]
    public async Task ExecuteTerminalCommand_UsesUiInvoker_ForConfirmation()
    {
        var projDir = Path.Combine(Path.GetTempPath(), "mdai_test_proj4");
        if (Directory.Exists(projDir)) Directory.Delete(projDir, true);
        Directory.CreateDirectory(projDir);

        var invocationCount = 0;
        var executor = new ToolExecutor(
            projDir,
            s => { },
            async (cmd) => ToolExecutor.ConfirmResult.Allow,
            async (f, o, n) => true,
            safeAutomationEnabled: true,
            confirmUiInvoker: async operation =>
            {
                invocationCount++;
                return await operation();
            });

        var args = JsonSerializer.Serialize(new { command = "echo hi" });
        var res = await executor.ExecuteAsync("ExecuteTerminalCommand", args);

        Assert.True(res.Success);
        Assert.Equal(1, invocationCount);

        Directory.Delete(projDir, true);
    }

    [Fact]
    public async Task ExecuteTerminalCommand_TimeoutsAndReturnsFailure()
    {
        var projDir = Path.Combine(Path.GetTempPath(), "mdai_test_proj_timeout");
        if (Directory.Exists(projDir)) Directory.Delete(projDir, true);
        Directory.CreateDirectory(projDir);

        var executor = new ToolExecutor(projDir, s => { }, async _ => ToolExecutor.ConfirmResult.Allow, async (f, o, n) => true, safeAutomationEnabled: true);
        var args = JsonSerializer.Serialize(new
        {
            command = "powershell -Command \"Start-Sleep -Seconds 30\"",
            workingDirectory = projDir,
            timeoutSeconds = 1
        });

        var res = await executor.ExecuteAsync("ExecuteTerminalCommand", args);

        Assert.False(res.Success);
        Assert.Contains("zaman aşıldı", res.Error ?? string.Empty, StringComparison.OrdinalIgnoreCase);

        Directory.Delete(projDir, true);
    }

    [Fact]
    public async Task CreateOrUpdateFile_RejectsOutsideProjectBoundary()
    {
        var projDir = Path.Combine(Path.GetTempPath(), "mdai_test_proj_guard");
        if (Directory.Exists(projDir)) Directory.Delete(projDir, true);
        Directory.CreateDirectory(projDir);

        var outsideRoot = Path.Combine(Path.GetTempPath(), "mdai_test_outside_guard_" + System.Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(outsideRoot);
        var outsideFile = Path.Combine(outsideRoot, "outside.txt");

        var executor = new ToolExecutor(projDir, s => { }, async (cmd) => ToolExecutor.ConfirmResult.Allow, async (f, o, n) => true);
        var args = JsonSerializer.Serialize(new { filePath = outsideFile, content = "payload" });
        var res = await executor.ExecuteAsync("CreateOrUpdateFile", args);

        Assert.False(res.Success);
        Assert.False(File.Exists(outsideFile));

        Directory.Delete(projDir, true);
        Directory.Delete(outsideRoot, true);
    }

    [Fact]
    public async Task RollbackToCheckpoint_UsesOnlySelectedLabel()
    {
        var projDir = Path.Combine(Path.GetTempPath(), "mdai_test_proj_rollback_label");
        if (Directory.Exists(projDir)) Directory.Delete(projDir, true);
        Directory.CreateDirectory(projDir);

        var appFile = Path.Combine(projDir, "app.txt");
        var otherFile = Path.Combine(projDir, "secondary.txt");
        File.WriteAllText(appFile, "v1");
        File.WriteAllText(otherFile, "keep");

        var executor = new ToolExecutor(projDir, _ => { }, async _ => true, async (_, _, _) => true);
        var firstCheckpoint = JsonSerializer.Serialize(new { projectPath = projDir, label = "first" });
        var secondCheckpoint = JsonSerializer.Serialize(new { projectPath = projDir, label = "second" });

        await executor.ExecuteAsync("CreateCheckpoint", firstCheckpoint);
        File.WriteAllText(appFile, "v2");
        File.WriteAllText(otherFile, "second-version");
        await executor.ExecuteAsync("CreateCheckpoint", secondCheckpoint);

        File.WriteAllText(appFile, "latest");
        File.WriteAllText(otherFile, "latest-secondary");

        var rollbackResult = await executor.ExecuteAsync("RollbackToCheckpoint", JsonSerializer.Serialize(new { projectPath = projDir, label = "first" }));

        Assert.True(rollbackResult.Success);
        Assert.Equal("v1", File.ReadAllText(appFile));
        Assert.Equal("keep", File.ReadAllText(otherFile));

        Directory.Delete(projDir, true);
    }

    [Fact]
    public void EventBus_Unsubscribe_RemovesHandler()
    {
        var count = 0;
        Action<string> handler = _ => count++;

        EventBus.Subscribe(handler);
        EventBus.Unsubscribe(handler);
        EventBus.Publish("sample");

        Assert.Equal(0, count);
    }

    [Fact]
    public async Task DiscoverProjectContext_ReturnsMdFormatReport()
    {
        var projDir = Path.Combine(Path.GetTempPath(), "mdai_discover_test");
        if (Directory.Exists(projDir)) Directory.Delete(projDir, true);
        Directory.CreateDirectory(projDir);

        File.WriteAllText(Path.Combine(projDir, "Project.csproj"), "");
        File.WriteAllText(Path.Combine(projDir, "MainClass.cs"), "");

        var subDir = Path.Combine(projDir, "Tests");
        Directory.CreateDirectory(subDir);
        File.WriteAllText(Path.Combine(subDir, "ProjectTests.cs"), "");

        var executor = new ToolExecutor(projDir, s => { }, (Func<string, Task<bool>>)(async cmd => true), async (f, o, n) => true);
        var result = await executor.ExecuteAsync("DiscoverProjectContext", "{}");

        Assert.True(result.Success);
        Assert.Contains("Proje Bağlamı Keşfi", result.Output);
        Assert.Contains(".csproj", result.Output);
        Assert.Contains("MainClass", result.Output);

        Directory.Delete(projDir, true);
    }

    [Fact]
    public async Task CreateQuickCommand_StoresCommand()
    {
        var executor = new ToolExecutor(Path.GetTempPath(), s => { }, (Func<string, Task<bool>>)(async cmd => true), async (f, o, n) => true);
        var args = JsonSerializer.Serialize(new { name = "test", command = "dotnet test" });
        var result = await executor.ExecuteAsync("CreateQuickCommand", args);

        Assert.True(result.Success);
        Assert.Contains("kaydedildi", result.Output);
    }

    [Fact]
    public async Task ExecuteQuickCommand_ReturnsErrorWhenNotFound()
    {
        var executor = new ToolExecutor(Path.GetTempPath(), s => { }, (Func<string, Task<bool>>)(async cmd => true), async (f, o, n) => true);
        var args = JsonSerializer.Serialize(new { name = "nonexistent" });
        var result = await executor.ExecuteAsync("ExecuteQuickCommand", args);

        Assert.False(result.Success);
        Assert.Contains("bulunamadı", result.Error);
    }

    [Fact]
    public async Task SmartRecovery_GeneratesRecoveryPlan()
    {
        var projDir = Path.Combine(Path.GetTempPath(), "mdai_recovery_test");
        if (Directory.Exists(projDir)) Directory.Delete(projDir, true);
        Directory.CreateDirectory(projDir);

        var executor = new ToolExecutor(projDir, s => { }, (Func<string, Task<bool>>)(async cmd => true), async (f, o, n) => true);
        var args = JsonSerializer.Serialize(new { lastError = "build failed", failedCommand = "dotnet build" });
        var result = await executor.ExecuteAsync("SmartRecovery", args);

        Assert.True(result.Success);
        using var recoveryDocument = JsonDocument.Parse(result.Output);
        var recovery = recoveryDocument.RootElement;
        Assert.False(string.IsNullOrWhiteSpace(recovery.GetProperty("Title").GetString()));
        Assert.Contains("build", recovery.GetProperty("Recovery").GetString(), StringComparison.OrdinalIgnoreCase);

        Directory.Delete(projDir, true);
    }
}
