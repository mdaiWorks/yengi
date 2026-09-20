#if UNITY_EDITOR
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
    /// <summary>
    /// Yengi AI Copilot - Unity Editor Live Socket Connection Handler (Port 8282).
    /// Place this file inside your Unity Project's `Assets/Editor/` folder.
    /// </summary>
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
                Debug.Log($"[Yengi Unity Copilot] Server listening on 127.0.0.1:{port}");
            }
            catch (Exception ex)
            {
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
                ExecuteScriptInEditor(item.script);
            }
        }

        private static void ExecuteScriptInEditor(string script)
        {
            try
            {
                Debug.Log($"[Yengi Unity Copilot] Executing received script:\n{script}");
                
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
}
#endif
