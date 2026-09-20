using System;
using System.IO;
using System.Linq;
using System.Net.Sockets;
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
                    txtTitle.Text = "🎨 Görsel Stüdyosu Ayarları";
                    panelImageStudio.Visibility = Visibility.Visible;
                    
                    chkUsePollinations.IsChecked = _settings.ImageStudioUseFreePollinations;
                    chkEnhancePrompt.IsChecked = _settings.ImageStudioEnhancePrompt;
                    txtPublicBaseUrl.Text = _settings.ImageStudioPublicBaseUrl;
                    chkEnableMultiAgent.IsChecked = _settings.EnableMultiAgentImageGeneration;
                    txtImageBaseUrl.Text = _settings.ImageStudioBaseUrl;
                    txtImageApiKey.Text = _settings.ImageStudioApiKey;
                    txtImageModel.Text = _settings.ImageStudioModel;
                    
                    UpdateImageStudioFormState();
                    break;
                    
                case AgentWorkspaceMode.BlenderCopilot:
                    txtTitle.Text = "🧊 Blender Asistanı Ayarları";
                    panelBlender.Visibility = Visibility.Visible;
                    
                    txtBlenderPort.Text = _settings.BlenderWebSocketPort.ToString();
                    
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
                    txtTitle.Text = "🎮 Unity Copilot Ayarları";
                    panelUnity.Visibility = Visibility.Visible;
                    
                    txtUnityPort.Text = _settings.UnityWebSocketPort.ToString();
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

                txtBlenderPath.Text = "Blender klasörü otomatik bulunamadı. Lütfen Gözat'a tıklayın.";
            }
            catch
            {
                txtBlenderPath.Text = "Blender klasörü seçilmedi.";
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
                    txtBlenderInstallResult.Text = "✅ Eklenti yüklü (yengi_copilot.py)";
                    txtBlenderInstallResult.Foreground = System.Windows.Media.Brushes.LimeGreen;
                    btnInstallAddon.Content = "🔄 Eklentiyi Yeniden Kur / Güncelle";
                    btnInstallAddon.Background = new System.Windows.Media.SolidColorBrush((System.Windows.Media.Color)System.Windows.Media.ColorConverter.ConvertFromString("#2e7d32"));
                    return;
                }
            }

            txtBlenderInstallResult.Text = "⚠️ Eklenti henüz yüklenmedi.";
            txtBlenderInstallResult.Foreground = System.Windows.Media.Brushes.Orange;
            btnInstallAddon.Content = "🚀 Eklentiyi Blender'a Otomatik Kur";
            btnInstallAddon.Background = new System.Windows.Media.SolidColorBrush((System.Windows.Media.Color)System.Windows.Media.ColorConverter.ConvertFromString("#007acc"));
        }

        private void BtnBrowseBlender_Click(object sender, RoutedEventArgs e)
        {
            var dialog = new System.Windows.Forms.FolderBrowserDialog
            {
                Description = "Blender AppData veya Sürüm Klasörünü Seçin (Örn: AppData/Roaming/Blender Foundation/Blender/4.2)",
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
                txtBlenderInstallResult.Text = "❌ Geçerli bir Blender klasörü seçilmedi!";
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

                // Eklenti kodunu kopyala/yaz
                string sourceAddonPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "yengi_blender_addon.py");
                
                if (File.Exists(sourceAddonPath))
                {
                    File.Copy(sourceAddonPath, targetAddonFile, true);
                }
                else
                {
                    string code = @"bl_info = {
    'name': 'Yengi Copilot for Blender',
    'author': 'Yengi IDE',
    'version': (1, 0, 0),
    'blender': (2, 80, 0),
    'category': 'Development',
}
import bpy, socket, threading, queue, time
PORT = 8181
SECRET_TOKEN = """ + _settings.BlenderSecretToken + @"""
server_socket = None
is_running = False
script_queue = queue.Queue()

def execute_queued_scripts():
    while not script_queue.empty():
        item = script_queue.get()
        payload, conn = item
        try:
            code = payload
            if payload.startswith('YENGI_TOKEN:'):
                lines = payload.split('\n', 1)
                sent_token = lines[0].replace('YENGI_TOKEN:', '').strip()
                if SECRET_TOKEN and sent_token != SECRET_TOKEN:
                    conn.sendall(b'ERROR: Unauthorized access - Invalid Token')
                    conn.close()
                    continue
                code = lines[1] if len(lines) > 1 else ''

            exec(code, {'bpy': bpy})
            conn.sendall(b'SUCCESS')
            conn.close()
        except Exception as ex:
            try:
                conn.sendall(f'ERROR: {ex}'.encode('utf-8'))
                conn.close()
            except: pass
    return 0.2

def socket_listener():
    global server_socket, is_running
    server_socket = socket.socket(socket.AF_INET, socket.SOCK_STREAM)
    server_socket.setsockopt(socket.SOL_SOCKET, socket.SO_REUSEADDR, 1)
    try:
        server_socket.bind(('127.0.0.1', PORT))
        server_socket.listen(5)
        is_running = True
    except: return
    while is_running:
        try:
            conn, addr = server_socket.accept()
            buf = bytearray()
            while True:
                chunk = conn.recv(4096)
                if not chunk: break
                buf.extend(chunk)
            if buf: script_queue.put((buf.decode('utf-8'), conn))
        except Exception:
            if not is_running: break
            time.sleep(0.05)
            continue

def register():
    if not bpy.app.timers.is_registered(execute_queued_scripts):
        bpy.app.timers.register(execute_queued_scripts)
    threading.Thread(target=socket_listener, daemon=True).start()

def unregister():
    global is_running, server_socket
    is_running = False
    if server_socket: server_socket.close()

if __name__ == '__main__': register()
";
                    File.WriteAllText(targetAddonFile, code);
                }

                txtBlenderInstallResult.Text = $"✅ Eklenti yüklendi: {targetAddonFile}\nBlender'da Edit -> Preferences -> Add-ons menüsünden 'Yengi Copilot'u aktif edin.";
                txtBlenderInstallResult.Foreground = System.Windows.Media.Brushes.LimeGreen;
                CheckBlenderAddonStatus();
            }
            catch (Exception ex)
            {
                txtBlenderInstallResult.Text = $"❌ Kurulum Hatası: {ex.Message}";
                txtBlenderInstallResult.Foreground = System.Windows.Media.Brushes.Tomato;
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
                    break;
                    
                case AgentWorkspaceMode.BlenderCopilot:
                    if (int.TryParse(txtBlenderPort.Text, out int bPort))
                        _settings.BlenderWebSocketPort = bPort;
                    _settings.BlenderPath = txtBlenderPath.Text;
                    break;
                    
                case AgentWorkspaceMode.UnityCopilot:
                    if (int.TryParse(txtUnityPort.Text, out int uPort))
                        _settings.UnityWebSocketPort = uPort;
                    break;
            }
            
            SettingsWindow.SaveSettings(_settings);
            DialogResult = true;
            Close();
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
                txtBlenderTestResult.Text = "❌ Geçersiz port numarası!";
                txtBlenderTestResult.Foreground = System.Windows.Media.Brushes.Tomato;
                return;
            }

            txtBlenderTestResult.Text = "⏳ Bağlanılıyor...";
            txtBlenderTestResult.Foreground = System.Windows.Media.Brushes.Gray;

            try
            {
                using var client = new TcpClient();
                var result = client.BeginConnect("127.0.0.1", port, null, null);
                var success = result.AsyncWaitHandle.WaitOne(TimeSpan.FromSeconds(2));
                
                if (success && client.Connected)
                {
                    txtBlenderTestResult.Text = $"✅ Bağlantı Başarılı! (Port {port} aktif — Blender Dinliyor)";
                    txtBlenderTestResult.Foreground = System.Windows.Media.Brushes.LimeGreen;
                }
                else
                {
                    txtBlenderTestResult.Text = $"❌ Bağlantı Başarısız! (Port {port}). Blender'da 'Yengi Copilot' eklentisinin aktif olduğundan emin olun.";
                    txtBlenderTestResult.Foreground = System.Windows.Media.Brushes.Tomato;
                }
            }
            catch
            {
                txtBlenderTestResult.Text = $"❌ Bağlantı Başarısız! (Port {port}). Blender'da 'Yengi Copilot' eklentisinin aktif olduğundan emin olun.";
                txtBlenderTestResult.Foreground = System.Windows.Media.Brushes.Tomato;
            }
        }

        private void BtnInstallUnityAddon_Click(object sender, RoutedEventArgs e)
        {
            var dialog = new Microsoft.Win32.OpenFolderDialog
            {
                Title = "Unity Projenizin Ana Klasörünü Seçin"
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
                        string scriptCode = @"#if UNITY_EDITOR
using System;
using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Threading;
using System.Collections.Concurrent;
using UnityEditor;
using UnityEngine;

namespace Yengi.UnityCopilot
{
    [InitializeOnLoad]
    public static class YengiUnityCopilot
    {
        private const int DEFAULT_PORT = 8282;
        private static TcpListener _listener;
        private static Thread _listenerThread;
        private static bool _isRunning;
        private static readonly ConcurrentQueue<(string script, TcpClient client)> _queue = new ConcurrentQueue<(string, TcpClient)>();

        static YengiUnityCopilot()
        {
            StartServer(DEFAULT_PORT);
            EditorApplication.update += OnEditorUpdate;
        }

        public static void StartServer(int port)
        {
            if (_isRunning) return;
            try
            {
                _listener = new TcpListener(IPAddress.Loopback, port);
                _listener.Start();
                _isRunning = true;
                _listenerThread = new Thread(ListenLoop) { IsBackground = true };
                _listenerThread.Start();
                Debug.Log($""[Yengi Unity Copilot] Server listening on 127.0.0.1:{port}"");
            }
            catch (Exception ex)
            {
                Debug.LogError($""[Yengi Unity Copilot] Failed to bind port {port}: {ex.Message}"");
            }
        }

        private static void ListenLoop()
        {
            while (_isRunning && _listener != null)
            {
                try
                {
                    var client = _listener.AcceptTcpClient();
                    using var stream = client.GetStream();
                    using var ms = new MemoryStream();
                    byte[] buffer = new byte[4096];
                    int bytesRead;
                    while ((bytesRead = stream.Read(buffer, 0, buffer.Length)) > 0)
                    {
                        ms.Write(buffer, 0, bytesRead);
                    }
                    string code = Encoding.UTF8.GetString(ms.ToArray());
                    if (!string.IsNullOrWhiteSpace(code))
                    {
                        _queue.Enqueue((code, client));
                    }
                }
                catch
                {
                    if (!_isRunning) break;
                }
            }
        }

        private static void OnEditorUpdate()
        {
            while (_queue.TryDequeue(out var item))
            {
                ExecuteScriptInEditor(item.script);
            }
        }

        private static void ExecuteScriptInEditor(string script)
        {
            try
            {
                Debug.Log($""[Yengi Unity Copilot] Executing received script:\n{script}"");
                string tempDir = Path.Combine(Application.dataPath, ""Editor"", ""YengiGenerated"");
                if (!Directory.Exists(tempDir)) Directory.CreateDirectory(tempDir);
                string scriptPath = Path.Combine(tempDir, ""YengiAction.cs"");
                File.WriteAllText(scriptPath, script);
                AssetDatabase.Refresh();
                Debug.Log(""[Yengi Unity Copilot] Script compiled and imported into Assets/Editor/YengiGenerated/YengiAction.cs"");
            }
            catch (Exception ex)
            {
                Debug.LogError($""[Yengi Unity Copilot] Execution error: {ex.Message}"");
            }
        }
    }
}
#endif
";
                        File.WriteAllText(targetFilePath, scriptCode);
                    }

                    txtUnityInstallResult.Text = $"✅ Eklenti scripti yüklendi: {targetFilePath}\nUnity Editörünü açtığınızda port 8282 otomatik dinlenmeye başlar.";
                    txtUnityInstallResult.Foreground = System.Windows.Media.Brushes.LimeGreen;
                }
                catch (Exception ex)
                {
                    txtUnityInstallResult.Text = $"❌ Yükleme Hatası: {ex.Message}";
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
                    txtUnityTestResult.Text = "❌ Geçersiz port numarası!";
                    txtUnityTestResult.Foreground = System.Windows.Media.Brushes.Tomato;
                    return;
                }

                txtUnityTestResult.Text = "⏳ Bağlanılıyor...";
                txtUnityTestResult.Foreground = System.Windows.Media.Brushes.Gray;

                try
                {
                    using var client = new TcpClient();
                    var result = client.BeginConnect("127.0.0.1", port, null, null);
                    var success = result.AsyncWaitHandle.WaitOne(TimeSpan.FromSeconds(2));

                    if (success && client.Connected)
                    {
                        txtUnityTestResult.Text = $"✅ Bağlantı Başarılı! (Port {port} aktif — Unity Dinliyor)";
                        txtUnityTestResult.Foreground = System.Windows.Media.Brushes.LimeGreen;
                    }
                    else
                    {
                        txtUnityTestResult.Text = $"❌ Bağlantı Başarısız! (Port {port}). Unity Editöründe 'YengiUnityCopilot.cs' scriptinin aktif olduğundan emin olun.";
                        txtUnityTestResult.Foreground = System.Windows.Media.Brushes.Tomato;
                    }
                }
                catch
                {
                    txtUnityTestResult.Text = $"❌ Bağlantı Başarısız! (Port {port}). Unity Editöründe 'YengiUnityCopilot.cs' scriptinin aktif olduğundan emin olun.";
                    txtUnityTestResult.Foreground = System.Windows.Media.Brushes.Tomato;
                }
            }
            catch { /* Ignore unhandled UI exceptions */ }
        }
    }
}
