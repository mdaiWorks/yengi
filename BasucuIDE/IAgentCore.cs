using System;
using System.Threading.Tasks;

namespace mdaiAgent;

/// <summary>
/// Agent Core Interface - UI'dan bağımsız agent runtime
/// Tüm tool execution, planning, checkpoint, delegation logic burada
/// </summary>
public interface IAgentCore
{
    /// <summary>
    /// Execute any registered tool
    /// </summary>
    Task<ToolResult> ExecuteToolAsync(string toolName, string jsonArguments);

    /// <summary>
    /// Get current agent status (busy, idle, etc)
    /// </summary>
    AgentStatus GetStatus();

    /// <summary>
    /// Subscribe to agent events
    /// </summary>
    void Subscribe<T>(Action<T> handler) where T : class;

    /// <summary>
    /// Unsubscribe from agent events
    /// </summary>
    void Unsubscribe<T>(Action<T> handler) where T : class;

    /// <summary>
    /// Initialize agent with project folder
    /// </summary>
    Task InitializeAsync(string projectFolder, IAiProvider apiClient);

    /// <summary>
    /// Run verification loop (build/test validation)
    /// </summary>
    Task<bool> VerifyAsync(string description);

    /// <summary>
    /// Create checkpoint with label
    /// </summary>
    Task<ToolResult> CreateCheckpointAsync(string label, string description);

    /// <summary>
    /// Rollback to checkpoint
    /// </summary>
    Task<ToolResult> RollbackAsync(string label);
}

/// <summary>
/// Agent runtime status
/// </summary>
public enum AgentStatus
{
    Idle,
    ExecutingTool,
    Verifying,
    PlanningTask,
    DelegatingTask,
    Error
}
