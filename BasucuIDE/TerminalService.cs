using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace mdaiAgent;

public class TerminalService
{
    private readonly Func<string?> _selectedFolderProvider;
    private readonly ProcessQueue _processQueue;
    private readonly object _terminalBufferLock = new();
    private readonly List<string> _terminalLines = new();
    private const int MaxTerminalLines = 1200;
    private readonly List<string> _commandHistory = new();
    private int _commandHistoryIndex = -1;
    private string? _currentProcessId;
    private bool _isBusy;

    public event EventHandler<string>? TerminalTextChanged;
    public event EventHandler<string>? StatusChanged;
    public event EventHandler<bool>? IsBusyChanged;

    public string TerminalText { get; private set; } = string.Empty;
    public bool IsBusy => _isBusy;
    public bool CanKill => !string.IsNullOrEmpty(_currentProcessId);

    public TerminalService(Func<string?> selectedFolderProvider, ProcessQueue? processQueue = null)
    {
        _selectedFolderProvider = selectedFolderProvider;
        _processQueue = processQueue ?? new ProcessQueue(OnOutput, OnError);
    }

    public void Clear()
    {
        lock (_terminalBufferLock)
        {
            _terminalLines.Clear();
            TerminalText = string.Empty;
        }

        TerminalTextChanged?.Invoke(this, TerminalText);
    }

    public void AppendLine(string line)
    {
        if (string.IsNullOrWhiteSpace(line))
            return;

        EnqueueTerminalLine(line);
    }

    public string? GetPreviousHistory()
    {
        if (_commandHistory.Count == 0)
            return null;

        if (_commandHistoryIndex < 0)
            _commandHistoryIndex = _commandHistory.Count;

        _commandHistoryIndex = Math.Max(0, _commandHistoryIndex - 1);
        return _commandHistory[_commandHistoryIndex];
    }

    public string? GetNextHistory()
    {
        if (_commandHistory.Count == 0)
            return null;

        if (_commandHistoryIndex < 0)
            return null;

        _commandHistoryIndex = Math.Min(_commandHistory.Count - 1, _commandHistoryIndex + 1);
        return _commandHistory[_commandHistoryIndex];
    }

    public async Task ExecuteCommandAsync(string command)
    {
        if (string.IsNullOrWhiteSpace(command))
            return;

        command = command.Trim();

        if (_commandHistory.Count == 0 || _commandHistory[^1] != command)
        {
            _commandHistory.Add(command);
        }
        _commandHistoryIndex = _commandHistory.Count;

        var workingDir = GetWorkingDirectory();
        var shortDir = Path.GetFileName(workingDir.TrimEnd(Path.DirectorySeparatorChar));
        EnqueueTerminalLine($"[{shortDir}]> {command}");

        SetBusy(true);
        StatusChanged?.Invoke(this, "Çalışıyor...");

        try
        {
            var (processId, _) = await _processQueue.EnqueueProcessAsync(
                "cmd.exe",
                $"/c {command}",
                workingDir);

            _currentProcessId = processId;

            var exitCode = await _processQueue.ExecuteProcessAsync(
                processId,
                "cmd.exe",
                $"/c {command}",
                workingDir);

            EnqueueTerminalLine($"--- Süreç tamamlandı (çıkış kodu: {exitCode}) ---");
            StatusChanged?.Invoke(this, "Tamamlandı");
        }
        catch (OperationCanceledException)
        {
            EnqueueTerminalLine("--- Süreç kullanıcı tarafından iptal edildi ---");
            StatusChanged?.Invoke(this, "İptal edildi");
        }
        catch (Exception ex)
        {
            EnqueueTerminalLine($"[HATA] Komut çalıştırılamadı: {ex.Message}");
            StatusChanged?.Invoke(this, $"Hata: {ex.Message}");
            throw;
        }
        finally
        {
            _currentProcessId = null;
            SetBusy(false);
        }
    }

    public bool KillCurrentProcess()
    {
        if (string.IsNullOrEmpty(_currentProcessId))
            return false;

        var canceled = _processQueue.CancelProcess(_currentProcessId);
        if (canceled)
        {
            EnqueueTerminalLine("⏹ Süreç durduruldu.");
            StatusChanged?.Invoke(this, "Durduruldu");
        }

        return canceled;
    }

    public void WriteInput(string input)
    {
        if (string.IsNullOrEmpty(_currentProcessId))
            return;
            
        _processQueue.WriteInput(input);
        EnqueueTerminalLine(input); // Echo to terminal so user sees what they typed/sent
    }

    private string GetWorkingDirectory()
    {
        var selectedFolder = _selectedFolderProvider();
        if (!string.IsNullOrEmpty(selectedFolder) && Directory.Exists(selectedFolder))
            return selectedFolder;

        return Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
    }

    private void SetBusy(bool busy)
    {
        if (_isBusy == busy)
            return;

        _isBusy = busy;
        IsBusyChanged?.Invoke(this, busy);
    }

    public void ResetBusyState()
    {
        SetBusy(false);
    }

    private void OnOutput(string line)
    {
        EnqueueTerminalLine(line);
    }

    private void OnError(string line)
    {
        EnqueueTerminalLine(line);
    }

    private void EnqueueTerminalLine(string line)
    {
        lock (_terminalBufferLock)
        {
            _terminalLines.Add(line);
            if (_terminalLines.Count > MaxTerminalLines)
                _terminalLines.RemoveRange(0, _terminalLines.Count - MaxTerminalLines);

            TerminalText = string.Join(Environment.NewLine, _terminalLines) + Environment.NewLine;
        }

        TerminalTextChanged?.Invoke(this, TerminalText);
    }
}
