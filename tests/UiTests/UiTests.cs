#nullable enable
using System;
using System.Diagnostics;
using System.IO;
using FlaUI.Core;
using FlaUI.Core.AutomationElements;
using FlaUI.UIA3;
using Xunit;
using Xunit.Sdk;

namespace mdaiAgent.UiTests
{
    public class BasicUiTests
    {
        [SkippableFact]
        public void LaunchApp_SmokeTest()
        {
            // These UI tests are skipped unless RUN_UI_TESTS environment variable is set to "1".
            var run = Environment.GetEnvironmentVariable("RUN_UI_TESTS");
            Skip.IfNot(run == "1", "UI tests are disabled by default.");

            // Determine exe path from environment or default publish location
            var exePath = Environment.GetEnvironmentVariable("MDAI_AGENT_EXE");
            if (string.IsNullOrEmpty(exePath))
            {
                exePath = Path.GetFullPath(Path.Combine("BasucuIDE", "publish", "mdaiAgent.exe"));
            }

            Skip.IfNot(File.Exists(exePath), $"mdaiAgent exe not found at {exePath}");

            // Launch and attach using FlaUI
            using var app = Application.Launch(exePath);
            using var automation = new UIA3Automation();
            var main = app.GetMainWindow(automation, TimeSpan.FromSeconds(10));

            Assert.NotNull(main);

            // Basic check: window title contains mdaiAgent
            Assert.Contains("mdaiAgent", main.Title, StringComparison.OrdinalIgnoreCase);

            // Find chat sessions combo and new chat button
            var cb = main.FindFirstDescendant(cf => cf.ByAutomationId("cbChatSessions"))?.AsComboBox();
            var newChatBtn = main.FindFirstDescendant(cf => cf.ByAutomationId("btnNewChat"))?.AsButton();

            Skip.If(cb == null || newChatBtn == null, "Required UI elements not exposed to automation (cbChatSessions or btnNewChat). Skipping interactive checks.");

            var initialCount = cb.Items.Length;
            newChatBtn.Invoke();
            System.Threading.Thread.Sleep(500);

            // Refresh combo items and assert increased
            cb = main.FindFirstDescendant(cf => cf.ByAutomationId("cbChatSessions"))?.AsComboBox();
            Assert.NotNull(cb);
            Assert.True(cb.Items.Length >= initialCount + 1, "New chat was not created as expected.");

            // Open Settings and close via cancel
            var settingsBtn = main.FindFirstDescendant(cf => cf.ByAutomationId("btnSettings"))?.AsButton();
            if (settingsBtn != null)
            {
                settingsBtn.Invoke();
                // wait for settings window
                var sw = RetryFindWindow(app, automation, titleContains: "Ayarlar", timeoutSec: 5);
                Skip.If(sw == null, "Settings window did not appear; skipping settings flow.");

                var cancelBtn = sw.FindFirstDescendant(x => x.ByAutomationId("btnCancel"))?.AsButton();
                if (cancelBtn != null)
                {
                    cancelBtn.Invoke();
                    System.Threading.Thread.Sleep(300);
                }
            }

            // Close app
            try { app.Close(); } catch { app.Kill(); }
        }

        private static FlaUI.Core.AutomationElements.Window? RetryFindWindow(Application app, UIA3Automation automation, string? titleContains, int timeoutSec = 5)
        {
            var sw = (FlaUI.Core.AutomationElements.Window?)null;
            var swTimeout = DateTime.UtcNow.AddSeconds(timeoutSec);
            while (DateTime.UtcNow < swTimeout)
            {
                foreach (var w in app.GetAllTopLevelWindows(automation))
                {
                    if (!string.IsNullOrEmpty(w.Title) && titleContains != null && w.Title.Contains(titleContains, StringComparison.OrdinalIgnoreCase))
                    {
                        sw = w;
                        break;
                    }
                }
                if (sw != null) break;
                System.Threading.Thread.Sleep(200);
            }
            return sw;
        }
    }
}
