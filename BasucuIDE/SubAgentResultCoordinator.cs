using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;

namespace mdaiAgent;

/// <summary>
/// Sub-agent görevlerinin sonuçlarını standardize eder ve takip eder
/// Problem: Sub-agent → Görevi yap → Sonuç? → Ana agent, sonucu nasıl bilecek?
/// Çözüm: TaskId-based tracking + Structured result format
/// </summary>
public class SubAgentResult
{
    public string TaskId { get; set; } = Guid.NewGuid().ToString();
    public string Role { get; set; } = "SubAgent";
    public string Status { get; set; } = "Pending"; // Pending, Running, Success, Failed, Timeout
    public string? Output { get; set; }
    public string? Error { get; set; }
    public List<string> ModifiedFiles { get; set; } = new();
    public DateTime StartTime { get; set; } = DateTime.UtcNow;
    public DateTime? EndTime { get; set; }
    public int RetryCount { get; set; } = 0;
    public Dictionary<string, object> Metadata { get; set; } = new();

    public double ElapsedSeconds => EndTime.HasValue ? (EndTime.Value - StartTime).TotalSeconds : (DateTime.UtcNow - StartTime).TotalSeconds;
    public bool IsComplete => Status is "Success" or "Failed" or "Timeout";

    public string ToJson()
    {
        return JsonSerializer.Serialize(this, new JsonSerializerOptions { WriteIndented = true });
    }
}

/// <summary>
/// Sub-agent görevlerini yönetir ve sonuçlarını takip eder
/// </summary>
public class SubAgentResultCoordinator
{
    private readonly ConcurrentDictionary<string, SubAgentResult> _taskResults = new();
    private readonly ConcurrentDictionary<string, TaskCompletionSource<SubAgentResult>> _taskCompletion = new();
    private readonly Action<string> _terminalLog;
    private const int TimeoutSeconds = 300; // 5 dakika

    public event Action<SubAgentResult>? TaskCompleted;
    public event Action<SubAgentResult>? TaskFailed;

    public SubAgentResultCoordinator(Action<string> terminalLog)
    {
        _terminalLog = terminalLog;
    }

    /// <summary>
    /// Yeni bir sub-agent görevini register eder
    /// </summary>
    public SubAgentResult RegisterTask(string role, string prompt, string context = "")
    {
        var result = new SubAgentResult
        {
            Role = role,
            Status = "Pending",
            Metadata = new()
            {
                ["prompt"] = prompt,
                ["context"] = context
            }
        };

        _taskResults.TryAdd(result.TaskId, result);
        _taskCompletion.TryAdd(result.TaskId, new TaskCompletionSource<SubAgentResult>());

        _terminalLog?.Invoke($"[SubAgent Task] {result.TaskId} registered for role '{role}'");

        return result;
    }

    /// <summary>
    /// Görevi çalışıyor olarak işaretler
    /// </summary>
    public void MarkTaskRunning(string taskId)
    {
        if (_taskResults.TryGetValue(taskId, out var result))
        {
            result.Status = "Running";
            result.StartTime = DateTime.UtcNow;
            _terminalLog?.Invoke($"[SubAgent Task] {taskId} running...");
        }
    }

    /// <summary>
    /// Görevi başarıyla tamamlar
    /// </summary>
    public void CompleteTask(string taskId, string output, List<string>? modifiedFiles = null)
    {
        if (_taskResults.TryGetValue(taskId, out var result))
        {
            result.Status = "Success";
            result.Output = output;
            result.EndTime = DateTime.UtcNow;
            result.ModifiedFiles = modifiedFiles ?? new();

            _terminalLog?.Invoke($"✓ [SubAgent Task] {taskId} completed in {result.ElapsedSeconds:F2}s");

            if (_taskCompletion.TryGetValue(taskId, out var tcs))
            {
                tcs.SetResult(result);
            }

            TaskCompleted?.Invoke(result);
        }
    }

    /// <summary>
    /// Görevi hata ile tamamlar
    /// </summary>
    public void FailTask(string taskId, string error, bool retriable = false)
    {
        if (_taskResults.TryGetValue(taskId, out var result))
        {
            result.Status = retriable && result.RetryCount < 3 ? "Pending" : "Failed";
            result.Error = error;
            result.EndTime = DateTime.UtcNow;
            result.RetryCount++;

            _terminalLog?.Invoke($"❌ [SubAgent Task] {taskId} failed: {error}");

            if (result.Status == "Failed")
            {
                if (_taskCompletion.TryGetValue(taskId, out var tcs))
                {
                    tcs.SetResult(result);
                }

                TaskFailed?.Invoke(result);
            }
        }
    }

    /// <summary>
    /// Sonuçun tamamlanmasını bekler
    /// </summary>
    public async Task<SubAgentResult> WaitForResultAsync(string taskId, int timeoutSeconds = TimeoutSeconds)
    {
        if (!_taskCompletion.TryGetValue(taskId, out var tcs))
        {
            return new SubAgentResult
            {
                TaskId = taskId,
                Status = "Failed",
                Error = "Task not found"
            };
        }

        try
        {
            var timeoutTask = Task.Delay(TimeSpan.FromSeconds(timeoutSeconds));
            var resultTask = tcs.Task;

            var completedTask = await Task.WhenAny(resultTask, timeoutTask);

            if (completedTask == timeoutTask)
            {
                FailTask(taskId, $"Task timeout after {timeoutSeconds} seconds");
                return _taskResults[taskId];
            }

            return await resultTask;
        }
        catch (Exception ex)
        {
            FailTask(taskId, $"Error waiting for result: {ex.Message}");
            return _taskResults[taskId];
        }
    }

    /// <summary>
    /// Tüm açık görevleri listeler
    /// </summary>
    public List<SubAgentResult> GetAllTasks()
    {
        return _taskResults.Values.ToList();
    }

    /// <summary>
    /// Belirli bir görevi getirir
    /// </summary>
    public SubAgentResult? GetTask(string taskId)
    {
        _taskResults.TryGetValue(taskId, out var result);
        return result;
    }

    /// <summary>
    /// Başarısız görevleri döner
    /// </summary>
    public List<SubAgentResult> GetFailedTasks()
    {
        return _taskResults.Values.Where(t => t.Status == "Failed").ToList();
    }

    /// <summary>
    /// Eski görevleri temizler (30 dakikadan eski)
    /// </summary>
    public void CleanupOldTasks(int olderThanMinutes = 30)
    {
        var cutoff = DateTime.UtcNow.AddMinutes(-olderThanMinutes);
        var oldTasks = _taskResults.Where(x => x.Value.EndTime < cutoff).ToList();

        foreach (var (taskId, _) in oldTasks)
        {
            _taskResults.TryRemove(taskId, out _);
            _taskCompletion.TryRemove(taskId, out _);
            _terminalLog?.Invoke($"[Cleanup] Old task removed: {taskId}");
        }
    }
}

/// <summary>
/// Sub-agent görev sonuç olayı
/// </summary>
public class SubAgentTaskResultEvent
{
    public string TaskId { get; set; } = "";
    public SubAgentResult Result { get; set; } = new();
    public DateTime Timestamp { get; set; } = DateTime.UtcNow;
}
