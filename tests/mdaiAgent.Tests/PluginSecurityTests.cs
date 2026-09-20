using System;
using System.Collections.Generic;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Xunit;

namespace mdaiAgent.Tests;

public class PluginSecurityTests
{
    [Fact]
    public void ExternalPluginWithoutManifest_IsRejected()
    {
        var directory = Path.Combine(PluginManager.PluginsFolder, "security-test-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        var dllPath = Path.Combine(directory, "unsigned.dll");
        File.WriteAllBytes(dllPath, Encoding.UTF8.GetBytes("not a real assembly"));

        try
        {
            Assert.False(PluginManager.TryValidateExternalPlugin(dllPath, out var error));
            Assert.Contains("manifest", error, StringComparison.OrdinalIgnoreCase);
        }
        finally
        {
            Directory.Delete(directory, true);
        }
    }

    [Fact]
    public void ExternalPluginWithMismatchedHash_IsRejected()
    {
        var directory = Path.Combine(PluginManager.PluginsFolder, "security-test-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        var dllPath = Path.Combine(directory, "plugin.dll");
        File.WriteAllBytes(dllPath, Encoding.UTF8.GetBytes("plugin bytes"));
        var manifest = new PluginManifest
        {
            Id = "test-plugin",
            Name = "Test Plugin",
            Version = "1.0.0",
            FileExtensions = new[] { ".test" },
            IsBuiltIn = false,
            Sha256 = "0000000000000000000000000000000000000000000000000000000000000000"
        };
        File.WriteAllText(Path.ChangeExtension(dllPath, ".json"), JsonSerializer.Serialize(manifest));

        try
        {
            Assert.False(PluginManager.TryValidateExternalPlugin(dllPath, out var error));
            Assert.Contains("SHA-256", error, StringComparison.OrdinalIgnoreCase);
        }
        finally
        {
            Directory.Delete(directory, true);
        }
    }

    [Fact]
    public void ExternalPluginWithoutHash_IsRejected()
    {
        var directory = Path.Combine(PluginManager.PluginsFolder, "security-test-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        var dllPath = Path.Combine(directory, "plugin.dll");
        File.WriteAllBytes(dllPath, Encoding.UTF8.GetBytes("plugin bytes"));
        var manifest = new PluginManifest
        {
            Id = "test-plugin-no-hash",
            Name = "Test Plugin",
            Version = "1.0.0",
            FileExtensions = new[] { ".test" },
            IsBuiltIn = false
        };
        File.WriteAllText(Path.ChangeExtension(dllPath, ".json"), JsonSerializer.Serialize(manifest));

        try
        {
            Assert.False(PluginManager.TryValidateExternalPlugin(dllPath, out var error));
            Assert.Contains("SHA-256", error, StringComparison.OrdinalIgnoreCase);
        }
        finally
        {
            Directory.Delete(directory, true);
        }
    }

    [Fact]
    public void MarketplaceManifestRequiresTrustedHttpsSource()
    {
        var manifest = new PluginManifest
        {
            Id = "test-plugin",
            Name = "Test Plugin",
            Version = "1.0.0",
            FileExtensions = new[] { ".test" },
            IsBuiltIn = false,
            DownloadUrl = "http://example.com/plugin.dll",
            Sha256 = new string('a', 64)
        };

        Assert.False(PluginManager.ValidateMarketplaceManifest(manifest, out var error));
        Assert.Contains("HTTPS", error, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void SignedMarketplaceCatalog_RejectsTampering()
    {
        using var rsa = RSA.Create(2048);
        var catalog = new PluginCatalogDocument
        {
            KeyId = "test-key",
            Plugins = new List<PluginManifest>
            {
                new()
                {
                    Id = "signed-plugin",
                    Name = "Signed Plugin",
                    Version = "1.0.0",
                    FileExtensions = new[] { ".signed" },
                    IsBuiltIn = false,
                    DownloadUrl = "https://github.com/example/signed-plugin/releases/download/v1.0.0/plugin.dll",
                    Sha256 = new string('b', 64)
                }
            }
        };
        var json = JsonSerializer.Serialize(catalog);
        var signature = Convert.ToBase64String(rsa.SignData(Encoding.UTF8.GetBytes(json), HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1));
        var publicKey = rsa.ExportSubjectPublicKeyInfoPem();

        Assert.True(PluginManager.TryValidateSignedCatalog(json, signature, publicKey, out var validated, out var validError), validError);
        Assert.Single(validated.Plugins);

        var tamperedJson = json.Replace("Signed Plugin", "Tampered Plugin", StringComparison.Ordinal);
        Assert.False(PluginManager.TryValidateSignedCatalog(tamperedJson, signature, publicKey, out _, out var tamperedError));
        Assert.Contains("imza", tamperedError, StringComparison.OrdinalIgnoreCase);
    }
}
