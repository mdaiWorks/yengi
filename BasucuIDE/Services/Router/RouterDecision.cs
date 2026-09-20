using System.Collections.Generic;

namespace mdaiAgent
{
    public class RouterDecision
    {
        public string? ModelCategory { get; set; }
        public List<string> Tools { get; set; } = new();
        public bool NeedsPlanning { get; set; }
        public double Confidence { get; set; }
        public string? ErrorMessage { get; set; }
    }
}
