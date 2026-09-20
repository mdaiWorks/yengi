using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Net.Http;
using System.Security.Cryptography;
using System.Threading.Tasks;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;

namespace mdaiAgent
{
    // Eklenti arayüzü: Tüm dil eklentileri bunu uygulamalı
    public interface ILanguageErrorCheckerPlugin
    {
        string Id { get; }
        string Name { get; }
        string[] FileExtensions { get; }
        string Version { get; }
        string Description { get; }
        IReadOnlyList<LanguageDiagnostic> GetDiagnostics(string filePath, string content);
        bool IsBuiltIn { get; }
    }

    // Eklenti bilgilerini saklamak için model (JSON için)
    public class PluginManifest
    {
        public string Id { get; set; } = "";
        public string Name { get; set; } = "";
        public string[] FileExtensions { get; set; } = Array.Empty<string>();
        public string Version { get; set; } = "1.0.0";
        public string Description { get; set; } = "";
        public bool IsBuiltIn { get; set; } = true;
        public string? DownloadUrl { get; set; }
        public string? Sha256 { get; set; }
    }

    public sealed class PluginCatalogDocument
    {
        public int SchemaVersion { get; set; } = 1;
        public string KeyId { get; set; } = "";
        public List<PluginManifest> Plugins { get; set; } = new();
    }

    public enum DiagnosticSource { Plugin, Lsp }

    public record LanguageDiagnostic(string Message, int Line = 0, int Column = 0)
    {
        public DiagnosticSource Source { get; init; } = DiagnosticSource.Plugin;
    }

    // Eklenti yöneticisi: Eklentileri yükler, yönetir
    public static class PluginManager
    {
        private const long MaxPluginDownloadBytes = 10 * 1024 * 1024;
        private static readonly HashSet<string> TrustedMarketplaceHosts = new(StringComparer.OrdinalIgnoreCase)
        {
            "github.com",
            "raw.githubusercontent.com",
            "objects.githubusercontent.com"
        };

        public static string PluginsFolder => Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "Yengi",
            "Plugins"
        );

        public static List<ILanguageErrorCheckerPlugin> LoadExternalPlugins()
        {
            var externalPlugins = new List<ILanguageErrorCheckerPlugin>();
            try
            {
                if (!Directory.Exists(PluginsFolder))
                {
                    Directory.CreateDirectory(PluginsFolder);
                    return externalPlugins;
                }

                var dllFiles = Directory.GetFiles(PluginsFolder, "*.dll", SearchOption.AllDirectories);
                foreach (var dllPath in dllFiles)
                {
                    try
                    {
                        if (!TryLoadManifestForDll(dllPath, out var manifest, out var validationError))
                        {
                            Logger.LogError($"Plugin yüklenmedi: {Path.GetFileName(dllPath)} - {validationError}");
                            continue;
                        }

                        var assembly = System.Reflection.Assembly.LoadFrom(dllPath);
                        var pluginTypes = assembly.GetTypes().Where(t => 
                            typeof(ILanguageErrorCheckerPlugin).IsAssignableFrom(t) && 
                            !t.IsAbstract && 
                            t.GetConstructor(Type.EmptyTypes) != null
                        );

                        foreach (var pluginType in pluginTypes)
                        {
                            var pluginInstance = (ILanguageErrorCheckerPlugin?)Activator.CreateInstance(pluginType);
                            if (pluginInstance != null && MatchesManifest(pluginInstance, manifest))
                            {
                                externalPlugins.Add(pluginInstance);
                            }
                        }
                    }
                    catch
                    {
                        // Ignore DLL load errors for now
                    }
                }
            }
            catch
            {
                // Ignore errors for now
            }
            return externalPlugins;
        }

        public static bool TryValidateExternalPlugin(string dllPath, out string error)
        {
            return TryLoadManifestForDll(dllPath, out _, out error);
        }

        private static bool TryLoadManifestForDll(string dllPath, out PluginManifest manifest, out string error)
        {
            manifest = new PluginManifest();
            error = string.Empty;

            try
            {
                var fullDllPath = Path.GetFullPath(dllPath);
                var fullPluginsFolder = Path.GetFullPath(PluginsFolder).TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
                if (!fullDllPath.StartsWith(fullPluginsFolder, StringComparison.OrdinalIgnoreCase))
                {
                    error = "DLL plugin klasörü dışından yüklenemez.";
                    return false;
                }

                var manifestPath = Path.ChangeExtension(fullDllPath, ".json");
                if (!File.Exists(manifestPath))
                {
                    error = "Eşleşen manifest dosyası bulunamadı.";
                    return false;
                }

                manifest = JsonSerializer.Deserialize<PluginManifest>(File.ReadAllText(manifestPath)) ?? new PluginManifest();
                if (manifest.IsBuiltIn || string.IsNullOrWhiteSpace(manifest.Id) || string.IsNullOrWhiteSpace(manifest.Version))
                {
                    error = "Harici plugin manifesti geçersiz.";
                    return false;
                }

                if (!IsValidPluginId(manifest.Id))
                {
                    error = "Plugin kimliği geçersiz.";
                    return false;
                }

                if (manifest.FileExtensions.Length == 0 || manifest.FileExtensions.Any(extension =>
                    string.IsNullOrWhiteSpace(extension) || !extension.StartsWith(".", StringComparison.Ordinal)))
                {
                    error = "Plugin dosya uzantıları geçersiz.";
                    return false;
                }

                if (string.IsNullOrWhiteSpace(manifest.Sha256) || !Regex.IsMatch(manifest.Sha256, "^[0-9a-fA-F]{64}$"))
                {
                    error = "Harici plugin için geçerli SHA-256 zorunludur.";
                    return false;
                }

                if (!string.IsNullOrWhiteSpace(manifest.Sha256))
                {
                    using var stream = File.OpenRead(fullDllPath);
                    var actualHash = Convert.ToHexString(SHA256.HashData(stream));
                    if (!actualHash.Equals(manifest.Sha256, StringComparison.OrdinalIgnoreCase))
                    {
                        error = "Plugin SHA-256 doğrulaması başarısız.";
                        return false;
                    }
                }

                return true;
            }
            catch (Exception ex)
            {
                error = $"Manifest doğrulama hatası: {ex.Message}";
                return false;
            }
        }

        public static bool ValidateMarketplaceManifest(PluginManifest manifest, out string error)
        {
            error = string.Empty;
            if (manifest == null || manifest.IsBuiltIn || !IsValidPluginId(manifest.Id))
            {
                error = "Marketplace plugin manifesti geçersiz.";
                return false;
            }

            if (!Uri.TryCreate(manifest.DownloadUrl, UriKind.Absolute, out var uri) || uri.Scheme != Uri.UriSchemeHttps)
            {
                error = "Plugin indirme adresi HTTPS olmalıdır.";
                return false;
            }

            if (!TrustedMarketplaceHosts.Contains(uri.Host))
            {
                error = "Plugin indirme adresi güvenilir marketplace alan adında değil.";
                return false;
            }

            if (string.IsNullOrWhiteSpace(manifest.Sha256) || !Regex.IsMatch(manifest.Sha256, "^[0-9a-fA-F]{64}$"))
            {
                error = "Marketplace plugin için geçerli SHA-256 zorunludur.";
                return false;
            }

            return true;
        }

        public static bool TryValidateSignedCatalog(
            string catalogJson,
            string signatureBase64,
            string publicKeyPem,
            out PluginCatalogDocument catalog,
            out string error)
        {
            catalog = new PluginCatalogDocument();
            error = string.Empty;

            try
            {
                var payload = Encoding.UTF8.GetBytes(catalogJson ?? string.Empty);
                var signature = Convert.FromBase64String(signatureBase64 ?? string.Empty);
                using var rsa = RSA.Create();
                rsa.ImportFromPem(publicKeyPem);
                if (!rsa.VerifyData(payload, signature, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1))
                {
                    error = "Plugin kataloğu imza doğrulaması başarısız.";
                    return false;
                }

                catalog = JsonSerializer.Deserialize<PluginCatalogDocument>(catalogJson) ?? new PluginCatalogDocument();
                if (catalog.SchemaVersion != 1 || string.IsNullOrWhiteSpace(catalog.KeyId) || catalog.Plugins.Count == 0)
                {
                    error = "Plugin kataloğu şeması geçersiz.";
                    return false;
                }

                var ids = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                foreach (var plugin in catalog.Plugins)
                {
                    if (!ids.Add(plugin.Id) || !ValidateMarketplaceManifest(plugin, out error))
                        return false;
                }

                return true;
            }
            catch (Exception ex)
            {
                error = $"Plugin kataloğu doğrulama hatası: {ex.Message}";
                return false;
            }
        }

        private static bool IsValidPluginId(string? id) =>
            !string.IsNullOrWhiteSpace(id) && Regex.IsMatch(id, "^[a-zA-Z0-9][a-zA-Z0-9._-]{0,63}$");

        private static bool MatchesManifest(ILanguageErrorCheckerPlugin plugin, PluginManifest manifest)
        {
            return string.Equals(plugin.Id, manifest.Id, StringComparison.OrdinalIgnoreCase) &&
                   string.Equals(plugin.Version, manifest.Version, StringComparison.OrdinalIgnoreCase) &&
                   plugin.FileExtensions.SequenceEqual(manifest.FileExtensions, StringComparer.OrdinalIgnoreCase);
        }

        private static readonly List<ILanguageErrorCheckerPlugin> _loadedPlugins = new();
        public static IReadOnlyList<ILanguageErrorCheckerPlugin> LoadedPlugins => _loadedPlugins.AsReadOnly();

        static PluginManager()
        {
            if (!Directory.Exists(PluginsFolder))
            {
                Directory.CreateDirectory(PluginsFolder);
            }

            LoadDefaultPlugins();
            LoadUserPlugins();
        }

        // Varsayılan eklentileri yükle
        private static void LoadDefaultPlugins()
        {
            _loadedPlugins.Add(new PythonErrorCheckerPlugin());
            _loadedPlugins.Add(new CSharpErrorCheckerPlugin());
            _loadedPlugins.Add(new JavaScriptErrorCheckerPlugin());
            _loadedPlugins.Add(new HtmlCssErrorCheckerPlugin());
            _loadedPlugins.Add(new JavaErrorCheckerPlugin());
            _loadedPlugins.Add(new PhpErrorCheckerPlugin());
            _loadedPlugins.Add(new GoErrorCheckerPlugin());
            _loadedPlugins.Add(new CppErrorCheckerPlugin());
            _loadedPlugins.Add(new DartErrorCheckerPlugin());
            _loadedPlugins.Add(new SqlErrorCheckerPlugin());
            _loadedPlugins.Add(new RubyErrorCheckerPlugin());
            _loadedPlugins.Add(new RustErrorCheckerPlugin());
            _loadedPlugins.Add(new KotlinErrorCheckerPlugin());
            _loadedPlugins.Add(new SwiftErrorCheckerPlugin());

            // Dışarıdan eklentileri yükle
            _loadedPlugins.AddRange(LoadExternalPlugins());
        }

        // Kullanıcı eklentilerini yükle
        private static void LoadUserPlugins()
        {
            try
            {
                if (Directory.Exists(PluginsFolder))
                {
                    var pluginFiles = Directory.GetFiles(PluginsFolder, "*.json");
                    foreach (var pluginFile in pluginFiles)
                    {
                        try
                        {
                            var json = File.ReadAllText(pluginFile);
                            var manifest = JsonSerializer.Deserialize<PluginManifest>(json);
                            // Şimdilik sadece manifest okuyoruz, dinamik yükleme ileride
                        }
                        catch { }
                    }
                }
            }
            catch { }
        }

        public static ILanguageErrorCheckerPlugin? GetPluginForFile(string filePath)
        {
            string extension = Path.GetExtension(filePath).ToLowerInvariant();
            return _loadedPlugins.FirstOrDefault(p => 
                p.FileExtensions.Contains(extension, StringComparer.OrdinalIgnoreCase));
        }

        public static IReadOnlyList<LanguageDiagnostic> GetDiagnostics(string filePath, string content)
        {
            try
            {
                var plugin = GetPluginForFile(filePath);
                if (plugin != null)
                {
                    return plugin.GetDiagnostics(filePath, content);
                }

                return Array.Empty<LanguageDiagnostic>();
            }
            catch
            {
                return Array.Empty<LanguageDiagnostic>();
            }
        }

        private static bool HasGenericErrors(string filePath, string content)
        {
            var ext = Path.GetExtension(filePath).ToLowerInvariant();
            var commonExtensions = new[] { ".js", ".ts", ".java", ".php", ".go", ".cpp", ".c", ".h", ".sql", ".json", ".xml", ".rb", ".rs", ".kt", ".swift", ".dart" };
            if (!commonExtensions.Contains(ext))
            {
                return false;
            }

            var lines = content.Split('\n');
            foreach (var line in lines)
            {
                var trimmed = line.Trim();
                if (trimmed.Length == 0)
                    continue;
            }
            return false;
        }

        private static int GetFirstDiagnosticLine(string content, ILanguageErrorCheckerPlugin? plugin, string ext)
        {
            var lines = content.Split('\n');
            for (int i = 0; i < lines.Length; i++)
            {
                var trimmed = lines[i].Trim();
                if (string.IsNullOrEmpty(trimmed))
                    continue;

                if (ext == ".py")
                {
                    if (trimmed.StartsWith("prin ") || trimmed.StartsWith("prin(") || trimmed.StartsWith("prnt ") || trimmed.StartsWith("prnt("))
                        return i + 1;
                }
                else
                {
                    if (trimmed.Contains("error", StringComparison.OrdinalIgnoreCase) || trimmed.Contains("Console", StringComparison.OrdinalIgnoreCase))
                        return i + 1;
                }
            }
            return 1;
        }

        public static List<PluginManifest> GetAllPluginManifests()
        {
            return _loadedPlugins.Select(p => new PluginManifest
            {
                Id = p.Id,
                Name = p.Name,
                FileExtensions = p.FileExtensions,
                Version = p.Version,
                Description = p.Description,
                IsBuiltIn = p.IsBuiltIn
            }).ToList();
        }

        // GitHub'dan mevcut eklentileri çek
        public static async Task<List<PluginManifest>> GetAvailablePluginsFromGitHubAsync()
        {
            var catalogUrl = Environment.GetEnvironmentVariable("MDAI_PLUGIN_CATALOG_URL");
            var signatureUrl = Environment.GetEnvironmentVariable("MDAI_PLUGIN_CATALOG_SIGNATURE_URL");
            var publicKey = Environment.GetEnvironmentVariable("MDAI_PLUGIN_CATALOG_PUBLIC_KEY");

            if (string.IsNullOrWhiteSpace(catalogUrl) ||
                string.IsNullOrWhiteSpace(signatureUrl) ||
                string.IsNullOrWhiteSpace(publicKey))
            {
                return new List<PluginManifest>();
            }

            if (!Uri.TryCreate(catalogUrl, UriKind.Absolute, out var catalogUri) ||
                catalogUri.Scheme != Uri.UriSchemeHttps ||
                !TrustedMarketplaceHosts.Contains(catalogUri.Host))
            {
                return new List<PluginManifest>();
            }

            if (!Uri.TryCreate(signatureUrl, UriKind.Absolute, out var signatureUri) ||
                signatureUri.Scheme != Uri.UriSchemeHttps ||
                !TrustedMarketplaceHosts.Contains(signatureUri.Host))
            {
                return new List<PluginManifest>();
            }

            try
            {
                using var client = new HttpClient { Timeout = TimeSpan.FromSeconds(15) };
                client.DefaultRequestHeaders.UserAgent.ParseAdd("mdaiAgent-PluginCatalog/1.0");
                var catalogJson = await client.GetStringAsync(catalogUri);
                var signature = (await client.GetStringAsync(signatureUri)).Trim();

                return TryValidateSignedCatalog(
                    catalogJson,
                    signature,
                    publicKey,
                    out var catalog,
                    out _)
                    ? catalog.Plugins
                    : new List<PluginManifest>();
            }
            catch
            {
                return new List<PluginManifest>();
            }
        }

        // Gerçek GitHub indirmesi için metot
        public static async Task<bool> DownloadAndInstallPluginAsync(PluginManifest manifest)
        {
            if (!ValidateMarketplaceManifest(manifest, out _))
                return false;

            try
            {
                Directory.CreateDirectory(PluginsFolder);
                var dllPath = Path.Combine(PluginsFolder, $"{manifest.Id}.dll");
                var manifestPath = Path.Combine(PluginsFolder, $"{manifest.Id}.json");
                var tempPath = dllPath + ".download";

                using var client = new HttpClient { Timeout = TimeSpan.FromSeconds(30) };
                using var response = await client.GetAsync(manifest.DownloadUrl, HttpCompletionOption.ResponseHeadersRead);
                response.EnsureSuccessStatusCode();
                if (response.Content.Headers.ContentLength is > MaxPluginDownloadBytes)
                    return false;

                await using (var input = await response.Content.ReadAsStreamAsync())
                await using (var output = File.Create(tempPath))
                {
                    await input.CopyToAsync(output);
                    if (output.Length > MaxPluginDownloadBytes)
                    {
                        File.Delete(tempPath);
                        return false;
                    }
                }

                using (var stream = File.OpenRead(tempPath))
                {
                    var actualHash = Convert.ToHexString(SHA256.HashData(stream));
                    if (!actualHash.Equals(manifest.Sha256, StringComparison.OrdinalIgnoreCase))
                    {
                        File.Delete(tempPath);
                        return false;
                    }
                }

                File.Move(tempPath, dllPath, true);
                await File.WriteAllTextAsync(manifestPath, JsonSerializer.Serialize(manifest, new JsonSerializerOptions { WriteIndented = true }));
                return true;
            }
            catch
            {
                return false;
            }
        }

        // Mevcut eklentileri (örnek)
        public static List<PluginManifest> GetAvailablePlugins()
        {
            // Guvenilir ve imzali katalog yapilandirilana kadar sahte paket gosterilmez.
            return new List<PluginManifest>();
        }

        // Güncellemeleri denetle
        public static async Task<List<(PluginManifest local, PluginManifest? remote)>> CheckForUpdatesAsync()
        {
            var updates = new List<(PluginManifest, PluginManifest?)>();
            try
            {
                // Mevcut yerel eklentileri al
                var localPlugins = GetAllPluginManifests();
                // GitHub'dan son sürümleri al
                var remotePlugins = await GetAvailablePluginsFromGitHubAsync();

                foreach (var local in localPlugins)
                {
                    var remote = remotePlugins.FirstOrDefault(p => p.Id == local.Id);
                    if (remote != null && remote.Version != local.Version)
                    {
                        // Sürüm farklı, güncelleme var
                        updates.Add((local, remote));
                    }
                }
            }
            catch
            {
                // Hata durumunda boş dön
            }
            return updates;
        }

        // Yeni eklenti sınıfları
        private class KotlinErrorCheckerPlugin : BuiltInPlugin
        {
            public override string Id => "kotlin";
            public override string Name => "Kotlin";
            public override string[] FileExtensions => new[] { ".kt", ".kts" };
            public override string Description => "Kotlin dili için basit hata denetimi";
            public override bool IsBuiltIn => true;

            public override IReadOnlyList<LanguageDiagnostic> GetDiagnostics(string filePath, string content)
            {
                var diagnostics = new List<LanguageDiagnostic>();
                try
                {
                    // Kotlin için özel kontroller gelecekte eklenecek
                }
                catch { }
                return diagnostics;
            }
        }

        private class SwiftErrorCheckerPlugin : BuiltInPlugin
        {
            public override string Id => "swift";
            public override string Name => "Swift";
            public override string[] FileExtensions => new[] { ".swift" };
            public override string Description => "Swift dili için basit hata denetimi";
            public override bool IsBuiltIn => true;

            public override IReadOnlyList<LanguageDiagnostic> GetDiagnostics(string filePath, string content)
            {
                var diagnostics = new List<LanguageDiagnostic>();
                try
                {
                    // Swift için özel kontroller gelecekte eklenecek
                }
                catch { }
                return diagnostics;
            }
        }

        #region Built-in Plugins

        private abstract class BuiltInPlugin : ILanguageErrorCheckerPlugin
        {
            public abstract string Id { get; }
            public abstract string Name { get; }
            public abstract string[] FileExtensions { get; }
            public string Version { get; } = "1.0.0";
            public abstract string Description { get; }
            public virtual bool IsBuiltIn => true;
            public abstract IReadOnlyList<LanguageDiagnostic> GetDiagnostics(string filePath, string content);
        }

        private static string? _cachedPythonExecutable;

        private static bool TryGetPythonExecutable(out string pythonExecutable)
        {
            if (!string.IsNullOrEmpty(_cachedPythonExecutable))
            {
                pythonExecutable = _cachedPythonExecutable;
                return true;
            }

            foreach (var candidate in new[] { "python", "python3", "py" })
            {
                if (IsExecutableAvailable(candidate))
                {
                    _cachedPythonExecutable = candidate;
                    pythonExecutable = candidate;
                    return true;
                }
            }

            pythonExecutable = string.Empty;
            return false;
        }

        private static bool IsExecutableAvailable(string executable)
        {
            try
            {
                var psi = new ProcessStartInfo(executable, "--version")
                {
                    CreateNoWindow = true,
                    UseShellExecute = false,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true
                };

                using var process = Process.Start(psi);
                if (process == null)
                    return false;

                process.WaitForExit(3000);
                return process.ExitCode == 0;
            }
            catch
            {
                return false;
            }
        }

        private static IReadOnlyList<LanguageDiagnostic> GetPythonDiagnostics(string content)
        {
            if (string.IsNullOrWhiteSpace(content))
                return Array.Empty<LanguageDiagnostic>();

            if (TryGetPythonExecutable(out var pythonExecutable))
            {
                try
                {
                    var (exitCode, stderr) = RunPythonSyntaxCheck(content, pythonExecutable);
                    var heuristics = GetPythonHeuristicDiagnostics(content);
                    var allDiagnostics = new List<LanguageDiagnostic>(heuristics);

                    if (exitCode != 0)
                    {
                        var parsed = TryParsePythonCompileError(stderr);
                        if (parsed != null && !allDiagnostics.Any(d => d.Line == parsed.Line && d.Column == parsed.Column))
                        {
                            allDiagnostics.Add(parsed);
                        }
                    }

                    return allDiagnostics;
                }
                catch
                {
                    return GetPythonHeuristicDiagnostics(content);
                }
            }

            return GetPythonHeuristicDiagnostics(content);
        }

        private static LanguageDiagnostic? TryParsePythonCompileError(string stderr)
        {
            if (string.IsNullOrWhiteSpace(stderr))
                return null;

            var lineMatch = Regex.Match(stderr, @"line\s+(\d+)");
            var messageMatch = Regex.Match(stderr.Trim(), @"^(?:[A-Za-z]+Error|SyntaxError|IndentationError):\s*(.+)$", RegexOptions.Multiline);

            if (lineMatch.Success && int.TryParse(lineMatch.Groups[1].Value, out var lineNumber))
            {
                var message = messageMatch.Success ? messageMatch.Groups[1].Value.Trim() : stderr.Trim().Split('\n').LastOrDefault() ?? "Python sözdizimi hatası";
                return new LanguageDiagnostic(message, lineNumber, 1);
            }

            return null;
        }

        private static IReadOnlyList<LanguageDiagnostic> GetPythonHeuristicDiagnostics(string content)
        {
            var diagnostics = new List<LanguageDiagnostic>();
            var lines = content.Split('\n');
            for (int i = 0; i < lines.Length; i++)
            {
                var trimmed = lines[i].Trim();
                if (string.IsNullOrWhiteSpace(trimmed) || trimmed.StartsWith("#"))
                    continue;

                if (trimmed.StartsWith("prin ") || trimmed.StartsWith("prin(") || trimmed == "prin" ||
                    trimmed.StartsWith("prnt ") || trimmed.StartsWith("prnt(") || trimmed == "prnt")
                {
                    diagnostics.Add(new LanguageDiagnostic("Bilinmeyen Python fonksiyonu. 'print' yazımı kontrol edin.", i + 1, 1));
                }

                if (trimmed.StartsWith("def ") && !trimmed.EndsWith(":"))
                    diagnostics.Add(new LanguageDiagnostic("'def' ifadesinden sonra ':' eksik.", i + 1, 1));
                if (trimmed.StartsWith("if ") && !trimmed.EndsWith(":"))
                    diagnostics.Add(new LanguageDiagnostic("'if' ifadesinden sonra ':' eksik.", i + 1, 1));
                if (trimmed.StartsWith("for ") && !trimmed.EndsWith(":"))
                    diagnostics.Add(new LanguageDiagnostic("'for' ifadesinden sonra ':' eksik.", i + 1, 1));
                if (trimmed.StartsWith("while ") && !trimmed.EndsWith(":"))
                    diagnostics.Add(new LanguageDiagnostic("'while' ifadesinden sonra ':' eksik.", i + 1, 1));
                if (trimmed.StartsWith("class ") && !trimmed.EndsWith(":"))
                    diagnostics.Add(new LanguageDiagnostic("'class' ifadesinden sonra ':' eksik.", i + 1, 1));
                if (trimmed.StartsWith("try ") && !trimmed.EndsWith(":"))
                    diagnostics.Add(new LanguageDiagnostic("'try' ifadesinden sonra ':' eksik.", i + 1, 1));
                if (trimmed.StartsWith("except ") && !trimmed.EndsWith(":"))
                    diagnostics.Add(new LanguageDiagnostic("'except' ifadesinden sonra ':' eksik.", i + 1, 1));
                if (trimmed.StartsWith("with ") && !trimmed.EndsWith(":"))
                    diagnostics.Add(new LanguageDiagnostic("'with' ifadesinden sonra ':' eksik.", i + 1, 1));
            }
            return diagnostics;
        }

        private static IReadOnlyList<LanguageDiagnostic> GetCSharpDiagnostics(string content)
        {
            try
            {
                var tree = CSharpSyntaxTree.ParseText(content, new CSharpParseOptions(LanguageVersion.Latest));
                var diagnostics = tree.GetDiagnostics().Where(d => d.Severity == DiagnosticSeverity.Error).ToList();
                if (!diagnostics.Any())
                    return Array.Empty<LanguageDiagnostic>();

                return diagnostics.Select(d =>
                {
                    var span = d.Location.GetLineSpan();
                    return new LanguageDiagnostic(d.GetMessage(), span.StartLinePosition.Line + 1, span.StartLinePosition.Character + 1);
                }).ToList();
            }
            catch
            {
                return Array.Empty<LanguageDiagnostic>();
            }
        }

        private static bool CheckPythonSyntax(string content)
        {
            if (string.IsNullOrWhiteSpace(content))
                return false;

            if (!TryGetPythonExecutable(out var pythonExecutable))
                return false;

            try
            {
                var (exitCode, _) = RunPythonSyntaxCheck(content, pythonExecutable);
                return exitCode != 0;
            }
            catch
            {
                return false;
            }
        }

        private static (int ExitCode, string ErrorText) RunPythonSyntaxCheck(string content, string pythonExecutable)
        {
            var psi = new ProcessStartInfo(pythonExecutable, "-c \"import ast, sys; ast.parse(sys.stdin.read())\"")
            {
                CreateNoWindow = true,
                UseShellExecute = false,
                RedirectStandardInput = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true
            };

            using var process = Process.Start(psi);
            if (process == null)
                return (-1, string.Empty);

            process.StandardInput.Write(content);
            process.StandardInput.Close();

            var stderr = process.StandardError.ReadToEnd();
            process.WaitForExit(5000);
            return (process.ExitCode, stderr);
        }

        private static bool CheckPythonHeuristics(string content)
        {
            try
            {
                var lines = content.Split('\n');
                foreach (var line in lines)
                {
                    var trimmed = line.Trim();
                    if (string.IsNullOrWhiteSpace(trimmed) || trimmed.StartsWith("#"))
                        continue;

                    if (trimmed.StartsWith("prin ") || trimmed.StartsWith("prin(") ||
                        trimmed.StartsWith("prnt ") || trimmed.StartsWith("prnt("))
                        return true;

                    if (IsLikelyBuiltinTypo(trimmed))
                        return true;

                    if (trimmed.StartsWith("def ") && !trimmed.EndsWith(":")) return true;
                    if (trimmed.StartsWith("if ") && !trimmed.EndsWith(":")) return true;
                    if (trimmed.StartsWith("for ") && !trimmed.EndsWith(":")) return true;
                    if (trimmed.StartsWith("while ") && !trimmed.EndsWith(":")) return true;
                    if (trimmed.StartsWith("class ") && !trimmed.EndsWith(":")) return true;
                    if (trimmed.StartsWith("try ") && !trimmed.EndsWith(":")) return true;
                    if (trimmed.StartsWith("except ") && !trimmed.EndsWith(":")) return true;
                    if (trimmed.StartsWith("with ") && !trimmed.EndsWith(":")) return true;
                }
                return false;
            }
            catch
            {
                return false;
            }
        }

        private static bool IsLikelyBuiltinTypo(string trimmed)
        {
            int parenIdx = trimmed.IndexOf('(');
            if (parenIdx <= 0)
                return false;

            var identifier = trimmed.Substring(0, parenIdx).Trim();
            if (string.IsNullOrEmpty(identifier) || !char.IsLetter(identifier[0]))
                return false;

            if (PythonKeywords.Contains(identifier) || PythonBuiltins.Contains(identifier))
                return false;

            foreach (var builtin in PythonBuiltins)
            {
                if (GetLevenshteinDistance(identifier, builtin) == 1)
                    return true;
            }
            return false;
        }

        private static readonly HashSet<string> PythonBuiltins = new(StringComparer.OrdinalIgnoreCase)
        {
            "print", "len", "range", "open", "int", "float", "str", "list", "dict", "set", "tuple",
            "input", "map", "filter", "sum", "min", "max", "abs", "bool", "sorted", "reversed",
            "enumerate", "zip", "all", "any", "type", "super"
        };

        private static readonly HashSet<string> PythonKeywords = new(StringComparer.OrdinalIgnoreCase)
        {
            "False", "True", "None", "and", "as", "assert", "async", "await", "break", "class",
            "continue", "def", "del", "elif", "else", "except", "finally", "for", "from",
            "global", "if", "import", "in", "is", "lambda", "nonlocal", "not", "or", "pass",
            "raise", "return", "try", "while", "with", "yield"
        };

        private static int GetLevenshteinDistance(string a, string b)
        {
            var n = a.Length;
            var m = b.Length;
            if (n == 0) return m;
            if (m == 0) return n;

            var d = new int[n + 1, m + 1];
            for (int i = 0; i <= n; i++) d[i, 0] = i;
            for (int j = 0; j <= m; j++) d[0, j] = j;

            for (int i = 1; i <= n; i++)
            {
                for (int j = 1; j <= m; j++)
                {
                    int cost = a[i - 1] == b[j - 1] ? 0 : 1;
                    d[i, j] = Math.Min(
                        Math.Min(d[i - 1, j] + 1, d[i, j - 1] + 1),
                        d[i - 1, j - 1] + cost);
                }
            }
            return d[n, m];
        }

        private static bool CheckCSharpSyntax(string content)
        {
            var tree = CSharpSyntaxTree.ParseText(content, new CSharpParseOptions(LanguageVersion.Latest));
            var diagnostics = tree.GetDiagnostics();
            return diagnostics.Any(d => d.Severity == DiagnosticSeverity.Error);
        }

        private class PythonErrorCheckerPlugin : BuiltInPlugin
        {
            public override string Id => "python";
            public override string Name => "Python";
            public override string[] FileExtensions => new[] { ".py" };
            public override string Description => "Python dili için gerçek sözdizimi denetimi";

            public override IReadOnlyList<LanguageDiagnostic> GetDiagnostics(string filePath, string content)
            {
                return GetPythonDiagnostics(content);
            }
        }

        private class CSharpErrorCheckerPlugin : BuiltInPlugin
        {
            public override string Id => "csharp";
            public override string Name => "C#";
            public override string[] FileExtensions => new[] { ".cs" };
            public override string Description => "C# dili için gerçek sözdizimi denetimi";

            public override IReadOnlyList<LanguageDiagnostic> GetDiagnostics(string filePath, string content)
            {
                return GetCSharpDiagnostics(content);
            }
        }

        private class JavaScriptErrorCheckerPlugin : BuiltInPlugin
        {
            public override string Id => "javascript";
            public override string Name => "JavaScript / TypeScript";
            public override string[] FileExtensions => new[] { ".js", ".ts", ".jsx", ".tsx", ".mjs", ".cjs" };
            public override string Description => "JavaScript ve TypeScript için basit hata denetimi";

            public override IReadOnlyList<LanguageDiagnostic> GetDiagnostics(string filePath, string content)
            {
                var diagnostics = new List<LanguageDiagnostic>();
                try
                {
                    var lines = content.Split('\n');
                    int openParen = 0, openBracket = 0, openBrace = 0;
                    
                    for (int i = 0; i < lines.Length; i++)
                    {
                        var line = lines[i];
                        var trimmed = line.Trim();
                        
                        // Yazım hataları kontrolü
                        if (trimmed.StartsWith("consol.") || trimmed.StartsWith("consol(") || trimmed.StartsWith("consol;"))
                        {
                            diagnostics.Add(new LanguageDiagnostic("Olası yazım hatası: 'console' kontrol edin.", i + 1, 1));
                        }
                        if (trimmed.StartsWith("func "))
                        {
                            diagnostics.Add(new LanguageDiagnostic("Olası yazım hatası: 'function' kontrol edin.", i + 1, 1));
                        }
                        
                        // Parantez, köşeli parantez ve süslü parantez dengesi
                        foreach (char c in line)
                        {
                            if (c == '(') openParen++;
                            if (c == ')') openParen--;
                            if (c == '[') openBracket++;
                            if (c == ']') openBracket--;
                            if (c == '{') openBrace++;
                            if (c == '}') openBrace--;
                        }
                    }
                    
                    // Genel denge kontrolleri
                    if (openParen != 0)
                    {
                        diagnostics.Add(new LanguageDiagnostic($"Parantez dengesi bozuk! Açıkta {Math.Abs(openParen)} parantez var.", 1, 1));
                    }
                    if (openBracket != 0)
                    {
                        diagnostics.Add(new LanguageDiagnostic($"Köşeli parantez dengesi bozuk! Açıkta {Math.Abs(openBracket)} tane var.", 1, 1));
                    }
                    if (openBrace != 0)
                    {
                        diagnostics.Add(new LanguageDiagnostic($"Süslü parantez dengesi bozuk! Açıkta {Math.Abs(openBrace)} tane var.", 1, 1));
                    }
                }
                catch { }
                return diagnostics;
            }
        }

        private class HtmlCssErrorCheckerPlugin : BuiltInPlugin
        {
            public override string Id => "htmlcss";
            public override string Name => "HTML / CSS";
            public override string[] FileExtensions => new[] { ".html", ".htm", ".css", ".scss", ".sass" };
            public override string Description => "HTML ve CSS için basit hata denetimi";

            public override IReadOnlyList<LanguageDiagnostic> GetDiagnostics(string filePath, string content)
            {
                var diagnostics = new List<LanguageDiagnostic>();
                try
                {
                    var ext = Path.GetExtension(filePath).ToLowerInvariant();
                    var lines = content.Split('\n');
                    
                    if (ext is ".css" or ".scss" or ".sass")
                    {
                        int openBrace = 0;
                        for (int i = 0; i < lines.Length; i++)
                        {
                            var line = lines[i];
                            var trimmed = line.Trim();
                            
                            foreach (char c in line)
                            {
                                if (c == '{') openBrace++;
                                if (c == '}') openBrace--;
                            }

                            // CSS için basit kontroller
                            if (trimmed.StartsWith(":") && !trimmed.StartsWith("::"))
                            {
                                diagnostics.Add(new LanguageDiagnostic("CSS pseudo-elementi '::' ile başlamalıdır.", i + 1, 1));
                            }
                        }
                        
                        if (openBrace != 0)
                        {
                            diagnostics.Add(new LanguageDiagnostic("Süslü parantez dengesi bozuk.", 1, 1));
                        }
                    }
                    else if (ext is ".html" or ".htm")
                    {
                        // HTML için öncelik LSP sunucusundadır. Basit ayrıştırma hatalarını önlemek için sadece temel kontroller yapılır.
                        // inline script/style blokları veya özel şablon etiketleri hatalı kırmızı işaretleme üretmesin.
                    }
                }
                catch { }
                return diagnostics;
            }
        }

        private class JavaErrorCheckerPlugin : BuiltInPlugin
        {
            public override string Id => "java";
            public override string Name => "Java";
            public override string[] FileExtensions => new[] { ".java" };
            public override string Description => "Java dili için basit hata denetimi";

            public override IReadOnlyList<LanguageDiagnostic> GetDiagnostics(string filePath, string content)
            {
                var diagnostics = new List<LanguageDiagnostic>();
                try
                {
                    var lines = content.Split('\n');
                    int openParen = 0, openBracket = 0, openBrace = 0;
                    
                    for (int i = 0; i < lines.Length; i++)
                    {
                        var line = lines[i];
                        var trimmed = line.Trim();
                        
                        // Parantez, köşeli parantez ve süslü parantez dengesi
                        foreach (char c in line)
                        {
                            if (c == '(') openParen++;
                            if (c == ')') openParen--;
                            if (c == '[') openBracket++;
                            if (c == ']') openBracket--;
                            if (c == '{') openBrace++;
                            if (c == '}') openBrace--;
                        }
                        
                        // Yazım hataları
                        if (trimmed.StartsWith("pubic "))
                        {
                            diagnostics.Add(new LanguageDiagnostic("Olası yazım hatası: 'public' kontrol edin.", i + 1, 1));
                        }
                        if (trimmed.StartsWith("privte "))
                        {
                            diagnostics.Add(new LanguageDiagnostic("Olası yazım hatası: 'private' kontrol edin.", i + 1, 1));
                        }
                        if (trimmed.StartsWith("voif "))
                        {
                            diagnostics.Add(new LanguageDiagnostic("Olası yazım hatası: 'void' kontrol edin.", i + 1, 1));
                        }
                        if (trimmed.StartsWith("inte "))
                        {
                            diagnostics.Add(new LanguageDiagnostic("Olası yazım hatası: 'int' kontrol edin.", i + 1, 1));
                        }
                    }
                    
                    if (openParen != 0) diagnostics.Add(new LanguageDiagnostic($"Parantez dengesi bozuk! Açıkta {Math.Abs(openParen)} tane.", 1, 1));
                    if (openBracket != 0) diagnostics.Add(new LanguageDiagnostic($"Köşeli parantez dengesi bozuk! Açıkta {Math.Abs(openBracket)} tane.", 1, 1));
                    if (openBrace != 0) diagnostics.Add(new LanguageDiagnostic($"Süslü parantez dengesi bozuk! Açıkta {Math.Abs(openBrace)} tane.", 1, 1));
                }
                catch { }
                return diagnostics;
            }
        }

        private class PhpErrorCheckerPlugin : BuiltInPlugin
        {
            public override string Id => "php";
            public override string Name => "PHP";
            public override string[] FileExtensions => new[] { ".php" };
            public override string Description => "PHP dili için basit hata denetimi";

            public override IReadOnlyList<LanguageDiagnostic> GetDiagnostics(string filePath, string content)
            {
                var diagnostics = new List<LanguageDiagnostic>();
                try
                {
                    var lines = content.Split('\n');
                    int openParen = 0, openBracket = 0, openBrace = 0;
                    bool hasPhpOpen = content.Contains("<?php") || content.Contains("<?=");
                    
                    if (!hasPhpOpen && !string.IsNullOrWhiteSpace(content))
                    {
                        diagnostics.Add(new LanguageDiagnostic("PHP dosyası '<?php' veya '<?=' ile başlamalıdır.", 1, 1));
                    }
                    
                    for (int i = 0; i < lines.Length; i++)
                    {
                        var line = lines[i];
                        var trimmed = line.Trim();
                        
                        // Parantez, köşeli parantez ve süslü parantez dengesi
                        foreach (char c in line)
                        {
                            if (c == '(') openParen++;
                            if (c == ')') openParen--;
                            if (c == '[') openBracket++;
                            if (c == ']') openBracket--;
                            if (c == '{') openBrace++;
                            if (c == '}') openBrace--;
                        }
                        
                        // Yazım hataları
                        if (trimmed.StartsWith("ech "))
                        {
                            diagnostics.Add(new LanguageDiagnostic("Olası yazım hatası: 'echo' kontrol edin.", i + 1, 1));
                        }
                        if (trimmed.StartsWith("prin "))
                        {
                            diagnostics.Add(new LanguageDiagnostic("Olası yazım hatası: 'print' kontrol edin.", i + 1, 1));
                        }
                    }
                    
                    if (openParen != 0) diagnostics.Add(new LanguageDiagnostic($"Parantez dengesi bozuk! Açıkta {Math.Abs(openParen)} tane.", 1, 1));
                    if (openBracket != 0) diagnostics.Add(new LanguageDiagnostic($"Köşeli parantez dengesi bozuk! Açıkta {Math.Abs(openBracket)} tane.", 1, 1));
                    if (openBrace != 0) diagnostics.Add(new LanguageDiagnostic($"Süslü parantez dengesi bozuk! Açıkta {Math.Abs(openBrace)} tane.", 1, 1));
                }
                catch { }
                return diagnostics;
            }
        }

        private class GoErrorCheckerPlugin : BuiltInPlugin
        {
            public override string Id => "go";
            public override string Name => "Go";
            public override string[] FileExtensions => new[] { ".go" };
            public override string Description => "Go dili için basit hata denetimi";

            public override IReadOnlyList<LanguageDiagnostic> GetDiagnostics(string filePath, string content)
            {
                var diagnostics = new List<LanguageDiagnostic>();
                try
                {
                    var lines = content.Split('\n');
                    int openParen = 0, openBracket = 0, openBrace = 0;
                    
                    for (int i = 0; i < lines.Length; i++)
                    {
                        var line = lines[i];
                        var trimmed = line.Trim();
                        
                        // Parantez, köşeli parantez ve süslü parantez dengesi
                        foreach (char c in line)
                        {
                            if (c == '(') openParen++;
                            if (c == ')') openParen--;
                            if (c == '[') openBracket++;
                            if (c == ']') openBracket--;
                            if (c == '{') openBrace++;
                            if (c == '}') openBrace--;
                        }
                        
                        // Yazım hataları
                        if (trimmed.StartsWith("fun "))
                        {
                            diagnostics.Add(new LanguageDiagnostic("Olası yazım hatası: 'func' kontrol edin.", i + 1, 1));
                        }
                        if (trimmed.StartsWith("packge "))
                        {
                            diagnostics.Add(new LanguageDiagnostic("Olası yazım hatası: 'package' kontrol edin.", i + 1, 1));
                        }
                        if (trimmed.StartsWith("imprt "))
                        {
                            diagnostics.Add(new LanguageDiagnostic("Olası yazım hatası: 'import' kontrol edin.", i + 1, 1));
                        }
                    }
                    
                    if (openParen != 0) diagnostics.Add(new LanguageDiagnostic($"Parantez dengesi bozuk! Açıkta {Math.Abs(openParen)} tane.", 1, 1));
                    if (openBracket != 0) diagnostics.Add(new LanguageDiagnostic($"Köşeli parantez dengesi bozuk! Açıkta {Math.Abs(openBracket)} tane.", 1, 1));
                    if (openBrace != 0) diagnostics.Add(new LanguageDiagnostic($"Süslü parantez dengesi bozuk! Açıkta {Math.Abs(openBrace)} tane.", 1, 1));
                }
                catch { }
                return diagnostics;
            }
        }

        private class CppErrorCheckerPlugin : BuiltInPlugin
        {
            public override string Id => "cpp";
            public override string Name => "C / C++";
            public override string[] FileExtensions => new[] { ".c", ".cpp", ".cxx", ".cc", ".h", ".hpp" };
            public override string Description => "C ve C++ için basit hata denetimi";

            public override IReadOnlyList<LanguageDiagnostic> GetDiagnostics(string filePath, string content)
            {
                var diagnostics = new List<LanguageDiagnostic>();
                try
                {
                    var lines = content.Split('\n');
                    int openParen = 0, openBracket = 0, openBrace = 0;
                    
                    for (int i = 0; i < lines.Length; i++)
                    {
                        var line = lines[i];
                        var trimmed = line.Trim();
                        
                        // Parantez, köşeli parantez ve süslü parantez dengesi
                        foreach (char c in line)
                        {
                            if (c == '(') openParen++;
                            if (c == ')') openParen--;
                            if (c == '[') openBracket++;
                            if (c == ']') openBracket--;
                            if (c == '{') openBrace++;
                            if (c == '}') openBrace--;
                        }
                        
                        // Yazım hataları
                        if (trimmed.StartsWith("inclue "))
                        {
                            diagnostics.Add(new LanguageDiagnostic("Olası yazım hatası: 'include' kontrol edin.", i + 1, 1));
                        }
                        if (trimmed.StartsWith("std::cint "))
                        {
                            diagnostics.Add(new LanguageDiagnostic("Olası yazım hatası: 'std::cout' kontrol edin.", i + 1, 1));
                        }
                    }
                    
                    if (openParen != 0) diagnostics.Add(new LanguageDiagnostic($"Parantez dengesi bozuk! Açıkta {Math.Abs(openParen)} tane.", 1, 1));
                    if (openBracket != 0) diagnostics.Add(new LanguageDiagnostic($"Köşeli parantez dengesi bozuk! Açıkta {Math.Abs(openBracket)} tane.", 1, 1));
                    if (openBrace != 0) diagnostics.Add(new LanguageDiagnostic($"Süslü parantez dengesi bozuk! Açıkta {Math.Abs(openBrace)} tane.", 1, 1));
                }
                catch { }
                return diagnostics;
            }
        }

        private class DartErrorCheckerPlugin : BuiltInPlugin
        {
            public override string Id => "dart";
            public override string Name => "Dart / Flutter";
            public override string[] FileExtensions => new[] { ".dart" };
            public override string Description => "Dart ve Flutter için basit hata denetimi";

            public override IReadOnlyList<LanguageDiagnostic> GetDiagnostics(string filePath, string content)
            {
                var diagnostics = new List<LanguageDiagnostic>();
                try
                {
                    var lines = content.Split('\n');
                    int openParen = 0, openBracket = 0, openBrace = 0;
                    
                    for (int i = 0; i < lines.Length; i++)
                    {
                        var line = lines[i];
                        var trimmed = line.Trim();
                        
                        // Parantez, köşeli parantez ve süslü parantez dengesi
                        foreach (char c in line)
                        {
                            if (c == '(') openParen++;
                            if (c == ')') openParen--;
                            if (c == '[') openBracket++;
                            if (c == ']') openBracket--;
                            if (c == '{') openBrace++;
                            if (c == '}') openBrace--;
                        }
                        
                        // Yazım hataları
                        if (trimmed.StartsWith("funct "))
                        {
                            diagnostics.Add(new LanguageDiagnostic("Olası yazım hatası: 'function' kontrol edin.", i + 1, 1));
                        }
                        if (trimmed.StartsWith("strig "))
                        {
                            diagnostics.Add(new LanguageDiagnostic("Olası yazım hatası: 'String' kontrol edin.", i + 1, 1));
                        }
                        if (trimmed.StartsWith("intt "))
                        {
                            diagnostics.Add(new LanguageDiagnostic("Olası yazım hatası: 'int' kontrol edin.", i + 1, 1));
                        }
                        if (trimmed.StartsWith("widg "))
                        {
                            diagnostics.Add(new LanguageDiagnostic("Olası yazım hatası: 'Widget' kontrol edin.", i + 1, 1));
                        }
                    }
                    
                    if (openParen != 0) diagnostics.Add(new LanguageDiagnostic($"Parantez dengesi bozuk! Açıkta {Math.Abs(openParen)} tane.", 1, 1));
                    if (openBracket != 0) diagnostics.Add(new LanguageDiagnostic($"Köşeli parantez dengesi bozuk! Açıkta {Math.Abs(openBracket)} tane.", 1, 1));
                    if (openBrace != 0) diagnostics.Add(new LanguageDiagnostic($"Süslü parantez dengesi bozuk! Açıkta {Math.Abs(openBrace)} tane.", 1, 1));
                }
                catch { }
                return diagnostics;
            }
        }

        private class SqlErrorCheckerPlugin : BuiltInPlugin
        {
            public override string Id => "sql";
            public override string Name => "SQL";
            public override string[] FileExtensions => new[] { ".sql" };
            public override string Description => "SQL dosyaları için basit hata denetimi";

            public override IReadOnlyList<LanguageDiagnostic> GetDiagnostics(string filePath, string content)
            {
                var diagnostics = new List<LanguageDiagnostic>();
                try
                {
                    // SQL için özel kontroller gelecekte eklenecek
                }
                catch { }
                return diagnostics;
            }
        }

        private class RubyErrorCheckerPlugin : BuiltInPlugin
        {
            public override string Id => "ruby";
            public override string Name => "Ruby";
            public override string[] FileExtensions => new[] { ".rb" };
            public override string Description => "Ruby dili için basit hata denetimi";

            public override IReadOnlyList<LanguageDiagnostic> GetDiagnostics(string filePath, string content)
            {
                var diagnostics = new List<LanguageDiagnostic>();
                try
                {
                    // Ruby için özel kontroller gelecekte eklenecek
                }
                catch { }
                return diagnostics;
            }
        }

        private class RustErrorCheckerPlugin : BuiltInPlugin
        {
            public override string Id => "rust";
            public override string Name => "Rust";
            public override string[] FileExtensions => new[] { ".rs" };
            public override string Description => "Rust dili için basit hata denetimi";

            public override IReadOnlyList<LanguageDiagnostic> GetDiagnostics(string filePath, string content)
            {
                var diagnostics = new List<LanguageDiagnostic>();
                try
                {
                    // Rust için özel kontroller gelecekte eklenecek
                }
                catch { }
                return diagnostics;
            }
        }

        // Bu metod hala var ama sadece tek satırlar için kullanılacak (ihtiyaç olduğunda)
        private static bool CheckParenthesesBalance(string line)
        {
            int openParen = 0, openBracket = 0, openBrace = 0;
            foreach (char c in line)
            {
                if (c == '(') openParen++;
                if (c == ')') openParen--;
                if (c == '[') openBracket++;
                if (c == ']') openBracket--;
                if (c == '{') openBrace++;
                if (c == '}') openBrace--;
            }
            return openParen != 0 || openBracket != 0 || openBrace != 0;
        }

        #endregion

        // ============================================================
        // LSP Sunucusu Yönetimi — Gerçek Otomatik Kurulum Sistemi
        // ============================================================

        public enum LspInstallStrategy
        {
            GitHubRelease,   // GitHub Releases API'den zip indir + çıkart
            Npm,             // npm install -g {package}
            Pip,             // pip install {package}
            SdkBundled,      // Başka bir SDK ile gelir (Dart, Java vb.)
            Manual           // Otomatik kurulum desteklenmiyor
        }

        public class LspServerInfo
        {
            public string Id { get; set; } = "";
            public string Name { get; set; } = "";
            public string Description { get; set; } = "";
            public string[] FileExtensions { get; set; } = Array.Empty<string>();
            public string ExecutableName { get; set; } = "";
            public string[] DownloadUrls { get; set; } = Array.Empty<string>();
            public string InstallationGuideUrl { get; set; } = "";
            public bool IsInstalled { get; set; }

            // Otomatik kurulum için ek metadata
            public LspInstallStrategy InstallStrategy { get; set; } = LspInstallStrategy.Manual;
            public string? GitHubOwner { get; set; }
            public string? GitHubRepo { get; set; }
            public string? AssetPattern { get; set; }        // örn: "omnisharp-win-x64*.zip"
            public string? ExecutableSubPath { get; set; }   // zip içindeki exe yolu (null = kök dizinde ara)
            public string? NpmPackage { get; set; }
            public string? PipPackage { get; set; }
            public string? ResolvedExecutablePath { get; set; }
            public string? ResolvedSource { get; set; }
            public bool IsReady { get; set; }
            public string? LastError { get; set; }
            public string StatusLabel => !string.IsNullOrWhiteSpace(LastError)
                ? LocalizationManager.Instance.GetString("LspStatusError")
                : IsReady
                    ? LocalizationManager.Instance.GetString("LspStatusReady")
                    : IsInstalled
                        ? LocalizationManager.Instance.GetString("LspStatusInstalled")
                        : LocalizationManager.Instance.GetString("LspStatusNotInstalled");
            public string StatusColor => !string.IsNullOrWhiteSpace(LastError)
                ? "#f87171"
                : IsReady
                    ? "#34d399"
                    : IsInstalled
                        ? "#fbbf24"
                        : "#9ca3af";
            public string StatusBgColor => !string.IsNullOrWhiteSpace(LastError)
                ? "#451a1a"
                : IsReady
                    ? "#064e3b"
                    : IsInstalled
                        ? "#451a03"
                        : "#1f2937";
            public string StatusDetails => !string.IsNullOrWhiteSpace(LastError)
                ? LastError
                : ResolvedExecutablePath == null
                    ? LocalizationManager.Instance.GetString("LspExecutableNotFound")
                    : LocalizationManager.Instance.GetString("LspExecutablePathInfo")
                        .Replace("{path}", ResolvedExecutablePath)
                        .Replace("{source}", ResolvedSource ?? "");
        }

        public static class LspServerManager
        {
            public static string LspServersFolder => Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "Yengi",
                "LspServers"
            );

            private static List<LspServerInfo>? _cachedServers;

            public static List<LspServerInfo> GetAllLspServers()
            {
                if (_cachedServers != null) return _cachedServers;

                var servers = new List<LspServerInfo>
                {
                    new LspServerInfo
                    {
                        Id = "omnisharp",
                        Name = "OmniSharp",
                        Description = "C# ve .NET için tam özellikli LSP desteği",
                        FileExtensions = new[] { ".cs" },
                        ExecutableName = "OmniSharp",
                        DownloadUrls = new[] { "https://github.com/OmniSharp/omnisharp-roslyn/releases" },
                        InstallationGuideUrl = "https://github.com/OmniSharp/omnisharp-roslyn",
                        InstallStrategy = LspInstallStrategy.GitHubRelease,
                        GitHubOwner = "OmniSharp",
                        GitHubRepo = "omnisharp-roslyn",
                        AssetPattern = "omnisharp-win-x64-net6.0.zip",
                        ExecutableSubPath = null, // kök dizinde OmniSharp.exe arar
                        IsInstalled = CheckIfInstalledLocally("omnisharp", "OmniSharp") ||
                                      CheckIfExecutableOnPath("OmniSharp")
                    },
                    new LspServerInfo
                    {
                        Id = "clangd",
                        Name = "clangd",
                        Description = "C, C++ ve Objective-C için tam özellikli LSP desteği",
                        FileExtensions = new[] { ".c", ".cpp", ".cxx", ".cc", ".h", ".hpp" },
                        ExecutableName = "clangd",
                        DownloadUrls = new[] { "https://github.com/clangd/clangd/releases" },
                        InstallationGuideUrl = "https://clangd.llvm.org/installation.html",
                        InstallStrategy = LspInstallStrategy.GitHubRelease,
                        GitHubOwner = "clangd",
                        GitHubRepo = "clangd",
                        AssetPattern = "clangd-windows-*.zip",
                        ExecutableSubPath = null,
                        IsInstalled = CheckIfInstalledLocally("clangd", "clangd") ||
                                      CheckIfExecutableOnPath("clangd")
                    },
                    new LspServerInfo
                    {
                        Id = "pyright",
                        Name = "Pyright",
                        Description = "Python için hızlı ve modern LSP desteği",
                        FileExtensions = new[] { ".py" },
                        ExecutableName = "pyright-langserver",
                        DownloadUrls = new[] { "https://github.com/microsoft/pyright#installation" },
                        InstallationGuideUrl = "https://github.com/microsoft/pyright#installation",
                        InstallStrategy = LspInstallStrategy.Npm,
                        NpmPackage = "pyright",
                        PipPackage = "pyright",
                        IsInstalled = CheckIfExecutableOnPath("pyright-langserver") ||
                                      CheckIfExecutableOnPath("pyright")
                    },
                    new LspServerInfo
                    {
                        Id = "typescript",
                        Name = "TypeScript Language Server",
                        Description = "TypeScript ve JavaScript için tam özellikli LSP desteği",
                        FileExtensions = new[] { ".ts", ".tsx", ".js", ".jsx" },
                        ExecutableName = "typescript-language-server",
                        DownloadUrls = new[] { "https://www.npmjs.com/package/typescript-language-server" },
                        InstallationGuideUrl = "https://github.com/typescript-language-server/typescript-language-server",
                        InstallStrategy = LspInstallStrategy.Npm,
                        NpmPackage = "typescript-language-server typescript",
                        IsInstalled = CheckIfExecutableOnPath("typescript-language-server")
                    },
                    new LspServerInfo
                    {
                        Id = "dart",
                        Name = "Dart Analysis Server",
                        Description = "Flutter ve Dart için tam özellikli LSP desteği (Flutter SDK ile gelir)",
                        FileExtensions = new[] { ".dart" },
                        ExecutableName = "dart",
                        DownloadUrls = new[] { "https://flutter.dev/docs/get-started/install" },
                        InstallationGuideUrl = "https://flutter.dev/docs/get-started/install",
                        InstallStrategy = LspInstallStrategy.SdkBundled,
                        IsInstalled = CheckIfExecutableOnPath("dart")
                    },
                    new LspServerInfo
                    {
                        Id = "jdtls",
                        Name = "Eclipse JDT Language Server",
                        Description = "Java için tam özellikli LSP desteği (JRE 11+ gerektirir)",
                        FileExtensions = new[] { ".java" },
                        ExecutableName = "jdtls",
                        DownloadUrls = new[] { "https://download.eclipse.org/jdtls/milestones/" },
                        InstallationGuideUrl = "https://github.com/eclipse/eclipse.jdt.ls",
                        InstallStrategy = LspInstallStrategy.GitHubRelease,
                        GitHubOwner = "eclipse-jdtls",
                        GitHubRepo = "eclipse.jdt.ls",
                        AssetPattern = "jdt-language-server-*.tar.gz",
                        IsInstalled = CheckIfInstalledLocally("jdtls", "jdtls") ||
                                      CheckIfExecutableOnPath("jdtls")
                    },
                    new LspServerInfo
                    {
                        Id = "html-css",
                        Name = "HTML & CSS Language Server",
                        Description = "HTML ve CSS için IntelliSense, otomatik tamamlama ve sözdizimi doğrulaması",
                        FileExtensions = new[] { ".html", ".htm", ".css", ".scss", ".less" },
                        ExecutableName = "vscode-html-language-server",
                        DownloadUrls = new[] { "https://www.npmjs.com/package/vscode-langservers-extracted" },
                        InstallationGuideUrl = "https://github.com/hrsh7th/vscode-langservers-extracted",
                        InstallStrategy = LspInstallStrategy.Npm,
                        NpmPackage = "vscode-langservers-extracted",
                        IsInstalled = LspExecutableResolver.Resolve(
                                          null,
                                          "vscode-html-language-server",
                                          "html-languageserver",
                                          "vscode-css-language-server") != null
                    },
                    new LspServerInfo
                    {
                        Id = "gopls",
                        Name = "gopls (Go Language Server)",
                        Description = "Go dili için Google resmi LSP desteği (go install golang.org/x/tools/gopls@latest)",
                        FileExtensions = new[] { ".go" },
                        ExecutableName = "gopls",
                        DownloadUrls = new[] { "https://pkg.go.dev/golang.org/x/tools/gopls" },
                        InstallationGuideUrl = "https://github.com/golang/tools/tree/master/gopls",
                        InstallStrategy = LspInstallStrategy.Manual,
                        IsInstalled = CheckIfExecutableOnPath("gopls")
                    },
                    new LspServerInfo
                    {
                        Id = "rust-analyzer",
                        Name = "rust-analyzer (Rust)",
                        Description = "Rust dili için resmi gelişmiş LSP tamamlama ve derleme analizi",
                        FileExtensions = new[] { ".rs" },
                        ExecutableName = "rust-analyzer",
                        DownloadUrls = new[] { "https://github.com/rust-lang/rust-analyzer/releases" },
                        InstallationGuideUrl = "https://rust-analyzer.github.io/manual.html",
                        InstallStrategy = LspInstallStrategy.GitHubRelease,
                        GitHubOwner = "rust-lang",
                        GitHubRepo = "rust-analyzer",
                        AssetPattern = "rust-analyzer-x86_64-pc-windows-msvc.zip",
                        IsInstalled = CheckIfExecutableOnPath("rust-analyzer") || CheckIfInstalledLocally("rust-analyzer", "rust-analyzer")
                    },
                    new LspServerInfo
                    {
                        Id = "kotlin",
                        Name = "Kotlin Language Server",
                        Description = "Kotlin dili için akıllı kod tamamlama ve sözdizimi doğrulaması",
                        FileExtensions = new[] { ".kt", ".kts" },
                        ExecutableName = "kotlin-language-server",
                        DownloadUrls = new[] { "https://github.com/fwcd/kotlin-language-server/releases" },
                        InstallationGuideUrl = "https://github.com/fwcd/kotlin-language-server",
                        InstallStrategy = LspInstallStrategy.GitHubRelease,
                        GitHubOwner = "fwcd",
                        GitHubRepo = "kotlin-language-server",
                        AssetPattern = "server.zip",
                        IsInstalled = CheckIfExecutableOnPath("kotlin-language-server") || CheckIfInstalledLocally("kotlin", "kotlin-language-server")
                    },
                    new LspServerInfo
                    {
                        Id = "phpactor",
                        Name = "Intelephense / Phpactor (PHP)",
                        Description = "PHP için yüksek performanslı kod tamamlama ve LSP desteği",
                        FileExtensions = new[] { ".php" },
                        ExecutableName = "intelephense",
                        DownloadUrls = new[] { "https://www.npmjs.com/package/intelephense" },
                        InstallationGuideUrl = "https://intelephense.com/",
                        InstallStrategy = LspInstallStrategy.Npm,
                        NpmPackage = "intelephense",
                        IsInstalled = LspExecutableResolver.Resolve(null, "intelephense", "phpactor") != null
                    },
                    new LspServerInfo
                    {
                        Id = "solargraph",
                        Name = "Solargraph (Ruby)",
                        Description = "Ruby dili için IntelliSense, tamamlama ve dokümantasyon desteği",
                        FileExtensions = new[] { ".rb" },
                        ExecutableName = "solargraph",
                        DownloadUrls = new[] { "https://rubygems.org/gems/solargraph" },
                        InstallationGuideUrl = "https://solargraph.org/",
                        InstallStrategy = LspInstallStrategy.Manual,
                        IsInstalled = CheckIfExecutableOnPath("solargraph")
                    },
                    new LspServerInfo
                    {
                        Id = "sqls",
                        Name = "SQL Language Server (sqls)",
                        Description = "SQL sorguları ve veritabanı dosyaları için otomatik tamamlama ve LSP desteği",
                        FileExtensions = new[] { ".sql" },
                        ExecutableName = "sqls",
                        DownloadUrls = new[] { "https://github.com/sqls-server/sqls/releases" },
                        InstallationGuideUrl = "https://github.com/sqls-server/sqls",
                        InstallStrategy = LspInstallStrategy.GitHubRelease,
                        GitHubOwner = "sqls-server",
                        GitHubRepo = "sqls",
                        AssetPattern = "sqls_Windows_x86_64.zip",
                        IsInstalled = CheckIfExecutableOnPath("sqls") || CheckIfInstalledLocally("sqls", "sqls")
                    },
                    new LspServerInfo
                    {
                        Id = "swift",
                        Name = "SourceKit-LSP (Swift)",
                        Description = "Swift ve C/C++ için Apple resmi LSP sunucusu (Xcode / Swift Toolchain ile gelir)",
                        FileExtensions = new[] { ".swift" },
                        ExecutableName = "sourcekit-lsp",
                        DownloadUrls = new[] { "https://github.com/apple/sourcekit-lsp" },
                        InstallationGuideUrl = "https://github.com/apple/sourcekit-lsp",
                        InstallStrategy = LspInstallStrategy.SdkBundled,
                        IsInstalled = CheckIfExecutableOnPath("sourcekit-lsp") || CheckIfExecutableOnPath("swift")
                    }
                };

                foreach (var server in servers)
                {
                    UpdateInstallStatus(server);
                }

                _cachedServers = servers;
                return servers;
            }

            // ---- Kurulum Kontrolü ----

            /// <summary>Yerel LspServers klasörümüzde kurulu mu kontrol eder</summary>
            private static bool CheckIfInstalledLocally(string serverId, string executableName)
            {
                try
                {
                    var folder = Path.Combine(LspServersFolder, serverId);
                    if (!Directory.Exists(folder)) return false;

                    var extensions = OperatingSystem.IsWindows()
                        ? new[] { ".exe", ".cmd", ".bat" }
                        : new[] { ".exe", ".cmd", ".bat", "", ".ps1" };
                    foreach (var ext in extensions)
                    {
                        if (File.Exists(Path.Combine(folder, executableName + ext)))
                            return true;
                    }
                    // Alt klasörlerde ara
                    foreach (var dir in Directory.GetDirectories(folder))
                    {
                        foreach (var ext in extensions)
                        {
                            if (File.Exists(Path.Combine(dir, executableName + ext)))
                                return true;
                        }
                    }
                    return false;
                }
                catch { return false; }
            }

            /// <summary>Sistem PATH'inde var mı kontrol eder</summary>
            private static bool CheckIfExecutableOnPath(string executableName)
            {
                return LspExecutableResolver.Resolve(null, executableName) != null;
            }

            public static void RefreshInstallStatus()
            {
                _cachedServers = null;
                GetAllLspServers();
            }
            public static async Task RefreshRuntimeStatusAsync()
            {
                RefreshInstallStatus();
                var servers = GetAllLspServers();

                foreach (var server in servers)
                {
                    var executableNames = server.Id switch
                    {
                        "html-css" => new[] { "vscode-html-language-server", "html-languageserver", "vscode-css-language-server" },
                        "pyright" => new[] { "pyright-langserver", "pyright" },
                        _ => new[] { server.ExecutableName }
                    };

                    var resolution = LspExecutableResolver.Resolve(null, executableNames);
                    if (resolution != null)
                    {
                        server.IsInstalled = true;
                        server.ResolvedExecutablePath = resolution.ExecutablePath;
                        server.ResolvedSource = resolution.Source;
                        server.IsReady = true;
                        server.LastError = null;
                    }
                    else
                    {
                        server.IsInstalled = false;
                        server.IsReady = false;
                        server.ResolvedExecutablePath = null;
                        server.ResolvedSource = null;
                        server.LastError = null;
                    }
                }
                await Task.CompletedTask;
            }

            public static void MarkRuntimeReady(string languageExtension, string executablePath, string source)
            {
                var server = FindServer(languageExtension);
                if (server == null) return;

                server.IsInstalled = true;
                server.IsReady = true;
                server.LastError = null;
                server.ResolvedExecutablePath = executablePath;
                server.ResolvedSource = source;
            }

            public static void MarkRuntimeFailure(string languageExtension, string error)
            {
                var server = FindServer(languageExtension);
                if (server == null) return;

                server.IsReady = false;
                server.LastError = error;
            }

            private static LspServerInfo? FindServer(string languageExtension)
            {
                return GetAllLspServers().FirstOrDefault(server =>
                    server.Id.Equals(languageExtension, StringComparison.OrdinalIgnoreCase) ||
                    server.FileExtensions.Any(extension =>
                        extension.TrimStart('.').Equals(languageExtension, StringComparison.OrdinalIgnoreCase)));
            }

            private static void UpdateInstallStatus(LspServerInfo server)
            {
                var executableNames = server.Id switch
                {
                    "html-css" => new[] { "vscode-html-language-server", "html-languageserver", "vscode-css-language-server" },
                    "pyright" => new[] { "pyright-langserver", "pyright" },
                    _ => new[] { server.ExecutableName }
                };

                var resolution = LspExecutableResolver.Resolve(null, executableNames);
                server.ResolvedExecutablePath = resolution?.ExecutablePath;
                server.ResolvedSource = resolution?.Source;
                server.IsInstalled = resolution != null;
                server.IsReady = false;
                server.LastError = null;
            }

            private static string? GetHealthCheckLanguage(LspServerInfo server)
            {
                return server.Id switch
                {
                    "omnisharp" => "cs",
                    "clangd" => "cpp",
                    "pyright" => "py",
                    "typescript" => "ts",
                    "dart" => "dart",
                    "jdtls" => "java",
                    "html-css" => "html",
                    "gopls" => "go",
                    "rust-analyzer" => "rs",
                    "kotlin" => "kt",
                    "phpactor" => "php",
                    "solargraph" => "rb",
                    "sqls" => "sql",
                    "swift" => "swift",
                    _ => null
                };
            }

            // ---- Ana Kurulum Metodu ----

            public static async Task<string?> DownloadAndInstallAsync(
                LspServerInfo server,
                IProgress<(double percentage, string message)> progress)
            {
                try
                {
                    return server.InstallStrategy switch
                    {
                        LspInstallStrategy.GitHubRelease => await InstallViaGitHubAsync(server, progress),
                        LspInstallStrategy.Npm           => await InstallViaNpmAsync(server, progress),
                        LspInstallStrategy.Pip           => await InstallViaPipAsync(server, progress),
                        LspInstallStrategy.SdkBundled    => HandleSdkBundled(server, progress),
                        _                                => "Bu LSP sunucusu için otomatik kurulum desteklenmiyor. Lütfen kılavuza bakın."
                    };
                }
                catch (Exception ex)
                {
                    return $"Kurulum sırasında beklenmeyen hata: {ex.Message}";
                }
            }

            // ---- GitHub Release Kurulumu ----

            private static async Task<string?> InstallViaGitHubAsync(
                LspServerInfo server,
                IProgress<(double, string)> progress)
            {
                if (string.IsNullOrEmpty(server.GitHubOwner) || string.IsNullOrEmpty(server.GitHubRepo))
                    return "GitHub deposu bilgisi eksik.";

                progress.Report((5, "GitHub'dan son sürüm bilgisi alınıyor..."));

                // 1. GitHub API — son release
                string downloadUrl;
                string assetName;
                try
                {
                    using var http = new System.Net.Http.HttpClient();
                    http.DefaultRequestHeaders.Add("User-Agent", "mdaiAgent-LSP-Installer/1.0");
                    http.Timeout = TimeSpan.FromSeconds(15);

                    var apiUrl = $"https://api.github.com/repos/{server.GitHubOwner}/{server.GitHubRepo}/releases/latest";
                    var json = await http.GetStringAsync(apiUrl);

                    // JSON parse — System.Text.Json ile
                    using var doc = System.Text.Json.JsonDocument.Parse(json);
                    var assets = doc.RootElement.GetProperty("assets");
                    var tagName = doc.RootElement.GetProperty("tag_name").GetString() ?? "?";

                    progress.Report((8, $"Son sürüm bulundu: {tagName}"));

                    // Asset filtrele — pattern eşleştir
                    string? foundUrl = null;
                    string? foundName = null;
                    var pattern = server.AssetPattern ?? "";
                    // Wildcard pattern → Regex'e çevir
                    var regexPattern = "^" + System.Text.RegularExpressions.Regex.Escape(pattern)
                        .Replace(@"\*", ".*").Replace(@"\?", ".") + "$";

                    foreach (var asset in assets.EnumerateArray())
                    {
                        var name = asset.GetProperty("name").GetString() ?? "";
                        var url  = asset.GetProperty("browser_download_url").GetString() ?? "";
                        if (System.Text.RegularExpressions.Regex.IsMatch(name, regexPattern, System.Text.RegularExpressions.RegexOptions.IgnoreCase))
                        {
                            foundUrl  = url;
                            foundName = name;
                            break;
                        }
                    }

                    if (string.IsNullOrEmpty(foundUrl))
                        return $"GitHub release sayfasında '{pattern}' ile eşleşen dosya bulunamadı. Lütfen manuel kurulum yapın:\n{server.DownloadUrls[0]}";

                    downloadUrl = foundUrl;
                    assetName   = foundName!;
                    progress.Report((10, $"İndirilecek: {assetName}"));
                }
                catch (Exception ex)
                {
                    return $"GitHub'a erişilemedi: {ex.Message}\nManuel kurulum için: {server.DownloadUrls[0]}";
                }

                // 2. İndir
                var targetFolder = Path.Combine(LspServersFolder, server.Id);
                Directory.CreateDirectory(targetFolder);
                var zipPath = Path.Combine(targetFolder, assetName);

                try
                {
                    using var http = new System.Net.Http.HttpClient();
                    http.DefaultRequestHeaders.Add("User-Agent", "mdaiAgent-LSP-Installer/1.0");
                    http.Timeout = TimeSpan.FromMinutes(10);

                    using var response = await http.GetAsync(downloadUrl, System.Net.Http.HttpCompletionOption.ResponseHeadersRead);
                    response.EnsureSuccessStatusCode();

                    var totalBytes = response.Content.Headers.ContentLength ?? 0L;
                    var totalMB    = totalBytes > 0 ? $" / {totalBytes / 1024.0 / 1024.0:F1} MB" : "";

                    using var fileStream   = new FileStream(zipPath, FileMode.Create, FileAccess.Write, FileShare.None, 81920, true);
                    using var httpStream   = await response.Content.ReadAsStreamAsync();

                    var buffer       = new byte[81920];
                    long downloaded  = 0;
                    int  read;

                    while ((read = await httpStream.ReadAsync(buffer, 0, buffer.Length)) > 0)
                    {
                        await fileStream.WriteAsync(buffer, 0, read);
                        downloaded += read;

                        double pct = totalBytes > 0 ? 10 + (downloaded / (double)totalBytes) * 75 : 50;
                        var dlMB   = $"{downloaded / 1024.0 / 1024.0:F1} MB";
                        progress.Report((pct, $"İndiriliyor: {dlMB}{totalMB}"));
                    }
                }
                catch (Exception ex)
                {
                    TryDeleteFile(zipPath);
                    return $"İndirme başarısız: {ex.Message}";
                }

                // 3. Çıkart
                progress.Report((87, "Dosyalar çıkartılıyor..."));
                try
                {
                    // Önceki kurulumu temizle (sadece eski exe dosyalarını sil, zip'i koru)
                    var extractPath = Path.Combine(targetFolder, "bin");
                    if (Directory.Exists(extractPath))
                        Directory.Delete(extractPath, true);
                    Directory.CreateDirectory(extractPath);

                    if (zipPath.EndsWith(".tar.gz", StringComparison.OrdinalIgnoreCase))
                    {
                        // tar.gz için system tar komutunu kullan (Windows 10 1803+ dahili)
                        var psi = new System.Diagnostics.ProcessStartInfo
                        {
                            FileName = "tar",
                            Arguments = $"-xzf \"{zipPath}\" -C \"{extractPath}\"",
                            UseShellExecute = false,
                            CreateNoWindow = true,
                            RedirectStandardOutput = true,
                            RedirectStandardError = true
                        };
                        using var proc = System.Diagnostics.Process.Start(psi);
                        proc?.WaitForExit(60000);
                    }
                    else
                    {
                        System.IO.Compression.ZipFile.ExtractToDirectory(zipPath, extractPath, overwriteFiles: true);
                    }

                    // İndirilen zip'i temizle (yer tasarrufu)
                    File.Delete(zipPath);
                }
                catch (Exception ex)
                {
                    TryDeleteFile(zipPath);
                    return $"Dosyalar çıkartılırken hata: {ex.Message}";
                }

                progress.Report((98, "Kurulum doğrulanıyor..."));

                // 4. Doğrula
                if (!CheckIfInstalledLocally(server.Id, server.ExecutableName))
                {
                    TryDeleteFile(zipPath);
                    return $"{server.Name} kuruldu ancak çalıştırılabilir dosya bulunamadı. " +
                           $"Lütfen {Path.Combine(LspServersFolder, server.Id, "bin")} klasörünü kontrol edin.";
                }

                progress.Report((100, $"{server.Name} başarıyla kuruldu! ✅"));
                return null; // başarı
            }

            private static void TryDeleteFile(string path)
            {
                try
                {
                    if (File.Exists(path))
                        File.Delete(path);
                }
                catch
                {
                    // Cleanup is best effort; the original installation error remains authoritative.
                }
            }

            // ---- npm Kurulumu ----

            private static async Task<string?> InstallViaNpmAsync(
                LspServerInfo server,
                IProgress<(double, string)> progress)
            {
                // npm var mı kontrol et
                progress.Report((5, "npm kontrol ediliyor..."));
                if (!CheckIfExecutableOnPath("npm"))
                {
                    // pip ile dene (Pyright için)
                    if (!string.IsNullOrEmpty(server.PipPackage))
                        return await InstallViaPipAsync(server, progress);

                    return "npm bulunamadı. Lütfen önce Node.js kurun:\nhttps://nodejs.org/en/download/";
                }

                progress.Report((15, $"npm ile kuruluyor: {server.NpmPackage}..."));
                try
                {
                    var psi = new System.Diagnostics.ProcessStartInfo
                    {
                        FileName  = "npm",
                        Arguments = $"install -g {server.NpmPackage}",
                        UseShellExecute       = false,
                        CreateNoWindow        = true,
                        RedirectStandardOutput = true,
                        RedirectStandardError  = true
                    };

                    using var proc = new System.Diagnostics.Process { StartInfo = psi };
                    proc.Start();

                    // Çıktıyı oku ve progress olarak bildir
                    var outputTask = Task.Run(async () =>
                    {
                        string? line;
                        while ((line = await proc.StandardOutput.ReadLineAsync()) != null)
                        {
                            if (!string.IsNullOrWhiteSpace(line))
                                progress.Report((50, line.Trim().Length > 80 ? line.Trim()[..80] : line.Trim()));
                        }
                    });

                    await Task.WhenAny(outputTask, Task.Run(() => proc.WaitForExit(120000)));

                    if (proc.ExitCode != 0)
                    {
                        var err = await proc.StandardError.ReadToEndAsync();
                        return $"npm kurulumu başarısız (kod {proc.ExitCode}):\n{err.Trim()}";
                    }

                    progress.Report((100, $"{server.Name} npm ile başarıyla kuruldu! ✅"));
                    return null;
                }
                catch (Exception ex)
                {
                    return $"npm kurulumu sırasında hata: {ex.Message}";
                }
            }

            // ---- pip Kurulumu ----

            private static async Task<string?> InstallViaPipAsync(
                LspServerInfo server,
                IProgress<(double, string)> progress)
            {
                if (string.IsNullOrEmpty(server.PipPackage))
                    return "pip paketi bilgisi eksik.";

                // pip veya pip3 var mı kontrol et
                progress.Report((5, "pip kontrol ediliyor..."));
                var pipCmd = CheckIfExecutableOnPath("pip3") ? "pip3"
                           : CheckIfExecutableOnPath("pip")  ? "pip"
                           : null;

                if (pipCmd == null)
                    return "pip bulunamadı. Lütfen önce Python kurun:\nhttps://www.python.org/downloads/";

                progress.Report((15, $"pip ile kuruluyor: {server.PipPackage}..."));
                try
                {
                    var psi = new System.Diagnostics.ProcessStartInfo
                    {
                        FileName  = pipCmd,
                        Arguments = $"install {server.PipPackage}",
                        UseShellExecute       = false,
                        CreateNoWindow        = true,
                        RedirectStandardOutput = true,
                        RedirectStandardError  = true
                    };

                    using var proc = new System.Diagnostics.Process { StartInfo = psi };
                    proc.Start();

                    var outputTask = Task.Run(async () =>
                    {
                        string? line;
                        while ((line = await proc.StandardOutput.ReadLineAsync()) != null)
                        {
                            if (!string.IsNullOrWhiteSpace(line))
                                progress.Report((50, line.Trim().Length > 80 ? line.Trim()[..80] : line.Trim()));
                        }
                    });

                    await Task.WhenAny(outputTask, Task.Run(() => proc.WaitForExit(120000)));

                    if (proc.ExitCode != 0)
                    {
                        var err = await proc.StandardError.ReadToEndAsync();
                        return $"pip kurulumu başarısız (kod {proc.ExitCode}):\n{err.Trim()}";
                    }

                    progress.Report((100, $"{server.Name} pip ile başarıyla kuruldu! ✅"));
                    return null;
                }
                catch (Exception ex)
                {
                    return $"pip kurulumu sırasında hata: {ex.Message}";
                }
            }

            // ---- SDK Bundled ----

            private static string HandleSdkBundled(
                LspServerInfo server,
                IProgress<(double, string)> progress)
            {
                progress.Report((50, $"{server.Name} sistem PATH'inde aranıyor..."));

                if (CheckIfExecutableOnPath(server.ExecutableName))
                {
                    progress.Report((100, $"{server.Name} zaten kurulu! ✅"));
                    return null!;
                }

                progress.Report((100, "SDK bulunamadı."));
                return server.Id switch
                {
                    "dart" => "Dart Analysis Server, Flutter SDK ile birlikte gelir.\n" +
                              "Flutter'ı kurmak için:\nhttps://flutter.dev/docs/get-started/install\n\n" +
                              "Kurulum sonrası 'Durumu Yenile' butonuna tıklayın.",
                    _      => $"{server.Name} için SDK bulunamadı.\nKurulum kılavuzu:\n{server.InstallationGuideUrl}"
                };
            }
        }
    }
}
