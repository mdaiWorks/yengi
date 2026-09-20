using System.Collections.Generic;

namespace mdaiAgent
{
    public class RouterContext
    {
        public List<ToolDefinition> AvailableTools { get; set; } = new();
        public string? SystemPrompt { get; set; }
        // Additional context can be added here (e.g., active file, selected folder)
    }
}
