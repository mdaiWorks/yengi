using System;
using System.Diagnostics;
using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace mdaiAgent
{
    public class PreviewService
    {
        private Process? _previewProcess;
        private HttpListener? _httpListener;
        private CancellationTokenSource? _cancellationTokenSource;

        public event EventHandler<string>? PreviewUrlChanged;
        public event EventHandler<string>? PreviewStatusChanged;
        public event EventHandler<string>? PreviewError;

        public bool IsRunning => _previewProcess != null && !_previewProcess.HasExited || _httpListener != null && _httpListener.IsListening;

        public async Task StartFlutterWebPreviewAsync(string projectDirectory, string? flutterPath = null)
        {
            if (IsRunning)
            {
                StopPreview();
            }

            try
            {
                if (flutterPath == null)
                {
                    flutterPath = GetFlutterPath();
                    if (flutterPath == null)
                    {
                        throw new InvalidOperationException("Flutter executable not found. Please ensure Flutter is installed and in PATH.");
                    }
                }

                OnPreviewStatusChanged("Starting Flutter web preview...");

                var tcs = new TaskCompletionSource<string>();

                _previewProcess = new Process
                {
                    StartInfo = new ProcessStartInfo
                    {
                        FileName = flutterPath,
                        Arguments = "run -d web-server --web-port 0",
                        WorkingDirectory = projectDirectory,
                        UseShellExecute = false,
                        RedirectStandardOutput = true,
                        RedirectStandardError = true,
                        CreateNoWindow = true
                    },
                    EnableRaisingEvents = true
                };

                var urlBuilder = new StringBuilder();
                _previewProcess.OutputDataReceived += (sender, args) =>
                {
                    if (!string.IsNullOrEmpty(args.Data))
                    {
                        var line = args.Data;
                        // Look for both http://localhost and possibly http://127.0.0.1
                        if (line.Contains("http://"))
                        {
                            var urlStart = line.IndexOf("http://");
                            // Find the end of URL by looking for whitespace, or end of line, or quotes
                            var urlEnd = urlStart;
                            while (urlEnd < line.Length && !char.IsWhiteSpace(line[urlEnd]) && line[urlEnd] != '"' && line[urlEnd] != '\'')
                            {
                                urlEnd++;
                            }
                            var url = line.Substring(urlStart, urlEnd - urlStart);
                            tcs.TrySetResult(url);
                        }
                    }
                };

                _previewProcess.ErrorDataReceived += (sender, args) =>
                {
                    if (!string.IsNullOrEmpty(args.Data))
                    {
                        OnPreviewError(args.Data);
                    }
                };

                _previewProcess.Exited += (sender, args) =>
                {
                    tcs.TrySetException(new InvalidOperationException("Flutter process exited unexpectedly."));
                };

                _previewProcess.Start();
                _previewProcess.BeginOutputReadLine();
                _previewProcess.BeginErrorReadLine();

                var previewUrl = await tcs.Task;
                OnPreviewUrlChanged(previewUrl);
                OnPreviewStatusChanged($"Preview running at {previewUrl}");
            }
            catch (Exception ex)
            {
                OnPreviewError(ex.Message);
                StopPreview();
            }
        }

        public Task StartWebServerPreviewAsync(string projectDirectory, int port = 8080)
        {
            if (IsRunning)
            {
                StopPreview();
            }

            try
            {
                OnPreviewStatusChanged("Starting static web server...");

                _cancellationTokenSource = new CancellationTokenSource();
                _httpListener = new HttpListener();
                _httpListener.Prefixes.Add($"http://localhost:{port}/");
                _httpListener.Start();

                var serverTask = Task.Run(() => HandleRequestsAsync(projectDirectory, _cancellationTokenSource.Token), _cancellationTokenSource.Token);
                var previewUrl = $"http://localhost:{port}";
                OnPreviewUrlChanged(previewUrl);
                OnPreviewStatusChanged($"Preview running at {previewUrl}");
                return Task.CompletedTask;
            }
            catch (Exception ex)
            {
                OnPreviewError(ex.Message);
                StopPreview();
                return Task.FromException(ex);
            }
        }

        private async Task HandleRequestsAsync(string rootDirectory, CancellationToken cancellationToken)
        {
            while (!cancellationToken.IsCancellationRequested && _httpListener != null && _httpListener.IsListening)
            {
                try
                {
                    var context = await _httpListener.GetContextAsync();
                    await HandleRequestAsync(context, rootDirectory, cancellationToken);
                }
                catch (OperationCanceledException)
                {
                    // Ignore cancellation
                }
                catch (HttpListenerException)
                {
                    // Ignore: listener stopped or IO aborted (expected during shutdown)
                }
                catch (Exception) when (cancellationToken.IsCancellationRequested || _httpListener == null || !_httpListener.IsListening)
                {
                    // Ignore all exceptions if we're shutting down
                }
                catch (Exception ex)
                {
                    OnPreviewError($"Error handling request: {ex.Message}");
                }
            }
        }

        private async Task HandleRequestAsync(HttpListenerContext context, string rootDirectory, CancellationToken cancellationToken)
        {
            try
            {
                var request = context.Request;
                var response = context.Response;

                var path = request.Url?.AbsolutePath ?? "/";
                if (path == "/")
                {
                    path = "/index.html";
                }

                var filePath = Path.Combine(rootDirectory, path.TrimStart('/'));

                if (File.Exists(filePath))
                {
                    var content = await File.ReadAllBytesAsync(filePath, cancellationToken);
                    var extension = Path.GetExtension(filePath).ToLower();
                    response.ContentType = GetMimeType(extension);
                    response.ContentLength64 = content.Length;
                    await response.OutputStream.WriteAsync(content, 0, content.Length, cancellationToken);
                }
                else
                {
                    response.StatusCode = (int)HttpStatusCode.NotFound;
                    var buffer = Encoding.UTF8.GetBytes("404 - File Not Found");
                    response.ContentLength64 = buffer.Length;
                    await response.OutputStream.WriteAsync(buffer, 0, buffer.Length, cancellationToken);
                }
                response.Close();
            }
            catch (OperationCanceledException)
            {
                // Ignore: cancelled
            }
            catch (HttpListenerException)
            {
                // Ignore: connection aborted or listener stopped
            }
            catch (Exception) when (cancellationToken.IsCancellationRequested || _httpListener == null || !_httpListener.IsListening)
            {
                // Ignore all shutdown-related exceptions
            }
        }

        private string GetMimeType(string extension) => extension switch
        {
            ".html" => "text/html",
            ".htm" => "text/html",
            ".css" => "text/css",
            ".js" => "application/javascript",
            ".json" => "application/json",
            ".png" => "image/png",
            ".jpg" => "image/jpeg",
            ".jpeg" => "image/jpeg",
            ".gif" => "image/gif",
            ".svg" => "image/svg+xml",
            ".ico" => "image/x-icon",
            _ => "application/octet-stream"
        };

        public void StopPreview()
        {
            try
            {
                _cancellationTokenSource?.Cancel();
                _cancellationTokenSource?.Dispose();
                _cancellationTokenSource = null;

                if (_httpListener != null)
                {
                    _httpListener.Stop();
                    _httpListener.Close();
                    _httpListener = null;
                }

                if (_previewProcess != null && !_previewProcess.HasExited)
                {
                    _previewProcess.Kill();
                    _previewProcess.WaitForExit();
                }

                _previewProcess?.Dispose();
                _previewProcess = null;

                OnPreviewStatusChanged("Preview stopped");
            }
            catch (Exception ex)
            {
                OnPreviewError($"Error stopping preview: {ex.Message}");
            }
        }

        private string? GetFlutterPath()
        {
            var pathEnv = Environment.GetEnvironmentVariable("PATH");
            if (!string.IsNullOrEmpty(pathEnv))
            {
                var extensions = new[] { ".exe", ".cmd", ".bat", "" };
                foreach (var dir in pathEnv.Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries))
                {
                    foreach (var ext in extensions)
                    {
                        var candidate = Path.Combine(dir, "flutter" + ext);
                        if (File.Exists(candidate))
                            return candidate;
                    }
                }
            }
            return null;
        }

        protected virtual void OnPreviewUrlChanged(string url)
        {
            PreviewUrlChanged?.Invoke(this, url);
        }

        protected virtual void OnPreviewStatusChanged(string status)
        {
            PreviewStatusChanged?.Invoke(this, status);
        }

        protected virtual void OnPreviewError(string error)
        {
            PreviewError?.Invoke(this, error);
        }
    }
}