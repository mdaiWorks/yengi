using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;

namespace mdaiAgent
{
#pragma warning disable VSTHRD100
    public partial class MainWindow : Window
    {
        private string GetAdbPath()
        {
            // İlk olarak PATH'te adb var mı kontrol edelim
            var pathEnv = Environment.GetEnvironmentVariable("PATH");
            if (pathEnv != null)
            {
                var paths = pathEnv.Split(Path.PathSeparator);
                foreach (var p in paths)
                {
                    var fullPath = Path.Combine(p, "adb.exe");
                    if (File.Exists(fullPath))
                        return fullPath;
                }
            }

            // Yoksa standart yerlere bakalım
            var localAppData = Environment.GetEnvironmentVariable("LOCALAPPDATA");
            if (!string.IsNullOrEmpty(localAppData))
            {
                var defaultAdbPath = Path.Combine(localAppData, "Android", "Sdk", "platform-tools", "adb.exe");
                if (File.Exists(defaultAdbPath))
                    return defaultAdbPath;
            }

            return "adb"; // Sistemde kayıtlı varsayarak dönelim
        }

        private static string? GetFlutterPath()
        {
            var pathEnv = Environment.GetEnvironmentVariable("PATH");
            if (!string.IsNullOrEmpty(pathEnv))
            {
                var extensions = new[] { ".exe", ".cmd", ".bat", string.Empty };
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

        private static bool IsFlutterProject(string folder)
        {
            if (string.IsNullOrWhiteSpace(folder))
                return false;

            if (!File.Exists(Path.Combine(folder, "pubspec.yaml")))
                return false;

            if (Directory.Exists(Path.Combine(folder, "lib")) &&
                (Directory.Exists(Path.Combine(folder, "android")) ||
                 Directory.Exists(Path.Combine(folder, "ios")) ||
                 Directory.Exists(Path.Combine(folder, "web")) ||
                 File.Exists(Path.Combine(folder, "pubspec.lock"))))
            {
                return true;
            }

            return Directory.Exists(Path.Combine(folder, "lib"));
        }

        private void CmbDevices_DropDownOpened(object sender, EventArgs e)
        {
            _ = CmbDevices_DropDownOpenedAsync();
        }

        private async Task CmbDevices_DropDownOpenedAsync()
    {
        cmbDevices.Items.Clear();
        cmbDevices.Items.Add(new ComboBoxItem { Content = LocalizationManager.Instance.GetString("CihazlarTaraniyor"), IsSelected = true, IsEnabled = false });

        string adbPath = GetAdbPath();
        bool isFlutterProject = IsFlutterProject(_selectedFolder ?? string.Empty);
        bool hasIndexHtml = !string.IsNullOrEmpty(_selectedFolder) && Directory.Exists(_selectedFolder) && Directory.EnumerateFiles(_selectedFolder, "*.html", SearchOption.AllDirectories).Any();

        try
        {
            var tcs = new TaskCompletionSource<string>();
            var process = new Process
            {
                StartInfo = new ProcessStartInfo
                {
                    FileName = adbPath,
                    Arguments = "devices",
                    UseShellExecute = false,
                    RedirectStandardOutput = true,
                    CreateNoWindow = true
                }
            };

            process.EnableRaisingEvents = true;
            process.Exited += (s, args) =>
            {
                tcs.TrySetResult(process.StandardOutput.ReadToEnd());
            };
            
            process.Start();

            var output = await tcs.Task;
            var lines = output.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries);

            cmbDevices.Items.Clear();

            // Add preview options first
            if (isFlutterProject)
            {
                cmbDevices.Items.Add(new ComboBoxItem { Tag = "FlutterWeb", Content = LocalizationManager.Instance.GetString("FlutterWebOnizleme") });
                cmbDevices.Items.Add(new ComboBoxItem { Tag = "FlutterWindows", Content = LocalizationManager.Instance.GetString("FlutterWindowsMasaustu") });
            }
            if (hasIndexHtml)
            {
                cmbDevices.Items.Add(new ComboBoxItem { Tag = "StaticWeb", Content = LocalizationManager.Instance.GetString("StatikWebOnizleme") });
            }

            // Add physical devices
            foreach (var line in lines)
            {
                if (line.StartsWith("List") || string.IsNullOrWhiteSpace(line)) continue;
                
                var parts = line.Split(new[] { '\t', ' ' }, StringSplitOptions.RemoveEmptyEntries);
                if (parts.Length >= 2 && parts[1] == "device")
                {
                    cmbDevices.Items.Add(new ComboBoxItem { Content = "📱 " + parts[0] });
                }
            }

            if (cmbDevices.Items.Count == 0)
            {
                cmbDevices.Items.Add(new ComboBoxItem { Content = LocalizationManager.Instance.GetString("CihazBulunamadi"), IsSelected = true, IsEnabled = false });
            }
            else
            {
                ((ComboBoxItem)cmbDevices.Items[0]).IsSelected = true;
            }
        }
        catch (Exception ex)
        {
            cmbDevices.Items.Clear();
            
            // Still add preview options even if ADB fails
            bool isFlutterProjectLocal = IsFlutterProject(_selectedFolder ?? string.Empty);
            bool hasIndexHtmlLocal = !string.IsNullOrEmpty(_selectedFolder) && Directory.Exists(_selectedFolder) && Directory.EnumerateFiles(_selectedFolder, "*.html", SearchOption.AllDirectories).Any();
            
            if (isFlutterProjectLocal)
            {
                cmbDevices.Items.Add(new ComboBoxItem { Tag = "FlutterWeb", Content = LocalizationManager.Instance.GetString("FlutterWebOnizleme") });
                cmbDevices.Items.Add(new ComboBoxItem { Tag = "FlutterWindows", Content = LocalizationManager.Instance.GetString("FlutterWindowsMasaustu") });
            }
            if (hasIndexHtmlLocal)
            {
                cmbDevices.Items.Add(new ComboBoxItem { Tag = "StaticWeb", Content = LocalizationManager.Instance.GetString("StatikWebOnizleme") });
            }
            
            if (cmbDevices.Items.Count == 0)
            {
                cmbDevices.Items.Add(new ComboBoxItem { Content = LocalizationManager.Instance.GetString("ADBHatasi"), IsSelected = true, IsEnabled = false });
            }
            else
            {
                ((ComboBoxItem)cmbDevices.Items[0]).IsSelected = true;
            }
            AddTerminalMessage(LocalizationManager.Instance.GetString("ADBBaslatilamadiExMessage", ex.Message));
        }
    }

        private void BtnRunApp_Click(object sender, RoutedEventArgs e)
        {
            _ = BtnRunApp_ClickAsync();
        }

        private async Task BtnRunApp_ClickAsync()
    {
        var folder = _selectedFolder;
        if (string.IsNullOrEmpty(folder))
        {
            AddTerminalMessage("❌ Please open a project folder first.");
            return;
        }

        if (cmbDevices.SelectedItem is ComboBoxItem cbi && cbi.Tag is string previewTag)
        {
            // Check for preview options
            if (previewTag == "FlutterWeb")
            {
                if (_previewService == null)
                {
                    AddTerminalMessage(LocalizationManager.Instance.GetString("OnizlemeServisiHazirDegil"));
                    return;
                }
                GetOrOpenPreviewWindow();
                AddTerminalMessage(LocalizationManager.Instance.GetString("FlutterWebOnizlemesiBaslatiliyor"));
                await _previewService.StartFlutterWebPreviewAsync(folder, GetFlutterPath());
                return;
            }
            
            if (previewTag == "FlutterWindows")
            {
                AddTerminalMessage(LocalizationManager.Instance.GetString("FlutterWindowsUygulamasiBaslatiliyor"));
                var flutterPath = GetFlutterPath();
                if (flutterPath == null)
                {
                    AddTerminalMessage(LocalizationManager.Instance.GetString("FlutterYurutulebilirBulunamadi"));
                    return;
                }
                if (_terminalService != null)
                {
                    await _terminalService.ExecuteCommandAsync($"\"{flutterPath}\" run -d windows");
                }
                return;
            }
            
            if (previewTag == "StaticWeb")
            {
                if (_previewService == null)
                {
                    AddTerminalMessage(LocalizationManager.Instance.GetString("OnizlemeServisiHazirDegil"));
                    return;
                }
                GetOrOpenPreviewWindow();
                AddTerminalMessage(LocalizationManager.Instance.GetString("StatikWebOnizlemesiBaslatiliyor"));
                await _previewService.StartWebServerPreviewAsync(folder);
                return;
            }
        }
        
        string? selectedDevice = null;
        if (cmbDevices.SelectedItem is ComboBoxItem cbiDevice && cbiDevice.Content?.ToString()?.StartsWith("📱 ") == true)
        {
            selectedDevice = cbiDevice.Content.ToString()!.Substring(3).Trim();
        }

        // Proje Türünü Belirle
        if (IsFlutterProject(folder))
        {
            AddTerminalMessage(LocalizationManager.Instance.GetString("FlutterProjesiAlgilandiBaslatiliyor"));
            var flutterPath = GetFlutterPath();
            if (flutterPath == null)
            {
                AddTerminalMessage(LocalizationManager.Instance.GetString("FlutterYurutulebilirBulunamadi"));
                return;
            }

            string deviceArg = selectedDevice != null ? $"-d {selectedDevice}" : "";
            if (_terminalService != null)
            {
                await _terminalService.ExecuteCommandAsync($"\"{flutterPath}\" run {deviceArg}");
            }
        }
        else if (File.Exists(Path.Combine(folder, "build.gradle")) || File.Exists(Path.Combine(folder, "build.gradle.kts")))
        {
            // Native Android Projesi
            AddTerminalMessage(Localization.Get("🚀 Native Android projesi algılandı. Derleniyor ve yükleniyor...", "🚀 Native Android project detected. Building and deploying..."));
            if (_terminalService != null)
            {
                await _terminalService.ExecuteCommandAsync("./gradlew installDebug");
                AddTerminalMessage(Localization.Get("✅ Yükleme komutu gönderildi. Cihazınızı kontrol edin.", "✅ Deployment command sent. Check your device."));
            }
        }
        else if (!string.IsNullOrEmpty(folder) && Directory.Exists(folder) && Directory.EnumerateFiles(folder, "*.html", SearchOption.AllDirectories).Any())
        {
            // Web Projesi
            var htmlFiles = Directory.EnumerateFiles(folder, "*.html", SearchOption.AllDirectories).ToList();
            var targetHtmlFile = htmlFiles.FirstOrDefault(f => Path.GetFileName(f).Equals("index.html", StringComparison.OrdinalIgnoreCase)) ?? htmlFiles.First();

            AddTerminalMessage(Localization.Get($"🌐 Web projesi algılandı. Tarayıcıda açılıyor: {Path.GetFileName(targetHtmlFile)}...", $"🌐 Web project detected. Opening in browser: {Path.GetFileName(targetHtmlFile)}..."));
            Process.Start(new ProcessStartInfo
            {
                FileName = targetHtmlFile,
                UseShellExecute = true
            });
        }
        else if (File.Exists(Path.Combine(folder, "package.json")))
        {
            // Node.js veya React (web) Projesi
            AddTerminalMessage(Localization.Get("📦 Node/React projesi algılandı. NPM başlatılıyor...", "📦 Node/React project detected. Starting NPM..."));
            if (_terminalService != null)
            {
                await _terminalService.ExecuteCommandAsync("npm start");
            }
        }
        else
        {
            AddTerminalMessage(Localization.Get("⚠️ Proje türü algılanamadı. Çalıştırmak için bir Flutter, Android veya HTML projesi açın.", "⚠️ Project type could not be detected. Open a Flutter, Android, or HTML project to run it."));
        }
    }

        private void BtnHotReload_Click(object sender, RoutedEventArgs e)
        {
            var folder = _selectedFolder;
            if (!string.IsNullOrEmpty(folder) && File.Exists(Path.Combine(folder, "pubspec.yaml")) && _terminalService != null && _terminalService.IsBusy)
            {
                AddTerminalMessage(LocalizationManager.Instance.GetString("HotReloadKomutuRGonderiliyor"));
                _terminalService.WriteInput("r");
            }
            else
            {
                AddTerminalMessage(LocalizationManager.Instance.GetString("HotReloadSadeceCalisanFlutterProjelerindeKullanilabilir"));
            }
        }

        private void BtnStopApp_Click(object sender, RoutedEventArgs e)
        {
            _ = BtnStopApp_ClickAsync();
        }

        private async Task BtnStopApp_ClickAsync()
    {
        var folder = _selectedFolder;
        
        // Önizlemeyi durdur
        _previewService?.StopPreview();
        
        // Eğer Terminal arka planda bir süreç çalıştırıyorsa onu öldürebiliriz
        if (_terminalService != null && _terminalService.IsBusy && _terminalService.CanKill)
        {
            AddTerminalMessage(LocalizationManager.Instance.GetString("CalisanTerminalSureciDurduruluyor"));
            bool killed = _terminalService.KillCurrentProcess();
            if (killed)
            {
                AddTerminalMessage(LocalizationManager.Instance.GetString("TerminalSureciKapatildi"));
            }
            else
            {
                AddTerminalMessage(LocalizationManager.Instance.GetString("SurecKapatilamadi"));
            }
        }
        else if (!string.IsNullOrEmpty(folder) && File.Exists(Path.Combine(folder, "build.gradle")) && _terminalService != null)
        {
            AddTerminalMessage(LocalizationManager.Instance.GetString("AndroidUygulamasiDurdurulmayaCalisiliyor"));
            string adbPath = GetAdbPath();
            await _terminalService.ExecuteCommandAsync($"{adbPath} shell am force-stop com.example.app"); // Paket adını dinamik bulmak gerekir.
        }
        else
        {
                AddTerminalMessage(LocalizationManager.Instance.GetString("DurdurulacakAktifBirUygulamaBulunamadi"));
        }
    }
    }
}
