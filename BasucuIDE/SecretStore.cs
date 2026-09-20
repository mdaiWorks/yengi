using System.IO;
using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace mdaiAgent;

internal static class SecretStore
{
    private static readonly string StorePath = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
        "Yengi",
        "secrets.dat");

    private static readonly string[] SecretNames =
    {
        "ApiKey",
        "AnthropicApiKey",
        "GoogleApiKey",
        "OpenAIApiKey",
        "TavilyApiKey",
        "RouterApiKey",
        "GitHubToken"
    };

    public static Dictionary<string, string?> Load()
    {
        if (!File.Exists(StorePath))
            return new Dictionary<string, string?>();

        try
        {
            var encrypted = File.ReadAllBytes(StorePath);
            var json = ProtectedData.Unprotect(encrypted, null, DataProtectionScope.CurrentUser);
            return JsonSerializer.Deserialize<Dictionary<string, string?>>(json) ?? new();
        }
        catch
        {
            return new Dictionary<string, string?>();
        }
    }

    public static void Save(AppSettings settings)
    {
        var secrets = SecretNames.ToDictionary(
            name => name,
            name => GetValue(settings, name));
        var json = JsonSerializer.SerializeToUtf8Bytes(secrets);
        var encrypted = ProtectedData.Protect(json, null, DataProtectionScope.CurrentUser);
        var directory = Path.GetDirectoryName(StorePath)!;
        Directory.CreateDirectory(directory);
        File.WriteAllBytes(StorePath, encrypted);
    }

    public static void Apply(AppSettings settings, IReadOnlyDictionary<string, string?> secrets)
    {
        foreach (var name in SecretNames)
        {
            if (secrets.TryGetValue(name, out var value) && value != null)
                SetValue(settings, name, value);
        }
    }

    public static void RemoveSecretsFromJson(JsonObject settingsJson)
    {
        foreach (var name in SecretNames)
            settingsJson.Remove(name);
    }

    private static string? GetValue(AppSettings settings, string name) => name switch
    {
        "ApiKey" => settings.ApiKey,
        "AnthropicApiKey" => settings.AnthropicApiKey,
        "GoogleApiKey" => settings.GoogleApiKey,
        "OpenAIApiKey" => settings.OpenAIApiKey,
        "TavilyApiKey" => settings.TavilyApiKey,
        "RouterApiKey" => settings.RouterApiKey,
        "GitHubToken" => settings.GitHubToken,
        _ => null
    };

    private static void SetValue(AppSettings settings, string name, string value)
    {
        switch (name)
        {
            case "ApiKey": settings.ApiKey = value; break;
            case "AnthropicApiKey": settings.AnthropicApiKey = value; break;
            case "GoogleApiKey": settings.GoogleApiKey = value; break;
            case "OpenAIApiKey": settings.OpenAIApiKey = value; break;
            case "TavilyApiKey": settings.TavilyApiKey = value; break;
            case "RouterApiKey": settings.RouterApiKey = value; break;
            case "GitHubToken": settings.GitHubToken = value; break;
        }
    }
}
