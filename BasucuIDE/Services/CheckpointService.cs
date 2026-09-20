using System;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace mdaiAgent.Services;

/// <summary>
/// Handles checkpoint creation and rollback operations.
/// Delegates to CheckpointManager for reliable atomic operations.
/// </summary>
public class CheckpointService
{
    private readonly string? _projectFolder;
    private readonly Action<string> _terminalLog;

    public CheckpointService(
        string? projectFolder,
        Action<string> terminalLog)
    {
        _projectFolder = projectFolder;
        _terminalLog = terminalLog;
    }

    public async Task<ToolResult> CreateCheckpointAsync(JsonElement arguments, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var projectPath = arguments.GetProperty("projectPath").GetString();
        var label = arguments.GetProperty("label").GetString();
        var description = arguments.TryGetProperty("description", out var descProp)
            ? descProp.GetString()
            : "Checkpoint created by CheckpointService";

        if (string.IsNullOrWhiteSpace(projectPath) || string.IsNullOrWhiteSpace(label))
            return new ToolResult { Success = false, Error = "projectPath ve label zorunlu." };

        var resolvedRoot = ResolvePath(projectPath);

        try
        {
            cancellationToken.ThrowIfCancellationRequested();
            EventBus.Publish(new TimelineEvent { Type = TimelineEventType.Info, Message = $"Checkpoint oluşturuluyor: {label}" });
            var manager = new CheckpointManager(resolvedRoot, _terminalLog);
            var (success, message, path) = await manager.CreateCheckpointAsync(label, description, cancellationToken: cancellationToken);
            EventBus.Publish(new TimelineEvent
            {
                Type = success ? TimelineEventType.Info : TimelineEventType.Failed,
                IsCompleted = success,
                IsFailed = !success,
                Message = success ? $"Checkpoint oluşturuldu: {label}" : "Checkpoint oluşturulamadı."
            });

            return new ToolResult
            {
                Success = success,
                Output = success ? $"Checkpoint created: {path}" : null,
                Error = success ? null : message
            };
        }
        catch (OperationCanceledException) { throw; }
        catch (Exception ex)
        {
            EventBus.Publish(new TimelineEvent { Type = TimelineEventType.Failed, IsFailed = true, Message = "Checkpoint oluşturulamadı." });
            return new ToolResult { Success = false, Error = $"Checkpoint creation failed: {ex.Message}" };
        }
    }

    public async Task<ToolResult> RollbackToCheckpointAsync(JsonElement arguments, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var projectPath = arguments.GetProperty("projectPath").GetString();
        var label = arguments.GetProperty("label").GetString();

        if (string.IsNullOrWhiteSpace(projectPath) || string.IsNullOrWhiteSpace(label))
            return new ToolResult { Success = false, Error = "projectPath ve label zorunlu." };

        var resolvedRoot = ResolvePath(projectPath);

        try
        {
            cancellationToken.ThrowIfCancellationRequested();
            EventBus.Publish(new TimelineEvent { Type = TimelineEventType.Info, Message = $"Checkpoint geri yükleniyor: {label}" });
            var manager = new CheckpointManager(resolvedRoot, _terminalLog);
            var (success, message, filesRestored) = await manager.RollbackToCheckpointAsync(label, cancellationToken);
            EventBus.Publish(new TimelineEvent
            {
                Type = success ? TimelineEventType.Info : TimelineEventType.Failed,
                IsCompleted = success,
                IsFailed = !success,
                Message = success ? $"Checkpoint geri yüklendi: {label}" : "Checkpoint geri yüklenemedi."
            });

            return new ToolResult
            {
                Success = success,
                Output = success ? $"Rolled back to checkpoint: {label} ({filesRestored} files)" : null,
                Error = success ? null : message
            };
        }
        catch (OperationCanceledException) { throw; }
        catch (Exception ex)
        {
            EventBus.Publish(new TimelineEvent { Type = TimelineEventType.Failed, IsFailed = true, Message = "Checkpoint geri yüklenemedi." });
            return new ToolResult { Success = false, Error = $"Rollback failed: {ex.Message}" };
        }
    }

    private string ResolvePath(string path)
    {
        if (string.IsNullOrEmpty(path))
            return _projectFolder ?? ".";

        if (System.IO.Path.IsPathRooted(path))
        {
            if (_projectFolder != null && !IsWithinProjectBoundary(path, _projectFolder))
                throw new UnauthorizedAccessException($"Path outside project boundary: {path}");
            return path;
        }

        var resolved = _projectFolder != null 
            ? System.IO.Path.Combine(_projectFolder, path) 
            : System.IO.Path.GetFullPath(path);

        if (_projectFolder != null && !IsWithinProjectBoundary(resolved, _projectFolder))
            throw new UnauthorizedAccessException($"Path outside project boundary: {resolved}");

        return resolved;
    }

    private static bool IsWithinProjectBoundary(string candidatePath, string projectRoot)
    {
        var normalized = System.IO.Path.GetFullPath(candidatePath);
        var normalRoot = System.IO.Path.GetFullPath(projectRoot);
        
        if (normalized.Equals(normalRoot, StringComparison.OrdinalIgnoreCase))
            return true;

        try 
        {
            var relative = System.IO.Path.GetRelativePath(normalRoot, normalized);
            return !relative.StartsWith("..") && !System.IO.Path.IsPathRooted(relative);
        }
        catch { return false; }
    }
}
