using System;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace mdaiAgent.Services;

/// <summary>
/// Handles task delegation and project context discovery.
/// </summary>
public class DelegationService
{
    private readonly string? _projectFolder;
    private readonly Action<string> _terminalLog;
    private readonly SubAgentResultCoordinator _subAgentCoordinator;

    public DelegationService(
        string? projectFolder,
        Action<string> terminalLog,
        SubAgentResultCoordinator subAgentCoordinator)
    {
        _projectFolder = projectFolder;
        _terminalLog = terminalLog;
        _subAgentCoordinator = subAgentCoordinator;
    }

    public async Task<ToolResult> DelegateTaskAsync(JsonElement arguments, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        try
        {
            cancellationToken.ThrowIfCancellationRequested();
            var role = arguments.TryGetProperty("role", out var roleProp) ? roleProp.GetString() : "SubAgent";
            var prompt = arguments.TryGetProperty("prompt", out var promptProp) ? promptProp.GetString() : "";
            var context = arguments.TryGetProperty("context", out var ctxProp) ? ctxProp.GetString() : "";
            var waitForCompletion = arguments.TryGetProperty("waitForCompletion", out var waitProp) && waitProp.GetBoolean();
            var timeoutSeconds = arguments.TryGetProperty("timeoutSeconds", out var timeoutProp) ? timeoutProp.GetInt32() : 300;

            if (string.IsNullOrWhiteSpace(prompt))
            {
                return new ToolResult { Success = false, Error = "Prompt boş olamaz." };
            }

            var taskResult = _subAgentCoordinator.RegisterTask(role ?? "SubAgent", prompt, context ?? "");
            taskResult.Status = "Running";
            
            _terminalLog?.Invoke($"📤 [SubAgent] Task {taskResult.TaskId} initiated for role '{role}'");
            EventBus.Publish(new TimelineEvent { Type = TimelineEventType.Thinking, Message = $"Alt ajan görevi başlatıldı: {role}" });

            EventBus.Publish(new SubAgentRequestEvent
            {
                Role = role ?? "SubAgent",
                Prompt = prompt,
                Context = context ?? "",
                TaskId = taskResult.TaskId
            });

            var responseJson = JsonSerializer.Serialize(new
            {
                taskId = taskResult.TaskId,
                role = role,
                status = "Started",
                message = $"[{role}] adlı alt-ajan görevi ({taskResult.TaskId}) arka planda başlatıldı.",
                waitForCompletion = waitForCompletion
            });

            if (waitForCompletion)
            {
                _terminalLog?.Invoke($"⏳ [SubAgent] Waiting for task {taskResult.TaskId} (timeout: {timeoutSeconds}s)");
                cancellationToken.ThrowIfCancellationRequested();
                var completedResult = await _subAgentCoordinator.WaitForResultAsync(taskResult.TaskId, timeoutSeconds);
                
                if (completedResult.Status == "Success")
                {
                    return new ToolResult { Success = true, Output = completedResult.ToJson() };
                }
                else
                {
                    return new ToolResult { Success = false, Error = $"SubAgent task failed: {completedResult.Error}" };
                }
            }

            return new ToolResult { Success = true, Output = responseJson };
        }
        catch (OperationCanceledException) { throw; }
        catch (Exception ex)
        {
            EventBus.Publish(new TimelineEvent { Type = TimelineEventType.Failed, IsFailed = true, Message = "Alt ajan görevi başlatılamadı." });
            _terminalLog?.Invoke($"❌ [SubAgent] DelegateTask failed: {ex.Message}");
            return new ToolResult { Success = false, Error = $"DelegateTask failed: {ex.Message}" };
        }
    }

    public async Task<ToolResult> DiscoverProjectContextAsync(JsonElement arguments, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        try
        {
            cancellationToken.ThrowIfCancellationRequested();
            var projectPath = ResolvePath(_projectFolder ?? ".");

            if (!System.IO.Directory.Exists(projectPath))
                return new ToolResult { Success = false, Error = $"Proje dizini bulunamadı: {projectPath}" };

            var context = new System.Text.StringBuilder();
            context.AppendLine("# Proje Bağlamı Keşfi");
            context.AppendLine($"Proje Kök: {projectPath}");
            context.AppendLine();

            var csprojFiles = System.IO.Directory.GetFiles(projectPath, "*.csproj", System.IO.SearchOption.TopDirectoryOnly);
            if (csprojFiles.Length > 0)
            {
                context.AppendLine("## C# Projeleri");
                foreach (var proj in csprojFiles)
                {
                    context.AppendLine($"- {System.IO.Path.GetFileName(proj)}");
                }
                context.AppendLine();
            }

            var sourceFiles = System.IO.Directory.GetFiles(projectPath, "*.cs", System.IO.SearchOption.AllDirectories);
            context.AppendLine($"## Kaynak Dosyaları: {sourceFiles.Length} adet");
            foreach (var sourceFile in sourceFiles.Take(20))
                context.AppendLine($"- {System.IO.Path.GetFileName(sourceFile)}");
            context.AppendLine();

            var testFiles = System.IO.Directory.GetFiles(projectPath, "*Test*.cs", System.IO.SearchOption.AllDirectories);
            if (testFiles.Length > 0)
            {
                context.AppendLine($"## Test Dosyaları: {testFiles.Length} adet");
            }

            EventBus.Publish(new TimelineEvent { Type = TimelineEventType.Info, IsCompleted = true, Message = "Proje bağlamı keşfi tamamlandı." });
            return new ToolResult { Success = true, Output = context.ToString() };
        }
        catch (OperationCanceledException) { throw; }
        catch (Exception ex)
        {
            EventBus.Publish(new TimelineEvent { Type = TimelineEventType.Failed, IsFailed = true, Message = "Proje bağlamı keşfi başarısız oldu." });
            return new ToolResult { Success = false, Error = $"Bağlam keşfi hatası: {ex.Message}" };
        }
    }

    private string ResolvePath(string path)
    {
        if (string.IsNullOrEmpty(path))
            return _projectFolder ?? ".";

        if (System.IO.Path.IsPathRooted(path))
            return path;

        return _projectFolder != null
            ? System.IO.Path.Combine(_projectFolder, path)
            : System.IO.Path.GetFullPath(path);
    }
}
