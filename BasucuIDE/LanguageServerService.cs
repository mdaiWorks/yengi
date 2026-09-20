using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace mdaiAgent
{
    public class LanguageServerService : IDisposable
    {
        private Process? _lspProcess;
        private bool _disposed;
        private int _nextRequestId = 1;
        private readonly ConcurrentDictionary<int, TaskCompletionSource<JsonElement>> _pendingRequests = new();
        private readonly SemaphoreSlim _writeLock = new(1, 1);
        private CancellationTokenSource? _cancellationTokenSource;
        public string? ActiveLanguageExtension { get; private set; }
        
        public bool IsConnected => _lspProcess != null && !_lspProcess.HasExited;

        public bool IsServingLanguage(string languageExtension) =>
            IsConnected && string.Equals(ActiveLanguageExtension, languageExtension, StringComparison.OrdinalIgnoreCase);
        
        public event EventHandler<string>? LogReceived;
        public event EventHandler<(string filePath, LanguageDiagnostic[] diagnostics)>? DiagnosticsReceived;
        
        public async Task<bool> StartServerAsync(string languageExtension, string projectPath)
        {
            try
            {
                if (IsServingLanguage(languageExtension)) return true;
                if (IsConnected) StopServer();
                if (!LspLanguageRegistry.TryGet(languageExtension, out var serverInfo)) return false;

                var executableNames = LspLanguageRegistry.GetExecutableNames(serverInfo);

                var executableResolution = LspExecutableResolver.Resolve(projectPath, executableNames);
                var executablePath = executableResolution?.ExecutablePath;

                if (string.IsNullOrWhiteSpace(executablePath))
                {
                    PluginManager.LspServerManager.MarkRuntimeFailure(
                        languageExtension,
                        $"LSP yürütülebilir bulunamadı: {string.Join(", ", executableNames)}");
                    LogReceived?.Invoke(this, $"LSP yürütülebilir bulunamadı: {serverInfo.ExecutableName}");
                    if (!string.IsNullOrWhiteSpace(projectPath))
                    {
                        LogReceived?.Invoke(this, $"{projectPath} projesinde local paket araması yapılacak.");
                    }
                    return false;
                }

                LogReceived?.Invoke(this, Localization.Format(
                    "LSP yürütülebilir bulundu: {0} ({1})",
                    "LSP executable found: {0} ({1})",
                    executablePath,
                    executableResolution!.Source));

                if ((languageExtension.Equals("js", StringComparison.OrdinalIgnoreCase) || languageExtension.Equals("ts", StringComparison.OrdinalIgnoreCase))
                    && !IsTypeScriptAvailable(projectPath))
                {
                    var version = GetTypeScriptPackageVersion(projectPath);
                    var tsserverPath = FindTypeScriptServerPath(projectPath);
                    if (!string.IsNullOrWhiteSpace(version) && string.IsNullOrWhiteSpace(tsserverPath))
                    {
                        LogReceived?.Invoke(this, $"Workspace'ta TypeScript {version} yüklü, ancak tsserver.js veya uyumlu tsserver yolu bulunamadı. `typescript-language-server` için TypeScript 5.x/6.x kullanın. `npm install --save-dev typescript@^5.9 typescript-language-server` veya `npm install -g typescript@^5.9 typescript-language-server` çalıştırın.");
                    }
                    else
                    {
                        LogReceived?.Invoke(this, "TypeScript Language Server bulundu ancak uyumlu tsserver.js bulunamadı. Lütfen proje kökünde `npm install --save-dev typescript@^5.9 typescript-language-server` veya global `npm install -g typescript@^5.9 typescript-language-server` çalıştırın.");
                    }
                    return false;
                }

                var args = serverInfo.Arguments.Select(arg => Environment.ExpandEnvironmentVariables(arg)).ToList();

                _cancellationTokenSource = new CancellationTokenSource();
                
                var processedArgs = args.Select(QuoteArgument).ToArray();
                
                var startInfo = new ProcessStartInfo
                {
                    FileName = executablePath,
                    Arguments = string.Join(" ", processedArgs),
                    UseShellExecute = false,
                    RedirectStandardInput = true,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    CreateNoWindow = true,
                    StandardOutputEncoding = Encoding.UTF8,
                    StandardErrorEncoding = Encoding.UTF8,
                };
                
                if (!string.IsNullOrEmpty(projectPath))
                {
                    startInfo.WorkingDirectory = projectPath;
                }

                _lspProcess = Process.Start(startInfo);
                if (_lspProcess == null) return false;

                // Start reading output and error streams
                _ = Task.Run(() => ReadOutputAsync(_cancellationTokenSource.Token));
                _ = Task.Run(() => ReadErrorAsync(_cancellationTokenSource.Token));

                // Send initialize request with a bounded wait so a broken server cannot hang the UI.
                var initializeTask = InitializeAsync(projectPath);
                var completedTask = await Task.WhenAny(initializeTask, Task.Delay(TimeSpan.FromSeconds(10)));
                if (completedTask != initializeTask)
                    throw new TimeoutException("LSP initialize zaman aşımına uğradı.");

                await initializeTask;
                ActiveLanguageExtension = languageExtension;
                PluginManager.LspServerManager.MarkRuntimeReady(
                    languageExtension,
                    executablePath,
                    executableResolution!.Source);
                
                LogReceived?.Invoke(this, Localization.Format(
                    "{0} LSP sunucusu başarıyla başlatıldı.",
                    "{0} LSP server started successfully.",
                    Path.GetFileName(executablePath)));
                return true;
            }
            catch (Exception ex)
            {
                StopServer();
                PluginManager.LspServerManager.MarkRuntimeFailure(languageExtension, ex.Message);
                LogReceived?.Invoke(this, Localization.Format(
                    "LSP başlatma hatası: {0}",
                    "LSP startup error: {0}",
                    ex.Message));
                return false;
            }
        }

        private async Task ReadOutputAsync(CancellationToken cancellationToken)
        {
            var process = _lspProcess;
            if (process == null) return;
            
            try
            {
                var stream = process.StandardOutput.BaseStream;
                
                while (!cancellationToken.IsCancellationRequested && !process.HasExited)
                {
                    var line = await ReadAsciiLineAsync(stream, cancellationToken);
                    if (line == null) break;
                    
                    if (line.StartsWith("Content-Length:"))
                    {
                        var lengthStr = line.Substring("Content-Length:".Length).Trim();
                        if (int.TryParse(lengthStr, out var contentLength))
                        {
                            await ReadAsciiLineAsync(stream, cancellationToken);

                            var buffer = new byte[contentLength];
                            var totalRead = 0;
                            while (totalRead < contentLength)
                            {
                                var read = await stream.ReadAsync(buffer.AsMemory(totalRead, contentLength - totalRead), cancellationToken);
                                if (read == 0) break;
                                totalRead += read;
                            }
                            
                            ProcessMessage(Encoding.UTF8.GetString(buffer, 0, totalRead));
                        }
                    }
                }
            }
            catch (OperationCanceledException)
            {
                // Expected when stopping
            }
            catch (IOException)
            {
                // The process can close its stream while the session is stopping.
            }
        }

        private static async Task<string?> ReadAsciiLineAsync(Stream stream, CancellationToken cancellationToken)
        {
            var bytes = new List<byte>();
            var buffer = new byte[1];

            while (true)
            {
                var read = await stream.ReadAsync(buffer.AsMemory(), cancellationToken);
                if (read == 0)
                    return bytes.Count == 0 ? null : Encoding.ASCII.GetString(bytes.ToArray());

                if (buffer[0] == (byte)'\n')
                    return Encoding.ASCII.GetString(bytes.ToArray()).TrimEnd('\r');

                bytes.Add(buffer[0]);
            }
        }

        private async Task ReadErrorAsync(CancellationToken cancellationToken)
        {
            if (_lspProcess == null) return;
            
            try
            {
                var reader = _lspProcess.StandardError;
                string? line;
                
                while (!cancellationToken.IsCancellationRequested && !_lspProcess.HasExited)
                {
                    line = await reader.ReadLineAsync(cancellationToken);
                    if (line != null)
                    {
                        LogReceived?.Invoke(this, line);
                    }
                }
            }
            catch (OperationCanceledException)
            {
                // Expected when stopping
            }
        }

        private void ProcessMessage(string content)
        {
            try
            {
                var json = JsonSerializer.Deserialize<JsonElement>(content);
                
                if (json.TryGetProperty("id", out var idProp) && idProp.ValueKind == JsonValueKind.Number)
                {
                    var id = idProp.GetInt32();
                    if (_pendingRequests.TryRemove(id, out var tcs))
                    {
                        if (json.TryGetProperty("result", out var result))
                        {
                            tcs.SetResult(result);
                        }
                        else if (json.TryGetProperty("error", out var error))
                        {
                            tcs.SetException(new Exception(error.GetRawText()));
                        }
                    }
                }
                else if (json.TryGetProperty("method", out var methodProp))
                {
                    var method = methodProp.GetString();
                    if (method == "textDocument/publishDiagnostics")
                    {
                        ProcessDiagnostics(json);
                    }
                }
            }
            catch
            {
                // Ignore invalid JSON for now
            }
        }

        private void ProcessDiagnostics(JsonElement json)
        {
            if (!json.TryGetProperty("params", out var paramsProp)) return;
            
            string? filePath = null;
            if (paramsProp.TryGetProperty("uri", out var uriProp))
            {
                var uriString = uriProp.GetString();
                if (uriString != null && Uri.TryCreate(uriString, UriKind.Absolute, out var uri))
                {
                    try
                    {
                        filePath = uri.LocalPath;
                    }
                    catch { }
                }
            }
            
            var diagnostics = new List<LanguageDiagnostic>();
            if (paramsProp.TryGetProperty("diagnostics", out var diagsArray) && diagsArray.ValueKind == JsonValueKind.Array)
            {
                foreach (var diag in diagsArray.EnumerateArray())
                {
                    try
                    {
                        var range = diag.GetProperty("range");
                        var start = range.GetProperty("start");
                        
                        diagnostics.Add(new LanguageDiagnostic(
                            diag.GetProperty("message").GetString() ?? "",
                            start.GetProperty("line").GetInt32() + 1,
                            start.GetProperty("character").GetInt32() + 1
                        ) { Source = DiagnosticSource.Lsp });
                    }
                    catch
                    {
                        // Skip invalid diagnostics
                    }
                }
            }
            
            if (filePath != null)
            {
                DiagnosticsReceived?.Invoke(this, (filePath, diagnostics.ToArray()));
            }
        }

        private async Task InitializeAsync(string projectPath)
        {
            var workspaceFolders = string.IsNullOrWhiteSpace(projectPath)
                ? null
                : new[] { new { uri = new Uri(projectPath).AbsoluteUri, name = Path.GetFileName(projectPath) } };

            var initializeParams = new
            {
                processId = Process.GetCurrentProcess().Id,
                rootPath = projectPath,
                rootUri = projectPath != null ? new Uri(projectPath).AbsoluteUri : null,
                capabilities = new
                {
                    workspace = new
                    {
                        workspaceFolders = true,
                        configuration = true,
                    },
                    textDocument = new
                    {
                        synchronization = new
                        {
                            didSave = true,
                            willSave = false,
                            willSaveWaitUntil = false,
                        }
                    }
                },
                trace = "off",
                workspaceFolders = workspaceFolders,
            };

            await SendRequestAsync("initialize", initializeParams);
            await SendNotificationAsync("initialized", new { });
        }

        public async Task OpenDocumentAsync(string filePath, string content, string languageId)
        {
            if (!IsConnected) return;
            
            var uri = new Uri(filePath).AbsoluteUri;
            await SendNotificationAsync("textDocument/didOpen", new
            {
                textDocument = new
                {
                    uri = uri,
                    languageId = languageId,
                    version = 1,
                    text = content,
                }
            });
        }

        public async Task ChangeDocumentAsync(string filePath, string content, int version)
        {
            if (!IsConnected) return;
            
            var uri = new Uri(filePath).AbsoluteUri;
            await SendNotificationAsync("textDocument/didChange", new
            {
                textDocument = new
                {
                    uri = uri,
                    version = version,
                },
                contentChanges = new[]
                {
                    new { text = content }
                }
            });
        }

        public async Task<JsonElement> SendRequestAsync(string method, object @params)
        {
            if (!IsConnected) throw new InvalidOperationException("Language server not connected");
            
            var requestId = Interlocked.Increment(ref _nextRequestId);
            var tcs = new TaskCompletionSource<JsonElement>();
            _pendingRequests[requestId] = tcs;

            var request = new
            {
                jsonrpc = "2.0",
                id = requestId,
                method = method,
                @params = @params,
            };

            var json = JsonSerializer.Serialize(request);
            var contentLength = Encoding.UTF8.GetByteCount(json);
            var message = $"Content-Length: {contentLength}\r\n\r\n{json}";

            await _writeLock.WaitAsync();
            try
            {
                if (_lspProcess == null || _lspProcess.HasExited)
                    throw new InvalidOperationException("Language server process exited");

                await _lspProcess.StandardInput.WriteAsync(message);
                await _lspProcess.StandardInput.FlushAsync();
            }
            finally
            {
                _writeLock.Release();
            }

            return await tcs.Task;
        }

        public async Task SendNotificationAsync(string method, object @params)
        {
            if (!IsConnected) return;
            
            var notification = new
            {
                jsonrpc = "2.0",
                method = method,
                @params = @params,
            };

            var json = JsonSerializer.Serialize(notification);
            var contentLength = Encoding.UTF8.GetByteCount(json);
            var message = $"Content-Length: {contentLength}\r\n\r\n{json}";

            await _writeLock.WaitAsync();
            try
            {
                if (_lspProcess == null || _lspProcess.HasExited) return;

                await _lspProcess.StandardInput.WriteAsync(message);
                await _lspProcess.StandardInput.FlushAsync();
            }
            catch
            {
                // Process stream closed
            }
            finally
            {
                _writeLock.Release();
            }
        }

        public void StopServer()
        {
            try
            {
                _cancellationTokenSource?.Cancel();
                
                if (_lspProcess != null && !_lspProcess.HasExited)
                {
                    try
                    {
                        // Fire-and-forget shutdown notifications; do not block the caller thread.
                        var _ = Task.Run(async () =>
                        {
                            try
                            {
                                await SendNotificationAsync("shutdown", new { }).ConfigureAwait(false);
                                await SendNotificationAsync("exit", new { }).ConfigureAwait(false);
                            }
                            catch
                            {
                                // ignore
                            }
                        });
                    }
                    catch
                    {
                        // Ignore cleanup errors
                    }

                    if (!_lspProcess.HasExited)
                    {
                        _lspProcess.Kill(true);
                    }
                }
                
                _lspProcess?.WaitForExit(2000);
                _lspProcess?.Dispose();
            }
            catch
            {
                // Ignore cleanup errors
            }
            finally
            {
                _lspProcess = null;
                ActiveLanguageExtension = null;
                _cancellationTokenSource?.Dispose();
                _cancellationTokenSource = null;
            }
        }


        private static string? FindExecutableInLspServersFolder(string executableName)
        {
            try
            {
                var lspDir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Yengi", "LspServers");
                if (!Directory.Exists(lspDir)) return null;
                
                var extensions = new[] { ".exe", ".cmd", ".bat", "", ".ps1" };
                
                // Root kontrolü
                foreach (var ext in extensions)
                {
                    var p = Path.Combine(lspDir, executableName + ext);
                    if (File.Exists(p)) return p;
                }
                
                // Alt klasörlerde arama
                foreach (var dir in Directory.GetDirectories(lspDir))
                {
                    foreach (var ext in extensions)
                    {
                        var p = Path.Combine(dir, executableName + ext);
                        if (File.Exists(p)) return p;
                    }
                    
                    var binDir = Path.Combine(dir, "bin");
                    if (Directory.Exists(binDir))
                    {
                        foreach (var ext in extensions)
                        {
                            var p = Path.Combine(binDir, executableName + ext);
                            if (File.Exists(p)) return p;
                        }
                        
                        foreach (var subDir in Directory.GetDirectories(binDir, "*", SearchOption.AllDirectories))
                        {
                            foreach (var ext in extensions)
                            {
                                var p = Path.Combine(subDir, executableName + ext);
                                if (File.Exists(p)) return p;
                            }
                        }
                    }
                }
            }
            catch { }
            return null;
        }

        private static string? FindExecutableOnPath(string executableName)
        {
            if (string.IsNullOrWhiteSpace(executableName))
                return null;

            if (Path.IsPathRooted(executableName) && File.Exists(executableName))
                return executableName;

            var pathEnv = Environment.GetEnvironmentVariable("PATH");
            if (string.IsNullOrWhiteSpace(pathEnv))
                return null;

            foreach (var pathDir in pathEnv.Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries))
            {
                var candidate = FindExecutableInDirectory(executableName, pathDir);
                if (!string.IsNullOrWhiteSpace(candidate))
                    return candidate;
            }

            return null;
        }

        private static string? FindExecutableInProjectNodeModulesBin(string executableName, string? projectPath)
        {
            if (string.IsNullOrWhiteSpace(projectPath))
                return null;

            var current = new DirectoryInfo(projectPath);
            while (current != null)
            {
                var nodeBin = Path.Combine(current.FullName, "node_modules", ".bin");
                var candidate = FindExecutableInDirectory(executableName, nodeBin);
                if (!string.IsNullOrWhiteSpace(candidate))
                    return candidate;

                current = current.Parent;
            }

            return null;
        }

        private static string? FindExecutableInDirectory(string executableName, string directory)
        {
            if (string.IsNullOrWhiteSpace(directory) || !Directory.Exists(directory))
                return null;

            var extensions = Environment.GetEnvironmentVariable("PATHEXT")?.Split(';', StringSplitOptions.RemoveEmptyEntries)
                ?? new[] { ".exe", ".cmd", ".bat", ".ps1" };

            var candidate = Path.Combine(directory, executableName);
            foreach (var ext in extensions)
            {
                var candidateWithExt = candidate.EndsWith(ext, StringComparison.OrdinalIgnoreCase)
                    ? candidate
                    : candidate + ext;
                if (File.Exists(candidateWithExt))
                    return candidateWithExt;
            }

            if (File.Exists(candidate))
                return candidate;

            return null;
        }

        private static string QuoteArgument(string value)
        {
            if (string.IsNullOrEmpty(value))
                return string.Empty;

            if (value.Contains(' ') || value.Contains('\t') || value.Contains('"'))
                return '"' + value.Replace("\"", "\\\"") + '"';

            return value;
        }

        private static bool IsTypeScriptAvailable(string? projectPath)
        {
            if (FindTypeScriptServerPath(projectPath) != null)
                return true;

            return false;
        }

        private static string? FindTypeScriptServerPath(string? projectPath)
        {
            if (!string.IsNullOrWhiteSpace(projectPath))
            {
                var current = new DirectoryInfo(projectPath);
                while (current != null)
                {
                    var tsserverPath = Path.Combine(current.FullName, "node_modules", "typescript", "lib", "tsserver.js");
                    if (File.Exists(tsserverPath))
                        return tsserverPath;

                    current = current.Parent;
                }
            }

            var tscPath = FindExecutableOnPath("tsc");
            if (!string.IsNullOrWhiteSpace(tscPath))
            {
                var npmDirectory = Directory.GetParent(tscPath)?.FullName;
                if (!string.IsNullOrWhiteSpace(npmDirectory))
                {
                    var globalTsserverPath = Path.Combine(npmDirectory, "node_modules", "typescript", "lib", "tsserver.js");
                    if (File.Exists(globalTsserverPath))
                        return globalTsserverPath;
                }
            }

            return null;
        }

        private static string? GetTypeScriptPackageVersion(string? projectPath)
        {
            if (string.IsNullOrWhiteSpace(projectPath))
                return null;

            var current = new DirectoryInfo(projectPath);
            while (current != null)
            {
                var packageJson = Path.Combine(current.FullName, "node_modules", "typescript", "package.json");
                if (File.Exists(packageJson))
                {
                    try
                    {
                        using var doc = JsonDocument.Parse(File.ReadAllText(packageJson));
                        if (doc.RootElement.TryGetProperty("version", out var versionProp))
                            return versionProp.GetString();
                    }
                    catch
                    {
                        // ignore invalid package.json
                    }
                }

                current = current.Parent;
            }

            return null;
        }

        private static bool HasPackageJsonPackage(string? projectPath, string packageName)
        {
            if (string.IsNullOrWhiteSpace(projectPath))
                return false;

            var current = new DirectoryInfo(projectPath);
            while (current != null)
            {
                var packageJson = Path.Combine(current.FullName, "package.json");
                if (File.Exists(packageJson))
                {
                    try
                    {
                        using var doc = JsonDocument.Parse(File.ReadAllText(packageJson));
                        if (doc.RootElement.TryGetProperty("dependencies", out var deps) && deps.ValueKind == JsonValueKind.Object && deps.TryGetProperty(packageName, out _))
                            return true;
                        if (doc.RootElement.TryGetProperty("devDependencies", out var devDeps) && devDeps.ValueKind == JsonValueKind.Object && devDeps.TryGetProperty(packageName, out _))
                            return true;
                    }
                    catch
                    {
                        // ignore invalid package.json
                    }
                }

                current = current.Parent;
            }

            return false;
        }

        public async Task<List<CompletionItem>> GetCompletionsAsync(string filePath, int line, int character)
        {
            if (!IsConnected) return new List<CompletionItem>();
            
            var uri = new Uri(filePath).AbsoluteUri;
            var result = await SendRequestAsync("textDocument/completion", new
            {
                textDocument = new { uri = uri },
                position = new { line = line, character = character }
            });
            
            var items = new List<CompletionItem>();
            
            try
            {
                if (result.TryGetProperty("items", out var itemsArray) && itemsArray.ValueKind == JsonValueKind.Array)
                {
                    foreach (var item in itemsArray.EnumerateArray())
                    {
                        var label = item.GetProperty("label").GetString() ?? "";
                        var kind = item.TryGetProperty("kind", out var kindProp) ? kindProp.GetInt32() : 0;
                        var insertText = item.TryGetProperty("insertText", out var insertTextProp) 
                            ? insertTextProp.GetString() ?? label 
                            : label;
                        
                        items.Add(new CompletionItem
                        {
                            Label = label,
                            Kind = (CompletionItemKind)kind,
                            InsertText = insertText
                        });
                    }
                }
            }
            catch
            {
                // Ignore parsing errors
            }
            
            return items;
        }

        public async Task<List<LanguageLocation>> GetDefinitionsAsync(string filePath, int line, int character)
        {
            if (!IsConnected) return new List<LanguageLocation>();

            var result = await SendRequestAsync("textDocument/definition", new
            {
                textDocument = new { uri = new Uri(filePath).AbsoluteUri },
                position = new { line, character }
            });

            var locations = new List<LanguageLocation>();
            if (result.ValueKind == JsonValueKind.Object)
            {
                AddLocation(result, locations);
            }
            else if (result.ValueKind == JsonValueKind.Array)
            {
                foreach (var location in result.EnumerateArray())
                    AddLocation(location, locations);
            }

            return locations;
        }

        public async Task<List<LanguageLocation>> GetReferencesAsync(string filePath, int line, int character)
        {
            if (!IsConnected) return new List<LanguageLocation>();

            var result = await SendRequestAsync("textDocument/references", new
            {
                textDocument = new { uri = new Uri(filePath).AbsoluteUri },
                position = new { line, character },
                context = new { includeDeclaration = true }
            });

            return ParseLocations(result);
        }

        public async Task<List<LanguageSymbol>> GetDocumentSymbolsAsync(string filePath)
        {
            if (!IsConnected) return new List<LanguageSymbol>();

            var result = await SendRequestAsync("textDocument/documentSymbol", new
            {
                textDocument = new { uri = new Uri(filePath).AbsoluteUri }
            });

            var symbols = new List<LanguageSymbol>();
            if (result.ValueKind != JsonValueKind.Array)
                return symbols;

            foreach (var symbol in result.EnumerateArray())
            {
                if (!symbol.TryGetProperty("name", out var nameProp) || !symbol.TryGetProperty("range", out var rangeProp))
                    continue;

                var start = rangeProp.GetProperty("start");
                symbols.Add(new LanguageSymbol(
                    nameProp.GetString() ?? "",
                    symbol.TryGetProperty("kind", out var kindProp) ? kindProp.GetInt32() : 0,
                    new LanguageLocation(
                        filePath,
                        start.GetProperty("line").GetInt32() + 1,
                        start.GetProperty("character").GetInt32() + 1)));
            }

            return symbols;
        }

        private static List<LanguageLocation> ParseLocations(JsonElement result)
        {
            var locations = new List<LanguageLocation>();
            if (result.ValueKind != JsonValueKind.Array)
                return locations;

            foreach (var location in result.EnumerateArray())
                AddLocation(location, locations);

            return locations;
        }

        private static void AddLocation(JsonElement element, List<LanguageLocation> locations)
        {
            if (!element.TryGetProperty("uri", out var uriProp) || !element.TryGetProperty("range", out var rangeProp))
                return;

            var uriText = uriProp.GetString();
            if (!Uri.TryCreate(uriText, UriKind.Absolute, out var uri))
                return;

            var start = rangeProp.GetProperty("start");
            locations.Add(new LanguageLocation(
                uri.LocalPath,
                start.GetProperty("line").GetInt32() + 1,
                start.GetProperty("character").GetInt32() + 1));
        }

        public void Dispose()
        {
            if (!_disposed)
            {
                StopServer();
                _disposed = true;
            }
        }
    }

    public class CompletionItem
    {
        public string Label { get; set; } = "";
        public CompletionItemKind Kind { get; set; }
        public string InsertText { get; set; } = "";
    }

    public sealed record LanguageLocation(string FilePath, int Line, int Character);

    public sealed record LanguageSymbol(string Name, int Kind, LanguageLocation Location);

    public enum CompletionItemKind
    {
        Text = 1,
        Method = 2,
        Function = 3,
        Constructor = 4,
        Field = 5,
        Variable = 6,
        Class = 7,
        Interface = 8,
        Module = 9,
        Property = 10,
        Unit = 11,
        Value = 12,
        Enum = 13,
        Keyword = 14,
        Snippet = 15,
        Color = 16,
        File = 17,
        Reference = 18,
        Folder = 19,
        EnumMember = 20,
        Constant = 21,
        Struct = 22,
        Event = 23,
        Operator = 24,
        TypeParameter = 25
    }
}
