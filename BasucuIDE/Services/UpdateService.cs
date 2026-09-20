using System;
using System.Diagnostics;
using System.IO;
using System.Net.Http;
using System.Reflection;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading.Tasks;
using System.Windows;

namespace mdaiAgent.Services;

public class UpdateInfo
{
    [JsonPropertyName("version")]
    public string Version { get; set; } = "";

    [JsonPropertyName("download_url")]
    public string DownloadUrl { get; set; } = "";

    [JsonPropertyName("changelog")]
    public string Changelog { get; set; } = "";

    [JsonPropertyName("mandatory")]
    public bool Mandatory { get; set; } = false;
}

public class UpdateService
{
    private static readonly Lazy<UpdateService> _instance = new(() => new UpdateService());
    public static UpdateService Instance => _instance.Value;

    public const string CurrentVersion = "1.0.0";
    private const string VersionManifestUrl = "https://raw.githubusercontent.com/mdaiWorks/yengi/main/version.json";
    private static readonly HttpClient HttpClient = new() { Timeout = TimeSpan.FromSeconds(10) };

    static UpdateService()
    {
        HttpClient.DefaultRequestHeaders.UserAgent.ParseAdd("Yengi-IDE/1.0");
    }

    private UpdateService() { }

    /// <summary>
    /// Arka planda sunucudan en son sürümü kontrol eder.
    /// Yeni sürüm varsa UpdateInfo nesnesini döndürür, yoksa null döner.
    /// </summary>
    public async Task<UpdateInfo?> CheckForUpdateAsync()
    {
        try
        {
            var json = await HttpClient.GetStringAsync(VersionManifestUrl);
            if (string.IsNullOrWhiteSpace(json)) return null;

            var info = JsonSerializer.Deserialize<UpdateInfo>(json);
            if (info == null || string.IsNullOrWhiteSpace(info.Version)) return null;

            if (IsNewerVersion(info.Version, CurrentVersion))
            {
                return info;
            }
        }
        catch
        {
            // Ağ hatası veya dosya olmaması durumunda sessizce yutulur
        }
        return null;
    }

    /// <summary>
    /// Kurulum exe'sini geçici klasöre indirir, oturum durumunu kaydeder ve sessizce güncelleyiciyi başlatır.
    /// </summary>
    public async Task DownloadAndInstallUpdateAsync(UpdateInfo info, IProgress<int>? progress = null, MainWindow? mainWindow = null)
    {
        if (string.IsNullOrWhiteSpace(info.DownloadUrl)) return;

        string tempFolder = Path.Combine(Path.GetTempPath(), "YengiUpdate");
        Directory.CreateDirectory(tempFolder);
        string setupExePath = Path.Combine(tempFolder, $"Yengi_Setup_v{info.Version}.exe");

        // 1. Dosyayı indir
        using (var response = await HttpClient.GetAsync(info.DownloadUrl, HttpCompletionOption.ResponseHeadersRead))
        {
            response.EnsureSuccessStatusCode();
            var totalBytes = response.Content.Headers.ContentLength ?? -1L;

            await using var contentStream = await response.Content.ReadAsStreamAsync();
            await using var fileStream = new FileStream(setupExePath, FileMode.Create, FileAccess.Write, FileShare.None, 8192, true);

            var buffer = new byte[8192];
            long totalReadBytes = 0;
            int readBytes;

            while ((readBytes = await contentStream.ReadAsync(buffer, 0, buffer.Length)) > 0)
            {
                await fileStream.WriteAsync(buffer, 0, readBytes);
                totalReadBytes += readBytes;

                if (totalBytes > 0 && progress != null)
                {
                    int pct = (int)((totalReadBytes * 100) / totalBytes);
                    progress.Report(pct);
                }
            }
        }

        // 2. Oturum durumunu diske kaydet
        if (mainWindow != null)
        {
            App.SaveSessionState(mainWindow);
        }

        // 3. Inno Setup'ı sessiz modda çalıştır ve uygulamayı kapat
        var psi = new ProcessStartInfo
        {
            FileName = setupExePath,
            Arguments = "/VERYSILENT /SUPPRESSMSGBOXES /NORESTART",
            UseShellExecute = true
        };

        Process.Start(psi);

        // Kendi uygulamasını kapat, Inno Setup dosyaları yenileyip Yengi'yi tekrar açacak
        Application.Current.Dispatcher.Invoke(() =>
        {
            Application.Current.Shutdown();
        });
    }

    public static bool IsNewerVersion(string remoteVersionStr, string currentVersionStr)
    {
        if (Version.TryParse(CleanVersionString(remoteVersionStr), out var remoteVersion) &&
            Version.TryParse(CleanVersionString(currentVersionStr), out var currentVersion))
        {
            return remoteVersion > currentVersion;
        }
        return false;
    }

    private static string CleanVersionString(string ver)
    {
        ver = ver.TrimStart('v', 'V').Trim();
        var parts = ver.Split('.');
        if (parts.Length == 1) return $"{parts[0]}.0.0";
        if (parts.Length == 2) return $"{parts[0]}.{parts[1]}.0";
        return ver;
    }
}
