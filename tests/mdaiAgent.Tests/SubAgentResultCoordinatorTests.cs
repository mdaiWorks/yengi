using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Xunit;

namespace mdaiAgent.Tests;

public class SubAgentResultCoordinatorTests
{
    [Fact]
    public void RegisterTask_CreatesUniqueTaskId()
    {
        var coordinator = new SubAgentResultCoordinator(s => { });
        
        var result1 = coordinator.RegisterTask("CodeReviewer", "Review this code", "");
        var result2 = coordinator.RegisterTask("CodeReviewer", "Review that code", "");

        Assert.NotEqual(result1.TaskId, result2.TaskId);
        Assert.Equal("Pending", result1.Status);
    }

    [Fact]
    public void RegisterTask_SetsRole()
    {
        var coordinator = new SubAgentResultCoordinator(s => { });
        var result = coordinator.RegisterTask("Debugger", "Fix the bug", "context");

        Assert.Equal("Debugger", result.Role);
    }

    [Fact]
    public void MarkTaskRunning_UpdatesStatus()
    {
        var coordinator = new SubAgentResultCoordinator(s => { });
        var result = coordinator.RegisterTask("Tester", "Run tests", "");

        coordinator.MarkTaskRunning(result.TaskId);
        var updated = coordinator.GetTask(result.TaskId);

        Assert.Equal("Running", updated?.Status);
    }

    [Fact]
    public void CompleteTask_MarkSuccessful()
    {
        var coordinator = new SubAgentResultCoordinator(s => { });
        var result = coordinator.RegisterTask("Helper", "Do something", "");

        coordinator.CompleteTask(result.TaskId, "Task completed successfully", new List<string> { "file1.cs" });
        var completed = coordinator.GetTask(result.TaskId);

        Assert.Equal("Success", completed?.Status);
        Assert.Equal("Task completed successfully", completed?.Output);
        Assert.Contains("file1.cs", completed?.ModifiedFiles ?? new());
    }

    [Fact]
    public void FailTask_MarksFailure()
    {
        var coordinator = new SubAgentResultCoordinator(s => { });
        var result = coordinator.RegisterTask("Worker", "Do work", "");

        coordinator.FailTask(result.TaskId, "Something went wrong", false);
        var failed = coordinator.GetTask(result.TaskId);

        Assert.Equal("Failed", failed?.Status);
        Assert.Equal("Something went wrong", failed?.Error);
    }

    [Fact]
    public void ElapsedSeconds_CalculatesCorrectly()
    {
        var coordinator = new SubAgentResultCoordinator(s => { });
        var result = coordinator.RegisterTask("Timer", "Check timing", "");
        
        result.StartTime = DateTime.UtcNow.AddSeconds(-5);
        result.EndTime = DateTime.UtcNow;

        Assert.InRange(result.ElapsedSeconds, 4.9, 5.1);
    }

    [Fact]
    public void GetFailedTasks_FiltersCorrectly()
    {
        var coordinator = new SubAgentResultCoordinator(s => { });
        
        var success = coordinator.RegisterTask("A", "task", "");
        var failed1 = coordinator.RegisterTask("B", "task", "");
        var failed2 = coordinator.RegisterTask("C", "task", "");

        coordinator.CompleteTask(success.TaskId, "ok");
        coordinator.FailTask(failed1.TaskId, "err1");
        coordinator.FailTask(failed2.TaskId, "err2");

        var failures = coordinator.GetFailedTasks();
        Assert.Equal(2, failures.Count);
    }

    [Fact]
    public void ToJson_SerializesResult()
    {
        var result = new SubAgentResult
        {
            TaskId = "test-123",
            Role = "Tester",
            Status = "Success",
            Output = "All tests passed"
        };

        var json = result.ToJson();
        
        Assert.Contains("test-123", json);
        Assert.Contains("Tester", json);
        Assert.Contains("Success", json);
    }

    [Fact]
    public async Task WaitForResultAsync_ReturnsWhenCompleted()
    {
        var coordinator = new SubAgentResultCoordinator(s => { });
        var result = coordinator.RegisterTask("Async", "async task", "");

        // Background task to complete after a short delay
        _ = Task.Run(async () =>
        {
            await Task.Delay(100);
            coordinator.CompleteTask(result.TaskId, "Done!");
        });

        var completed = await coordinator.WaitForResultAsync(result.TaskId, 5);

        Assert.Equal("Success", completed.Status);
        Assert.Equal("Done!", completed.Output);
    }

    [Fact]
    public async Task WaitForResultAsync_TimesOut()
    {
        var coordinator = new SubAgentResultCoordinator(s => { });
        var result = coordinator.RegisterTask("Slow", "slow task", "");

        // Don't complete the task, let it timeout
        var timedOut = await coordinator.WaitForResultAsync(result.TaskId, 1);

        Assert.Equal("Failed", timedOut.Status);
        Assert.Contains("timeout", timedOut.Error ?? "", StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void GetAllTasks_ReturnsAll()
    {
        var coordinator = new SubAgentResultCoordinator(s => { });
        
        coordinator.RegisterTask("A", "task", "");
        coordinator.RegisterTask("B", "task", "");
        coordinator.RegisterTask("C", "task", "");

        var all = coordinator.GetAllTasks();
        Assert.Equal(3, all.Count);
    }

    [Fact]
    public void CleanupOldTasks_RemovesExpired()
    {
        var coordinator = new SubAgentResultCoordinator(s => { });
        
        var task1 = coordinator.RegisterTask("Old", "task", "");
        var task2 = coordinator.RegisterTask("New", "task", "");

        // Mark task1 as old (30+ minutes ago)
        task1.EndTime = DateTime.UtcNow.AddMinutes(-31);

        coordinator.CleanupOldTasks(30);

        var remaining = coordinator.GetAllTasks();
        Assert.Single(remaining);
        Assert.Equal(task2.TaskId, remaining[0].TaskId);
    }

    [Fact]
    public void SubAgentResult_IsCompleteFlagWorks()
    {
        var result = new SubAgentResult { Status = "Pending" };
        Assert.False(result.IsComplete);

        result.Status = "Success";
        Assert.True(result.IsComplete);

        result.Status = "Failed";
        Assert.True(result.IsComplete);

        result.Status = "Timeout";
        Assert.True(result.IsComplete);
    }
}
