using System.Configuration;
using System.Data;
using System.Globalization;
using System.Windows;
using System.IO;
using System.Text.Json;
using System.Reflection;
using System.Threading;
using ICSharpCode.AvalonEdit.Highlighting;
using ICSharpCode.AvalonEdit.Highlighting.Xshd;

namespace mdaiAgent;

/// <summary>
/// Interaction logic for App.xaml
/// </summary>
public partial class App : Application
{
    private static readonly string CrashLogDir = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
        "Yengi", "crash_logs");

    private static readonly string SessionStateFile = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
        "Yengi", "session_state.json");

    public App()
    {
        EnsureAppDataMigration();

        // Global exception handling for unhandled exceptions
        DispatcherUnhandledException += App_DispatcherUnhandledException;
        AppDomain.CurrentDomain.UnhandledException += CurrentDomain_UnhandledException;
        TaskScheduler.UnobservedTaskException += TaskScheduler_UnobservedTaskException;

        // Load custom HTML highlighting definition
        LoadCustomHighlightings();

        InitializeComponent();
    }

    private static void EnsureAppDataMigration()
    {
        try
        {
            string oldPath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "mdaiAgent");
            string newPath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "Yengi");

            if (Directory.Exists(oldPath) && !Directory.Exists(newPath))
            {
                Directory.CreateDirectory(newPath);
                foreach (string dirPath in Directory.GetDirectories(oldPath, "*", SearchOption.AllDirectories))
                {
                    Directory.CreateDirectory(dirPath.Replace(oldPath, newPath));
                }
                foreach (string filePath in Directory.GetFiles(oldPath, "*.*", SearchOption.AllDirectories))
                {
                    File.Copy(filePath, filePath.Replace(oldPath, newPath), true);
                }
            }
        }
        catch { /* Ignore migration errors */ }
    }

    private void Application_Startup(object sender, StartupEventArgs e)
    {
        var settings = SettingsWindow.GetSettings();

        string? cmdLang = ParseLanguageFromArgs(e.Args);
        if (!string.IsNullOrEmpty(cmdLang))
        {
            settings.Language = cmdLang;
            SettingsWindow.SaveSettings(settings);
        }

        var systemLanguageSupported = CultureInfo.CurrentUICulture.Name.StartsWith("tr", StringComparison.OrdinalIgnoreCase) ||
                                      CultureInfo.CurrentUICulture.Name.StartsWith("en", StringComparison.OrdinalIgnoreCase) ||
                                      CultureInfo.CurrentUICulture.Name.StartsWith("zh", StringComparison.OrdinalIgnoreCase);

        if (string.IsNullOrWhiteSpace(settings.Language))
        {
            var detectedLanguage = LocalizationManager.DetectSystemLanguage();

            if (systemLanguageSupported)
            {
                settings.Language = detectedLanguage;
            }
            else
            {
                var languageDialog = new LanguageSelectionWindow();
                bool? dialogResult = languageDialog.ShowDialog();

                settings.Language = dialogResult == true ? languageDialog.SelectedLanguage : "tr";
            }

            SettingsWindow.SaveSettings(settings);
        }

        LocalizationManager.Instance.Initialize(settings.Language);

        // Silent background telemetry heartbeat
        _ = Services.TelemetryAnalyticsService.ReportHeartbeatAsync();

        var mainWindow = new MainWindow();
        MainWindow = mainWindow;
        mainWindow.Show();
    }

    private static string? ParseLanguageFromArgs(string[] args)
    {
        if (args == null || args.Length == 0) return null;
        foreach (var arg in args)
        {
            if (arg.StartsWith("/lang=", StringComparison.OrdinalIgnoreCase) ||
                arg.StartsWith("--lang=", StringComparison.OrdinalIgnoreCase) ||
                arg.StartsWith("-lang=", StringComparison.OrdinalIgnoreCase))
            {
                var parts = arg.Split('=', 2);
                if (parts.Length == 2)
                {
                    var code = parts[1].Trim().ToLowerInvariant();
                    if (code == "en" || code == "english") return "en";
                    if (code == "tr" || code == "turkish") return "tr";
                    if (code == "zh" || code == "chinese") return "zh";
                }
            }
        }
        return null;
    }

    public static void ApplyLanguage(string? languageCode)
    {
        LocalizationManager.Instance.SetLanguage(languageCode);

        var settings = SettingsWindow.GetSettings();
        settings.Language = LocalizationManager.Instance.CurrentLanguageCode;
        SettingsWindow.SaveSettings(settings);
    }

    private void LoadCustomHighlightings()
    {
        try
        {
            System.Diagnostics.Debug.WriteLine("=== LoadCustomHighlightings starting ===");
            var assembly = Assembly.GetExecutingAssembly();
            var resourceName = "mdaiAgent.Themes.Highlighting.HTML.xshd";
            System.Diagnostics.Debug.WriteLine($"Trying to load resource: {resourceName}");

            using var stream = assembly.GetManifestResourceStream(resourceName);
            if (stream != null)
            {
                System.Diagnostics.Debug.WriteLine("Resource stream found!");
                using var reader = new System.Xml.XmlTextReader(stream);
                var xshd = HighlightingLoader.LoadXshd(reader);
                System.Diagnostics.Debug.WriteLine($"Loaded XSHD: {xshd.Name}");
                var highlighting = HighlightingLoader.Load(xshd, null); // ReferenceResolver'ı null olarak geç
                System.Diagnostics.Debug.WriteLine($"Loaded highlighting: {highlighting.Name}");

                // Register the custom HTML highlighting with explicit extensions
                var extensions = new string[] { ".htm", ".html", ".hta" };
                HighlightingManager.Instance.RegisterHighlighting(highlighting.Name, extensions, highlighting);
                System.Diagnostics.Debug.WriteLine($"Registered custom highlighting: {highlighting.Name} with extensions: {string.Join(", ", extensions)}");
            }
            else
            {
                System.Diagnostics.Debug.WriteLine("Resource stream NOT found!");
                var allResources = assembly.GetManifestResourceNames();
                System.Diagnostics.Debug.WriteLine("Available resources: " + string.Join(", ", allResources));
            }
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Failed to load custom highlightings: {ex.Message}\nStack Trace: {ex.StackTrace}");
        }
        System.Diagnostics.Debug.WriteLine("=== LoadCustomHighlightings done ===");
    }

    private void App_DispatcherUnhandledException(object sender, System.Windows.Threading.DispatcherUnhandledExceptionEventArgs e)
    {
        LogCrash(e.Exception, "DispatcherUnhandledException");
        e.Handled = true;

        var loc = LocalizationManager.Instance;
        MessageBox.Show(
            $"{loc.GetString("ProgramCrashMessage")}\n\n{e.Exception.Message}\n\n{loc.GetString("LogAt")} {CrashLogDir}",
            loc.GetString("CriticalError"),
            MessageBoxButton.OK,
            MessageBoxImage.Error);

        Current.Shutdown(1);
    }

    private void CurrentDomain_UnhandledException(object sender, System.UnhandledExceptionEventArgs e)
    {
        if (e.ExceptionObject is Exception ex)
        {
            LogCrash(ex, "UnhandledException");
        }
    }

    private void TaskScheduler_UnobservedTaskException(object? sender, System.Threading.Tasks.UnobservedTaskExceptionEventArgs e)
    {
        LogCrash(e.Exception, "UnobservedTaskException");
        e.SetObserved();
    }

    private static void LogCrash(Exception ex, string source)
    {
        try
        {
            Directory.CreateDirectory(CrashLogDir);

            string timestamp = DateTime.Now.ToString("yyyyMMdd_HHmmss");
            string logFile = Path.Combine(CrashLogDir, $"crash_{timestamp}.txt");

            string content = $"""
                Crash Report: {timestamp}
                Source: {source}

                Exception Type: {ex.GetType().Name}
                Message: {ex.Message}

                Stack Trace:
                {ex.StackTrace}

                Inner Exception: {ex.InnerException?.Message}
                """;

            File.WriteAllText(logFile, content);

            // Keep only last 10 crash logs
            var files = Directory.GetFiles(CrashLogDir, "crash_*.txt")
                .OrderByDescending(f => File.GetLastWriteTime(f))
                .Skip(10);
            foreach (var f in files) File.Delete(f);
        }
        catch { /* Ignore logging errors */ }
    }

    // Save application state for recovery
    public static void SaveSessionState(MainWindow mainWindow)
    {
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(SessionStateFile)!);

            var state = new
            {
                SelectedFolder = mainWindow.SelectedFolder,
                OpenFiles = mainWindow.GetOpenFilePaths(),
                CurrentTabIndex = mainWindow.tcEditor.SelectedIndex,
                Timestamp = DateTime.Now
            };

            File.WriteAllText(SessionStateFile, JsonSerializer.Serialize(state, 
                new JsonSerializerOptions { WriteIndented = true }));
        }
        catch { /* Ignore state save errors */ }
    }

    // Restore application state after crash
    public static void RestoreSessionState(MainWindow mainWindow)
    {
        try
        {
            if (!File.Exists(SessionStateFile)) return;

            string content = File.ReadAllText(SessionStateFile);
            using JsonDocument doc = JsonDocument.Parse(content);
            var root = doc.RootElement;

            // Restore selected folder first
            if (root.TryGetProperty("SelectedFolder", out var folder))
            {
                string? folderPath = folder.GetString();
                if (!string.IsNullOrEmpty(folderPath) && Directory.Exists(folderPath))
                {
                    mainWindow.OpenProjectFolder(folderPath);
                }
            }

            // Restore open files
            if (root.TryGetProperty("OpenFiles", out var files))
            {
                foreach (var file in files.EnumerateArray())
                {
                    string? filePath = file.GetString();
                    if (!string.IsNullOrEmpty(filePath) && File.Exists(filePath))
                    {
                        mainWindow.RestoreOpenFile(filePath);
                    }
                }
            }

            // Clean up state file after restore
            File.Delete(SessionStateFile);
        }
        catch { /* Ignore restore errors */ }
    }
}
