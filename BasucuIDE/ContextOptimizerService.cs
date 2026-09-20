using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Text.Json;

namespace mdaiAgent
{
    public class ContextOptimizerService
    {
        /// <summary>
        /// Sıkıştırılmış terminal çıktısı üretir. Hata ve uyarı satırlarına öncelik verir.
        /// </summary>
        public string OptimizeTerminalOutput(string output, int maxTotalLines = 50, int linesAroundError = 5)
        {
            if (string.IsNullOrWhiteSpace(output))
                return output;

            var lines = output.Split(new[] { '\r', '\n' }, StringSplitOptions.None);
            if (lines.Length <= maxTotalLines)
                return output;

            var importantKeywords = new[] { "error", "exception", "fail", "fatal", "warning", "hata", "uyarı" };
            var importantLineIndices = new HashSet<int>();

            for (int i = 0; i < lines.Length; i++)
            {
                var lowerLine = lines[i].ToLowerInvariant();
                if (importantKeywords.Any(kw => lowerLine.Contains(kw)))
                {
                    // Hatanın etrafındaki satırları da ekle
                    for (int j = Math.Max(0, i - linesAroundError); j <= Math.Min(lines.Length - 1, i + linesAroundError); j++)
                    {
                        importantLineIndices.Add(j);
                    }
                }
            }

            // Başlangıç ve bitiş kısımlarını her zaman dahil et
            int headCount = 10;
            int tailCount = 10;
            for (int i = 0; i < Math.Min(headCount, lines.Length); i++) importantLineIndices.Add(i);
            for (int i = Math.Max(0, lines.Length - tailCount); i < lines.Length; i++) importantLineIndices.Add(i);

            var sortedIndices = importantLineIndices.OrderBy(x => x).ToList();
            if (sortedIndices.Count == 0)
            {
                return OptimizeGenericText(output, 2000);
            }

            var sb = new StringBuilder();
            int errorCount = lines.Count(line =>
                line.Contains("error", StringComparison.OrdinalIgnoreCase) ||
                line.Contains("exception", StringComparison.OrdinalIgnoreCase) ||
                line.Contains("fatal", StringComparison.OrdinalIgnoreCase) ||
                line.Contains("fail", StringComparison.OrdinalIgnoreCase) ||
                line.Contains("hata", StringComparison.OrdinalIgnoreCase));
            int warningCount = lines.Count(line =>
                line.Contains("warning", StringComparison.OrdinalIgnoreCase) ||
                line.Contains("uyarı", StringComparison.OrdinalIgnoreCase));
            sb.AppendLine($"[Terminal özeti: {errorCount} hata, {warningCount} uyarı; çıktı kısaltıldı.]");
            int lastIndex = -1;

            foreach (var index in sortedIndices)
            {
                if (lastIndex != -1 && index > lastIndex + 1)
                {
                    int skipped = index - lastIndex - 1;
                    sb.AppendLine($"\n... [{skipped} satır atlandı (optimizasyon)] ...\n");
                }
                sb.AppendLine(lines[index]);
                lastIndex = index;
            }

            return sb.ToString();
        }

        /// <summary>
        /// Uzun JSON dizilerini (örneğin API yanıtları) sadece başı ve sonu kalacak şekilde sıkıştırır.
        /// </summary>
        public string OptimizeJsonOutput(string json, int itemsToKeepAtEnds = 2)
        {
            if (string.IsNullOrWhiteSpace(json)) return json;

            try
            {
                using var doc = JsonDocument.Parse(json);
                if (doc.RootElement.ValueKind == JsonValueKind.Array)
                {
                    var arrayLength = doc.RootElement.GetArrayLength();
                    if (arrayLength > itemsToKeepAtEnds * 2)
                    {
                        var firstItems = doc.RootElement.EnumerateArray().Take(itemsToKeepAtEnds).Select(x => x.GetRawText());
                        var lastItems = doc.RootElement.EnumerateArray().Skip(arrayLength - itemsToKeepAtEnds).Take(itemsToKeepAtEnds).Select(x => x.GetRawText());

                        var sb = new StringBuilder();
                        sb.AppendLine("[");
                        sb.AppendLine(string.Join(",\n", firstItems));
                        sb.AppendLine($",\n  ... [{arrayLength - (itemsToKeepAtEnds * 2)} adet obje atlandı] ...\n,");
                        sb.AppendLine(string.Join(",\n", lastItems));
                        sb.AppendLine("]");
                        return sb.ToString();
                    }
                }
            }
            catch
            {
                // Geçerli bir JSON değilse veya dizi değilse normal string kısaltması yap.
            }

            return OptimizeGenericText(json, 4000);
        }

        /// <summary>
        /// Büyük metinleri başı ve sonu kalacak şekilde kırpar.
        /// </summary>
        public string OptimizeGenericText(string text, int maxLength = 4000)
        {
            if (string.IsNullOrEmpty(text) || text.Length <= maxLength)
                return text;

            int headLen = maxLength / 2;
            int tailLen = maxLength / 2;

            var head = text.Substring(0, headLen);
            var tail = text.Substring(text.Length - tailLen, tailLen);

            return $"{head}\n\n... [İçerik çok uzun olduğu için {text.Length - maxLength} karakter atlandı] ...\n\n{tail}";
        }
    }
}
