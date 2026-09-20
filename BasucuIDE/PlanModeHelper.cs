using System.Collections.Generic;
using System.Text.RegularExpressions;

namespace mdaiAgent;

public static class PlanModeHelper
{
    public static List<PlanOption> ParsePlanOptions(string planText)
    {
        var options = new List<PlanOption>();
        var lines = planText.Split(new[] { '\r', '\n' }, System.StringSplitOptions.None);
        PlanOption? current = null;
        var itemHeaderPattern = new Regex(@"^(?:\*+)?\s*(\d+)[\.)]\s*(.+)$", RegexOptions.Compiled);
        var separatorPattern = new Regex(@"^[\-_*]{3,}$", RegexOptions.Compiled);

        foreach (var line in lines)
        {
            var trimmed = line.Trim();
            if (string.IsNullOrEmpty(trimmed) || separatorPattern.IsMatch(trimmed))
            {
                continue;
            }

            trimmed = trimmed.Trim('*', '_', '~').Trim();
            var headerMatch = itemHeaderPattern.Match(trimmed);
            if (headerMatch.Success)
            {
                if (current != null)
                {
                    options.Add(current);
                }

                current = new PlanOption
                {
                    Id = headerMatch.Groups[1].Value,
                    Title = headerMatch.Groups[2].Value.Trim(),
                    Description = string.Empty
                };
                continue;
            }

            if (current != null)
            {
                if (string.IsNullOrEmpty(current.Description))
                {
                    current.Description = trimmed;
                }
                else
                {
                    current.Description += "\n" + trimmed;
                }
            }
        }

        if (current != null)
        {
            options.Add(current);
        }
        else if (!string.IsNullOrWhiteSpace(planText))
        {
            var full = planText.Trim();
            options.Add(new PlanOption
            {
                Id = "1",
                Title = full,
                Description = string.Empty
            });
        }

        return options;
    }

    public static IReadOnlyList<TodoItemViewModel> ParseTodoItemsFromPlanText(string planText)
    {
        var items = new List<TodoItemViewModel>();
        if (string.IsNullOrWhiteSpace(planText)) return items;

        // Eger plan JSON olarak geldiyse (ornegin {"plan": "..."} icindeyse), onu cikar
        try
        {
            var jsonDoc = System.Text.Json.JsonDocument.Parse(planText);
            if (jsonDoc.RootElement.TryGetProperty("plan", out var planProp))
            {
                planText = planProp.GetString() ?? planText;
            }
        }
        catch { /* JSON degilse sorun yok, normal metindir */ }

        var lines = planText.Split(new[] { '\r', '\n' }, System.StringSplitOptions.None);
        // Sadece numaralı listeleri (1. veya ## 1. gibi) task olarak algıla
        var stepPattern = new Regex(@"^(?:\s*#+\s*)?(?:\d+[\.\)])\s+(.+)$", RegexOptions.Compiled);

        foreach (var rawLine in lines)
        {
            var line = rawLine.Trim();
            if (string.IsNullOrEmpty(line)) continue;

            var match = stepPattern.Match(line);
            if (match.Success)
            {
                var desc = match.Groups[1].Value.Trim();
                if (!string.IsNullOrEmpty(desc))
                {
                    // Çok uzun satırları keselim ki UI bozulmasın
                    if (desc.Length > 200) desc = desc.Substring(0, 197) + "...";
                    items.Add(new TodoItemViewModel { Description = desc });
                }
            }
        }

        if (items.Count == 0 && !string.IsNullOrWhiteSpace(planText))
        {
            var desc = planText.Trim();
            if (desc.Length > 200) desc = desc.Substring(0, 197) + "...";
            items.Add(new TodoItemViewModel { Description = desc });
        }

        return items;
    }
}
