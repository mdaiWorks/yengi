// ============================================================
using System;
using System.Collections.Generic;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace mdaiAgent;

/// <summary>
/// Agent Runtime - UI'dan bağımsız agent yürütme ve doğrulama sınırı.
/// </summary>
public class AgentCoreRuntime : IAgentCore
{
    private readonly ToolExecutor _toolExecutor;
    private readonly CheckpointManager _checkpointManager;
    private AgentStatus _status = AgentStatus.Idle;
    private string _projectFolder = "";
    private readonly bool _productionSafeMode;

    private readonly Dictionary<Type, List<Delegate>> _subscribers = new();
    private readonly Action<string> _terminalLog;
    private readonly Action<string, NotificationSeverity> _notify;
    private readonly Action<string> _updateOperationStep;
    private readonly Func<string, Task<bool>>? _verificationRunner;
    private readonly AgentVerificationLoopService? _verificationService;

    public static AgentCoreRuntime CreateSafeRuntime(
        string projectFolder,
        Action<string> terminalLog,
        Action<string, NotificationSeverity> notify,
        Action<string> updateOperationStep,
        ToolExecutor? toolExecutor = null,
        Func<string, Task<bool>>? verificationRunner = null,
        AgentVerificationLoopService? verificationService = null)
    {
        return new AgentCoreRuntime(
            projectFolder,
            terminalLog,
            notify,
            updateOperationStep,
            toolExecutor,
            verificationRunner,
            verificationService,
            productionSafeMode: true);
    }

    public bool IsProductionReady => !string.IsNullOrWhiteSpace(_projectFolder) && _toolExecutor != null && _productionSafeMode;

    public bool CanHandleTool(string toolName)
    {
        if (string.IsNullOrWhiteSpace(toolName)) return false;

        return ToolRegistry.GetTools()
            .Any(tool => tool.Function != null &&
                string.Equals(tool.Function.Name, toolName, StringComparison.OrdinalIgnoreCase));
    }

    public AgentCoreRuntime(
        string projectFolder,
        Action<string> terminalLog,
        Action<string, NotificationSeverity> notify,
        Action<string> updateOperationStep,
        ToolExecutor? toolExecutor = null,
        Func<string, Task<bool>>? verificationRunner = null,
        AgentVerificationLoopService? verificationService = null,
        bool productionSafeMode = false)
    {
        _projectFolder = projectFolder;
        _terminalLog = terminalLog;
        _notify = notify;
        _updateOperationStep = updateOperationStep;
        _verificationRunner = verificationRunner;
        _verificationService = verificationService;
        _productionSafeMode = productionSafeMode;

        // Use provided ToolExecutor or create default
        if (toolExecutor != null)
        {
            _toolExecutor = toolExecutor;
        }
        else
        {
            // Create default ToolExecutor with minimal callbacks
            _toolExecutor = new ToolExecutor(
                projectFolder,
                terminalLog,
                async _ => ToolExecutor.ConfirmResult.Cancel,
                async (_, _, _) => false,
                safeAutomationEnabled: true,
                updateOperationStep: updateOperationStep,
                notify: notify
            );
        }

        _checkpointManager = new CheckpointManager(projectFolder, terminalLog);
    }

    public async Task InitializeAsync(string projectFolder, IAiProvider apiClient)
    {
        _projectFolder = projectFolder;
        _terminalLog($"🚀 Agent Core initializing: {projectFolder}");
        
        if (_toolExecutor != null)
        {
            // ToolExecutor'ın API client'ı güncelle
            // Not: ToolExecutor'da SetApiClient method eklenmeli gerek
            _terminalLog("✅ Agent Core initialized");
        }
    }

    public AgentStatus GetStatus() => _status;

    private void SetStatus(AgentStatus status, TimelineEventType eventType, string message, string? details = null)
    {
        _status = status;
        EventBus.Publish(new TimelineEvent
        {
            Type = eventType,
            Message = message,
            Details = details,
            IsCompleted = status == AgentStatus.Idle && eventType == TimelineEventType.Completed,
            IsFailed = status == AgentStatus.Error || eventType == TimelineEventType.Failed
        });
    }

    private void ClearOperationStep()
    {
        _updateOperationStep("");
    }

    public void Subscribe<T>(Action<T> handler) where T : class
    {
        var type = typeof(T);
        if (!_subscribers.ContainsKey(type))
        {
            _subscribers[type] = new List<Delegate>();
        }
        _subscribers[type].Add(handler);
    }

    public void Unsubscribe<T>(Action<T> handler) where T : class
    {
        var type = typeof(T);
        if (_subscribers.TryGetValue(type, out var list))
        {
            list.Remove(handler);
            if (list.Count == 0)
            {
                _subscribers.Remove(type);
            }
        }
    }

    private void PublishEvent<T>(T evt) where T : class
    {
        var type = typeof(T);
        if (_subscribers.TryGetValue(type, out var handlers))
        {
            foreach (var handler in handlers)
            {
                if (handler is Action<T> action)
                {
                    action?.Invoke(evt);
                }
            }
        }
    }

    public async Task<ToolResult> ExecuteToolAsync(string toolName, string jsonArguments)
    {
        return await ExecuteToolAsync(toolName, jsonArguments, CancellationToken.None, null);
    }

    public async Task<ToolResult> ExecuteToolAsync(
        string toolName,
        string jsonArguments,
        CancellationToken cancellationToken,
        string? lastUserMessage)
    {
        try
        {
            SetStatus(AgentStatus.ExecutingTool, TimelineEventType.Thinking, $"Araç çalıştırılıyor: {toolName}");
            _updateOperationStep($"🛠️  Executing {toolName}...");

            var result = await _toolExecutor.ExecuteAsync(toolName, jsonArguments, cancellationToken, lastUserMessage);

            SetStatus(
                result.Success ? AgentStatus.Idle : AgentStatus.Error,
                result.Success ? TimelineEventType.Completed : TimelineEventType.Failed,
                result.Success ? $"Araç tamamlandı: {toolName}" : $"Araç başarısız: {toolName}",
                result.Error ?? result.Output);
            ClearOperationStep();

            return result;
        }
        catch (Exception ex)
        {
            SetStatus(AgentStatus.Error, TimelineEventType.Failed, $"Araç hatası: {toolName}", ex.Message);
            _terminalLog($"❌ Tool execution error: {ex.Message}");
            ClearOperationStep();
            return new ToolResult { Success = false, Error = ex.Message };
        }
    }

    public async Task<bool> VerifyAsync(string description)
    {
        try
        {
            SetStatus(AgentStatus.Verifying, TimelineEventType.Building, "Runtime doğrulaması başladı");
            _updateOperationStep("🔍 Verifying changes...");

            if (_verificationRunner != null)
            {
                var verified = await _verificationRunner(description);
                SetStatus(
                    verified ? AgentStatus.Idle : AgentStatus.Error,
                    verified ? TimelineEventType.Completed : TimelineEventType.Failed,
                    verified ? "Runtime doğrulaması tamamlandı" : "Runtime doğrulaması başarısız");
                ClearOperationStep();
                return verified;
            }

            // Simple verification: try to build
            var buildResult = await _toolExecutor.ExecuteAsync("BuildProject", 
                JsonSerializer.Serialize(new { projectPath = _projectFolder }));
            
            if (!buildResult.Success)
            {
                _terminalLog($"⚠️  Verification failed: {buildResult.Error}");
                SetStatus(AgentStatus.Error, TimelineEventType.Failed, "Runtime doğrulaması başarısız", buildResult.Error);
                ClearOperationStep();
                return false;
            }

            SetStatus(AgentStatus.Idle, TimelineEventType.Completed, "Runtime doğrulaması tamamlandı");
            ClearOperationStep();
            _terminalLog("✅ Verification passed");

            return true;
        }
        catch (Exception ex)
        {
            SetStatus(AgentStatus.Error, TimelineEventType.Failed, "Runtime doğrulama hatası", ex.Message);
            _terminalLog($"❌ Verification error: {ex.Message}");
            ClearOperationStep();
            return false;
        }
    }

    public async Task<VerificationResult> VerifyChangesAsync(
        string projectFolder,
        List<string> changedFiles,
        string systemPrompt,
        string userMessage = "")
    {
        if (_verificationService == null)
        {
            throw new InvalidOperationException("Verification servisi runtime'a bağlanmadı.");
        }

        SetStatus(AgentStatus.Verifying, TimelineEventType.Building, "Verification 2.0 başladı");
        _updateOperationStep("🔍 Verification 2.0 çalışıyor...");

        try
        {
            var result = await _verificationService.VerifyChangesAsync(projectFolder, changedFiles, systemPrompt, userMessage);
            SetStatus(
                result.Success ? AgentStatus.Idle : AgentStatus.Error,
                result.Success ? TimelineEventType.Completed : TimelineEventType.Failed,
                result.Success ? "Verification 2.0 tamamlandı" : "Verification 2.0 başarısız",
                result.Summary);
            ClearOperationStep();
            return result;
        }
        catch (Exception ex)
        {
            SetStatus(AgentStatus.Error, TimelineEventType.Failed, "Verification 2.0 hatası", ex.Message);
            ClearOperationStep();
            throw;
        }
    }

    public async Task<ToolResult> CreateCheckpointAsync(string label, string description)
    {
        try
        {
            SetStatus(AgentStatus.ExecutingTool, TimelineEventType.Info, $"Checkpoint oluşturuluyor: {label}");
            _updateOperationStep($"💾 Creating checkpoint: {label}...");

            await _checkpointManager.CreateCheckpointAsync(label, description);
            
            SetStatus(AgentStatus.Idle, TimelineEventType.Completed, $"Checkpoint oluşturuldu: {label}");
            ClearOperationStep();

            _terminalLog($"✅ Checkpoint created: {label}");
            return new ToolResult { Success = true, Output = $"Checkpoint '{label}' created successfully" };
        }
        catch (Exception ex)
        {
            SetStatus(AgentStatus.Error, TimelineEventType.Failed, $"Checkpoint oluşturulamadı: {label}", ex.Message);
            _terminalLog($"❌ Checkpoint creation error: {ex.Message}");
            ClearOperationStep();
            return new ToolResult { Success = false, Error = ex.Message };
        }
    }

    public async Task<ToolResult> RollbackAsync(string label)
    {
        try
        {
            SetStatus(AgentStatus.ExecutingTool, TimelineEventType.Info, $"Checkpoint geri yükleniyor: {label}");
            _updateOperationStep($"⏮️  Rolling back to: {label}...");

            await _checkpointManager.RollbackToCheckpointAsync(label);
            
            SetStatus(AgentStatus.Idle, TimelineEventType.Completed, $"Checkpoint geri yüklendi: {label}");
            ClearOperationStep();

            _terminalLog($"✅ Rolled back to: {label}");
            return new ToolResult { Success = true, Output = $"Rolled back to checkpoint '{label}'" };
        }
        catch (Exception ex)
        {
            SetStatus(AgentStatus.Error, TimelineEventType.Failed, $"Checkpoint geri yüklenemedi: {label}", ex.Message);
            _terminalLog($"❌ Rollback error: {ex.Message}");
            ClearOperationStep();
            return new ToolResult { Success = false, Error = ex.Message };
        }
    }
}
