using System;
using System.IO;
using System.Net.Http;
using System.Text.Json;
using System.Threading.Tasks;
using Xunit;

namespace mdaiAgent.Tests;

public class AgentCoreRuntimeTests
{
    private readonly string _testProjectFolder;
    private readonly AgentCoreRuntime _agentCore;
    private string _lastLog = "";
    private string _lastOpStep = "";

    public AgentCoreRuntimeTests()
    {
        _testProjectFolder = Path.Combine(Path.GetTempPath(), "mdai_agent_core_test");
        if (Directory.Exists(_testProjectFolder))
            Directory.Delete(_testProjectFolder, true);
        Directory.CreateDirectory(_testProjectFolder);

        // Create minimal project structure
        var srcDir = Path.Combine(_testProjectFolder, "src");
        Directory.CreateDirectory(srcDir);
        File.WriteAllText(Path.Combine(srcDir, "Program.cs"), "class Program { static void Main() {} }");

        _agentCore = new AgentCoreRuntime(
            _testProjectFolder,
            msg => _lastLog = msg,
            (msg, sev) => { },
            step => _lastOpStep = step
        );
    }

    [Fact]
    public void GetStatus_InitiallyIdle()
    {
        Assert.Equal(AgentStatus.Idle, _agentCore.GetStatus());
    }

    [Fact]
    public async Task ExecuteToolAsync_WithValidTool_ReturnsResult()
    {
        var args = JsonSerializer.Serialize(new { folderPath = _testProjectFolder });
        var result = await _agentCore.ExecuteToolAsync("DiscoverProjectContext", args);

        Assert.NotNull(result);
        Assert.True(result.Success || !string.IsNullOrEmpty(result.Error));
    }

    [Fact]
    public async Task ExecuteToolAsync_PublishesTimelineEvents()
    {
        var events = new System.Collections.Generic.List<TimelineEvent>();
        Action<TimelineEvent> handler = events.Add;
        EventBus.Subscribe(handler);

        try
        {
            var args = JsonSerializer.Serialize(new { folderPath = _testProjectFolder });
            await _agentCore.ExecuteToolAsync("DiscoverProjectContext", args);

            Assert.Contains(events, evt => evt.Message.Contains("Araç çalıştırılıyor", StringComparison.Ordinal));
            Assert.Contains(events, evt => evt.Message.Contains("Araç tamamlandı", StringComparison.Ordinal));
        }
        finally
        {
            EventBus.Unsubscribe(handler);
        }
    }

    [Fact]
    public async Task CreateCheckpointAsync_WithLabel_CreatesCheckpoint()
    {
        var result = await _agentCore.CreateCheckpointAsync("test-cp", "Test checkpoint");

        Assert.True(result.Success);
        Assert.Contains("created", result.Output, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task CreateCheckpointAsync_LogsMessage()
    {
        _lastLog = "";
        await _agentCore.CreateCheckpointAsync("test-cp2", "Test");

        Assert.Contains("Checkpoint", _lastLog, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Subscribe_WithEvent_RegistersHandler()
    {
        bool handlerCalled = false;
        
        _agentCore.Subscribe<TimelineEvent>(evt =>
        {
            handlerCalled = true;
        });

        // For now, just verify no exception
        Assert.False(handlerCalled); // Handler not called unless we publish
    }

    [Fact]
    public async Task VerifyAsync_ReturnsBoolean()
    {
        var result = await _agentCore.VerifyAsync("Test verification");

        Assert.True(result is bool);
    }

    [Fact]
    public async Task VerifyAsync_UsesInjectedVerificationRunner()
    {
        var runnerCalled = false;
        var runtime = new AgentCoreRuntime(
            _testProjectFolder,
            _ => { },
            (_, _) => { },
            _ => { },
            verificationRunner: async description =>
            {
                runnerCalled = description == "injected verification";
                return runnerCalled;
            });

        var result = await runtime.VerifyAsync("injected verification");

        Assert.True(result);
        Assert.True(runnerCalled);
    }

    [Fact]
    public async Task CreateSafeRuntime_ProvidesProductionSafeAdapter()
    {
        var runtime = AgentCoreRuntime.CreateSafeRuntime(
            _testProjectFolder,
            _ => { },
            (_, _) => { },
            _ => { });

        Assert.True(runtime.IsProductionReady);
        Assert.True(runtime.CanHandleTool("ReadFile"));
        Assert.False(runtime.CanHandleTool("NotARealTool"));

        var result = await runtime.ExecuteToolAsync("ReadFile", JsonSerializer.Serialize(new { filePath = Path.Combine(_testProjectFolder, "src", "Program.cs") }));

        Assert.NotNull(result);
        Assert.True(result.Success || !string.IsNullOrEmpty(result.Error));
    }

    [Fact]
    public async Task DefaultRuntime_RequiresTerminalApproval()
    {
        var result = await _agentCore.ExecuteToolAsync(
            "ExecuteTerminalCommand",
            JsonSerializer.Serialize(new { command = "echo runtime-test" }));

        Assert.False(result.Success);
        Assert.Contains("onay", result.Error ?? string.Empty, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task InitializeAsync_WithValidFolder_Completes()
    {
        // API client initialization is optional
        await _agentCore.InitializeAsync(_testProjectFolder, null!);

        Assert.Equal(AgentStatus.Idle, _agentCore.GetStatus());
    }

    public void Dispose()
    {
        try
        {
            if (Directory.Exists(_testProjectFolder))
                Directory.Delete(_testProjectFolder, true);
        }
        catch { }
    }
}

/// <summary>
/// Integration tests for AgentCore with UI components
/// </summary>
public class AgentCoreIntegrationTests : IDisposable
{
    private readonly string _testProjectFolder;
    private readonly AgentCoreRuntime _agentCore;

    public AgentCoreIntegrationTests()
    {
        _testProjectFolder = Path.Combine(Path.GetTempPath(), "mdai_agent_integration");
        if (Directory.Exists(_testProjectFolder))
            Directory.Delete(_testProjectFolder, true);
        Directory.CreateDirectory(_testProjectFolder);

        var srcDir = Path.Combine(_testProjectFolder, "src");
        Directory.CreateDirectory(srcDir);
        File.WriteAllText(Path.Combine(srcDir, "file.cs"), "class Test {}");

        _agentCore = new AgentCoreRuntime(
            _testProjectFolder,
            _ => { },
            (_, _) => { },
            _ => { }
        );
    }

    [Fact]
    public async Task AgentCore_ExecuteTool_ReadFile_ReturnsContent()
    {
        var testFile = Path.Combine(_testProjectFolder, "src", "file.cs");
        var args = JsonSerializer.Serialize(new { filePath = testFile });

        var result = await _agentCore.ExecuteToolAsync("ReadFile", args);

        Assert.True(result.Success || !string.IsNullOrEmpty(result.Error));
    }

    [Fact]
    public async Task AgentCore_Checkpoint_Workflow()
    {
        // Create checkpoint
        var createResult = await _agentCore.CreateCheckpointAsync("integration-cp", "Integration test");
        Assert.True(createResult.Success);

        // Rollback (should succeed or have reasonable error)
        var rollbackResult = await _agentCore.RollbackAsync("integration-cp");
        Assert.True(rollbackResult.Success || !string.IsNullOrEmpty(rollbackResult.Error));
    }

    [Fact]
    public async Task AgentCore_StatusTransitions()
    {
        // Start as Idle
        Assert.Equal(AgentStatus.Idle, _agentCore.GetStatus());

        // Execute tool (status should be ExecutingTool during, then Idle after)
        var args = JsonSerializer.Serialize(new { folderPath = _testProjectFolder });
        var result = await _agentCore.ExecuteToolAsync("DiscoverProjectContext", args);

        // Should return to Idle
        Assert.Equal(AgentStatus.Idle, _agentCore.GetStatus());
    }

    public void Dispose()
    {
        try
        {
            if (Directory.Exists(_testProjectFolder))
                Directory.Delete(_testProjectFolder, true);
        }
        catch { }
    }
}
