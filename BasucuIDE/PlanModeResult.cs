using System.Collections.Generic;

namespace mdaiAgent;

public sealed class PlanModeResult
{
    public bool Success { get; init; }
    public string FullPlanText { get; init; } = string.Empty;
    public List<PlanOption> Options { get; init; } = new();
    public List<ChatFlowMessage> Messages { get; init; } = new();
    public string ErrorMessage { get; init; } = string.Empty;
}
