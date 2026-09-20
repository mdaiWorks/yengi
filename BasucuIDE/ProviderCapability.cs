namespace mdaiAgent;

/// <summary>
/// Yapay zeka sağlayıcılarının destekledikleri yetenekleri tanımlar.
/// Sesli Komut (STT), RAG (Embedding) ve diğer özel özelliklerin
/// otomatik olarak seçilmesini sağlar.
/// </summary>
[Flags]
public enum ProviderCapability
{
    None = 0,
    Chat = 1,                  // Temel sohbet yeteneği
    SpeechToText = 2,          // Ses → Metin (Whisper vb.)
    TextToSpeech = 4,          // Metin → Ses
    Embedding = 8,             // Metni vektöre dönüştürme (RAG için)
    ImageGeneration = 16       // Görüntü üretme
}

/// <summary>
/// Her provider'ın desteklediği yetenekleri tanımlar.
/// Kullanıcı seçtiği ana provider'a göre otomatik olarak
/// Sesli Komut ve RAG özelliklerinin kullanılıp kullanılamayacağı belirlenir.
/// </summary>
public static class ProviderCapabilities
{
    public static ProviderCapability GetCapabilities(ProviderType providerType)
    {
        return providerType switch
        {
            // 🟢 Ollama: Local, offline, tam yetkinlik (STT + Embedding)
            ProviderType.LocalModel => 
                ProviderCapability.Chat | 
                ProviderCapability.SpeechToText |    // Whisper model var
                ProviderCapability.Embedding,         // nomic-embed-text var

            // 🔵 OpenAI: Tüm hizmetler bir API anahtardan
            ProviderType.OpenAI => 
                ProviderCapability.Chat | 
                ProviderCapability.SpeechToText |    // Whisper API var
                ProviderCapability.TextToSpeech |    // TTS API var
                ProviderCapability.Embedding |        // text-embedding-3-small
                ProviderCapability.ImageGeneration,   // DALL-E

            // 🟣 Claude (Anthropic): Sadece sohbet, fallback gerekli
            ProviderType.Anthropic => 
                ProviderCapability.Chat,

            // 🟡 Gemini (Google): Sadece sohbet, fallback gerekli
            ProviderType.Google => 
                ProviderCapability.Chat,

            // ⚫ API Service (OpenRouter, DeepSeek, etc.): Sadece sohbet, fallback gerekli
            ProviderType.ApiService => 
                ProviderCapability.Chat,

            _ => ProviderCapability.Chat
        };
    }

    /// <summary>
    /// Provider belirli bir yeteneği destekliyor mu?
    /// </summary>
    public static bool HasCapability(this ProviderType providerType, ProviderCapability capability)
    {
        var supported = GetCapabilities(providerType);
        return (supported & capability) == capability;
    }

    /// <summary>
    /// Türkçe açıklama (UI gösterimi için)
    /// </summary>
    public static string GetCapabilityDescription(ProviderCapability capability)
    {
        return capability switch
        {
            ProviderCapability.SpeechToText => "🎤 Sesli Komut",
            ProviderCapability.TextToSpeech => "🔊 Metin → Ses",
            ProviderCapability.Embedding => "🧠 RAG (Embedding)",
            ProviderCapability.ImageGeneration => "🖼️ Görüntü Üretme",
            _ => "❓ Bilinmeyen"
        };
    }
}
