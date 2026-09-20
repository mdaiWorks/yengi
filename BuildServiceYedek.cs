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

    public BuildService(
        string? projectFolder,
        Action<string> terminalLog,
        Action<string>? updateOperationStep = null)
    {
        _projectFolder = projectFolder;
        _terminalLog = terminalLog;
        _updateOperationStep = updateOperationStep;
        _optimizer = new ContextOptimizerService();
    }

    public async Task<ToolResult> BuildProjectAsync(JsonElement arguments)
    {
        var projectPath = arguments.GetProperty("projectPath").GetString()!;
        var resolvedPath = ResolvePath(projectPath);

        _updateOperationStep?.Invoke("🔨 Proje derleniyor...");
        _terminalLog("Proje derleme başlatıldı...");

        try
        {
            var workDir = System.IO.Path.GetDirectoryName(resolvedPath) ?? ".";
            var projectFile = System.IO.Path.GetFileName(resolvedPath);
            var result = await RunProcessAsync("dotnet", $"build \"{projectFile}\"", workDir);

            EventBus.Publish(new TimelineEvent
            {
                Type = TimelineEventType.Building,
                IsCompleted = result.Success,
                IsFailed = !result.Success,
                Message = result.Success ? "Proje başarıyla derlendi!" : "Proje derlenirken hata oluştu!"
            });

            return new ToolResult
            {
                Success = result.Success,
                Output = _optimizer.OptimizeTerminalOutput(result.Output),
                Error = _optimizer.OptimizeTerminalOutput(result.Error ?? "")
            };
        }
        catch (Exception ex)
        {
            return new ToolResult { Success = false, Error = $"Derleme hatası: {ex.Message}" };
        }
    }

    public async Task<ToolResult> RunTestsAsync(JsonElement arguments)
    {
        var projectPath = arguments.GetProperty("projectPath").GetString()!;
        var resolvedPath = ResolvePath(projectPath);

        // Validate project file exists
        if (!File.Exists(resolvedPath))
        {
            return new ToolResult { Success = false, Error = $"Dosya bulunamadı: {resolvedPath}" };
        }

        _updateOperationStep?.Invoke("🧪 Testler çalıştırılıyor...");
        _terminalLog("Test çalıştırması başlatıldı...");

        try
        {
            var workDir = System.IO.Path.GetDirectoryName(resolvedPath) ?? ".";
            var projectFile = System.IO.Path.GetFileName(resolvedPath);
            var result = await RunProcessAsync("dotnet", $"test \"{projectFile}\" --logger \"console;verbosity=minimal\"", workDir);

            EventBus.Publish(new TimelineEvent
            {
                Type = TimelineEventType.Testing,
                IsCompleted = result.Success,
                IsFailed = !result.Success,
                Message = result.Success ? "Testler başarıyla tamamlandı!" : "Test çalıştırması başarısız oldu!"
            });

            return new ToolResult
            {
                Success = result.Success,
                Output = _optimizer.OptimizeTerminalOutput(result.Output),
                Error = _optimizer.OptimizeTerminalOutput(result.Error ?? "")
            };
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

        var resolvedWorkDir = ResolvePath(workingDirectory ?? (_projectFolder ?? "."));

        _updateOperationStep?.Invoke($"⚙️ Komut çalıştırılıyor: {command}");
        _terminalLog($"Terminal komutu: {command}");

        try
        {
            var result = await RunProcessAsync("powershell", $"-Command \"{command}\"", resolvedWorkDir, cancellationToken);

            return new ToolResult
            {
                Success = result.Success,
                Output = _optimizer.OptimizeTerminalOutput(result.Output),
                Error = _optimizer.OptimizeTerminalOutput(result.Error ?? "")
            };
        }
        catch (Exception ex)
        {
            return new ToolResult { Success = false, Error = $"Terminal komutu hatası: {ex.Message}" };
        }
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

            await Task.WhenAll(
                process.WaitForExitAsync(cancellationToken),
                outputTask,
                errorTask
            );

            var output = await outputTask;
            var error = await errorTask;

            return (process.ExitCode == 0, output, error);
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
            return path;

        return _projectFolder != null
            ? System.IO.Path.Combine(_projectFolder, path)
            : System.IO.Path.GetFullPath(path);
    }
}
