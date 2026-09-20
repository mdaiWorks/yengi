using System;
using System.Diagnostics;
using System.IO;
using System.Text;
using System.Threading.Tasks;

namespace mdaiAgent
{
    public static class DependencyInstaller
    {
        public static async Task<string?> InstallForLanguageAsync(string projectPath, string languageId, IProgress<(double, string)>? progress = null)
        {
            if (string.IsNullOrWhiteSpace(projectPath) || !Directory.Exists(projectPath))
                return "Geçersiz proje klasörü";

            progress?.Report((0, "Başlatılıyor..."));

            try
            {
                if (languageId == "html-css" || languageId == "html" || languageId == "css")
                {
                    var npmAvailable = await RunCommandCaptureAsync("npm --version", projectPath, null, waitForOutput: true) == 0;
                    if (!npmAvailable)
                        return "npm bulunamadı. Lütfen önce Node.js kurun.";

                    progress?.Report((10, "HTML/CSS Language Server npm ile yükleniyor..."));
                    var result = await RunCommandCaptureAsync(
                        "npm install -g vscode-langservers-extracted --no-audit --no-fund",
                        projectPath,
                        progress);
                    if (result == 0)
                    {
                        progress?.Report((100, "HTML/CSS Language Server kurulumu başarılı."));
                        return null;
                    }

                    return "HTML/CSS Language Server npm ile yüklenemedi.";
                }

                if (languageId == "typescript" || languageId == "javascript")
                {
                    // Ensure npm exists
                    var npmAvailable = await RunCommandCaptureAsync("npm --version", projectPath, null, waitForOutput: true) == 0;
                    if (!npmAvailable)
                    {
                        return "npm bulunamadı. Lütfen sisteminize Node.js ve npm yükleyin.";
                    }

                    // Try pinned TypeScript first (local dev dependency)
                    var pinned = "npm install --save-dev typescript@^5.9 typescript-language-server --no-audit --no-fund";
                    var fallback = "npm install --save-dev typescript typescript-language-server --no-audit --no-fund";

                    progress?.Report((5, "Pinned paket yüklemesi deneniyor..."));
                    var res = await RunCommandCaptureAsync(pinned, projectPath, progress);
                    if (res == 0)
                    {
                        progress?.Report((100, "Pinned yükleme başarılı."));
                        return null;
                    }

                    progress?.Report((50, "Pinned paket başarısız, fallback deneniyor..."));
                    var res2 = await RunCommandCaptureAsync(fallback, projectPath, progress);
                    if (res2 == 0)
                    {
                        progress?.Report((100, "Fallback yükleme başarılı."));
                        return null;
                    }

                    // Try global install as last resort (may require permissions)
                    progress?.Report((80, "Global yükleme deneniyor (izin gerektirebilir)..."));
                    var globalCmd = "npm install -g typescript@^5.9 typescript-language-server --no-audit --no-fund";
                    var res3 = await RunCommandCaptureAsync(globalCmd, projectPath, progress);
                    if (res3 == 0)
                    {
                        progress?.Report((100, "Global yükleme başarılı."));
                        return null;
                    }

                    return "TypeScript/Language Server paketleri npm ile yüklenemedi. Manuel kurulum deneyin.";
                }

                if (languageId == "pyright" || languageId == "python")
                {
                    // Prefer local npm install of pyright (pyright primarily distributed via npm)
                    var npmAvailable = await RunCommandCaptureAsync("npm --version", projectPath, null, waitForOutput: true) == 0;
                    var pythonAvailable = await RunCommandCaptureAsync("python --version", projectPath, null, waitForOutput: true) == 0 ||
                                          await RunCommandCaptureAsync("py --version", projectPath, null, waitForOutput: true) == 0;

                    var npmCmd = "npm install --save-dev pyright --no-audit --no-fund";
                    var pipCmd = "python -m pip install --user pyright";

                    if (npmAvailable)
                    {
                        progress?.Report((10, "npm bulundu, pyright (npm) yüklemesi deneniyor..."));
                        var res = await RunCommandCaptureAsync(npmCmd, projectPath, progress);
                        if (res == 0)
                        {
                            progress?.Report((100, "pyright (npm) başarıyla yüklendi."));
                            return null;
                        }
                    }

                    // Try python pip as fallback if python exists
                    if (pythonAvailable)
                    {
                        progress?.Report((50, "npm başarısız veya yok; pip ile pyright yüklemesi deneniyor..."));
                        var res2 = await RunCommandCaptureAsync(pipCmd, projectPath, progress);
                        if (res2 == 0)
                        {
                            progress?.Report((100, "pyright (pip) başarıyla yüklendi."));
                            return null;
                        }
                    }

                    return "pyright yüklemesi başarısız oldu veya gerekli paket yöneticisi bulunamadı.";
                }

                // For other languages open guide
                return "Otomatik yükleme desteklenmiyor; lütfen kurulum kılavuzunu takip edin.";
            }
            catch (Exception ex)
            {
                return ex.Message;
            }
        }

        private static Task<int> RunCommandCaptureAsync(string command, string workingDir, IProgress<(double, string)>? progress = null, bool waitForOutput = false)
        {
            return Task.Run(() =>
            {
                try
                {
                    progress?.Report((0, $"Çalıştırılıyor: {command}"));
                    
                    // Try to find pwsh first, fallback to powershell.exe
                    string shell = "powershell.exe";
                    string? shellPath = FindExecutableInDirectory("pwsh", Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles)) 
                        ?? FindExecutableInDirectory("pwsh", Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), "PowerShell"))
                        ?? FindExecutableOnPath("pwsh")
                        ?? FindExecutableOnPath("powershell.exe");
                    
                    if (!string.IsNullOrEmpty(shellPath))
                    {
                        shell = shellPath;
                    }

                    var psi = new ProcessStartInfo(shell, $"-NoProfile -Command \"{command.Replace("\"", "`\"")}\"")
                    {
                        WorkingDirectory = workingDir,
                        RedirectStandardOutput = true,
                        RedirectStandardError = true,
                        UseShellExecute = false,
                        CreateNoWindow = true
                    };

                    using var proc = Process.Start(psi);
                    if (proc == null)
                        return -1;

                    var stdoutSb = new StringBuilder();
                    var stderrSb = new StringBuilder();

                    if (waitForOutput)
                    {
                        var outText = proc.StandardOutput.ReadToEnd();
                        var errText = proc.StandardError.ReadToEnd();
                        proc.WaitForExit(60000);
                        stdoutSb.Append(outText);
                        stderrSb.Append(errText);
                        if (!string.IsNullOrWhiteSpace(outText) || !string.IsNullOrWhiteSpace(errText))
                        {
                            progress?.Report((50, outText + "\n" + errText));
                        }
                    }
                    else
                    {
                        while (!proc.StandardOutput.EndOfStream)
                        {
                            var line = proc.StandardOutput.ReadLine();
                            if (line != null)
                            {
                                stdoutSb.AppendLine(line);
                                progress?.Report((25, line));
                            }
                        }

                        while (!proc.StandardError.EndOfStream)
                        {
                            var line = proc.StandardError.ReadLine();
                            if (line != null)
                            {
                                stderrSb.AppendLine(line);
                                progress?.Report((25, "[ERR] " + line));
                            }
                        }

                        proc.WaitForExit();
                    }

                    return proc.ExitCode;
                }
                catch (Exception ex)
                {
                    progress?.Report((0, "Hata: " + ex.Message));
                    return -1;
                }
            });
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
    }
}
