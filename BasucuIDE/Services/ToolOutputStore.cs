using System;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace mdaiAgent.Services;

public sealed class ToolOutputStore
{
    private const int MaxStoredFiles = 50;
    private static readonly TimeSpan Retention = TimeSpan.FromHours(24);
    private readonly string _rootDirectory;

    public ToolOutputStore()
    {
        _rootDirectory = Path.Combine(Path.GetTempPath(), "Yengi", "tool-output");
        Directory.CreateDirectory(_rootDirectory);
        CleanupExpiredFiles();
    }

    public string? SaveIfTruncated(string fullOutput, string returnedOutput, string label)
    {
        if (string.IsNullOrEmpty(fullOutput) || string.Equals(fullOutput, returnedOutput, StringComparison.Ordinal))
            return null;

        var id = $"tool-{DateTime.UtcNow:yyyyMMddHHmmss}-{Guid.NewGuid():N}";
        var safeLabel = string.Concat(label.Where(char.IsLetterOrDigit));
        var path = Path.Combine(_rootDirectory, $"{id}-{safeLabel}.txt");
        File.WriteAllText(path, fullOutput, Encoding.UTF8);
        TrimFileCount();
        return id;
    }

    public async Task<ToolResult> ReadAsync(string outputId, int? startLine = null, int? endLine = null)
    {
        if (string.IsNullOrWhiteSpace(outputId) || outputId.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0)
            return new ToolResult { Success = false, Error = "Geçersiz çıktı kimliği." };

        var path = Directory.EnumerateFiles(_rootDirectory, $"{outputId}-*.txt").SingleOrDefault();
        if (path == null || !File.Exists(path))
            return new ToolResult { Success = false, Error = "Geçici araç çıktısı bulunamadı veya süresi dolmuş." };

        var lines = await File.ReadAllLinesAsync(path);
        var start = Math.Max(0, (startLine ?? 1) - 1);
        var end = Math.Min(lines.Length, endLine ?? Math.Min(lines.Length, start + 200));
        if (start >= end)
            return new ToolResult { Success = true, Output = "İstenen çıktı aralığı boş." };

        return new ToolResult
        {
            Success = true,
            Output = string.Join(Environment.NewLine, lines[start..end])
        };
    }

    private void CleanupExpiredFiles()
    {
        foreach (var file in Directory.EnumerateFiles(_rootDirectory, "tool-*.txt"))
        {
            try
            {
                if (DateTime.UtcNow - File.GetLastWriteTimeUtc(file) > Retention)
                    File.Delete(file);
            }
            catch
            {
                // Best-effort cleanup only.
            }
        }
    }

    private void TrimFileCount()
    {
        var files = Directory.EnumerateFiles(_rootDirectory, "tool-*.txt")
            .OrderByDescending(File.GetLastWriteTimeUtc)
            .Skip(MaxStoredFiles)
            .ToList();

        foreach (var file in files)
        {
            try { File.Delete(file); } catch { }
        }
    }
}
