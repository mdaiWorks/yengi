using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace mdaiAgent;

public class PlanModeService : IPlanModeService
{
    private readonly AiPlanGenerator? _aiPlanGenerator;
    private readonly IChatFlowService _chatFlowService;

    public PlanModeService(IChatFlowService chatFlowService)
    {
        _aiPlanGenerator = null;
        _chatFlowService = chatFlowService ?? throw new ArgumentNullException(nameof(chatFlowService));
    }

    public PlanModeService(AiPlanGenerator aiPlanGenerator, IChatFlowService chatFlowService)
    {
        _aiPlanGenerator = aiPlanGenerator ?? throw new ArgumentNullException(nameof(aiPlanGenerator));
        _chatFlowService = chatFlowService ?? throw new ArgumentNullException(nameof(chatFlowService));
    }

    public async Task<PlanModeResult> CreatePlanAsync(
        string? currentFilePath,
        string? currentFileContent,
        string? selectedFolder,
        string systemPrompt,
        CancellationToken cancellationToken = default)
    {
        try
        {
            // If AiPlanGenerator is available, use it for isolated plan generation
            if (_aiPlanGenerator != null)
            {
                // Build context from UI state
                var contextBuilder = new System.Text.StringBuilder();
                contextBuilder.AppendLine("=== Mevcut Bağlam ===");
                
                if (!string.IsNullOrWhiteSpace(currentFilePath))
                    contextBuilder.AppendLine($"Açık Dosya: {currentFilePath}");
                
                if (!string.IsNullOrWhiteSpace(currentFileContent))
                    contextBuilder.AppendLine($"Dosya İçeriği (ilk 2000 karakter):\n{(currentFileContent.Length > 2000 ? currentFileContent.Substring(0, 2000) + "..." : currentFileContent)}");
                
                if (!string.IsNullOrWhiteSpace(selectedFolder))
                    contextBuilder.AppendLine($"Seçili Klasör: {selectedFolder}");
                
                var context = contextBuilder.ToString();
                var objective = "Mevcut proje için uygulanabilir bir geliştirme planı oluştur.";

                // Use AiPlanGenerator for isolated plan creation (not part of main conversation)
                var planContent = await _aiPlanGenerator.GeneratePlanAsync(objective, context, cancellationToken);
                
                // Parse plan options
                var options = PlanModeHelper.ParsePlanOptions(planContent);
                if (options.Count == 0)
                {
                    options.Add(new PlanOption
                    {
                        Id = "default",
                        Title = LocalizationManager.Instance.GetString("PlaniIncele"),
                        Description = planContent
                    });
                }

                // Build result with parsed options
                var messages = new System.Collections.Generic.List<ChatFlowMessage>
                {
                    new ChatFlowMessage { Sender = "AI Asistan", Content = planContent }
                };

                return new PlanModeResult
                {
                    Success = true,
                    FullPlanText = planContent,
                    Options = options,
                    Messages = messages
                };
            }
            else
            {
                // Fallback: use ChatFlowService if AiPlanGenerator unavailable
                var planPrompt = @"Lütfen mevcut proje için uygulanabilir bir geliştirme planı oluştur.
- Eksik özellikleri ve mimari kararları madde madde sıralayın.
- Her adımı kısa bir başlık ve ardından ne yapılması gerektiğini açıklayan bir metin ile verin.
- Önceliklendirme yapın ve her adım için kendi eşsiz kimliğini belirtin.
- Sunumu, plan seçenekleri olarak kullanılabilecek şekilde düzenleyin.";

                var result = await _chatFlowService.SendMessageAsync(planPrompt, currentFilePath, currentFileContent, selectedFolder, systemPrompt, false, false);
                if (!result.Success || result.Messages.Count == 0)
                {
                    return new PlanModeResult
                    {
                        Success = false,
                        ErrorMessage = result.ErrorMessage ?? "AI plan oluşturulamadı."
                    };
                }

                var fullPlan = string.Join("\n\n", result.Messages.Where(m => m.Sender == "AI Asistan").Select(m => m.Content));
                var options = PlanModeHelper.ParsePlanOptions(fullPlan);
                if (options.Count == 0)
                {
                    options.Add(new PlanOption
                    {
                        Id = "default",
                        Title = LocalizationManager.Instance.GetString("PlaniIncele"),
                        Description = fullPlan
                    });
                }

                return new PlanModeResult
                {
                    Success = true,
                    FullPlanText = fullPlan,
                    Options = options,
                    Messages = result.Messages
                };
            }
        }
        catch (Exception ex)
        {
            return new PlanModeResult
            {
                Success = false,
                ErrorMessage = $"Plan oluşturma hatası: {ex.Message}"
            };
        }
    }

    public async Task<PlanModeResult> CreateFollowUpAsync(
        PlanOption selectedOption,
        string? currentFilePath,
        string? currentFileContent,
        string? selectedFolder,
        string systemPrompt)
    {
        var followUpPrompt = $"Lütfen aşağıdaki plan adımını projeme uygula:\n\n{selectedOption.Title}\n\nGerekli dosyaları okuyup düzenle ve işlemi tamamla.";

        var result = await _chatFlowService.SendMessageAsync(followUpPrompt, currentFilePath, currentFileContent, selectedFolder, systemPrompt, false, false);
        if (!result.Success || result.Messages.Count == 0)
        {
            return new PlanModeResult
            {
                Success = false,
                ErrorMessage = result.ErrorMessage ?? "AI takip planı oluşturulamadı."
            };
        }

        var fullPlan = string.Join("\n\n", result.Messages.Where(m => m.Sender == "AI Asistan").Select(m => m.Content));
        return new PlanModeResult
        {
            Success = true,
            FullPlanText = fullPlan,
            Options = new System.Collections.Generic.List<PlanOption> { selectedOption },
            Messages = result.Messages
        };
    }
}
