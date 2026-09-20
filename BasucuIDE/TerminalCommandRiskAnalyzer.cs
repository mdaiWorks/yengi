using System;

namespace mdaiAgent;

public enum TerminalCommandRiskLevel
{
    Low,
    Medium,
    High,
    Critical
}

public sealed record TerminalCommandRiskAssessment(
    TerminalCommandRiskLevel Level,
    string Reason);

public static class TerminalCommandRiskAnalyzer
{
    public static TerminalCommandRiskAssessment Analyze(string command)
    {
        var normalized = (command ?? string.Empty).Trim();
        if (string.IsNullOrWhiteSpace(normalized))
            return new TerminalCommandRiskAssessment(TerminalCommandRiskLevel.Critical, "Komut boş olamaz.");

        if (ContainsAny(normalized,
                "rm -rf", "rm -r -f", "del /f /q", "rmdir /s /q", "remove-item -recurse -force",
                "format ", "diskpart", "shutdown ", "restart-computer", "git reset --hard", "git clean -fd"))
        {
            return new TerminalCommandRiskAssessment(
                TerminalCommandRiskLevel.Critical,
                "Dosya sistemi, disk veya geri alınması zor Git işlemi içeriyor.");
        }

        if (ContainsAny(normalized,
                "git push", "git commit", "git checkout", "git switch", "git merge",
                "npm publish", "dotnet nuget push", "set-content ", "out-file "))
        {
            return new TerminalCommandRiskAssessment(
                TerminalCommandRiskLevel.High,
                "Uzak depo, paket yayını veya kalıcı dosya/depo değişikliği yapabilir.");
        }

        if (ContainsAny(normalized,
                "npm install", "npm uninstall", "pnpm add", "yarn add", "pip install",
                "dotnet restore", "dotnet tool install", "flutter pub get", "git pull",
                "git fetch", "curl ", "invoke-webrequest"))
        {
            return new TerminalCommandRiskAssessment(
                TerminalCommandRiskLevel.Medium,
                "Dış ağdan paket/veri indirebilir veya proje bağımlılıklarını değiştirebilir.");
        }

        return new TerminalCommandRiskAssessment(TerminalCommandRiskLevel.Low, "Salt okuma veya düşük etkili komut.");
    }

    private static bool ContainsAny(string value, params string[] patterns)
    {
        foreach (var pattern in patterns)
        {
            if (value.Contains(pattern, StringComparison.OrdinalIgnoreCase))
                return true;
        }

        return false;
    }
}
