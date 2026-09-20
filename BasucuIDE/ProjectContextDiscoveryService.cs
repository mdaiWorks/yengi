using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Tasks;

namespace mdaiAgent;

/// <summary>
/// Agent'ın mesajlarından otomatik olarak ilgili dosyaları keşfeder ve intelligent context oluşturur.
/// "ToolExecutor'da bir bug var" → ilgili dosyaları bulur, kod parçaları extract eder
/// </summary>
public class ProjectContextDiscoveryService
{
    private readonly string _projectRoot;
    private readonly Action<string> _terminalLog;

    public ProjectContextDiscoveryService(string projectRoot, Action<string> terminalLog)
    {
        _projectRoot = projectRoot;
        _terminalLog = terminalLog;
    }

    /// <summary>
    /// Kullanıcı mesajını analiz ederek ilgili dosyaları keşfeder
    /// </summary>
    public async Task<DiscoveredContext> DiscoverContextFromMessageAsync(string userMessage)
    {
        var context = new DiscoveredContext();
        
        try
        {
            // 1. Mesajdan keywords extract et
            var keywords = ExtractKeywords(userMessage);
            context.ExtractedKeywords = keywords;

            var fileHints = ExtractFileHints(userMessage);
            context.PrioritizedFiles = fileHints;
            
            // 2. İlgili dosyaları bul
            var relevantFiles = await FindRelevantFilesAsync(keywords, fileHints);
            context.RelevantFiles = relevantFiles;
            
            // 3. Her dosyadan ilgili kod parçalarını extract et
            foreach (var file in relevantFiles)
            {
                var snippets = ExtractRelevantCodeSnippets(file, keywords);
                if (snippets.Count > 0)
                {
                    context.CodeSnippets[file] = snippets;
                }
            }
            
            // 4. Context markdown oluştur
            context.ContextMarkdown = BuildContextMarkdown(context);
            
            _terminalLog?.Invoke(
                LocalizationManager.Instance
                    .GetString("ContextDiscoveryKeywordsCountKeywordRelevantFilesCountDosyaContextCodeSnippetsSumXXValueCountSnippetKesfedildi")
                    .Replace("{keywords.Count}", keywords.Count.ToString())
                    .Replace("{relevantFiles.Count}", relevantFiles.Count.ToString())
                    .Replace("{context.CodeSnippets.Sum(x => x.Value.Count)}", context.CodeSnippets.Sum(x => x.Value.Count).ToString()));
        }
        catch (Exception ex)
        {
            _terminalLog?.Invoke(
                LocalizationManager.Instance
                    .GetString("ContextDiscoveryHatasiExMessage")
                    .Replace("{ex.Message}", ex.Message));
        }
        
        return context;
    }

    /// <summary>
    /// Mesajdan önemli keywords extract eder
    /// </summary>
    private List<string> ExtractKeywords(string message)
    {
        if (string.IsNullOrWhiteSpace(message))
            return new List<string>();

        var keywords = new List<string>();
        var textParts = Regex.Matches(message, @"[A-Za-z][A-Za-z0-9_./-]*|[A-Z][a-zA-Z0-9_]*")
            .Select(m => m.Value)
            .Where(p => p.Length > 2)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

        var codeKeywords = new[]
        {
            "class", "method", "function", "async", "await", "interface", "service", "test",
            "error", "bug", "fix", "implement", "refactor", "optimize", "execute", "handle",
            "timeout", "build", "verification", "context", "patch", "compile", "selector", "router"
        };

        foreach (var part in textParts)
        {
            var lower = part.ToLowerInvariant();
            var isCodeIdentifier = Regex.IsMatch(part, @"^(?:[A-Z][a-zA-Z0-9]*|[a-z]+[A-Z][A-Za-z0-9]*)$") ||
                                   Regex.IsMatch(part, @"^[A-Za-z]+(?:[A-Z][a-zA-Z0-9]*)+$");

            if (codeKeywords.Contains(lower) || isCodeIdentifier)
            {
                keywords.Add(part);
            }
        }

        return keywords.Distinct(StringComparer.OrdinalIgnoreCase).ToList();
    }

    private List<string> ExtractFileHints(string message)
    {
        if (string.IsNullOrWhiteSpace(message))
            return new List<string>();

        var extensions = "cs|csx|py|js|ts|tsx|jsx|dart|html|css|java|cpp|c|h|go|rs|txt|md";
        return Regex.Matches(message, $@"[A-Za-z0-9_./\\-]+\.({extensions})\b", RegexOptions.IgnoreCase)
            .Select(match => match.Value)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Select(path => Path.IsPathRooted(path) ? Path.GetFullPath(path) : Path.GetFullPath(Path.Combine(_projectRoot, path)))
            .Where(File.Exists)
            .ToList();
    }

    /// <summary>
    /// Keywords bazında ilgili dosyaları bulur
    /// </summary>
    private async Task<List<string>> FindRelevantFilesAsync(List<string> keywords, List<string> prioritizedFiles)
    {
        var relevantFiles = new List<(string file, int score)>();

        try
        {
            var excludedDirs = new[] { "\\bin\\", "\\obj\\", "\\.git\\", "\\.vs\\", "\\node_modules\\", "\\.mdai\\", "\\build\\", "\\publish\\" };
            var allowedExtensions = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
            {
                ".cs", ".js", ".ts", ".tsx", ".jsx", ".py", ".dart", ".html", ".css", ".java", ".cpp", ".c", ".h", ".go", ".rs", ".txt", ".md"
            };

            var allFiles = Directory.GetFiles(_projectRoot, "*.*", SearchOption.AllDirectories)
                .Where(f => !excludedDirs.Any(ed => f.Contains(ed, StringComparison.OrdinalIgnoreCase)))
                .Where(f => allowedExtensions.Contains(Path.GetExtension(f)))
                .ToList();

            foreach (var file in allFiles)
            {
                var fileName = Path.GetFileNameWithoutExtension(file);
                var fileNameLower = fileName.ToLowerInvariant();
                var fileContent = await File.ReadAllTextAsync(file);
                var contentLower = fileContent.ToLowerInvariant();

                int matchCount = 0;

                if (prioritizedFiles.Any(priority => Path.GetFullPath(priority).Equals(Path.GetFullPath(file), StringComparison.OrdinalIgnoreCase)))
                    matchCount += 100;

                if (prioritizedFiles.Any(priority => IsImplementationTestPair(priority, file)))
                    matchCount += 80;

                foreach (var keyword in keywords)
                {
                    var keywordLower = keyword.ToLowerInvariant();

                    if (fileNameLower.Contains(keywordLower))
                        matchCount += 10;

                    if (fileNameLower.Equals(keywordLower, StringComparison.OrdinalIgnoreCase) ||
                        fileNameLower.StartsWith(keywordLower + "tests", StringComparison.OrdinalIgnoreCase) ||
                        fileNameLower.Contains(keywordLower + "tests", StringComparison.OrdinalIgnoreCase))
                        matchCount += 15;

                    if (fileNameLower.EndsWith("tests", StringComparison.OrdinalIgnoreCase) &&
                        (fileNameLower.Contains(keywordLower) || keywordLower.Contains("test")))
                        matchCount += 12;

                    if (contentLower.Contains(keywordLower))
                        matchCount += 3;
                }

                if (fileNameLower.EndsWith("tests", StringComparison.OrdinalIgnoreCase))
                    matchCount += 8;

                if (matchCount > 0)
                {
                    relevantFiles.Add((file, matchCount));
                }
            }

            return relevantFiles
                .OrderByDescending(x => x.score)
                .Take(12)
                .Select(x => x.file)
                .ToList();
        }
        catch (Exception ex)
        {
            _terminalLog?.Invoke($"[File Discovery Hatası] {ex.Message}");
        }

        return new List<string>();
    }

    private static bool IsImplementationTestPair(string prioritizedFile, string candidateFile)
    {
        var prioritizedStem = NormalizeTestStem(Path.GetFileNameWithoutExtension(prioritizedFile));
        var candidateStem = NormalizeTestStem(Path.GetFileNameWithoutExtension(candidateFile));

        if (string.Equals(prioritizedStem, candidateStem, StringComparison.OrdinalIgnoreCase))
            return !string.Equals(prioritizedFile, candidateFile, StringComparison.OrdinalIgnoreCase);

        return IsTestStem(candidateFile) && string.Equals(prioritizedStem, candidateStem, StringComparison.OrdinalIgnoreCase);
    }

    private static string NormalizeTestStem(string fileName)
    {
        var normalized = fileName;
        normalized = Regex.Replace(normalized, "(?:tests?|_spec|-spec)$", "", RegexOptions.IgnoreCase);
        normalized = Regex.Replace(normalized, "^test[_-]", "", RegexOptions.IgnoreCase);
        return normalized;
    }

    private static bool IsTestStem(string filePath)
    {
        var fileName = Path.GetFileNameWithoutExtension(filePath);
        return Regex.IsMatch(fileName, "(?:tests?|_spec|-spec)$", RegexOptions.IgnoreCase) ||
               Regex.IsMatch(fileName, "^test[_-]", RegexOptions.IgnoreCase);
    }

    /// <summary>
    /// Dosyadan ilgili kod parçalarını extract eder
    /// </summary>
    private List<CodeSnippet> ExtractRelevantCodeSnippets(string filePath, List<string> keywords)
    {
        var snippets = new List<CodeSnippet>();
        
        try
        {
            var lines = File.ReadAllLines(filePath);
            
            for (int i = 0; i < lines.Length; i++)
            {
                var line = lines[i];
                var lineLower = line.ToLowerInvariant();
                
                // Satırda keyword varsa
                foreach (var keyword in keywords)
                {
                    if (lineLower.Contains(keyword.ToLowerInvariant()))
                    {
                        // Context ile birlikte snippet al (±3 satır)
                        int startLine = Math.Max(0, i - 3);
                        int endLine = Math.Min(lines.Length - 1, i + 3);
                        
                        var snippetLines = lines[startLine..(endLine + 1)];
                        var snippet = new CodeSnippet
                        {
                            StartLine = startLine + 1,
                            EndLine = endLine + 1,
                            Code = string.Join("\n", snippetLines),
                            MatchedKeyword = keyword
                        };
                        
                        snippets.Add(snippet);
                        i = endLine; // Atlanacak satırları atla
                        break;
                    }
                }
            }
        }
        catch (Exception ex)
        {
            _terminalLog?.Invoke($"[Snippet Extraction Hatası] {filePath}: {ex.Message}");
        }
        
        return snippets.DistinctBy(s => s.Code).ToList(); // Duplicate snippet'ları kaldır
    }

    /// <summary>
    /// Keşfedilen context'i markdown format'ına dönüştürür
    /// </summary>
    private string BuildContextMarkdown(DiscoveredContext context)
    {
        var sb = new StringBuilder();
        
        sb.AppendLine("# 🔍 Otomatik Keşfedilen Context");
        sb.AppendLine();
        
        if (context.ExtractedKeywords.Count > 0)
        {
            sb.AppendLine($"## Keywords Keşfedildi ({context.ExtractedKeywords.Count})");
            sb.AppendLine($"`{string.Join("`, `", context.ExtractedKeywords)}`");
            sb.AppendLine();
        }

        if (context.PrioritizedFiles.Count > 0)
        {
            sb.AppendLine("## Öncelikli Dosyalar");
            foreach (var file in context.PrioritizedFiles)
                sb.AppendLine($"- `{Path.GetFileName(file)}` (kullanıcı mesajında doğrudan belirtildi)");
            sb.AppendLine();
        }
        
        if (context.RelevantFiles.Count > 0)
        {
            sb.AppendLine($"## İlgili Dosyalar ({context.RelevantFiles.Count})");
            foreach (var file in context.RelevantFiles)
            {
                var fileName = Path.GetFileName(file);
                sb.AppendLine($"- `{fileName}`");
            }
            sb.AppendLine();
        }
        
        if (context.CodeSnippets.Count > 0)
        {
            sb.AppendLine("## Örnek Kod Parçaları");
            foreach (var snippetGroup in context.CodeSnippets.OrderBy(x => x.Key))
            {
                var fileName = Path.GetFileName(snippetGroup.Key);
                sb.AppendLine($"### {fileName}");

                foreach (var snippet in snippetGroup.Value.Take(2))
                {
                    sb.AppendLine($"- Eşleşen anahtar: `{snippet.MatchedKeyword}` (satır {snippet.StartLine}-{snippet.EndLine})");
                    sb.AppendLine("```csharp");
                    sb.AppendLine(snippet.Code.Trim());
                    sb.AppendLine("```");
                    sb.AppendLine();
                }
            }
        }

        var prioritizedTextFiles = context.PrioritizedFiles
            .Where(file => File.Exists(file) &&
                           (string.Equals(Path.GetExtension(file), ".txt", StringComparison.OrdinalIgnoreCase) ||
                            string.Equals(Path.GetExtension(file), ".md", StringComparison.OrdinalIgnoreCase)))
            .ToList();

        if (prioritizedTextFiles.Count > 0)
        {
            sb.AppendLine("## Doğrudan Belirtilen Metin Dosyaları");
            foreach (var file in prioritizedTextFiles)
            {
                sb.AppendLine($"### {Path.GetFileName(file)}");
                sb.AppendLine("```text");
                sb.AppendLine(File.ReadAllText(file).Trim());
                sb.AppendLine("```");
                sb.AppendLine();
            }
        }

        return sb.ToString();
    }
}

/// <summary>
/// Keşfedilen context'i tutacak model
/// </summary>
public class DiscoveredContext
{
    public List<string> ExtractedKeywords { get; set; } = new();
    public List<string> PrioritizedFiles { get; set; } = new();
    public List<string> RelevantFiles { get; set; } = new();
    public Dictionary<string, List<CodeSnippet>> CodeSnippets { get; set; } = new();
    public string ContextMarkdown { get; set; } = "";
}

/// <summary>
/// Kod parçası modeli
/// </summary>
public class CodeSnippet
{
    public int StartLine { get; set; }
    public int EndLine { get; set; }
    public string Code { get; set; } = "";
    public string MatchedKeyword { get; set; } = "";
}
