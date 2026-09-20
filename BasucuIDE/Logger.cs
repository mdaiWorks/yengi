using System;
using System.IO;
using System.Text;
using System.Text.RegularExpressions;

namespace mdaiAgent;

public static class Logger
{
    private static readonly object _lock = new();
    private static string _logDir = "";

    // ============================================================
    // Faz 1-C: Secret Redaction — API Anahtarı Maskeleme
    // Log dosyalarına API anahtarlarının sızmasını önler.
    // Örnek: "sk-or-v1-abc...xyz" → "sk-or-***REDACTED***"
    // ============================================================
    private static readonly Regex[] _secretPatterns =
    [
        // OpenAI, OpenRouter, DeepSeek anahtarları: sk- ile başlar
        new Regex(@"sk-[a-zA-Z0-9\-_]{8,}", RegexOptions.Compiled),
        // Groq anahtarları: gsk_ ile başlar
        new Regex(@"gsk_[a-zA-Z0-9\-_]{8,}", RegexOptions.Compiled),
        // Bearer token'ları: Authorization header'larında
        new Regex(@"Bearer\s+[a-zA-Z0-9\-_\.]{8,}", RegexOptions.Compiled | RegexOptions.IgnoreCase),
        // Genel "key=" veya "apikey=" içeren değerler
        new Regex(@"(?:api[_-]?key|apikey)\s*[=:]\s*[""']?([a-zA-Z0-9\-_\.]{8,})[""']?",
            RegexOptions.Compiled | RegexOptions.IgnoreCase),
    ];

    /// <summary>
    /// Mesaj içindeki API anahtarlarını maskeler: ilk 6 karakter bırakılır, geri kalanı gizlenir.
    /// </summary>
    private static string RedactSecrets(string message)
    {
        if (string.IsNullOrEmpty(message)) return message;

        var result = message;
        result = _secretPatterns[0].Replace(result, m =>
            m.Value.Length > 6 ? m.Value.Substring(0, 6) + "***REDACTED***" : "***REDACTED***");
        result = _secretPatterns[1].Replace(result, m =>
            m.Value.Length > 4 ? m.Value.Substring(0, 4) + "***REDACTED***" : "***REDACTED***");
        result = _secretPatterns[2].Replace(result, m =>
        {
            var parts = m.Value.Split(' ', 2);
            return parts.Length == 2 ? $"{parts[0]} ***REDACTED***" : "***REDACTED***";
        });
        result = _secretPatterns[3].Replace(result, m =>
        {
            var eqIdx = m.Value.IndexOf('=');
            return eqIdx >= 0 ? m.Value.Substring(0, eqIdx + 1) + " ***REDACTED***" : "***REDACTED***";
        });

        return result;
    }


    public static void Init(string? baseDir = null)
    {
        if (!string.IsNullOrEmpty(baseDir))
            _logDir = baseDir;
        else
            _logDir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "Yengi", "logs");

        try
        {
            if (!Directory.Exists(_logDir)) Directory.CreateDirectory(_logDir);
        }
        catch { }
    }

    private static string GetLogPath()
    {
        var file = $"mdai_{DateTime.Now:yyyyMMdd}.log";
        return Path.Combine(_logDir, file);
    }

    public static void LogInfo(string message) => Write("INFO", message);
    public static void LogError(string message) => Write("ERROR", message);
    public static void LogDebug(string message) => Write("DEBUG", message);

    private static void Write(string level, string message)
    {
        try
        {
            lock (_lock)
            {
                var path = GetLogPath();
                // API anahtarlarını maskele, sonra yaz
                var safeMessage = RedactSecrets(message);
                var line = $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] {level}: {safeMessage}{Environment.NewLine}";
                File.AppendAllText(path, line, Encoding.UTF8);
            }
        }
        catch { }
    }
}
