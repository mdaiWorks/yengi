using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Threading;
using System.Threading.Tasks;

namespace mdaiAgent;

/// <summary>
/// Manages a queue of system processes to prevent concurrent execution issues.
/// Supports cancellation tokens and graceful shutdown with optimized event-based signaling.
/// </summary>
public class ProcessQueue : IDisposable
{
    private class QueuedProcess
    {
        public string Id { get; set; } = Guid.NewGuid().ToString();
        public Process? Process { get; set; }
        public CancellationTokenSource CancelTokenSource { get; set; } = new();
        public TaskCompletionSource<int> CompletionSource { get; set; } = new();
    }

    private readonly Queue<QueuedProcess> _queue = new();
    private readonly SemaphoreSlim _semaphore = new(1, 1);
    private readonly ManualResetEventSlim _queueSignal = new(false); // Event-based signaling for responsiveness
    private QueuedProcess? _currentProcess;
    private bool _isDisposed;
    private Action<string>? _onProcessOutput;
    private Action<string>? _onProcessError;
    private const int PollingIntervalMs = 50; // Optimized: 100ms -> 50ms for faster responsiveness

    public ProcessQueue(Action<string>? onOutput = null, Action<string>? onError = null)
    {
        _onProcessOutput = onOutput;
        _onProcessError = onError;
    }

    /// <summary>
    /// Queue a process for execution. Returns a token to monitor/cancel the process.
    /// </summary>
    public async Task<(string ProcessId, Task<int> CompletionTask)> EnqueueProcessAsync(
        string fileName, 
        string arguments, 
        string workingDirectory)
    {
        var queuedProcess = new QueuedProcess();
        
        lock (_queue)
        {
            _queue.Enqueue(queuedProcess);
        }

        // Signal waiting threads that queue has changed (event-based, not polling)
        _queueSignal.Set();

        // Wait for this process to become current
        await WaitForExecutionAsync(queuedProcess);

        return (queuedProcess.Id, queuedProcess.CompletionSource.Task);
    }

    private async Task WaitForExecutionAsync(QueuedProcess queuedProcess)
    {
        while (true)
        {
            await _semaphore.WaitAsync(queuedProcess.CancelTokenSource.Token).ConfigureAwait(false);
            
            QueuedProcess? currentFront = null;
            lock (_queue)
            {
                if (_queue.Count > 0 && _queue.Peek() == queuedProcess)
                {
                    _currentProcess = queuedProcess;
                    _queueSignal.Reset(); // Clear signal for next waiter
                    return;
                }
                currentFront = _queue.Count > 0 ? _queue.Peek() : null;
            }
            
            _semaphore.Release();
            
            // Use event signaling with timeout fallback: wait for signal OR timeout
            // This is much more responsive than pure polling
            _queueSignal.Wait(PollingIntervalMs, queuedProcess.CancelTokenSource.Token);
        }
    }

    /// <summary>
    /// Execute a process and handle its output/error streams.
    /// </summary>
    public async Task<int> ExecuteProcessAsync(
        string processId,
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
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                RedirectStandardInput = true,
                StandardOutputEncoding = System.Text.Encoding.UTF8,
                StandardErrorEncoding = System.Text.Encoding.UTF8,
                UseShellExecute = false,
                CreateNoWindow = true
            };

            using (var process = new Process { StartInfo = psi, EnableRaisingEvents = true })
            {
                _currentProcess!.Process = process;

                process.OutputDataReceived += (s, e) =>
                {
                    if (e.Data != null) _onProcessOutput?.Invoke(e.Data);
                };

                process.ErrorDataReceived += (s, e) =>
                {
                    if (e.Data != null) _onProcessError?.Invoke($"[HATA] {e.Data}");
                };

                process.Start();
                process.BeginOutputReadLine();
                process.BeginErrorReadLine();

                // Monitor cancellation
                using (cancellationToken.Register(() =>
                {
                    try { process.Kill(true); }
                    catch { }
                }))
                {
                    await Task.Run(() => process.WaitForExit(), cancellationToken).ConfigureAwait(false);
                }

                return process.ExitCode;
            }
        }
        finally
        {
            DequeueCurrentProcess();
        }
    }

    public void WriteInput(string input)
    {
        if (_currentProcess?.Process != null && !_currentProcess.Process.HasExited)
        {
            try
            {
                _currentProcess.Process.StandardInput.WriteLine(input);
            }
            catch { }
        }
    }

    private void DequeueCurrentProcess()
    {
        lock (_queue)
        {
            if (_queue.Count > 0)
            {
                _queue.Dequeue();
            }
            _currentProcess = null;
        }

        _semaphore.Release();
        
        // Signal next waiter that semaphore is available (event-based notification)
        _queueSignal.Set();

        // Process next in queue (fire and forget - next process will wait)
        ProcessNextInQueue();
    }

    private void ProcessNextInQueue()
    {
        var _ = Task.Run(async () =>
        {
            QueuedProcess? next = null;
            lock (_queue)
            {
                if (_queue.Count > 0)
                {
                    next = _queue.Peek();
                }
            }

            if (next != null)
            {
                await WaitForExecutionAsync(next).ConfigureAwait(false);
            }
        }).ContinueWith(tt =>
        {
            try
            {
                var ex = tt.Exception?.Flatten();
                if (ex != null)
                {
                    // swallow - optionally add logging here
                }
            }
            catch { }
        }, CancellationToken.None, TaskContinuationOptions.OnlyOnFaulted, TaskScheduler.Default);
    }

    /// <summary>
    /// Cancel a specific process by ID.
    /// </summary>
    public bool CancelProcess(string processId)
    {
        lock (_queue)
        {
            var process = _currentProcess;
            if (process?.Id == processId)
            {
                process.CancelTokenSource.Cancel();
                try { process.Process?.Kill(true); }
                catch { }
                return true;
            }
        }
        return false;
    }

    /// <summary>
    /// Get current queue length.
    /// </summary>
    public int QueueLength
    {
        get
        {
            lock (_queue) return _queue.Count;
        }
    }

    /// <summary>
    /// Check if a process is currently running.
    /// </summary>
    public bool IsProcessRunning(string processId)
    {
        lock (_queue)
        {
            return _currentProcess?.Id == processId && _currentProcess?.Process != null;
        }
    }

    public void Dispose()
    {
        if (_isDisposed) return;
        
        lock (_queue)
        {
            foreach (var proc in _queue)
            {
                proc.CancelTokenSource.Cancel();
                try { proc.Process?.Kill(true); }
                catch { }
                proc.CancelTokenSource.Dispose();
            }
            _queue.Clear();
        }

        _semaphore?.Dispose();
        _queueSignal?.Dispose();
        _isDisposed = true;
    }
}
