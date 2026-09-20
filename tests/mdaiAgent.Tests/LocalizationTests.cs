using Xunit;

namespace mdaiAgent.Tests;

public class LocalizationTests
{
    [Fact]
    public void DiagnosticsResourcesExistForTurkishEnglishAndChinese()
    {
        var localization = LocalizationManager.Instance;

        foreach (var language in new[] { "tr", "en", "zh" })
        {
            localization.SetLanguage(language);
            Assert.NotEqual($"[DiagnosticsTab]", localization.GetString("DiagnosticsTab"));
            Assert.NotEqual($"[ClearTelemetry]", localization.GetString("ClearTelemetry"));
            Assert.NotEqual($"[TelemetryProjectHint]", localization.GetString("TelemetryProjectHint"));
        }

        localization.SetLanguage("tr");
        Assert.True(localization.IsTurkish);
        localization.SetLanguage("en");
        Assert.True(localization.IsEnglish);
        localization.SetLanguage("zh");
        Assert.True(localization.IsChinese);
        localization.SetLanguage("tr");
    }
}
