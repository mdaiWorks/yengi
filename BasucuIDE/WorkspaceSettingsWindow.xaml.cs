using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Net.Sockets;
using System.Text.Json;
using System.Windows;

namespace mdaiAgent
{
    public partial class WorkspaceSettingsWindow : Window
    {
        private readonly AgentWorkspaceMode _mode;
        private AppSettings _settings;

        public WorkspaceSettingsWindow(AgentWorkspaceMode mode)
        {
            InitializeComponent();
            _mode = mode;
            _settings = SettingsWindow.GetSettings();

            LoadSettings();
        }

        private void LoadSettings()
        {
            switch (_mode)
            {
                case AgentWorkspaceMode.ImageStudio:
                    txtTitle.Text = LocalizationManager.Instance.GetString("ImageStudioSettingsTitle");
                    panelImageStudio.Visibility = Visibility.Visible;
                    
                    chkUsePollinations.IsChecked = _settings.ImageStudioUseFreePollinations;
                    chkEnhancePrompt.IsChecked = _settings.ImageStudioEnhancePrompt;
                    txtPublicBaseUrl.Text = _settings.ImageStudioPublicBaseUrl;
                    chkEnableMultiAgent.IsChecked = _settings.EnableMultiAgentImageGeneration;
                    txtImageBaseUrl.Text = _settings.ImageStudioBaseUrl;
                    txtImageApiKey.Text = _settings.ImageStudioApiKey;
                    txtImageModel.Text = _settings.ImageStudioModel;
                    SelectComboBoxValue(cmbImageSize, _settings.ImageStudioSize, "1024x1024");
                    SelectComboBoxValue(cmbImageSteps, _settings.ImageStudioSteps > 0 ? _settings.ImageStudioSteps.ToString() : "20", "20");
                    txtImageSeed.Text = _settings.ImageStudioSeed.ToString();

                    chkEnableControlNet.IsChecked = _settings.ControlNetEnabled;
                    SelectComboBoxValue(cmbControlNetProvider, _settings.ControlNetProvider, "Auto");
                    txtControlNetBaseUrl.Text = string.IsNullOrWhiteSpace(_settings.ControlNetBaseUrl) ? "http://192.168.1.122:8188" : _settings.ControlNetBaseUrl;
                    
                    UpdateImageStudioFormState();
                    break;
                    
                case AgentWorkspaceMode.BlenderCopilot:
                    txtTitle.Text = LocalizationManager.Instance.GetString("BlenderCopilotSettingsTitle");
                    panelBlender.Visibility = Visibility.Visible;
                    
                    txtBlenderPort.Text = _settings.BlenderWebSocketPort.ToString();
                    chkEnableBlenderPromptEnhancer.IsChecked = _settings.EnableBlenderPromptEnhancer;
                    
                    // Blender Yolunu Yükle veya Otomatik Tespit Et
                    if (!string.IsNullOrWhiteSpace(_settings.BlenderPath) && Directory.Exists(_settings.BlenderPath))
                    {
                        txtBlenderPath.Text = _settings.BlenderPath;
                    }
                    else
                    {
                        AutoDetectBlenderFolder();
                    }
                    CheckBlenderAddonStatus();
                    break;
                    
                case AgentWorkspaceMode.UnityCopilot:
                    txtTitle.Text = LocalizationManager.Instance.GetString("UnityCopilotSettingsTitle");
                    panelUnity.Visibility = Visibility.Visible;
                    
                    txtUnityPort.Text = _settings.UnityWebSocketPort.ToString();
                    chkEnableUnityPromptEnhancer.IsChecked = _settings.EnableUnityPromptEnhancer;
                    break;
            }
        }

        private void AutoDetectBlenderFolder()
        {
            try
            {
                string appData = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
                string blenderFoundationDir = Path.Combine(appData, "Blender Foundation", "Blender");
                
                if (Directory.Exists(blenderFoundationDir))
                {
                    // En yeni Blender sürüm klasörünü bul (örn: 4.2, 4.1, 3.6)
                    var versionDirs = Directory.GetDirectories(blenderFoundationDir)
                        .Select(d => Path.GetFileName(d))
                        .Where(v => double.TryParse(v, System.Globalization.NumberStyles.Any, System.Globalization.CultureInfo.InvariantCulture, out _))
                        .OrderByDescending(v => v)
                        .ToList();

                    if (versionDirs.Any())
                    {
                        txtBlenderPath.Text = Path.Combine(blenderFoundationDir, versionDirs.First());
                        return;
                    }
                }

                txtBlenderPath.Text = LocalizationManager.Instance.GetString("BlenderFolderNotFoundAuto");
            }
            catch
            {
                txtBlenderPath.Text = LocalizationManager.Instance.GetString("BlenderFolderNotSelected");
            }
        }

        private void ChkUsePollinations_Changed(object sender, RoutedEventArgs e)
        {
            UpdateImageStudioFormState();
        }

        private void UpdateImageStudioFormState()
        {
            if (chkUsePollinations == null || txtImageBaseUrl == null || txtImageApiKey == null || txtImageModel == null || lblCustomApiTitle == null)
                return;

            bool isFree = chkUsePollinations.IsChecked == true;
            if (txtPublicBaseUrl != null) txtPublicBaseUrl.IsEnabled = isFree;
            txtImageBaseUrl.IsEnabled = !isFree;
            txtImageApiKey.IsEnabled = !isFree;
            txtImageModel.IsEnabled = !isFree;
            lblCustomApiTitle.Foreground = isFree ? System.Windows.Media.Brushes.Gray : System.Windows.Media.Brushes.DeepSkyBlue;
        }

        private void CheckBlenderAddonStatus()
        {
            string blenderDir = txtBlenderPath.Text;
            if (!string.IsNullOrWhiteSpace(blenderDir) && Directory.Exists(blenderDir))
            {
                string targetAddonFile = Path.Combine(blenderDir, "scripts", "addons", "yengi_copilot.py");
                if (File.Exists(targetAddonFile))
                {
                    txtBlenderInstallResult.Text = LocalizationManager.Instance.GetString("BlenderAddonInstalled");
                    txtBlenderInstallResult.Foreground = System.Windows.Media.Brushes.LimeGreen;
                    btnInstallAddon.Content = LocalizationManager.Instance.GetString("ReinstallAddonButton");
                    btnInstallAddon.Background = new System.Windows.Media.SolidColorBrush((System.Windows.Media.Color)System.Windows.Media.ColorConverter.ConvertFromString("#2e7d32"));
                    return;
                }
            }

            txtBlenderInstallResult.Text = LocalizationManager.Instance.GetString("BlenderAddonNotInstalled");
            txtBlenderInstallResult.Foreground = System.Windows.Media.Brushes.Orange;
            btnInstallAddon.Content = LocalizationManager.Instance.GetString("AutoInstallAddonButton");
            btnInstallAddon.Background = new System.Windows.Media.SolidColorBrush((System.Windows.Media.Color)System.Windows.Media.ColorConverter.ConvertFromString("#007acc"));
        }

        private void BtnBrowseBlender_Click(object sender, RoutedEventArgs e)
        {
            var dialog = new System.Windows.Forms.FolderBrowserDialog
            {
                Description = LocalizationManager.Instance.GetString("BlenderFolderBrowserDesc"),
                UseDescriptionForTitle = true
            };

            if (dialog.ShowDialog() == System.Windows.Forms.DialogResult.OK)
            {
                txtBlenderPath.Text = dialog.SelectedPath;
                CheckBlenderAddonStatus();
            }
        }

        private void BtnInstallAddon_Click(object sender, RoutedEventArgs e)
        {
            string blenderDir = txtBlenderPath.Text;
            if (string.IsNullOrWhiteSpace(blenderDir) || !Directory.Exists(blenderDir))
            {
                txtBlenderInstallResult.Text = LocalizationManager.Instance.GetString("BlenderInvalidFolder");
                txtBlenderInstallResult.Foreground = System.Windows.Media.Brushes.Tomato;
                return;
            }

            try
            {
                string addonsDir = Path.Combine(blenderDir, "scripts", "addons");
                if (!Directory.Exists(addonsDir))
                {
                    Directory.CreateDirectory(addonsDir);
                }

                string targetAddonFile = Path.Combine(addonsDir, "yengi_copilot.py");

                string sourceAddonPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "yengi_blender_addon.py");
                
                if (File.Exists(sourceAddonPath))
                {
                    File.Copy(sourceAddonPath, targetAddonFile, true);
                }
                else
                {
                    string devAddonPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "..", "..", "..", "yengi_blender_addon.py");
                    if (File.Exists(devAddonPath))
                    {
                        File.Copy(devAddonPath, targetAddonFile, true);
                    }
                    else
                    {
                        string fullCode = File.Exists(targetAddonFile) ? File.ReadAllText(targetAddonFile) : "";
                        if (string.IsNullOrEmpty(fullCode))
                        {
                            txtBlenderInstallResult.Text = LocalizationManager.Instance.GetString("BlenderAddonFileNotFound");
                            txtBlenderInstallResult.Foreground = System.Windows.Media.Brushes.Tomato;
                            return;
                        }
                    }
                }

                txtBlenderInstallResult.Text = string.Format(LocalizationManager.Instance.GetString("BlenderAddonInstallSuccess"), targetAddonFile);
                txtBlenderInstallResult.Foreground = System.Windows.Media.Brushes.LimeGreen;
                CheckBlenderAddonStatus();
            }
            catch (Exception ex)
            {
                txtBlenderInstallResult.Text = string.Format(LocalizationManager.Instance.GetString("BlenderInstallError"), ex.Message);
                txtBlenderInstallResult.Foreground = System.Windows.Media.Brushes.Tomato;
            }
        }

        private async void BtnTestImageConnection_Click(object sender, RoutedEventArgs e)
        {
            var rawBaseUrl = txtImageBaseUrl.Text?.Trim();
            if (string.IsNullOrWhiteSpace(rawBaseUrl))
            {
                txtTestImageConnectionResult.Visibility = Visibility.Visible;
                txtTestImageConnectionResult.Foreground = System.Windows.Media.Brushes.Orange;
                txtTestImageConnectionResult.Text = "⚠️ Lütfen önce Base URL alanını doldurun (Örn: http://localhost:8000/v1 veya https://api.openai.com/v1)";
                return;
            }

            btnTestImageConnection.IsEnabled = false;
            btnTestImageConnection.Content = "⏳ Test Ediliyor...";
            txtTestImageConnectionResult.Visibility = Visibility.Visible;
            txtTestImageConnectionResult.Foreground = System.Windows.Media.Brushes.Gray;
            txtTestImageConnectionResult.Text = "Sunucuya bağlanılıyor...";

            try
            {
                var baseUrl = rawBaseUrl.TrimEnd('/');
                using var cts = new System.Threading.CancellationTokenSource(TimeSpan.FromSeconds(6));
                using var client = new HttpClient();
                client.Timeout = TimeSpan.FromSeconds(6);

                var apiKey = txtImageApiKey.Text?.Trim();
                if (!string.IsNullOrEmpty(apiKey))
                {
                    client.DefaultRequestHeaders.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", apiKey);
                }

                var sw = Stopwatch.StartNew();

                var testUrl = baseUrl.EndsWith("/models", StringComparison.OrdinalIgnoreCase) 
                    ? baseUrl 
                    : $"{baseUrl}/models";

                HttpResponseMessage response;
                string responseBody = "";

                try
                {
                    response = await client.GetAsync(testUrl, cts.Token);
                    responseBody = await response.Content.ReadAsStringAsync(cts.Token);
                }
                catch (Exception)
                {
                    response = await client.GetAsync(baseUrl, cts.Token);
                    responseBody = await response.Content.ReadAsStringAsync(cts.Token);
                }

                sw.Stop();

                if (response.IsSuccessStatusCode)
                {
                    string extraInfo = "";
                    try
                    {
                        using var doc = JsonDocument.Parse(responseBody);
                        if (doc.RootElement.TryGetProperty("data", out var dataElem) && dataElem.ValueKind == JsonValueKind.Array)
                        {
                            var modelNames = dataElem.EnumerateArray()
                                .Select(m => m.TryGetProperty("id", out var idElem) ? idElem.GetString() : null)
                                .Where(n => !string.IsNullOrEmpty(n))
                                .Take(3)
                                .ToList();

                            if (modelNames.Count > 0)
                            {
                                extraInfo = $" (Bulunan modeller: {string.Join(", ", modelNames)})";
                            }
                        }
                    }
                    catch { }

                    txtTestImageConnectionResult.Foreground = System.Windows.Media.Brushes.LimeGreen;
                    txtTestImageConnectionResult.Text = $"✅ Bağlantı başarılı! ({sw.ElapsedMilliseconds} ms){extraInfo}";
                }
                else if (response.StatusCode == System.Net.HttpStatusCode.Unauthorized)
                {
                    txtTestImageConnectionResult.Foreground = System.Windows.Media.Brushes.Orange;
                    txtTestImageConnectionResult.Text = $"⚠️ Sunucuya erişildi ancak API Key geçersiz (HTTP 401 Unauthorized, {sw.ElapsedMilliseconds} ms).";
                }
                else if ((int)response.StatusCode == 400 && responseBody.Contains("Invalid host header", StringComparison.OrdinalIgnoreCase))
                {
                    txtTestImageConnectionResult.Foreground = System.Windows.Media.Brushes.Orange;
                    txtTestImageConnectionResult.Text = "⚠️ Mac rapid-mlx sunucusu 'Invalid host header' döndü. Sunucuyu `--trusted-hosts \"*\"` parametresiyle başlatın.";
                }
                else
                {
                    txtTestImageConnectionResult.Foreground = System.Windows.Media.Brushes.Orange;
                    txtTestImageConnectionResult.Text = $"⚠️ Sunucu yanıt verdi: {(int)response.StatusCode} {response.ReasonPhrase} ({sw.ElapsedMilliseconds} ms).";
                }
            }
            catch (TaskCanceledException)
            {
                txtTestImageConnectionResult.Foreground = System.Windows.Media.Brushes.Tomato;
                txtTestImageConnectionResult.Text = "⏱️ Zaman aşımı! Sunucuya 6 saniye içinde ulaşılamadı. IP adresinin doğruluğunu ve güvenlik duvarını kontrol edin.";
            }
            catch (HttpRequestException ex)
            {
                txtTestImageConnectionResult.Foreground = System.Windows.Media.Brushes.Tomato;
                txtTestImageConnectionResult.Text = $"❌ Bağlantı kurulamadı: {ex.Message}";
            }
            catch (Exception ex)
            {
                txtTestImageConnectionResult.Foreground = System.Windows.Media.Brushes.Tomato;
                txtTestImageConnectionResult.Text = $"❌ Hata: {ex.Message}";
            }
            finally
            {
                btnTestImageConnection.IsEnabled = true;
                btnTestImageConnection.Content = "🔌 Bağlantıyı Test Et";
            }
        }

        private async void BtnTestControlNetConnection_Click(object sender, RoutedEventArgs e)
        {
            var rawBaseUrl = txtControlNetBaseUrl.Text?.Trim();
            if (string.IsNullOrWhiteSpace(rawBaseUrl))
            {
                txtTestControlNetResult.Visibility = Visibility.Visible;
                txtTestControlNetResult.Foreground = System.Windows.Media.Brushes.Orange;
                txtTestControlNetResult.Text = "⚠️ Lütfen bir ComfyUI adresi girin (Örn: http://192.168.1.122:8188)";
                return;
            }

            btnTestControlNetConnection.IsEnabled = false;
            btnTestControlNetConnection.Content = "⏳...";
            txtTestControlNetResult.Visibility = Visibility.Visible;
            txtTestControlNetResult.Foreground = System.Windows.Media.Brushes.Gray;
            txtTestControlNetResult.Text = "ComfyUI sunucusuna bağlanılıyor...";

            try
            {
                var baseUrl = rawBaseUrl.TrimEnd('/');
                using var cts = new System.Threading.CancellationTokenSource(TimeSpan.FromSeconds(5));
                using var client = new HttpClient();
                client.Timeout = TimeSpan.FromSeconds(5);

                var sw = Stopwatch.StartNew();
                var response = await client.GetAsync($"{baseUrl}/system_stats", cts.Token);
                sw.Stop();

                if (response.IsSuccessStatusCode)
                {
                    txtTestControlNetResult.Foreground = System.Windows.Media.Brushes.LimeGreen;
                    txtTestControlNetResult.Text = $"✅ ComfyUI hazır ve çalışıyor! ({sw.ElapsedMilliseconds} ms)";
                }
                else
                {
                    txtTestControlNetResult.Foreground = System.Windows.Media.Brushes.Orange;
                    txtTestControlNetResult.Text = $"⚠️ ComfyUI yanıt verdi: {(int)response.StatusCode} {response.ReasonPhrase}";
                }
            }
            catch (TaskCanceledException)
            {
                txtTestControlNetResult.Foreground = System.Windows.Media.Brushes.Tomato;
                txtTestControlNetResult.Text = "⏱️ Zaman aşımı! ComfyUI sunucusuna ulaşılamadı.";
            }
            catch (Exception ex)
            {
                txtTestControlNetResult.Foreground = System.Windows.Media.Brushes.Tomato;
                txtTestControlNetResult.Text = $"❌ Bağlantı başarısız: {ex.Message}";
            }
            finally
            {
                btnTestControlNetConnection.IsEnabled = true;
                btnTestControlNetConnection.Content = "🔌 ComfyUI Test Et";
            }
        }

        private void BtnSave_Click(object sender, RoutedEventArgs e)
        {
            switch (_mode)
            {
                case AgentWorkspaceMode.ImageStudio:
                    _settings.ImageStudioUseFreePollinations = chkUsePollinations.IsChecked == true;
                    _settings.ImageStudioEnhancePrompt = chkEnhancePrompt.IsChecked == true;
                    _settings.ImageStudioPublicBaseUrl = txtPublicBaseUrl.Text;
                    _settings.EnableMultiAgentImageGeneration = chkEnableMultiAgent.IsChecked == true;
                    _settings.ImageStudioBaseUrl = txtImageBaseUrl.Text;
                    _settings.ImageStudioApiKey = txtImageApiKey.Text;
                    _settings.ImageStudioModel = txtImageModel.Text;

                    var rawSize = GetComboBoxValue(cmbImageSize);
                    if (rawSize.Contains(' ')) rawSize = rawSize.Split(' ')[0];
                    _settings.ImageStudioSize = rawSize.Contains('x') ? rawSize : "1024x1024";

                    var rawSteps = GetComboBoxValue(cmbImageSteps);
                    if (rawSteps.Contains(' ')) rawSteps = rawSteps.Split(' ')[0];
                    if (int.TryParse(rawSteps, out int parsedSteps) && parsedSteps > 0)
                        _settings.ImageStudioSteps = parsedSteps;
                    else
                        _settings.ImageStudioSteps = 20;

                    if (long.TryParse(txtImageSeed.Text?.Trim(), out long parsedSeed))
                        _settings.ImageStudioSeed = parsedSeed;
                    else
                        _settings.ImageStudioSeed = -1;

                    _settings.ControlNetEnabled = chkEnableControlNet.IsChecked == true;
                    _settings.ControlNetProvider = GetComboBoxValue(cmbControlNetProvider);
                    _settings.ControlNetBaseUrl = string.IsNullOrWhiteSpace(txtControlNetBaseUrl.Text) ? "http://192.168.1.122:8188" : txtControlNetBaseUrl.Text.Trim();
                    break;
                    
                case AgentWorkspaceMode.BlenderCopilot:
                    if (int.TryParse(txtBlenderPort.Text, out int bPort))
                        _settings.BlenderWebSocketPort = bPort;
                    _settings.BlenderPath = txtBlenderPath.Text;
                    _settings.EnableBlenderPromptEnhancer = chkEnableBlenderPromptEnhancer.IsChecked == true;
                    break;
                    
                case AgentWorkspaceMode.UnityCopilot:
                    if (int.TryParse(txtUnityPort.Text, out int uPort))
                        _settings.UnityWebSocketPort = uPort;
                    _settings.EnableUnityPromptEnhancer = chkEnableUnityPromptEnhancer.IsChecked == true;
                    break;
            }
            
            SettingsWindow.SaveSettings(_settings);
            DialogResult = true;
            Close();
        }

        private static string GetComboBoxValue(System.Windows.Controls.ComboBox cmb)
        {
            string val = "";
            if (cmb.SelectedItem is System.Windows.Controls.ComboBoxItem item)
            {
                val = item.Content?.ToString() ?? "";
            }
            else if (cmb.SelectedItem is string str)
            {
                val = str;
            }
            else
            {
                val = cmb.Text ?? "";
            }

            if (val.StartsWith("System.Windows.Controls.ComboBoxItem:", StringComparison.OrdinalIgnoreCase))
            {
                val = val.Substring("System.Windows.Controls.ComboBoxItem:".Length).Trim();
            }

            return val.Trim();
        }

        private static void SelectComboBoxValue(System.Windows.Controls.ComboBox cmb, string targetVal, string defaultVal)
        {
            if (string.IsNullOrWhiteSpace(targetVal)) targetVal = defaultVal;

            foreach (var item in cmb.Items)
            {
                string itemText = item is System.Windows.Controls.ComboBoxItem cbi ? cbi.Content?.ToString() ?? "" : item?.ToString() ?? "";
                if (itemText.StartsWith(targetVal, StringComparison.OrdinalIgnoreCase) || itemText.Equals(targetVal, StringComparison.OrdinalIgnoreCase))
                {
                    cmb.SelectedItem = item;
                    return;
                }
            }
            cmb.Text = targetVal;
        }

        private void BtnCancel_Click(object sender, RoutedEventArgs e)
        {
            DialogResult = false;
            Close();
        }

        private void BtnTestBlenderConnection_Click(object sender, RoutedEventArgs e)
        {
            if (!int.TryParse(txtBlenderPort.Text, out int port))
            {
                txtBlenderTestResult.Text = LocalizationManager.Instance.GetString("InvalidPortNumber");
                txtBlenderTestResult.Foreground = System.Windows.Media.Brushes.Tomato;
                return;
            }

            txtBlenderTestResult.Text = LocalizationManager.Instance.GetString("ConnectingProgress");
            txtBlenderTestResult.Foreground = System.Windows.Media.Brushes.Gray;

            try
            {
                using var client = new TcpClient();
                var result = client.BeginConnect("127.0.0.1", port, null, null);
                var success = result.AsyncWaitHandle.WaitOne(TimeSpan.FromSeconds(2));
                
                if (success && client.Connected)
                {
                    txtBlenderTestResult.Text = string.Format(LocalizationManager.Instance.GetString("BlenderTestSuccess"), port);
                    txtBlenderTestResult.Foreground = System.Windows.Media.Brushes.LimeGreen;
                }
                else
                {
                    txtBlenderTestResult.Text = string.Format(LocalizationManager.Instance.GetString("BlenderTestFailed"), port);
                    txtBlenderTestResult.Foreground = System.Windows.Media.Brushes.Tomato;
                }
            }
            catch
            {
                txtBlenderTestResult.Text = string.Format(LocalizationManager.Instance.GetString("BlenderTestFailed"), port);
                txtBlenderTestResult.Foreground = System.Windows.Media.Brushes.Tomato;
            }
        }

        private void BtnInstallUnityAddon_Click(object sender, RoutedEventArgs e)
        {
            var dialog = new Microsoft.Win32.OpenFolderDialog
            {
                Title = LocalizationManager.Instance.GetString("UnitySelectProjectFolder")
            };

            if (dialog.ShowDialog() == true)
            {
                try
                {
                    string unityProjectDir = dialog.FolderName;
                    string editorDir = Path.Combine(unityProjectDir, "Assets", "Editor");
                    if (!Directory.Exists(editorDir))
                    {
                        Directory.CreateDirectory(editorDir);
                    }

                    string targetFilePath = Path.Combine(editorDir, "YengiUnityCopilot.cs");
                    string sourceFilePath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "YengiUnityCopilot.cs");

                    if (File.Exists(sourceFilePath))
                    {
                        File.Copy(sourceFilePath, targetFilePath, true);
                    }
                    else
                    {
                        string devSourcePath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "..", "..", "..", "YengiUnityCopilot.cs");
                        if (File.Exists(devSourcePath))
                        {
                            File.Copy(devSourcePath, targetFilePath, true);
                        }
                    }

                    txtUnityInstallResult.Text = string.Format(LocalizationManager.Instance.GetString("UnityInstallSuccess"), targetFilePath);
                    txtUnityInstallResult.Foreground = System.Windows.Media.Brushes.LimeGreen;
                }
                catch (Exception ex)
                {
                    txtUnityInstallResult.Text = string.Format(LocalizationManager.Instance.GetString("UnityInstallError"), ex.Message);
                    txtUnityInstallResult.Foreground = System.Windows.Media.Brushes.Tomato;
                }
            }
        }

#pragma warning disable VSTHRD100
        private async void BtnTestUnityConnection_Click(object sender, RoutedEventArgs e)
#pragma warning restore VSTHRD100
        {
            try
            {
                if (!int.TryParse(txtUnityPort.Text, out int port))
                {
                    txtUnityTestResult.Text = LocalizationManager.Instance.GetString("InvalidPortNumber");
                    txtUnityTestResult.Foreground = System.Windows.Media.Brushes.Tomato;
                    return;
                }

                txtUnityTestResult.Text = LocalizationManager.Instance.GetString("ConnectingProgress");
                txtUnityTestResult.Foreground = System.Windows.Media.Brushes.Gray;

                try
                {
                    using var client = new TcpClient();
                    var result = client.BeginConnect("127.0.0.1", port, null, null);
                    var success = result.AsyncWaitHandle.WaitOne(TimeSpan.FromSeconds(2));

                    if (success && client.Connected)
                    {
                        txtUnityTestResult.Text = string.Format(LocalizationManager.Instance.GetString("UnityTestSuccess"), port);
                        txtUnityTestResult.Foreground = System.Windows.Media.Brushes.LimeGreen;
                    }
                    else
                    {
                        txtUnityTestResult.Text = string.Format(LocalizationManager.Instance.GetString("UnityTestFailed"), port);
                        txtUnityTestResult.Foreground = System.Windows.Media.Brushes.Tomato;
                    }
                }
                catch
                {
                    txtUnityTestResult.Text = string.Format(LocalizationManager.Instance.GetString("UnityTestFailed"), port);
                    txtUnityTestResult.Foreground = System.Windows.Media.Brushes.Tomato;
                }
            }
            catch { /* Ignore unhandled UI exceptions */ }
        }
    }
}
