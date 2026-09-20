using System;
using System.IO;
using Xunit;

namespace mdaiAgent.Tests;

[Collection("ProjectTelemetry")]
public class ProjectTelemetryTests
{
    [Fact]
    public void VerificationTelemetry_IsPersistedPerProject()
    {
        var projectX = Path.Combine(Path.GetTempPath(), "mdai_telemetry_x_" + Guid.NewGuid().ToString("N"));
        var projectY = Path.Combine(Path.GetTempPath(), "mdai_telemetry_y_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(projectX);
        Directory.CreateDirectory(projectY);

        try
        {
            var tracker = TokenTrackerService.Instance;
            tracker.SetProject(projectX);
            tracker.RecordVerification(new VerificationResult { Success = true, Status = "All Checks Passed" }, 120);
            tracker.RecordModelRequest("model-x", true, 80);
            tracker.RecordModelRequest("model-x", true, 40, isRetry: true);

            tracker.SetProject(projectY);
            tracker.RecordVerification(new VerificationResult { Success = false, Status = "Test Failure" }, 240);

            tracker.SetProject(projectX);
            var telemetryX = tracker.GetVerificationTelemetry();
            var modelTelemetryX = tracker.GetModelTelemetrySnapshot();
            tracker.SetProject(projectY);
            var telemetryY = tracker.GetVerificationTelemetry();

            Assert.Equal(1, telemetryX.TotalRuns);
            Assert.Equal(1, telemetryX.SuccessfulRuns);
            Assert.Equal(0, telemetryX.FailedRuns);
            Assert.Equal("All Checks Passed", telemetryX.LastStatus);
            Assert.Equal(2, modelTelemetryX["model-x"].TotalRequests);
            Assert.Equal(1, modelTelemetryX["model-x"].RetryRequests);

            Assert.Equal(1, telemetryY.TotalRuns);
            Assert.Equal(0, telemetryY.SuccessfulRuns);
            Assert.Equal(1, telemetryY.FailedRuns);
            Assert.Equal("Test Failure", telemetryY.LastStatus);

            Assert.True(File.Exists(Path.Combine(projectX, ".mdai", "verification_telemetry.json")));
            Assert.True(File.Exists(Path.Combine(projectY, ".mdai", "verification_telemetry.json")));
            Assert.True(File.Exists(Path.Combine(projectX, ".mdai", "model_telemetry.json")));
        }
        finally
        {
            if (Directory.Exists(projectX)) Directory.Delete(projectX, true);
            if (Directory.Exists(projectY)) Directory.Delete(projectY, true);
        }
    }

    [Fact]
    public void ClearProjectTelemetry_RemovesOnlyTelemetryFiles()
    {
        var project = Path.Combine(Path.GetTempPath(), "mdai_telemetry_clear_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(project);

        try
        {
            var tracker = TokenTrackerService.Instance;
            tracker.SetProject(project);
            tracker.LogToolExecution("ReadFile", true, 10);
            tracker.RecordVerification(new VerificationResult { Success = true, Status = "All Checks Passed" }, 20);
            tracker.RecordModelRequest("model-clear", true, 15);

            tracker.ClearProjectTelemetry();
            var telemetry = tracker.GetVerificationTelemetry();
            var modelTelemetry = tracker.GetModelTelemetrySnapshot();

            Assert.Equal(0, telemetry.TotalRuns);
            Assert.Empty(modelTelemetry);
            Assert.True(File.Exists(Path.Combine(project, ".mdai", "tool_telemetry.json")));
            Assert.True(File.Exists(Path.Combine(project, ".mdai", "verification_telemetry.json")));
            Assert.Contains("{}", File.ReadAllText(Path.Combine(project, ".mdai", "tool_telemetry.json")));
        }
        finally
        {
            if (Directory.Exists(project)) Directory.Delete(project, true);
        }
    }

    [Fact]
    public void CentralTelemetry_TracksFailuresRetriesAndSessionSummary()
    {
        var project = Path.Combine(Path.GetTempPath(), "mdai_telemetry_summary_" + Guid.NewGuid().ToString("N"));
        var nextProject = Path.Combine(Path.GetTempPath(), "mdai_telemetry_summary_next_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(project);

        try
        {
            var tracker = TokenTrackerService.Instance;
            tracker.SetProject(project);
            tracker.RecordModelRequest("model-summary", true, 100);
            tracker.RecordModelRequest("model-summary", false, 200, isRetry: true);
            tracker.LogToolExecution("ReadFile", true, 25);
            tracker.RecordVerification(new VerificationResult { Success = true, Status = "All Checks Passed" }, 50);

            var summary = tracker.GetCentralTelemetrySnapshot();

            Assert.Equal(2, summary.TotalModelRequests);
            Assert.Equal(1, summary.SuccessfulModelRequests);
            Assert.Equal(1, summary.FailedModelRequests);
            Assert.Equal(1, summary.ProviderFailures);
            Assert.Equal(1, summary.RetryRequests);
            Assert.Equal(1, summary.TotalToolCalls);
            Assert.Equal(1, summary.TotalVerificationRuns);
            Assert.Equal(1, summary.SuccessfulVerificationRuns);
            Assert.True(File.Exists(Path.Combine(project, ".mdai", "telemetry_summary.json")));

            tracker.SetProject(nextProject);
            summary = tracker.GetCentralTelemetrySnapshot();
            Assert.Empty(summary.RecentSessions);
        }
        finally
        {
            if (Directory.Exists(project)) Directory.Delete(project, true);
            if (Directory.Exists(nextProject)) Directory.Delete(nextProject, true);
        }
    }

    [Fact]
    public void RouterTelemetry_TracksFallbackCausesAndPersistsPerProject()
    {
        var project = Path.Combine(Path.GetTempPath(), "mdai_router_telemetry_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(project);

        try
        {
            var tracker = TokenTrackerService.Instance;
            tracker.SetProject(project);
            tracker.RecordRouterDecision("selected", "2 araç seçildi.", 0.9, 100);
            tracker.RecordRouterDecision("fallback-low-confidence", "Güven 40%, eşik 70%.", 0.4, 200);
            tracker.RecordRouterDecision("fallback-timeout", "Router timeout", 0, 45000);
            tracker.RecordRouterDecision("fallback-invalid-selection", "Bilinmeyen araç", 0.8, 300);
            tracker.RecordRouterDecision("empty-selection", "Araç gerekmiyor", 0.95, 50);

            var telemetry = tracker.GetRouterTelemetrySnapshot();

            Assert.Equal(5, telemetry.TotalRoutes);
            Assert.Equal(2, telemetry.SuccessfulSelections);
            Assert.Equal(3, telemetry.FallbackRoutes);
            Assert.Equal(1, telemetry.LowConfidenceRoutes);
            Assert.Equal(1, telemetry.TimeoutRoutes);
            Assert.Equal(1, telemetry.InvalidSelectionRoutes);
            Assert.Equal(1, telemetry.EmptyToolSelections);
            Assert.Equal(60, telemetry.FallbackRate);
            Assert.True(File.Exists(Path.Combine(project, ".mdai", "router_telemetry.json")));
        }
        finally
        {
            if (Directory.Exists(project)) Directory.Delete(project, true);
        }
    }
}
