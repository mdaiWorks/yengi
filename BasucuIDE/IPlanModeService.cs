using System.Threading;
using System.Threading.Tasks;

namespace mdaiAgent;

public interface IPlanModeService
{
    Task<PlanModeResult> CreatePlanAsync(
        string? currentFilePath,
        string? currentFileContent,
        string? selectedFolder,
        string systemPrompt,
        CancellationToken cancellationToken = default);

    Task<PlanModeResult> CreateFollowUpAsync(
        PlanOption selectedOption,
        string? currentFilePath,
        string? currentFileContent,
        string? selectedFolder,
        string systemPrompt);
}
