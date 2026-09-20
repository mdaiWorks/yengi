
using System;
using System.Diagnostics;
using System.Text.Json;
using System.Threading.Tasks;
using StreamJsonRpc;

namespace mdaiAgent;

/// &lt;summary&gt;
/// Basit bir LSP istemci (client) sınıfı
/// &lt;/summary&gt;
public class LanguageServerClient : IDisposable
{
    private Process? _serverProcess;
    private JsonRpc? _rpc;
    private bool _disposed;

    /// &lt;summary&gt;
    /// Dil sunucusunu başlat
    /// &lt;/summary&gt;
    public async Task StartServerAsync(string serverPath, string workingDirectory, string[] arguments)
    {
        // Süreci başlat
        _serverProcess = new Process
        {
            StartInfo = new ProcessStartInfo
            {
                FileName = serverPath,
                Arguments = string.Join(" ", arguments),
                WorkingDirectory = workingDirectory,
                UseShellExecute = false,
                RedirectStandardInput = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                CreateNoWindow = true
            }
        };

        _serverProcess.Start();

        // JSON-RPC bağlantısını kur
        _rpc = JsonRpc.Attach(_serverProcess.StandardInput.BaseStream, _serverProcess.StandardOutput.BaseStream);
        
        // Initialize isteği gönder
        await InitializeAsync();
    }

    /// &lt;summary&gt;
    /// LSP initialize isteği
    /// &lt;/summary&gt;
    private async Task InitializeAsync()
    {
        if (_rpc == null) return;

        var initParams = new
        {
            processId = Environment.ProcessId,
            rootUri = (string?)null,
            capabilities = new { },
            workspaceFolders = (object?)null
        };

        await _rpc.InvokeAsync("initialize", initParams);
        await _rpc.InvokeAsync("initialized");
    }

    /// &lt;summary&gt;
    /// Kaynakları temizle
    /// &lt;/summary&gt;
    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        
        _rpc?.Dispose();
        if (_serverProcess != null && !_serverProcess.HasExited)
        {
            _serverProcess.Kill();
            _serverProcess.Dispose();
        }
    }
}
