using System;
using System.IO;
using System.Threading.Tasks;
using mdaiAgent.Services;
using System.Text.Json;
using Xunit;

namespace mdaiAgent.Tests;

public class ExternalPathAccessTests
{
    [Fact]
    public async Task RelativeProjectPath_IsResolvedInsideSelectedProject()
    {
        var project = Path.Combine(Path.GetTempPath(), "mdai_external_project_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(project);
        var approvalRequested = false;

        try
        {
            var executor = new ToolExecutor(
                project,
                _ => { },
                async _ => true,
                async (_, _, _) => true,
                requestExternalFolderAccess: _ =>
                {
                    approvalRequested = true;
                    return Task.FromResult(false);
                });

            var result = await executor.ExecuteAsync(
                "ListDirectory",
                JsonSerializer.Serialize(new { path = "basucuide" }));

            Assert.False(approvalRequested);
            Assert.False(result.Success);
            Assert.Contains("Dizin bulunamadı", result.Error ?? string.Empty, StringComparison.OrdinalIgnoreCase);
        }
        finally
        {
            if (Directory.Exists(project)) Directory.Delete(project, true);
        }
    }

    [Fact]
    public async Task ApprovedBackupFolder_IsReadableButUnapprovedFolderIsRejected()
    {
        var project = Path.Combine(Path.GetTempPath(), "mdai_external_project_" + Guid.NewGuid().ToString("N"));
        var backup = Path.Combine(Path.GetTempPath(), "mdai_external_backup_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(project);
        Directory.CreateDirectory(backup);

        try
        {
            var manager = new ExternalPathAccessManager(project, _ => Task.FromResult(true));
            Assert.True(await manager.EnsureAllowedAsync(backup));
            Assert.True(manager.IsAllowed(Path.Combine(backup, "old-file.cs")));

            var unapproved = Path.Combine(Path.GetTempPath(), "mdai_external_other_" + Guid.NewGuid().ToString("N"));
            Assert.False(manager.IsAllowed(unapproved));
        }
        finally
        {
            if (Directory.Exists(project)) Directory.Delete(project, true);
            if (Directory.Exists(backup)) Directory.Delete(backup, true);
        }
    }
}