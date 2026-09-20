using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace mdaiAgent.Services;

/// <summary>
/// Handles quick command creation and execution.
/// </summary>
public class CommandService
{
    private readonly string? _projectFolder;
    private readonly Action<string> _terminalLog;
    private readonly Dictionary<string, string> _quickCommands = new();
    private readonly ContextOptimizerService _optimizer;
    private readonly ToolOutputStore _outputStore;

    public CommandService(
        string? projectFolder,
        Action<string> terminalLog,
        ToolOutputStore? outputStore = null)
    {
        _projectFolder = projectFolder;
        _terminalLog = terminalLog;
        _optimizer = new ContextOptimizerService();
        _outputStore = outputStore ?? new ToolOutputStore();
    }

    public Task<ToolResult> CreateQuickCommandAsync(JsonElement arguments, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var name = arguments.GetProperty("name").GetString();
        var command = arguments.GetProperty("command").GetString();

        if (string.IsNullOrWhiteSpace(name) || string.IsNullOrWhiteSpace(command))
            return Task.FromResult(new ToolResult { Success = false, Error = "Ad ve komut zorunlu." });

        try
        {
            EventBus.Publish(new TimelineEvent { Type = TimelineEventType.Info, Message = $"Hızlı komut kaydediliyor: {name}" });
            _quickCommands[name] = command;

            // Persist to file
            var commandsFile = Path.Combine(_projectFolder ?? ".", ".mdai", "quickcommands.json");
            var dir = Path.GetDirectoryName(commandsFile);
            if (dir != null) Directory.CreateDirectory(dir);

            var json = JsonSerializer.Serialize(_quickCommands, new JsonSerializerOptions { WriteIndented = true });
            File.WriteAllText(commandsFile, json);

            cancellationToken.ThrowIfCancellationRequested();
            var rawOutput = $"Komut kaydedildi: {name}";
            var optimizedOutput = _optimizer.OptimizeTerminalOutput(rawOutput);
            var outputId = _outputStore.SaveIfTruncated(rawOutput, optimizedOutput, "quickcmd-create");

            _terminalLog($"✓ Hızlı komut oluşturuldu: {name}");
            EventBus.Publish(new TimelineEvent { Type = TimelineEventType.Info, IsCompleted = true, Message = $"Hızlı komut oluşturuldu: {name}" });
            return Task.FromResult(new ToolResult { Success = true, Output = AppendOutputReference(optimizedOutput, outputId) });
        }
        catch (OperationCanceledException)
        {
            EventBus.Publish(new TimelineEvent { Type = TimelineEventType.Failed, IsFailed = true, Message = $"Hızlı komut oluşturma iptal edildi: {name}" });
            return Task.FromResult(new ToolResult { Success = false, Error = "Hızlı komut oluşturma iptal edildi." });
        }
        catch (Exception ex)
        {
            EventBus.Publish(new TimelineEvent { Type = TimelineEventType.Failed, IsFailed = true, Message = $"Hızlı komut oluşturulamadı: {name}" });
            return Task.FromResult(new ToolResult { Success = false, Error = $"Komut oluşturma hatası: {ex.Message}" });
        }
    }

    public Task<ToolResult> ExecuteQuickCommandAsync(JsonElement arguments, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var name = arguments.GetProperty("name").GetString();

        if (string.IsNullOrWhiteSpace(name))
            return Task.FromResult(new ToolResult { Success = false, Error = "Komut adı zorunlu." });

        try
        {
            if (!_quickCommands.ContainsKey(name))
                return Task.FromResult(new ToolResult { Success = false, Error = $"Komut bulunamadı: {name}" });

            var command = _quickCommands[name];
            _terminalLog($"✓ Hızlı komut yürütülüyor: {name}");
            EventBus.Publish(new TimelineEvent { Type = TimelineEventType.RunningTerminal, IsCompleted = true, Message = $"Hızlı komut hazırlandı: {name}" });

            var rawOutput = $"Komut yürütülüyor: {command}";
            var optimizedOutput = _optimizer.OptimizeTerminalOutput(rawOutput);
            var outputId = _outputStore.SaveIfTruncated(rawOutput, optimizedOutput, "quickcmd-exec");

            return Task.FromResult(new ToolResult { Success = true, Output = AppendOutputReference(optimizedOutput, outputId) });
        }
        catch (Exception ex)
        {
            return Task.FromResult(new ToolResult { Success = false, Error = $"Komut yürütme hatası: {ex.Message}" });
        }
    }

    /// <summary>
    /// Load quick commands from persistent storage.
    /// </summary>
    public async Task LoadCommandsAsync(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        try
        {
            var commandsFile = Path.Combine(_projectFolder ?? ".", ".mdai", "quickcommands.json");
            if (!File.Exists(commandsFile))
                return;

            var json = await File.ReadAllTextAsync(commandsFile, cancellationToken);
            var commands = JsonSerializer.Deserialize<Dictionary<string, string>>(json) ?? new();

            foreach (var cmd in commands)
            {
                cancellationToken.ThrowIfCancellationRequested();
                _quickCommands[cmd.Key] = cmd.Value;
            }

            _terminalLog($"✓ {commands.Count} hızlı komut yüklendi");
        }
        catch (OperationCanceledException)
        {
            _terminalLog("⚠️ Komut yükleme iptal edildi");
        }
        catch (Exception ex)
        {
            _terminalLog($"⚠️ Komut yükleme hatası: {ex.Message}");
        }
    }

    private static string AppendOutputReference(string output, string? outputId)
    {
        return string.IsNullOrWhiteSpace(outputId)
            ? output
            : output + $"\n\n[FULL_OUTPUT_ID:{outputId}] (Uzun çıktı aralıklarını okumak için ReadToolOutput kullanın.)";
    }
}
