using System.IO;

namespace mdaiAgent
{
    public class SearchResultItem
    {
        public string FilePath { get; set; } = "";
        public int LineNumber { get; set; }
        public string LineText { get; set; } = "";
        public string SearchQuery { get; set; } = "";
        public string? CustomMessage { get; set; }

        // Arayüzde görüntülenecek formatlı metin
        public string DisplayText => CustomMessage ?? $"{Path.GetFileName(FilePath)}:{LineNumber} - {LineText.Trim()}";
    }
}
