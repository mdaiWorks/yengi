using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using System.Threading;

namespace mdaiAgent;

public class ChatSessionService
{
    private readonly Func<string?> _getSelectedFolder;

    public ChatSessionService(Func<string?> getSelectedFolder)
    {
        _getSelectedFolder = getSelectedFolder;
    }

    public string? GetChatsFilePath()
    {
        // Önce proje klasörünü dene
        var selectedFolder = _getSelectedFolder();
        if (!string.IsNullOrEmpty(selectedFolder) && Directory.Exists(selectedFolder))
        {
            try
            {
                var mdaiDir = Path.Combine(selectedFolder, ".mdai");
                Directory.CreateDirectory(mdaiDir);
                var path = Path.Combine(mdaiDir, "chats.json");
                // Yazma iznini kontrol etmek için boş dosya oluşturmayı dene
                if (!File.Exists(path))
                {
                    File.Create(path).Dispose();
                }

                // constitution.md henüz projede yoksa programa gömülü şablonu çıkar
                var constitutionDest = Path.Combine(mdaiDir, "constitution.md");
                if (!File.Exists(constitutionDest))
                {
                    var assembly = System.Reflection.Assembly.GetExecutingAssembly();
                    using var stream = assembly.GetManifestResourceStream("constitution.md");
                    if (stream != null)
                    {
                        using var reader = new System.IO.StreamReader(stream, System.Text.Encoding.UTF8);
                        var content = reader.ReadToEnd();
                        File.WriteAllText(constitutionDest, content, System.Text.Encoding.UTF8);
                    }
                }

                return path;
            }
            catch
            {
                // Proje klasöründe sorun varsa AppData'ya geç
            }
        }

        // Proje seçili değilse null döndür — sohbet listesi boş gösterilir
        return null;
    }

    public List<ChatSession> LoadSessions()
    {
        for (int attempt = 0; attempt < 3; attempt++)
        {
            try
            {
                var path = GetChatsFilePath();
                if (path == null) return new List<ChatSession>(); // Proje yok → boş liste
                if (!File.Exists(path)) return new List<ChatSession>();

                var json = File.ReadAllText(path);
                var sessions = JsonSerializer.Deserialize<List<ChatSession>>(json);
                var result = sessions ?? new List<ChatSession>();

                foreach (var session in result)
                {
                    session.Name = NormalizeDefaultSessionName(session.Name);
                }

                return result;
            }
            catch
            {
                Thread.Sleep(100); // Kısa bekle ve tekrar dene
            }
        }
        return new List<ChatSession>();
    }

    public void SaveSessions(IEnumerable<ChatSession> sessions)
    {
        for (int attempt = 0; attempt < 3; attempt++)
        {
            try
            {
                var path = GetChatsFilePath();
                if (path == null) return; // Proje yok → kaydetme
                
                var json = JsonSerializer.Serialize(sessions, new JsonSerializerOptions { WriteIndented = true });
                
                // Önce geçici dosyaya yaz, sonra değiştir - dosya kilitlenmesini önler
                var tempPath = path + ".tmp";
                File.WriteAllText(tempPath, json);
                
                // Eski dosya varsa sil ve yenisini yeniden adlandır
                if (File.Exists(path))
                {
                    File.Delete(path);
                }
                File.Move(tempPath, path);
                return;
            }
            catch
            {
                Thread.Sleep(100);
            }
        }
    }

    public ChatSession CreateNewSession(string name, IList<ChatSession> existingSessions)
    {
        var session = new ChatSession { Name = NormalizeDefaultSessionName(name) };
        existingSessions.Add(session);
        SaveSessions(existingSessions);
        return session;
    }

    private static string NormalizeDefaultSessionName(string? name)
    {
        var defaultName = Localization.Get("Yeni Sohbet", "New Chat");

        if (string.IsNullOrWhiteSpace(name))
            return defaultName;

        if (name.Equals("Yeni Sohbet", System.StringComparison.OrdinalIgnoreCase)
            || name.Equals("New Chat", System.StringComparison.OrdinalIgnoreCase))
        {
            return defaultName;
        }

        return name;
    }
}
