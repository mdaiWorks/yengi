using System;
using System.IO;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace mdaiAgent.Services;

/// <summary>
/// Handles all file-based operations: reading, writing, listing directories.
/// </summary>
public class FileOperationsService
{
    private readonly string? _projectFolder;
    private readonly Action<string> _terminalLog;
    private readonly Func<string, string, string, Task<bool>> _confirmFileChange;
    private readonly UiInvoker<bool>? _fileChangeUiInvoker;
    private readonly ContextOptimizerService _optimizer;
    private readonly ExternalPathAccessManager? _externalPathAccess;

    public FileOperationsService(
        string? projectFolder,
        Action<string> terminalLog,
        Func<string, string, string, Task<bool>> confirmFileChange,
        UiInvoker<bool>? fileChangeUiInvoker = null,
        ExternalPathAccessManager? externalPathAccess = null)
    {
        _projectFolder = projectFolder;
        _terminalLog = terminalLog;
        _confirmFileChange = confirmFileChange;
        _fileChangeUiInvoker = fileChangeUiInvoker;
        _optimizer = new ContextOptimizerService();
        _externalPathAccess = externalPathAccess;
    }

    public async Task<ToolResult> ReadFileAsync(JsonElement arguments, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var filePath = arguments.GetProperty("filePath").GetString()!;
        filePath = ResolvePath(filePath);
        EventBus.Publish(new TimelineEvent
        {
            Type = TimelineEventType.ReadingFile,
            Message = $"Dosya okunuyor: {Path.GetFileName(filePath)}"
        });

        int? startLine = arguments.TryGetProperty("startLine", out var startProp) && startProp.ValueKind == JsonValueKind.Number
            ? startProp.GetInt32() : null;
        int? endLine = arguments.TryGetProperty("endLine", out var endProp) && endProp.ValueKind == JsonValueKind.Number
            ? endProp.GetInt32() : null;

        if (!File.Exists(filePath))
            return new ToolResult { Success = false, Error = $"Dosya bulunamadı: {filePath}" };

        try
        {
            cancellationToken.ThrowIfCancellationRequested();
            string content = await File.ReadAllTextAsync(filePath, cancellationToken);
            if (startLine.HasValue && endLine.HasValue)
            {
                var lines = content.Split('\n');
                var start = Math.Max(0, startLine.Value - 1);
                var end = Math.Min(lines.Length, endLine.Value);
                content = string.Join('\n', lines[start..end]);
            }

            content = ShortenText(content);
            EventBus.Publish(new TimelineEvent
            {
                Type = TimelineEventType.ReadingFile,
                IsCompleted = true,
                Message = $"Dosya okundu: {Path.GetFileName(filePath)}"
            });
            return new ToolResult { Success = true, Output = content };
        }
        catch (OperationCanceledException) { throw; }
        catch (Exception ex)
        {
            EventBus.Publish(new TimelineEvent
            {
                Type = TimelineEventType.Failed,
                IsFailed = true,
                Message = $"Dosya okuma hatası: {Path.GetFileName(filePath)}"
            });
            return new ToolResult { Success = false, Error = $"Dosya okuma hatası: {ex.Message}" };
        }
    }

    public async Task<ToolResult> CreateOrUpdateFileAsync(JsonElement arguments, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var filePath = arguments.GetProperty("filePath").GetString()!;
        var content = arguments.GetProperty("content").GetString()!;
        filePath = ResolvePath(filePath);
        var isNewFile = !File.Exists(filePath);
        EventBus.Publish(new TimelineEvent
        {
            Type = isNewFile ? TimelineEventType.CreatingFile : TimelineEventType.EditingFile,
            Message = $"Dosya değiştiriliyor: {Path.GetFileName(filePath)}"
        });

        if (!IsPathAllowed(filePath))
            return new ToolResult { Success = false, Error = "Proje sınırı dışında dosya oluşturulamaz." };

        try
        {
            cancellationToken.ThrowIfCancellationRequested();
            var isNew = isNewFile;
            if (isNew)
            {
                var dir = Path.GetDirectoryName(filePath);
                if (dir != null)
                    Directory.CreateDirectory(dir);
            }

            var existing = isNew ? "" : await File.ReadAllTextAsync(filePath, cancellationToken);
            if (existing == content)
                return new ToolResult { Success = true, Output = $"Dosya zaten aynı içeriğe sahip: {filePath}" };

            string? backupPath = null;
            if (!isNew)
            {
                var backupDirectory = Path.Combine(_projectFolder ?? Path.GetDirectoryName(filePath) ?? ".", ".mdai", "backup");
                Directory.CreateDirectory(backupDirectory);
                backupPath = Path.Combine(
                    backupDirectory,
                    $"{Path.GetFileNameWithoutExtension(filePath)}_{DateTime.Now:yyyyMMdd_HHmmssfff}{Path.GetExtension(filePath)}");
                File.Copy(filePath, backupPath, overwrite: true);
            }

            bool confirmed = true;
            if (!isNew && _fileChangeUiInvoker != null)
            {
                confirmed = await _fileChangeUiInvoker(async () =>
                    await _confirmFileChange(filePath, existing, content)
                );
            }
            else if (!isNew)
            {
                confirmed = await _confirmFileChange(filePath, existing, content);
            }

            if (!confirmed)
                return new ToolResult { Success = false, Error = "Dosya değişimi kullanıcı tarafından reddedildi." };

            cancellationToken.ThrowIfCancellationRequested();
            await File.WriteAllTextAsync(filePath, content, cancellationToken);
            var operation = isNew ? "oluşturuldu" : "güncellendi";
            string logMsg = isNew
                ? LocalizationManager.Instance.GetString("DosyaOlusturuldu").Replace("{fileName}", Path.GetFileName(filePath))
                : LocalizationManager.Instance.GetString("DosyaGuncellendi").Replace("{fileName}", Path.GetFileName(filePath));
            _terminalLog(logMsg);
            EventBus.Publish(new TimelineEvent
            {
                Type = isNew ? TimelineEventType.CreatingFile : TimelineEventType.EditingFile,
                IsCompleted = true,
                Message = logMsg
            });
            return new ToolResult { Success = true, Output = $"SAVED_PATH:{filePath}\nDosya başarıyla {operation}." };
        }
        catch (OperationCanceledException) { throw; }
        catch (Exception ex)
        {
            EventBus.Publish(new TimelineEvent
            {
                Type = TimelineEventType.Failed,
                IsFailed = true,
                Message = $"Dosya yazma hatası: {Path.GetFileName(filePath)}"
            });
            return new ToolResult { Success = false, Error = $"Dosya yazma hatası: {ex.Message}" };
        }
    }

    public async Task<ToolResult> ReplaceFileContentAsync(JsonElement arguments, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var filePath = arguments.GetProperty("filePath").GetString()!;
        var oldContent = arguments.TryGetProperty("targetContent", out var targetProp) ? targetProp.GetString()! : arguments.GetProperty("oldContent").GetString()!;
        var newContent = arguments.TryGetProperty("replacementContent", out var replaceProp) ? replaceProp.GetString()! : arguments.GetProperty("newContent").GetString()!;
        filePath = ResolvePath(filePath);

        if (!File.Exists(filePath))
            return new ToolResult { Success = false, Error = $"Dosya bulunamadı: {filePath}" };

        if (string.IsNullOrWhiteSpace(oldContent))
            return new ToolResult
            {
                Success = false,
                Error = "Eski içerik boş/whitespace olamaz. Lütfen dosyadan tam olarak kopyalanmış, boş olmayan bir hedef metin belirtin."
            };

        try
        {
            cancellationToken.ThrowIfCancellationRequested();
            string fileContent = await File.ReadAllTextAsync(filePath, cancellationToken);
            
            int matchCount = (fileContent.Length - fileContent.Replace(oldContent, "").Length) / oldContent.Length;
            
            if (matchCount == 0)
                return new ToolResult
                {
                    Success = false,
                    Error = "PATCH_CONFLICT: Hedef içerik dosyada bulunamadı; dosya değişmiş olabilir. Önce ReadFile ile dosyanın güncel içeriğini okuyup patch'i güncel bağlama göre yeniden oluşturun."
                };
                
            if (matchCount > 1)
                return new ToolResult { Success = false, Error = $"Hedef içerikten {matchCount} adet eşleşme bulundu. Lütfen daha spesifik bir hedef içerik belirterek tekrar deneyin." };

            string updatedContent = fileContent.Replace(oldContent, newContent);

            bool confirmed = true;
            if (_fileChangeUiInvoker != null)
            {
                confirmed = await _fileChangeUiInvoker(async () =>
                    await _confirmFileChange(filePath, fileContent, updatedContent)
                );
            }
            else
            {
                confirmed = await _confirmFileChange(filePath, fileContent, updatedContent);
            }

            if (!confirmed)
                return new ToolResult { Success = false, Error = "Dosya değişimi reddedildi." };

            cancellationToken.ThrowIfCancellationRequested();
            await File.WriteAllTextAsync(filePath, updatedContent, cancellationToken);
            _terminalLog(LocalizationManager.Instance.GetString("DosyaGuncellendi").Replace("{fileName}", Path.GetFileName(filePath)));
            return new ToolResult { Success = true, Output = $"SAVED_PATH:{filePath}\nDosya başarıyla güncellendi." };
        }
        catch (OperationCanceledException) { throw; }
        catch (Exception ex)
        {
            return new ToolResult { Success = false, Error = $"Değiştirme hatası: {ex.Message}" };
        }
    }

    public async Task<ToolResult> ListDirectoryAsync(JsonElement arguments, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var directoryPath = arguments.TryGetProperty("path", out var pathProp) 
            ? pathProp.GetString() 
            : arguments.TryGetProperty("directoryPath", out var dirPathProp) ? dirPathProp.GetString() : null;

        if (string.IsNullOrWhiteSpace(directoryPath))
        {
            return new ToolResult { Success = false, Error = "path parametresi zorunlu." };
        }

        directoryPath = ResolvePath(directoryPath);

        if (!Directory.Exists(directoryPath))
            return new ToolResult { Success = false, Error = $"Dizin bulunamadı: {directoryPath}" };

        try
        {
            cancellationToken.ThrowIfCancellationRequested();
            var entries = Directory.EnumerateFileSystemEntries(directoryPath);
            var output = string.Join("\n", entries);
            output = _optimizer.OptimizeTerminalOutput(output);
            return new ToolResult { Success = true, Output = output };
        }
        catch (OperationCanceledException) { throw; }
        catch (Exception ex)
        {
            return new ToolResult { Success = false, Error = $"Dizin listeleme hatası: {ex.Message}" };
        }
    }

    private string ResolvePath(string path)
    {
        var candidatePath = Path.IsPathRooted(path)
            ? path
            : _projectFolder != null
                ? Path.Combine(_projectFolder, path)
                : path;

        var fullPath = Path.GetFullPath(candidatePath);
        if (!IsPathAllowed(fullPath))
            throw new UnauthorizedAccessException("Proje sınırı dışında dosya erişimine izin verilmiyor.");

        return fullPath;
    }

    private bool IsPathAllowed(string path)
    {
        return _externalPathAccess?.IsAllowed(path) ??
            (_projectFolder == null || IsWithinProjectBoundary(path, _projectFolder));
    }

    private static bool IsWithinProjectBoundary(string candidatePath, string projectRoot)
    {
        var normalizedCandidate = Path.GetFullPath(candidatePath)
            .TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        var normalizedRoot = Path.GetFullPath(projectRoot)
            .TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);

        if (normalizedCandidate.Equals(normalizedRoot, StringComparison.OrdinalIgnoreCase))
            return true;

        var rootWithSeparator = normalizedRoot + Path.DirectorySeparatorChar;
        return normalizedCandidate.StartsWith(rootWithSeparator, StringComparison.OrdinalIgnoreCase);
    }

    private static bool IsSymlink(string path)
    {
        try
        {
            var fileInfo = new FileInfo(path);
            return fileInfo.LinkTarget != null;
        }
        catch
        {
            return false;
        }
    }

    private static string ShortenText(string text, int maxLength = 40000)
    {
        if (text.Length <= maxLength)
            return text;

        var truncated = text[..maxLength];
        return truncated + $"\n\n... ({text.Length - maxLength} karakter kesildi)";
    }
}
