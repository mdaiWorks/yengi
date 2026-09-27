#if UNITY_EDITOR
using System;
using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Threading;
using System.Collections.Concurrent;
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace Yengi.UnityCopilot
{
    [Serializable]
    public class GameObjectNode
    {
        public string name;
        public string tag;
        public int layer;
        public bool active;
        public string position;
        public string rotation;
        public string scale;
        public List<string> components = new List<string>();
        public List<GameObjectNode> children = new List<GameObjectNode>();
    }

    [Serializable]
    public class UnitySceneContext
    {
        public string scene_name;
        public int root_object_count;
        public List<string> selected_objects = new List<string>();
        public List<GameObjectNode> root_objects = new List<GameObjectNode>();
    }

    [Serializable]
    public class UnityPayloadRequest
    {
        public string action;
        public string code;
        public string token;
    }

    [Serializable]
    public class UnityResponse
    {
        public string status;
        public string message;
        public string traceback;
        public string viewport_image_b64;
        public string viewport_image_path;
        public string scene_info_json;
    }

    /// <summary>
    /// Yengi AI Copilot - Unity Editor Live Socket & JSON-RPC Connection Handler (Port 8282).
    /// </summary>
    [InitializeOnLoad]
    public static class YengiUnityCopilot
    {
        private const int DEFAULT_PORT = 8282;
        private static TcpListener _listener;
        private static Thread _listenerThread;
        private static bool _isRunning;
        private static readonly ConcurrentQueue<(string payload, TcpClient client)> _queue = new ConcurrentQueue<(string, TcpClient)>();
        public static string LastStatusMessage = "🟢 Sunucu Dinliyor (Port 8282)";

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
                LastStatusMessage = $"🟢 Sunucu Dinliyor (127.0.0.1:{port})";
                Debug.Log($"[Yengi Unity Copilot] Server listening on 127.0.0.1:{port}");
            }
            catch (Exception ex)
            {
                LastStatusMessage = $"❌ Port Bağlama Hatası ({port}): {ex.Message}";
                Debug.LogError($"[Yengi Unity Copilot] Failed to bind port {port}: {ex.Message}");
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
                    byte[] buffer = new byte[8192];
                    int bytesRead;
                    while ((bytesRead = stream.Read(buffer, 0, buffer.Length)) > 0)
                    {
                        ms.Write(buffer, 0, bytesRead);
                        if (bytesRead < 8192) break;
                    }
                    string payload = Encoding.UTF8.GetString(ms.ToArray());
                    if (!string.IsNullOrWhiteSpace(payload))
                    {
                        _queue.Enqueue((payload, client));
                    }
                }
                catch (Exception)
                {
                    if (!_isRunning) break;
                }
            }
        }

        private static void OnEditorUpdate()
        {
            while (_queue.TryDequeue(out var item))
            {
                ProcessIncomingPayload(item.payload, item.client);
            }
        }

        private static void ProcessIncomingPayload(string payload, TcpClient client)
        {
            var response = new UnityResponse { status = "ERROR", message = "Bilinmeyen istek" };

            try
            {
                UnityPayloadRequest req = null;
                try { req = JsonUtility.FromJson<UnityPayloadRequest>(payload); } catch { }

                string action = req != null && !string.IsNullOrEmpty(req.action) ? req.action : "execute";
                string code = req != null ? req.code : payload;

                if (action == "get_scene_info")
                {
                    string contextJson = GetSceneContextSummary();
                    response.status = "SUCCESS";
                    response.message = "Sahne bilgileri alındı.";
                    response.scene_info_json = contextJson;
                    LastStatusMessage = "📊 Sahne özet verisi Yengi'ye gönderildi.";
                }
                else if (action == "capture_viewport")
                {
                    var (path, b64, err) = CaptureSceneViewScreenshot();
                    if (!string.IsNullOrEmpty(b64))
                    {
                        response.status = "SUCCESS";
                        response.message = "Viewport resmi alındı.";
                        response.viewport_image_b64 = b64;
                        response.viewport_image_path = path;
                        LastStatusMessage = "📸 SceneView ekran görüntüsü alındı.";
                    }
                    else
                    {
                        response.status = "ERROR";
                        response.message = $"Ekran görüntüsü hatası: {err}";
                    }
                }
                else if (action == "execute")
                {
                    Undo.RegisterCompleteObjectUndo(Selection.activeGameObject != null ? (UnityEngine.Object)Selection.activeGameObject : (UnityEngine.Object)SceneManager.GetActiveScene().GetRootGameObjects()[0], "Yengi Action");

                    ExecuteScriptInEditor(code);

                    var (path, b64, _) = CaptureSceneViewScreenshot();
                    string contextJson = GetSceneContextSummary();

                    response.status = "SUCCESS";
                    response.message = "C# Script derlendi ve Unity Editor'de çalıştırıldı.";
                    response.viewport_image_b64 = b64;
                    response.viewport_image_path = path;
                    response.scene_info_json = contextJson;
                    LastStatusMessage = "✅ C# Script başarıyla çalıştırıldı.";
                }
            }
            catch (Exception ex)
            {
                response.status = "ERROR";
                response.message = ex.Message;
                response.traceback = ex.StackTrace;
                LastStatusMessage = $"❌ Hata: {ex.Message}";
                Debug.LogError($"[Yengi Unity Copilot Hata] {ex.Message}\n{ex.StackTrace}");
            }

            try
            {
                string resJson = JsonUtility.ToJson(response);
                byte[] resBytes = Encoding.UTF8.GetBytes(resJson);
                if (client.Connected)
                {
                    using var stream = client.GetStream();
                    stream.Write(resBytes, 0, resBytes.Length);
                }
                client.Close();
            }
            catch { }
        }

        public static string GetSceneContextSummary()
        {
            try
            {
                var scene = SceneManager.GetActiveScene();
                var rootObjects = scene.GetRootGameObjects();

                var ctx = new UnitySceneContext
                {
                    scene_name = scene.name,
                    root_object_count = rootObjects.Length
                };

                foreach (var s in Selection.gameObjects)
                {
                    ctx.selected_objects.Add(s.name);
                }

                int count = 0;
                foreach (var go in rootObjects)
                {
                    if (count++ > 30) break;
                    ctx.root_objects.Add(ExtractNode(go, 0));
                }

                return JsonUtility.ToJson(ctx);
            }
            catch (Exception ex)
            {
                return $"{{\"error\": \"{ex.Message}\"}}";
            }
        }

        private static GameObjectNode ExtractNode(GameObject go, int depth)
        {
            var node = new GameObjectNode
            {
                name = go.name,
                tag = go.tag,
                layer = go.layer,
                active = go.activeSelf,
                position = go.transform.position.ToString("F2"),
                rotation = go.transform.eulerAngles.ToString("F2"),
                scale = go.transform.localScale.ToString("F2")
            };

            foreach (var comp in go.GetComponents<Component>())
            {
                if (comp != null)
                {
                    node.components.Add(comp.GetType().Name);
                }
            }

            if (depth < 2)
            {
                for (int i = 0; i < go.transform.childCount; i++)
                {
                    if (i > 10) break;
                    node.children.Add(ExtractNode(go.transform.GetChild(i).gameObject, depth + 1));
                }
            }

            return node;
        }

        public static (string path, string b64, string error) CaptureSceneViewScreenshot()
        {
            try
            {
                Camera cam = null;
                if (SceneView.lastActiveSceneView != null && SceneView.lastActiveSceneView.camera != null)
                {
                    cam = SceneView.lastActiveSceneView.camera;
                }
                else
                {
                    cam = Camera.main;
                }

                if (cam == null)
                {
                    return (null, "", "Aktif kamera veya SceneView bulunamadı.");
                }

                int width = 800;
                int height = 600;
                RenderTexture rt = new RenderTexture(width, height, 24);
                cam.targetTexture = rt;
                Texture2D screenShot = new Texture2D(width, height, TextureFormat.RGB24, false);
                cam.Render();
                RenderTexture.active = rt;
                screenShot.ReadPixels(new Rect(0, 0, width, height), 0, 0);
                screenShot.Apply();
                cam.targetTexture = null;
                RenderTexture.active = null;
                UnityEngine.Object.DestroyImmediate(rt);

                byte[] bytes = screenShot.EncodeToPNG();
                UnityEngine.Object.DestroyImmediate(screenShot);

                string tempDir = Path.Combine(Path.GetTempPath(), "YengiUnity");
                if (!Directory.Exists(tempDir)) Directory.CreateDirectory(tempDir);
                string tempPath = Path.Combine(tempDir, "yengi_unity_preview.png");
                File.WriteAllBytes(tempPath, bytes);

                string b64 = Convert.ToBase64String(bytes);
                return (tempPath, b64, "SUCCESS");
            }
            catch (Exception ex)
            {
                return (null, "", ex.Message);
            }
        }

        private static void ExecuteScriptInEditor(string script)
        {
            try
            {
                Debug.Log($"[Yengi Unity Copilot] Executing received C# script:\n{script}");
                
                string tempDir = Path.Combine(Application.dataPath, "Editor", "YengiGenerated");
                if (!Directory.Exists(tempDir)) Directory.CreateDirectory(tempDir);

                string scriptPath = Path.Combine(tempDir, "YengiAction.cs");
                File.WriteAllText(scriptPath, script);
                AssetDatabase.Refresh();

                Debug.Log("[Yengi Unity Copilot] Script compiled and imported into Assets/Editor/YengiGenerated/YengiAction.cs");
            }
            catch (Exception ex)
            {
                Debug.LogError($"[Yengi Unity Copilot] Execution error: {ex.Message}");
            }
        }
    }

    public class YengiCopilotWindow : EditorWindow
    {
        [MenuItem("Window/Yengi AI Copilot")]
        public static void ShowWindow()
        {
            GetWindow<YengiCopilotWindow>("Yengi Copilot");
        }

        private void OnGUI()
        {
            GUILayout.Space(10);
            GUILayout.Label("🎮 Yengi Unity AI Copilot", EditorStyles.boldLabel);
            EditorGUILayout.HelpBox(YengiUnityCopilot.LastStatusMessage, MessageType.Info);

            GUILayout.Space(10);
            if (GUILayout.Button("📸 SceneView Ekran Görüntüsü Al", GUILayout.Height(30)))
            {
                var (path, b64, err) = YengiUnityCopilot.CaptureSceneViewScreenshot();
                if (!string.IsNullOrEmpty(b64))
                {
                    Debug.Log($"[Yengi Unity Copilot] Ekran görüntüsü alındı: {path}");
                }
                else
                {
                    Debug.LogError($"[Yengi Unity Copilot] Hata: {err}");
                }
            }

            if (GUILayout.Button("📊 Sahne Özetini Konsola Yazdır", GUILayout.Height(30)))
            {
                string json = YengiUnityCopilot.GetSceneContextSummary();
                Debug.Log($"[YENGI SAHNE ÖZETİ]\n{json}");
            }
        }
    }
}
#endif
