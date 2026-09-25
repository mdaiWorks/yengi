using System;
using System.Diagnostics;
using System.IO;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace mdaiAgent.Services;

/// <summary>
/// Handles build, test, and terminal command execution.
/// </summary>
public class BuildService
{
    private readonly string? _projectFolder;
    private readonly Action<string> _terminalLog;
    private readonly Action<string>? _updateOperationStep;
    private readonly ContextOptimizerService _optimizer;
    private readonly ExternalPathAccessManager? _externalPathAccess;
    private readonly ToolOutputStore _outputStore;

    public BuildService(
        string? projectFolder,
        Action<string> terminalLog,
        Action<string>? updateOperationStep = null,
        ExternalPathAccessManager? externalPathAccess = null,
        ToolOutputStore? outputStore = null)
    {
        _projectFolder = projectFolder;
        _terminalLog = terminalLog;
        _updateOperationStep = updateOperationStep;
        _optimizer = new ContextOptimizerService();
        _externalPathAccess = externalPathAccess;
        _outputStore = outputStore ?? new ToolOutputStore();
    }

    public async Task<ToolResult> BuildProjectAsync(JsonElement arguments, CancellationToken cancellationToken = default)
    {
        var projectPath = arguments.GetProperty("projectPath").GetString()!;
        var resolvedPath = ResolvePath(projectPath);

        _updateOperationStep?.Invoke("🔨 Proje derleniyor...");
        _terminalLog(Localization.Get("Proje derleme başlatıldı...", "Project build started..."));

        try
        {
            var workDir = System.IO.Path.GetDirectoryName(resolvedPath) ?? ".";
            var projectFile = System.IO.Path.GetFileName(resolvedPath);
            var result = await RunProcessAsync("dotnet", $"build \"{projectFile}\"", workDir, cancellationToken);

            EventBus.Publish(new TimelineEvent
            {
                Type = TimelineEventType.Building,
                IsCompleted = result.Success,
                IsFailed = !result.Success,
                Message = result.Success ? "Proje başarıyla derlendi!" : "Proje derlenirken hata oluştu!"
            });

            return CreateProcessResult(result, "build");
        }
        catch (Exception ex)
        {
            return new ToolResult { Success = false, Error = $"Derleme hatası: {ex.Message}" };
        }
    }

    public async Task<ToolResult> RunTestsAsync(JsonElement arguments, CancellationToken cancellationToken = default)
    {
        var projectPath = arguments.GetProperty("projectPath").GetString()!;
        var resolvedPath = ResolvePath(projectPath);

        // Validate project file exists
        if (!File.Exists(resolvedPath))
        {
            return new ToolResult { Success = false, Error = $"Dosya bulunamadı: {resolvedPath}" };
        }

        _updateOperationStep?.Invoke("🧪 Testler çalıştırılıyor...");
        _terminalLog(Localization.Get("Test çalıştırması başlatıldı...", "Test run started..."));

        try
        {
            var workDir = System.IO.Path.GetDirectoryName(resolvedPath) ?? ".";
            var projectFile = System.IO.Path.GetFileName(resolvedPath);
            var result = await RunProcessAsync("dotnet", $"test \"{projectFile}\" --logger \"console;verbosity=minimal\"", workDir, cancellationToken);

            EventBus.Publish(new TimelineEvent
            {
                Type = TimelineEventType.Testing,
                IsCompleted = result.Success,
                IsFailed = !result.Success,
                Message = result.Success ? "Testler başarıyla tamamlandı!" : "Test çalıştırması başarısız oldu!"
            });

            return CreateProcessResult(result, "test");
        }
        catch (Exception ex)
        {
            return new ToolResult { Success = false, Error = $"Test hatası: {ex.Message}" };
        }
    }

    public async Task<ToolResult> ExecuteTerminalCommandAsync(JsonElement arguments, CancellationToken cancellationToken = default)
    {
        var command = arguments.GetProperty("command").GetString()!;
        var workingDirectory = arguments.TryGetProperty("workingDirectory", out var dirProp)
            ? dirProp.GetString()
            : null;
        var timeoutSeconds = arguments.TryGetProperty("timeoutSeconds", out var timeoutProp) && timeoutProp.ValueKind == JsonValueKind.Number
            ? timeoutProp.GetInt32()
            : 30; // Default to 30s to prevent infinite hangs on servers like http-server/npm start

        var resolvedWorkDir = ResolvePath(workingDirectory ?? (_projectFolder ?? "."));

        _updateOperationStep?.Invoke($"⚙️ Komut çalıştırılıyor: {command}");
        _terminalLog(LocalizationManager.Instance.GetString("TerminalKomutu").Replace("{cmd}", command));

        try
        {
            using var timeoutCts = timeoutSeconds > 0 ? CancellationTokenSource.CreateLinkedTokenSource(cancellationToken) : null;
            if (timeoutCts != null)
            {
                timeoutCts.CancelAfter(TimeSpan.FromSeconds(timeoutSeconds));
            }

            var result = await RunProcessAsync("powershell", $"-Command \"{command}\"", resolvedWorkDir, timeoutCts?.Token ?? cancellationToken);

            if (!result.Success && string.IsNullOrWhiteSpace(result.Error) && timeoutSeconds > 0 && timeoutCts?.IsCancellationRequested == true)
            {
                result = (false, result.Output, $"Komut zaman aşıldı ({timeoutSeconds} saniye). Arka plan sunucu komutları (npx http-server) yerine 'start index.html' kullanın.");
            }

            return CreateProcessResult(result, "terminal");
        }
        catch (Exception ex)
        {
            return new ToolResult { Success = false, Error = $"Terminal komutu hatası: {ex.Message}" };
        }
    }

    private ToolResult CreateProcessResult((bool Success, string Output, string? Error) result, string label)
    {
        var optimizedOutput = _optimizer.OptimizeTerminalOutput(result.Output);
        var optimizedError = _optimizer.OptimizeTerminalOutput(result.Error ?? "");
        var outputId = _outputStore.SaveIfTruncated(result.Output, optimizedOutput, label);
        var errorId = _outputStore.SaveIfTruncated(result.Error ?? "", optimizedError, $"{label}-error");

        return new ToolResult
        {
            Success = result.Success,
            Output = AppendOutputReference(optimizedOutput, outputId),
            Error = AppendOutputReference(optimizedError, errorId)
        };
    }

    private static string AppendOutputReference(string output, string? outputId)
    {
        return string.IsNullOrWhiteSpace(outputId)
            ? output
            : $"{output}\n\n[FULL_OUTPUT_ID:{outputId}] Use ReadToolOutput with this ID for more lines.";
    }

    private async Task<(bool Success, string Output, string? Error)> RunProcessAsync(
        string fileName,
        string arguments,
        string workingDirectory,
        CancellationToken cancellationToken = default)
    {
        try
        {
            var psi = new ProcessStartInfo
            {
                FileName = fileName,
                Arguments = arguments,
                WorkingDirectory = workingDirectory,
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                CreateNoWindow = true
            };

            using var process = Process.Start(psi);
            if (process == null)
                return (false, "", "Process başlatılamadı");

            var outputTask = process.StandardOutput.ReadToEndAsync();
            var errorTask = process.StandardError.ReadToEndAsync();

            try
            {
                await Task.WhenAll(
                    process.WaitForExitAsync(cancellationToken),
                    outputTask,
                    errorTask
                );
            }
            catch (OperationCanceledException)
            {
                try
                {
                    if (!process.HasExited)
                    {
                        process.Kill(entireProcessTree: true);
                    }
                }
                catch
                {
                    // best-effort; child may already be gone
                }

                string timedOutOutput = "";
                string timedOutError = "";
                try
                {
                    var readTasks = Task.WhenAll(outputTask, errorTask);
                    if (await Task.WhenAny(readTasks, Task.Delay(300)) == readTasks)
                    {
                        timedOutOutput = await outputTask;
                        timedOutError = await errorTask;
                    }
                }
                catch { }

                return (false, timedOutOutput, $"Komut zaman aşıldı veya iptal edildi. {timedOutError}");
            }

            var finalOutput = await outputTask;
            var finalError = await errorTask;

            return (process.ExitCode == 0, finalOutput, finalError);
        }
        catch (Exception ex)
        {
            return (false, "", ex.Message);
        }
    }

    private string ResolvePath(string path)
    {
        if (string.IsNullOrEmpty(path))
            return _projectFolder ?? ".";

        if (System.IO.Path.IsPathRooted(path))
        {
            if (!IsPathAllowed(path))
                throw new UnauthorizedAccessException($"Path outside project boundary: {path}");
            return path;
        }

        var resolved = _projectFolder != null 
            ? System.IO.Path.Combine(_projectFolder, path) 
            : System.IO.Path.GetFullPath(path);

        if (!IsPathAllowed(resolved))
            throw new UnauthorizedAccessException($"Path outside project boundary: {resolved}");

        return resolved;
    }

    private bool IsPathAllowed(string path)
    {
        return _externalPathAccess?.IsAllowed(path) ??
            (_projectFolder == null || IsWithinProjectBoundary(path, _projectFolder));
    }

    private static bool IsWithinProjectBoundary(string candidatePath, string projectRoot)
    {
        var normalized = System.IO.Path.GetFullPath(candidatePath);
        var normalRoot = System.IO.Path.GetFullPath(projectRoot);
        
        if (normalized.Equals(normalRoot, StringComparison.OrdinalIgnoreCase))
            return true;

        try 
        {
            var relative = System.IO.Path.GetRelativePath(normalRoot, normalized);
            return !relative.StartsWith("..") && !System.IO.Path.IsPathRooted(relative);
        }
        catch { return false; }
    }
}
