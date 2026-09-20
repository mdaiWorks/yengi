using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace mdaiAgent;

/// <summary>
/// Tüm yapay zeka sağlayıcılarının (OpenAI, Anthropic, Gemini, Ollama) uyması gereken ortak sözleşme.
/// </summary>
public interface IAiProvider
{
    string ModelName { get; }

    Task<ExtendedChatResponse?> SendChatWithToolsAsync(
        List<ExtendedChatMessage> messages, 
        List<ToolDefinition> tools, 
        CancellationToken cancellationToken = default);

    Task<ExtendedChatResponse?> SendChatWithToolsStreamAsync(
        List<ExtendedChatMessage> messages, 
        List<ToolDefinition> tools, 
        Action<string>? onTokenReceived = null, 
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Provider'ın belirtilen yeteneği destekleyip desteklemediğini kontrol eder.
    /// Sesli Komut ve RAG için otomatik provider seçiminde kullanılır.
    /// </summary>
    bool HasCapability(ProviderCapability capability);

    /// <summary>
    /// Ses dosyasını metne dönüştürür (Speech-to-Text)
    /// Kullananlar: VoiceCommandService (Sesli Komut)
    /// </summary>
    Task<string> TranscribeAudioAsync(string wavFilePath, CancellationToken cancellationToken = default);

    /// <summary>
    /// Metni vektöre dönüştürür (Embedding)
    /// Kullananlar: RagService (RAG Sistemi)
    /// </summary>
    Task<float[]> EmbedTextAsync(string text, CancellationToken cancellationToken = default);

    /// <summary>
    /// Metni sese dönüştürür (Text-to-Speech)
    /// Kullananlar: İleri özellikleri için (isteğe bağlı)
    /// </summary>
    Task<byte[]> GenerateSpeechAsync(string text, CancellationToken cancellationToken = default);

    /// <summary>
    /// Metinden görüntü üretir (Image Generation)
    /// Kullananlar: İleri özellikleri için (isteğe bağlı)
    /// </summary>
    Task<string> GenerateImageAsync(string prompt, CancellationToken cancellationToken = default);
}
