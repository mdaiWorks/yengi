using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Threading.Tasks;

namespace mdaiAgent;

/// <summary>
/// Agent'ın yaptığı değişiklikleri build/test ile doğrular ve hata durumunda otomatik kurtarma sağlar.
/// Döngü: Yazılan kod → Build → Test → Başarılı? → Hayır: Error → Analyze → Suggest Fix → Retry
/// </summary>
public class AgentVerificationLoopService
{
    private readonly ToolExecutor _toolExecutor;
    private readonly Action<string> _terminalLog;
    private readonly ChatFlowService _chatFlowService;
    private const int MaxRetries = 3;
    private const int MaxAutoFixAttempts = 2;

    public AgentVerificationLoopService(ToolExecutor toolExecutor, ChatFlowService chatFlowService, Action<string> terminalLog)
    {
        _toolExecutor = toolExecutor;
        _chatFlowService = chatFlowService;
        _terminalLog = terminalLog;
    }

    public Task<VerificationResult> VerifyChangesAsync(List<string> changedFiles, string systemPrompt, string userMessage)
    {
        return VerifyChangesAsync(_toolExecutor.ProjectFolder ?? Environment.CurrentDirectory, changedFiles, systemPrompt, userMessage);
    }

    /// <summary>
    /// Kod değişiklikleri yaptıktan sonra doğrulama döngüsünü başlatır
    /// </summary>
    public async Task<VerificationResult> VerifyChangesAsync(string projectFolder, List<string> changedFiles, string systemPrompt, string userMessage = "")
    {
        var result = new VerificationResult { ChangedFiles = changedFiles };
        result.ChangedFileReview = ReviewChangedFiles(projectFolder, changedFiles);
        result.BuildTestResults.Add(("Changed Files Review", result.ChangedFileReview.Success, result.ChangedFileReview.Summary));
        if (!result.ChangedFileReview.Success)
            result.ErrorSummary = $"Değişen dosya incelemesi başarısız: {result.ChangedFileReview.Summary}";
        
        _terminalLog?.Invoke(LocalizationManager.Instance.GetString("VerificationLoopBasladi"));
        
        // Adım 1: Build et
        _terminalLog?.Invoke(LocalizationManager.Instance.GetString("BuildKontrolEdiliyor"));
        var buildResult = await VerifyBuildAsync(projectFolder);
        result.BuildTestResults.Add(("Initial Build", buildResult.Success, buildResult.Error ?? ""));
        
        if (!buildResult.Success)
        {
            _terminalLog?.Invoke($"❌ Build başarısız: {buildResult.Error}");
            
            // Adım 1b: Build hatasında otomatik düzeltme denemesi
            _terminalLog?.Invoke("🔧 Build hatası için otomatik düzeltme başlatılıyor...");
            result.Success = await AttemptBuildFixAsync(projectFolder, buildResult.Error, systemPrompt, userMessage, result);
            
            if (result.Success)
            {
                _terminalLog?.Invoke("✓ Build hatası otomatik düzeltildi! Testler kontrol ediliyor...");
                result.Status = "Build Fixed Automatically";
                // Build düzeltildikten sonra testleri de koş
                var testAfterBuildFix = await VerifyTestsAsync(projectFolder);
                result.BuildTestResults.Add(("Tests After Build Fix", testAfterBuildFix.Success, testAfterBuildFix.Error ?? ""));
                if (!testAfterBuildFix.Success)
                {
                    result.Success = await AttemptAutoFixAsync(projectFolder, testAfterBuildFix.Error, systemPrompt, userMessage, result);
                    result.Status = result.Success ? "Fixed Automatically" : "Test Failure After Build Fix - Manual Fix Needed";
                }
                else
                {
                    result.Status = "All Checks Passed After Build Fix";
                }
            }
            else
            {
                _terminalLog?.Invoke("❌ Build hatası otomatik düzeltilemedi");
                result.Status = "Build Error - Manual Fix Needed";
                result.ErrorSummary = buildResult.Error;
            }
            
            return result;
        }
        
        _terminalLog?.Invoke(LocalizationManager.Instance.GetString("BuildBasariliLog"));
        
        // Adım 2: Test et
        _terminalLog?.Invoke(LocalizationManager.Instance.GetString("TestKontrolEdiliyor"));
        var testResult = await VerifyTestsAsync(projectFolder);
        result.BuildTestResults.Add(("Initial Tests", testResult.Success, testResult.Error ?? ""));
        
        if (!testResult.Success)
        {
            _terminalLog?.Invoke($"❌ Testler başarısız: {testResult.Error}");
            
            // Adım 3: Otomatik düzeltme denemesi
            result.Success = await AttemptAutoFixAsync(projectFolder, testResult.Error, systemPrompt, userMessage, result);
            
            if (result.Success)
            {
                _terminalLog?.Invoke("✓ Otomatik düzeltme başarılı!");
                result.Status = "Fixed Automatically";
            }
            else
            {
                _terminalLog?.Invoke("❌ Otomatik düzeltme başarısız");
                result.Status = "Test Failure - Manual Fix Needed";
            }
            
            return result;
        }
        
        _terminalLog?.Invoke(LocalizationManager.Instance.GetString("TumTestlerGectiLog"));
        
        // [FAZ 3] Adım 3: Statik Analiz (Roslyn Diagnostics)
        _terminalLog?.Invoke(LocalizationManager.Instance.GetString("StatikAnalizCalistiriliyor"));
        var analysisResult = RunStaticAnalysis(projectFolder, buildResult.Output ?? "");
        if (!analysisResult.Success && !string.IsNullOrEmpty(analysisResult.Error))
        {
            _terminalLog?.Invoke($"⚠️ Statik Analiz Uyarıları ({analysisResult.Error.Split('\n').Length} adet bulundu, süreci kesmez)");
            result.BuildTestResults.Add(("Static Analysis", true, analysisResult.Error));
            result.ErrorSummary = (result.ErrorSummary ?? "") + "\n\n⚠️ Statik Analiz Uyarıları:\n" + analysisResult.Error;
            // Statik analiz uyarıları süreci KESMEZ, sadece raporlanır.
        }
        else
        {
            _terminalLog?.Invoke(LocalizationManager.Instance.GetString("StatikAnalizTemiz"));
            result.BuildTestResults.Add(("Static Analysis", true, ""));
        }

        result.Success = result.ChangedFileReview.Success;
        result.Status = result.Success ? "All Checks Passed" : "Changed Files Review Failed";
        if (result.Success && result.Summary.Contains("uyarı", StringComparison.OrdinalIgnoreCase))
        {
            result.Status = "All Checks Passed With Warnings";
        }
        return result;
    }

    private enum ProjectType { DotNet, Node, Python, Flutter, Unknown }

    private static ChangedFileReviewResult ReviewChangedFiles(string projectFolder, IEnumerable<string> changedFiles)
    {
        var normalizedFiles = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var invalidEntries = 0;
        var existingFiles = 0;
        var missingFiles = 0;

        foreach (var changedFile in changedFiles ?? Enumerable.Empty<string>())
        {
            if (string.IsNullOrWhiteSpace(changedFile))
            {
                invalidEntries++;
                continue;
            }

            try
            {
                var fullPath = Path.GetFullPath(Path.IsPathRooted(changedFile)
                    ? changedFile
                    : Path.Combine(projectFolder, changedFile));

                if (!normalizedFiles.Add(fullPath))
                    continue;

                if (File.Exists(fullPath))
                    existingFiles++;
                else
                    missingFiles++;
            }
            catch
            {
                invalidEntries++;
            }
        }

        var success = invalidEntries == 0;
        var summary = $"{normalizedFiles.Count} benzersiz yol; {existingFiles} mevcut, {missingFiles} bulunamadı";
        if (invalidEntries > 0)
            summary += $"; {invalidEntries} geçersiz kayıt";

        return new ChangedFileReviewResult
        {
            Success = success,
            UniqueFileCount = normalizedFiles.Count,
            ExistingFileCount = existingFiles,
            MissingFileCount = missingFiles,
            InvalidEntryCount = invalidEntries,
            Summary = summary
        };
    }

    private ProjectType DetectProjectType(string folder)
    {
        try
        {
            // Ana dizinde .sln varsa veya alt dizinlerde .csproj varsa .NET projesidir
            if (System.IO.Directory.GetFiles(folder, "*.sln").Length > 0 ||
                System.IO.Directory.GetFiles(folder, "*.csproj", System.IO.SearchOption.AllDirectories).Length > 0)
                return ProjectType.DotNet;
                
            if (System.IO.File.Exists(System.IO.Path.Combine(folder, "package.json"))) return ProjectType.Node;
            if (System.IO.File.Exists(System.IO.Path.Combine(folder, "pubspec.yaml"))) return ProjectType.Flutter;
            
            // Python için ana dizindeki py dosyaları veya requirements.txt
            if (System.IO.File.Exists(System.IO.Path.Combine(folder, "requirements.txt")) || 
                System.IO.Directory.GetFiles(folder, "*.py").Length > 0) 
                return ProjectType.Python;
        }
        catch 
        {
            // Yetki hatası vs. olursa çökmeyi engelle
        }
        
        return ProjectType.Unknown;
    }

    /// <summary>
    /// Build başarısını doğrular
    /// </summary>
    private async Task<BuildTestResult> VerifyBuildAsync(string projectFolder)
    {
        var projectType = DetectProjectType(projectFolder);
        string? buildCmd = projectType switch
        {
            ProjectType.DotNet => $"dotnet build \"{projectFolder}\" -c Debug -nologo",
            ProjectType.Flutter => $"flutter analyze \"{projectFolder}\"",
            // Node ve Python için varsayılan bir derleme adımı gerekmiyor veya farklı komutlar (npm run build) gerekebilir. 
            // Şimdilik testleri koşmak yeterli.
            _ => null
        };

        if (string.IsNullOrEmpty(buildCmd))
        {
            _terminalLog?.Invoke(LocalizationManager.Instance.GetString("VerificationBuildStepSkipped").Replace("{projectType}", projectType.ToString()));
            return new BuildTestResult { Success = true };
        }
        
        try
        {
            var result = await _toolExecutor.ExecuteAsync("ExecuteTerminalCommand", 
                JsonSerializer.Serialize(new { command = buildCmd }));
            
            if (result.Success)
            {
                return new BuildTestResult { Success = true, Output = result.Output };
            }
            
            return new BuildTestResult 
            { 
                Success = false, 
                Error = ExtractErrorMessage(result.Output) 
            };
        }
        catch (Exception ex)
        {
            return new BuildTestResult { Success = false, Error = ex.Message };
        }
    }

    /// <summary>
    /// Test başarısını doğrular
    /// </summary>
    private async Task<BuildTestResult> VerifyTestsAsync(string projectFolder)
    {
        var projectType = DetectProjectType(projectFolder);

        // Unknown (vanilla HTML/CSS/JS) projeler için test adımını atla
        if (projectType == ProjectType.Unknown)
        {
            _terminalLog?.Invoke(LocalizationManager.Instance.GetString("VerificationPureWebTestSkipped"));
            return new BuildTestResult { Success = true };
        }

        // Node projesinde package.json'da test scripti yoksa atla
        if (projectType == ProjectType.Node)
        {
            var pkgPath = System.IO.Path.Combine(projectFolder, "package.json");
            if (System.IO.File.Exists(pkgPath))
            {
                try
                {
                    var pkgContent = await System.IO.File.ReadAllTextAsync(pkgPath);
                    var pkgDoc = JsonDocument.Parse(pkgContent);
                    bool hasTestScript = pkgDoc.RootElement.TryGetProperty("scripts", out var scripts)
                        && scripts.TryGetProperty("test", out var testScript)
                        && !string.IsNullOrWhiteSpace(testScript.GetString())
                        && testScript.GetString()?.Contains("echo") == false; // "echo 'no test'" gibi placeholder'ları atla

                    if (!hasTestScript)
                    {
                        _terminalLog?.Invoke($"[Verification] package.json'da test scripti bulunamadı. Test adımı atlanıyor.");
                        return new BuildTestResult { Success = true };
                    }
                }
                catch
                {
                    _terminalLog?.Invoke($"[Verification] package.json okunamadı. Test adımı atlanıyor.");
                    return new BuildTestResult { Success = true };
                }
            }
            else
            {
                _terminalLog?.Invoke($"[Verification] package.json bulunamadı. Test adımı atlanıyor.");
                return new BuildTestResult { Success = true };
            }
        }

        // Flutter için test dosyası yoksa atla
        if (projectType == ProjectType.Flutter)
        {
            var testDir = System.IO.Path.Combine(projectFolder, "test");
            if (!System.IO.Directory.Exists(testDir) || !System.IO.Directory.EnumerateFiles(testDir, "*.dart").Any())
            {
                _terminalLog?.Invoke($"[Verification] Flutter test dosyası bulunamadı. Test adımı atlanıyor.");
                return new BuildTestResult { Success = true };
            }
        }

        // Python için test dosyası yoksa atla
        if (projectType == ProjectType.Python)
        {
            var hasTests = System.IO.Directory.EnumerateFiles(projectFolder, "test_*.py", System.IO.SearchOption.AllDirectories).Any()
                        || System.IO.Directory.EnumerateFiles(projectFolder, "*_test.py", System.IO.SearchOption.AllDirectories).Any();
            if (!hasTests)
            {
                _terminalLog?.Invoke($"[Verification] Python test dosyası bulunamadı. Test adımı atlanıyor.");
                return new BuildTestResult { Success = true };
            }
        }

        // .NET için test projesi yoksa atla
        if (projectType == ProjectType.DotNet)
        {
            var hasTestProject = System.IO.Directory.EnumerateFiles(projectFolder, "*.csproj", System.IO.SearchOption.AllDirectories)
                .Any(f => System.IO.File.ReadAllText(f).Contains("Microsoft.NET.Test.Sdk", StringComparison.OrdinalIgnoreCase));
            if (!hasTestProject)
            {
                _terminalLog?.Invoke($"[Verification] .NET test projesi bulunamadı. Test adımı atlanıyor.");
                return new BuildTestResult { Success = true };
            }
        }

        string? testCmd = projectType switch
        {
            ProjectType.DotNet   => $"dotnet test \"{projectFolder}\" -nologo -v minimal",
            ProjectType.Node     => "npm test",
            ProjectType.Flutter  => $"flutter test \"{projectFolder}\"",
            ProjectType.Python   => "pytest",
            _                    => null
        };

        if (string.IsNullOrEmpty(testCmd))
        {
            _terminalLog?.Invoke($"[Verification] {projectType} projesi için test adımı atlanıyor.");
            return new BuildTestResult { Success = true };
        }
        
        try
        {
            var result = await _toolExecutor.ExecuteAsync("ExecuteTerminalCommand",
                JsonSerializer.Serialize(new { command = testCmd, cwd = projectFolder }));
            
            if (result.Success)
            {
                return new BuildTestResult { Success = true };
            }
            
            return new BuildTestResult 
            { 
                Success = false, 
                Error = ExtractTestFailure(result.Output) 
            };
        }
        catch (Exception ex)
        {
            return new BuildTestResult { Success = false, Error = ex.Message };
        }
    }

    /// <summary>
    /// Test hatası için otomatik düzeltme denemesi (test → fix → retest döngüsü)
    /// </summary>
    private async Task<bool> AttemptAutoFixAsync(string projectFolder, string errorMsg, string systemPrompt, string userMessage, VerificationResult result)
    {
        if (result.BuildTestResults.Count >= MaxRetries)
        {
            _terminalLog?.Invoke("⚠️ Max retry sayısı aşıldı");
            return false;
        }
        
        for (int attempt = 1; attempt <= MaxAutoFixAttempts; attempt++)
        {
            _terminalLog?.Invoke($"🔧 Test düzeltme denemesi {attempt}/{MaxAutoFixAttempts}...");
            
            try
            {
                var fixPrompt = BuildTestFixPrompt(errorMsg, userMessage);
                
                var fixResult = await _chatFlowService.SendMessageInternalAsync(
                    fixPrompt,
                    null,
                    null,
                    null,
                    systemPrompt,
                    false,
                    false,
                    null,
                    default,
                    null,
                    null,
                    true,
                    false,
                    skipVerification: true
                );
                
                if (!fixResult.Success)
                {
                    _terminalLog?.Invoke($"Test düzeltme denemesi {attempt} başarısız");
                    continue;
                }
                
                // Tekrar test et
                var retestResult = await VerifyTestsAsync(projectFolder);
                result.BuildTestResults.Add(($"Test Retry {attempt}", retestResult.Success, retestResult.Error ?? ""));
                
                if (retestResult.Success)
                {
                    _terminalLog?.Invoke($"✓ Test denemede {attempt} düzeltildi!");
                    return true;
                }
                
                errorMsg = retestResult.Error ?? errorMsg;
            }
            catch (Exception ex)
            {
                _terminalLog?.Invoke($"Test düzeltme denemesi hatası: {ex.Message}");
            }
        }
        
        return false;
    }

    /// <summary>
    /// Build hatası için otomatik düzeltme denemesi (build → fix → rebuild → test döngüsü)
    /// </summary>
    private async Task<bool> AttemptBuildFixAsync(string projectFolder, string? errorMsg, string systemPrompt, string userMessage, VerificationResult result)
    {
        if (result.BuildTestResults.Count >= MaxRetries)
        {
            _terminalLog?.Invoke("⚠️ Max retry sayısı aşıldı");
            return false;
        }

        for (int attempt = 1; attempt <= MaxAutoFixAttempts; attempt++)
        {
            _terminalLog?.Invoke($"🔧 Build düzeltme denemesi {attempt}/{MaxAutoFixAttempts}...");

            try
            {
                var fixPrompt = BuildBuildFixPrompt(errorMsg ?? "Build başarısız", userMessage);

                var fixResult = await _chatFlowService.SendMessageInternalAsync(
                    fixPrompt,
                    null,
                    null,
                    null,
                    systemPrompt,
                    false,
                    false,
                    null,
                    default,
                    null,
                    null,
                    true,
                    false,
                    skipVerification: true
                );

                if (!fixResult.Success)
                {
                    _terminalLog?.Invoke($"Build düzeltme denemesi {attempt} agent yanıtı başarısız");
                    continue;
                }

                // Tekrar build et
                var rebuildResult = await VerifyBuildAsync(projectFolder);
                result.BuildTestResults.Add(($"Build Retry {attempt}", rebuildResult.Success, rebuildResult.Error ?? ""));

                if (rebuildResult.Success)
                {
                    _terminalLog?.Invoke($"✓ Build denemede {attempt} düzeltildi!");
                    return true;
                }

                // Bir sonraki denemede güncel hata mesajını kullan
                errorMsg = rebuildResult.Error ?? errorMsg;
            }
            catch (Exception ex)
            {
                _terminalLog?.Invoke($"Build düzeltme denemesi hatası: {ex.Message}");
            }
        }

        return false;
    }

    /// <summary>
    /// Hata mesajından kısa özetini çıkarır
    /// </summary>
    private string ExtractErrorMessage(string output)
    {
        var lines = output.Split('\n');
        var errorLines = lines.Where(l => l.Contains("error", StringComparison.OrdinalIgnoreCase)).ToList();
        
        if (errorLines.Count > 0)
        {
            // Sadece ilk hatayı değil, ilk 5 hatayı al (AI hepsini tek seferde düzeltebilsin)
            return string.Join("\n", errorLines.Take(5).Select(e => e.Trim()));
        }
        
        return output.Length > 200 ? output.Substring(0, 200) + "..." : output;
    }

    /// <summary>
    /// Test başarısızlık mesajını analiz eder
    /// </summary>
    private string ExtractTestFailure(string output)
    {
        var match = Regex.Match(output, @"başarısız:\s+(\d+)");
        if (match.Success)
            return $"{match.Groups[1].Value} test başarısız oldu";
        
        var errorMatch = Regex.Match(output, @"Error.*?(?:\n|$)");
        if (errorMatch.Success)
            return errorMatch.Value.Trim();
        
        return "Testler başarısız";
    }

    /// <summary>
    /// Agent'a test hatası düzeltme prompt'u oluşturur
    /// </summary>
    private string BuildTestFixPrompt(string errorMsg, string originalMessage)
    {
        return $@"[TEST HATASI DÜZELTME - OTOMATIK VERIFICATION LOOP]

Unit testler başarısız oldu:

```
{errorMsg}
```

Lütfen bu test hatasını şu şekilde düzelt:
1. Başarısız olan testin neden başarısız olduğunu analiz et
2. İlgili kaynak dosyaları ve test dosyalarını kontrol et
3. Kaynak kodu veya testi uygun şekilde düzelt
4. Yan etkilerden kaçın — başka testleri bozma

Orijinal Talep: {originalMessage}";
    }

    /// <summary>
    /// Agent'a build hatası düzeltme prompt'u oluşturur
    /// </summary>
    private string BuildBuildFixPrompt(string errorMsg, string originalMessage)
    {
        return $@"[BUILD HATASI DÜZELTME - OTOMATIK VERIFICATION LOOP]

Proje build edilemiyor. Compiler hatası:

```
{errorMsg}
```

Lütfen bu derleme hatasını şu şekilde düzelt:
1. Hata mesajını ve hangi dosya/satırda oluştuğunu analiz et
2. İlgili dosyayı aç ve hatayı düzelt (tip uyumsuzluğu, eksik using, tanımsız üye vb.)
3. Düzeltmenin başka derleme hatası yaratmadığından emin ol
4. Çok sayıda hata varsa birbirine bağlı olanları birlikte düzelt

        Orijinal Talep: {originalMessage}";
    }

    // ============================================================
    // [FAZ 3] Statik Analiz / Roslyn Diagnostics
    // ============================================================
    /// <summary>
    /// Statik Analiz / Diagnostics adımı (Roslyn veya linter uyarılarını çeker)
    /// </summary>
    private BuildTestResult RunStaticAnalysis(string projectFolder, string buildOutput)
    {
        var projectType = DetectProjectType(projectFolder);
        if (projectType != ProjectType.DotNet)
        {
            // Şimdilik sadece .NET projeleri için Roslyn statik analizi destekleniyor
            return new BuildTestResult { Success = true };
        }

        try
        {
            // İkinci bir build komutu çalıştırmak yerine, ilk build'in çıktısını doğrudan analiz et.
            // Bu sayede MSBuild'in "incremental build" önbelleği bizi körleştiremiyor.
            var output = buildOutput ?? "";
            
            // "CS1234" veya "VSTHRD123" gibi uyarı kodlarını dil bağımsız (warning/uyarı) yakala
            var warningLines = output.Split('\n')
                .Where(l => Regex.IsMatch(l, @"\b(?:CS|VSTHRD)\d{3,}\b", RegexOptions.IgnoreCase))
                .Select(l => l.Trim())
                .Where(l => !string.IsNullOrEmpty(l))
                .ToList();

            if (warningLines.Count > 0)
            {
                // Çok fazla uyarı varsa AI'ın token limitini şişirmemek için kırp
                var formattedWarnings = string.Join("\n", warningLines.Take(10));
                if (warningLines.Count > 10) 
                {
                    formattedWarnings += $"\n... ve {warningLines.Count - 10} uyarı daha.";
                }
                
                return new BuildTestResult { Success = false, Error = formattedWarnings };
            }

            return new BuildTestResult { Success = true };
        }
        catch
        {
            // Statik analiz çalışırken oluşan teknik bir hata build/doğrulama döngüsünü KIRMAMALIDIR.
            return new BuildTestResult { Success = true }; 
        }
    }
}

/// <summary>
/// Doğrulama sonuçları
/// </summary>
public class VerificationResult
{
    public bool Success { get; set; }
    public string Status { get; set; } = "Verifying";
    public List<string> ChangedFiles { get; set; } = new();
    public List<(string Step, bool Success, string Error)> BuildTestResults { get; set; } = new();
    public string ErrorSummary { get; set; } = "";
    public ChangedFileReviewResult ChangedFileReview { get; set; } = new();
    public string Summary
    {
        get
        {
            var passedSteps = BuildTestResults.Count(step => step.Success && !IsWarning(step));
            var failedSteps = BuildTestResults.Count(step => !step.Success);
            var warningCount = BuildTestResults.Count(IsWarning);
            var fileCount = ChangedFiles.Distinct(StringComparer.OrdinalIgnoreCase).Count();
            var summary = $"Durum: {Status}; Değişen dosya: {fileCount}; Kontroller: {passedSteps} başarılı, {failedSteps} başarısız, {warningCount} uyarı.";

            return string.IsNullOrWhiteSpace(ErrorSummary)
                ? summary
                : summary + $" Ayrıntı: {ErrorSummary.Trim()}";
        }
    }

    private static bool IsWarning((string Step, bool Success, string Error) step)
    {
        if (!step.Success || string.IsNullOrWhiteSpace(step.Error))
            return false;

        var text = step.Error.Trim();
        return text.Contains("uyarı", StringComparison.OrdinalIgnoreCase)
            || text.Contains("warning", StringComparison.OrdinalIgnoreCase)
            || text.Contains("warn", StringComparison.OrdinalIgnoreCase)
            || text.Contains("CS", StringComparison.OrdinalIgnoreCase)
            || text.Contains("VSTHRD", StringComparison.OrdinalIgnoreCase);
    }
}

public class ChangedFileReviewResult
{
    public bool Success { get; set; } = true;
    public int UniqueFileCount { get; set; }
    public int ExistingFileCount { get; set; }
    public int MissingFileCount { get; set; }
    public int InvalidEntryCount { get; set; }
    public string Summary { get; set; } = "Değişen dosya kaydı bulunamadı.";
}

/// <summary>
/// Build/Test sonuçları
/// </summary>
public class BuildTestResult
{
    public bool Success { get; set; }
    public string? Error { get; set; }
    public string? Output { get; set; }
}
