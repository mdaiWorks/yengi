using System;
using System.IO;
using System.Net.Http;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading.Tasks;

namespace mdaiAgent.Services;

public class TelemetryData
{
    [JsonPropertyName("installation_id")]
    public string InstallationId { get; set; } = string.Empty;

    [JsonPropertyName("app_version")]
    public string AppVersion { get; set; } = "1.0.0";

    [JsonPropertyName("os")]
    public string OS { get; set; } = string.Empty;

    [JsonPropertyName("first_seen")]
    public string FirstSeen { get; set; } = string.Empty;

    [JsonPropertyName("last_seen")]
    public string LastSeen { get; set; } = string.Empty;

    [JsonPropertyName("last_seen_ms")]
    public long LastSeenMs { get; set; }

    [JsonPropertyName("active_provider")]
    public string ActiveProvider { get; set; } = "ApiService";

    [JsonPropertyName("router_state")]
    public string RouterState { get; set; } = "Disabled";

    [JsonPropertyName("language")]
    public string Language { get; set; } = "tr";

    [JsonPropertyName("launch_count")]
    public int LaunchCount { get; set; } = 1;

    [JsonPropertyName("rag_enabled")]
    public bool RagEnabled { get; set; } = false;
}

public class TelemetryIdentity
{
    [JsonPropertyName("installation_id")]
    public string InstallationId { get; set; } = string.Empty;

    [JsonPropertyName("first_seen")]
    public string FirstSeen { get; set; } = string.Empty;

    [JsonPropertyName("first_seen_ms")]
    public long FirstSeenMs { get; set; }

    [JsonPropertyName("launch_count")]
    public int LaunchCount { get; set; } = 0;
}

public static class TelemetryAnalyticsService
{
    // Live Firebase Realtime Database endpoint
    public static string FirebaseEndpointUrl { get; set; } = 
        "https://yengi-ad62b-default-rtdb.firebaseio.com/telemetry/installations";

    private static readonly string TelemetryIdFilePath = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
        "Yengi",
        "telemetry_id.json"
    );

    private static readonly HttpClient HttpClient = new()
    {
        Timeout = TimeSpan.FromSeconds(10)
    };

    /// <summary>
    /// Gets or initializes the unique anonymous installation identity for this device and increments launch count.
    /// </summary>
    public static TelemetryIdentity GetOrCreateIdentity()
    {
        TelemetryIdentity identity;
        try
        {
            if (File.Exists(TelemetryIdFilePath))
            {
                var content = File.ReadAllText(TelemetryIdFilePath);
                var existing = JsonSerializer.Deserialize<TelemetryIdentity>(content);
                if (existing != null && !string.IsNullOrWhiteSpace(existing.InstallationId))
                {
                    identity = existing;
                    identity.LaunchCount++;
                    SaveIdentity(identity);
                    return identity;
                }
            }
        }
        catch
        {
            // Ignore parse or read errors and recreate
        }

        identity = new TelemetryIdentity
        {
            InstallationId = Guid.NewGuid().ToString("D"),
            FirstSeen = DateTime.UtcNow.ToString("o"),
            FirstSeenMs = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds(),
            LaunchCount = 1
        };

        SaveIdentity(identity);
        return identity;
    }

    private static void SaveIdentity(TelemetryIdentity identity)
    {
        try
        {
            var dir = Path.GetDirectoryName(TelemetryIdFilePath);
            if (!string.IsNullOrWhiteSpace(dir) && !Directory.Exists(dir))
            {
                Directory.CreateDirectory(dir);
            }

            var json = JsonSerializer.Serialize(identity, new JsonSerializerOptions { WriteIndented = true });
            File.WriteAllText(TelemetryIdFilePath, json);
        }
        catch
        {
            // Ignore write permission errors
        }
    }

    /// <summary>
    /// Sends an anonymous telemetry heartbeat update to Firebase. Fails silently on network errors.
    /// </summary>
    public static async Task ReportHeartbeatAsync()
    {
        try
        {
            if (string.IsNullOrWhiteSpace(FirebaseEndpointUrl) || FirebaseEndpointUrl.Contains("YOUR_PROJECT"))
            {
                return;
            }

            var settings = SettingsWindow.GetSettings();
            if (!settings.TelemetryEnabled)
            {
                return;
            }

            var identity = GetOrCreateIdentity();
            var assemblyVersion = Assembly.GetExecutingAssembly().GetName().Version;
            var appVersion = assemblyVersion != null 
                ? $"{assemblyVersion.Major}.{assemblyVersion.Minor}.{assemblyVersion.Build}" 
                : "1.0.0";

            string routerState = settings.RouterUseLocalModel 
                ? "LocalRouter (1.5b)" 
                : (settings.RouterEnabled 
                    ? "CloudRouter" 
                    : (settings.RouterUseMainModel ? "MainModel" : "Disabled"));

            var nowUtc = DateTime.UtcNow;
            var payload = new TelemetryData
            {
                InstallationId = identity.InstallationId,
                AppVersion = appVersion,
                OS = RuntimeInformation.OSDescription,
                FirstSeen = identity.FirstSeen,
                LastSeen = nowUtc.ToString("o"),
                LastSeenMs = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds(),
                ActiveProvider = settings.ActiveProvider.ToString(),
                RouterState = routerState,
                Language = string.IsNullOrWhiteSpace(settings.Language) ? "tr" : settings.Language,
                LaunchCount = identity.LaunchCount,
                RagEnabled = settings.RagEnabled
            };

            var requestUrl = $"{FirebaseEndpointUrl.TrimEnd('/')}/{identity.InstallationId}.json";
            var jsonPayload = JsonSerializer.Serialize(payload);
            using var content = new StringContent(jsonPayload, Encoding.UTF8, "application/json");

            // PUT request updates or creates the record for this InstallationId
            await HttpClient.PutAsync(requestUrl, content);
        }
        catch
        {
            // Telemetry must never crash or block the application
        }
    }
}
