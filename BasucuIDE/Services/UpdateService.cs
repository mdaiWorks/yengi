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

public class GitHubReleaseInfo
{
    [JsonPropertyName("tag_name")]
    public string TagName { get; set; } = "";

    [JsonPropertyName("name")]
    public string Name { get; set; } = "";

    [JsonPropertyName("body")]
    public string Body { get; set; } = "";

    [JsonPropertyName("html_url")]
    public string HtmlUrl { get; set; } = "";

    [JsonPropertyName("assets")]
    public System.Collections.Generic.List<GitHubReleaseAsset>? Assets { get; set; }
}

public class GitHubReleaseAsset
{
    [JsonPropertyName("name")]
    public string Name { get; set; } = "";

    [JsonPropertyName("browser_download_url")]
    public string BrowserDownloadUrl { get; set; } = "";
}

public class UpdateService
{
    private static readonly Lazy<UpdateService> _instance = new(() => new UpdateService());
    public static UpdateService Instance => _instance.Value;

    public const string CurrentVersion = "1.0.4";
    private const string GitHubLatestReleaseUrl = "https://api.github.com/repos/mdaiWorks/yengi/releases/latest";
    private const string VersionManifestUrl = "https://raw.githubusercontent.com/mdaiWorks/yengi/main/version.json";
    private static readonly HttpClient HttpClient = new() { Timeout = TimeSpan.FromSeconds(10) };

    static UpdateService()
    {
        HttpClient.DefaultRequestHeaders.UserAgent.ParseAdd("Yengi-IDE/1.0");
    }

    private UpdateService() { }

    /// <summary>
    /// Arka planda GitHub Releases API üzerinden en son sürümü kontrol eder.
    /// Yeni sürüm varsa UpdateInfo nesnesini döndürür, yoksa null döner.
    /// </summary>
    public async Task<UpdateInfo?> CheckForUpdateAsync()
    {
        try
        {
            using var response = await HttpClient.GetAsync(GitHubLatestReleaseUrl);
            if (response.IsSuccessStatusCode)
            {
                var json = await response.Content.ReadAsStringAsync();
                if (!string.IsNullOrWhiteSpace(json))
                {
                    var release = JsonSerializer.Deserialize<GitHubReleaseInfo>(json);
                    if (release != null && !string.IsNullOrWhiteSpace(release.TagName))
                    {
                        string cleanRemoteVer = CleanVersionString(release.TagName);
                        if (IsNewerVersion(cleanRemoteVer, CurrentVersion))
                        {
                            string downloadUrl = "";
                            if (release.Assets != null && release.Assets.Count > 0)
                            {
                                var exeAsset = release.Assets.Find(a => a.Name.EndsWith(".exe", StringComparison.OrdinalIgnoreCase))
                                               ?? release.Assets[0];
                                downloadUrl = exeAsset.BrowserDownloadUrl;
                            }

                            if (string.IsNullOrWhiteSpace(downloadUrl))
                            {
                                downloadUrl = release.HtmlUrl;
                            }

                            return new UpdateInfo
                            {
                                Version = cleanRemoteVer,
                                DownloadUrl = downloadUrl,
                                Changelog = !string.IsNullOrWhiteSpace(release.Body) ? release.Body : release.Name,
                                Mandatory = false
                            };
                        }
                    }
                }
            }
        }
        catch
        {
            // GitHub API hatası olursa yedek kanalı dene
        }

        return await CheckFallbackVersionManifestAsync();
    }

    private async Task<UpdateInfo?> CheckFallbackVersionManifestAsync()
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
