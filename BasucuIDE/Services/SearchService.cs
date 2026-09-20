using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace mdaiAgent.Services;

/// <summary>
/// Handles file searching and code search operations.
/// </summary>
public class SearchService
{
    private readonly string? _projectFolder;
    private readonly Action<string> _terminalLog;
    private readonly ContextOptimizerService _optimizer;
    private readonly ExternalPathAccessManager? _externalPathAccess;

    public SearchService(
        string? projectFolder,
        Action<string> terminalLog,
        ExternalPathAccessManager? externalPathAccess = null)
    {
        _projectFolder = projectFolder;
        _terminalLog = terminalLog;
        _optimizer = new ContextOptimizerService();
        _externalPathAccess = externalPathAccess;
    }

    public Task<ToolResult> FindFilesAsync(JsonElement arguments, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var pattern = arguments.GetProperty("pattern").GetString()!;
        var searchRoot = arguments.TryGetProperty("rootPath", out var rootProp)
            ? rootProp.GetString()
            : arguments.TryGetProperty("searchRoot", out var legacyRootProp)
                ? legacyRootProp.GetString()
                : null;

        searchRoot = ResolvePath(searchRoot ?? ".") ?? _projectFolder ?? ".";
        EventBus.Publish(new TimelineEvent
        {
            Type = TimelineEventType.Info,
            Message = $"Dosyalar aranıyor: {pattern}"
        });

        try
        {
            cancellationToken.ThrowIfCancellationRequested();
            var excludedDirs = new[] { ".git", "node_modules", "bin", "obj", ".mdai", ".vs", "publish" };

            var files = new List<string>();
            WalkDirectory(searchRoot, pattern, excludedDirs, files, null, cancellationToken);

            if (files.Count == 0)
                return Task.FromResult(new ToolResult { Success = true, Output = $"'{pattern}' deseni için dosya bulunamadı." });

            var output = string.Join("\n", files.Take(200));
            if (files.Count > 200)
                output += $"\n\n... ve {files.Count - 200} dosya daha";

            output = _optimizer.OptimizeTerminalOutput(output);
            EventBus.Publish(new TimelineEvent
            {
                Type = TimelineEventType.Info,
                IsCompleted = true,
                Message = $"Dosya araması tamamlandı: {pattern}"
            });
            return Task.FromResult(new ToolResult { Success = true, Output = output });
        }
        catch (OperationCanceledException)
        {
            EventBus.Publish(new TimelineEvent { Type = TimelineEventType.Failed, IsFailed = true, Message = "Dosya araması iptal edildi." });
            return Task.FromResult(new ToolResult { Success = false, Error = "Dosya araması kullanıcı tarafından iptal edildi." });
        }
        catch (Exception ex)
        {
            EventBus.Publish(new TimelineEvent { Type = TimelineEventType.Failed, IsFailed = true, Message = "Dosya araması başarısız oldu." });
            return Task.FromResult(new ToolResult { Success = false, Error = $"Dosya arama hatası: {ex.Message}" });
        }
    }

    public async Task<ToolResult> SearchCodeAsync(JsonElement arguments, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var searchTerm = arguments.TryGetProperty("query", out var queryProp)
            ? queryProp.GetString()
            : arguments.GetProperty("searchTerm").GetString();
        var searchRoot = arguments.TryGetProperty("rootPath", out var rootProp)
            ? rootProp.GetString()
            : arguments.TryGetProperty("searchRoot", out var legacyRootProp)
                ? legacyRootProp.GetString()
                : null;

        searchRoot = ResolvePath(searchRoot ?? ".") ?? _projectFolder ?? ".";
        EventBus.Publish(new TimelineEvent
        {
            Type = TimelineEventType.Info,
            Message = $"Kod aranıyor: {searchTerm}"
        });

        try
        {
            var results = new List<string>();
            var excludedDirs = new[] { ".git", "node_modules", "bin", "obj", ".mdai", ".vs", "publish" };
            var codeExtensions = new[] { ".cs", ".ts", ".js", ".py", ".cpp", ".c", ".h", ".xaml" };

            await Task.Run(() =>
            {
                WalkDirectory(searchRoot, "*", excludedDirs, new List<string>(), file =>
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    if (!codeExtensions.Contains(Path.GetExtension(file).ToLower()))
                        return;

                    try
                    {
                        var content = File.ReadAllText(file);
                        if (content.Contains(searchTerm, StringComparison.OrdinalIgnoreCase))
                        {
                            var lines = content.Split('\n');
                            var matches = new List<int>();

                            for (int i = 0; i < lines.Length; i++)
                            {
                                if (lines[i].Contains(searchTerm, StringComparison.OrdinalIgnoreCase))
                                    matches.Add(i + 1);
                            }

                            if (matches.Count > 0)
                            {
                                var relativePath = Path.GetRelativePath(searchRoot, file);
                                var firstMatch = lines[matches[0] - 1].Trim();
                                results.Add($"{relativePath}: {string.Join(", ", matches.Take(3))} - {firstMatch}");
                            }
                        }
                    }
                    catch { }
                }, cancellationToken);
            }, cancellationToken);

            cancellationToken.ThrowIfCancellationRequested();
            if (results.Count == 0)
                return new ToolResult { Success = true, Output = $"'{searchTerm}' için kod bulunamadı." };

            var output = string.Join("\n", results.Take(100));
            if (results.Count > 100)
                output += $"\n\n... ve {results.Count - 100} eşleşme daha";

            output = _optimizer.OptimizeTerminalOutput(output);
            EventBus.Publish(new TimelineEvent
            {
                Type = TimelineEventType.Info,
                IsCompleted = true,
                Message = $"Kod araması tamamlandı: {searchTerm}"
            });
            return new ToolResult { Success = true, Output = output };
        }
        catch (OperationCanceledException)
        {
            EventBus.Publish(new TimelineEvent { Type = TimelineEventType.Failed, IsFailed = true, Message = "Kod araması iptal edildi." });
            return new ToolResult { Success = false, Error = "Kod araması kullanıcı tarafından iptal edildi." };
        }
        catch (Exception ex)
        {
            EventBus.Publish(new TimelineEvent { Type = TimelineEventType.Failed, IsFailed = true, Message = "Kod araması başarısız oldu." });
            return new ToolResult { Success = false, Error = $"Kod arama hatası: {ex.Message}" };
        }
    }

    private void WalkDirectory(string path, string pattern, string[] excludedDirs, List<string> results, Action<string>? fileProcessor = null, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        try
        {
            var dir = new DirectoryInfo(path);

            foreach (var subdir in dir.GetDirectories())
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (excludedDirs.Contains(subdir.Name, StringComparer.OrdinalIgnoreCase))
                    continue;

                WalkDirectory(subdir.FullName, pattern, excludedDirs, results, fileProcessor, cancellationToken);
            }

            foreach (var file in dir.GetFiles(pattern))
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (fileProcessor != null)
                    fileProcessor(file.FullName);
                else
                    results.Add(file.FullName);
            }
        }
        catch { }
    }

    private string? ResolvePath(string path)
    {
        if (string.IsNullOrEmpty(path))
            return null;

        if (Path.IsPathRooted(path))
        {
            if (!IsPathAllowed(path))
                return null;
            return path;
        }

        var resolved = _projectFolder != null 
            ? Path.Combine(_projectFolder, path) 
            : Path.GetFullPath(path);

        if (!IsPathAllowed(resolved))
            return null;

        return resolved;
    }

    private bool IsPathAllowed(string path)
    {
        return _externalPathAccess?.IsAllowed(path) ??
            (_projectFolder == null || IsWithinProjectBoundary(path, _projectFolder));
    }

    private static bool IsWithinProjectBoundary(string candidatePath, string projectRoot)
    {
        var normalized = Path.GetFullPath(candidatePath);
        var normalRoot = Path.GetFullPath(projectRoot);
        
        if (normalized.Equals(normalRoot, StringComparison.OrdinalIgnoreCase))
            return true;

        try 
        {
            var relative = Path.GetRelativePath(normalRoot, normalized);
            return !relative.StartsWith("..") && !Path.IsPathRooted(relative);
        }
        catch { return false; }
    }
}
