using System;
using System.Collections.Generic;
using System.Linq;

namespace mdaiAgent;

/// <summary>
/// Sesli Komut (STT) ve RAG (Embedding) için akıllı fallback mekanizması.
/// 
/// STRATEGY:
/// - STT Fallback: Seçili provider → Groq (free, fast) → OpenAI
/// - Embedding Fallback: Seçili provider → Ollama (free) → OpenAI
/// - Her fallback, mevcut credentials kontrol edilerek seçilir
/// 
/// SONUÇ: Kullanıcı extra config yazmaz, sistem otomatik çalışır
/// </summary>
public class ProviderFallbackStrategy
{
    private readonly AppSettings _settings;
    private readonly List<IAiProvider> _allProviders;

    public ProviderFallbackStrategy(AppSettings settings, List<IAiProvider> allProviders)
    {
        _settings = settings ?? throw new ArgumentNullException(nameof(settings));
        _allProviders = allProviders ?? throw new ArgumentNullException(nameof(allProviders));
    }

    /// <summary>
    /// STT (Sesli Komut) için fallback sırasını döndürür.
    /// Groq → OpenAI → Error
    /// </summary>
    private static readonly List<ProviderType> SpeechToTextFallback = new()
    {
        ProviderType.ApiService,  // OpenRouter veya DeepSeek ile Groq API çağrısı
        ProviderType.OpenAI,      // OpenAI Whisper API
    };

    /// <summary>
    /// Embedding (RAG) için fallback sırasını döndürür.
    /// Ollama → OpenAI → Error
    /// </summary>
    private static readonly List<ProviderType> EmbeddingFallback = new()
    {
        ProviderType.LocalModel,  // Ollama nomic-embed-text
        ProviderType.OpenAI,      // OpenAI text-embedding-3-small
    };

    /// <summary>
    /// Belirtilen yeteneğe göre fallback provider seçer
    /// </summary>
    public IAiProvider SelectFallback(ProviderCapability capability)
    {
        var fallbackList = capability == ProviderCapability.SpeechToText
            ? SpeechToTextFallback
            : EmbeddingFallback;

        foreach (var providerType in fallbackList)
        {
            // Credentials kontrol et
            if (!HasValidCredentials(providerType))
                continue;

            // Provider'ın yeteneğini kontrol et
            if (!providerType.HasCapability(capability))
                continue;

            // Bulundu!
            return CreateProviderByType(providerType);
        }

        throw new Exception(
            $"{capability} için uygun fallback provider bulunamadı. " +
            $"Lütfen settings'te en az bir provider'ın API anahtarını girin.");
    }

    /// <summary>
    /// Provider türüne göre provider instance'ı oluştur
    /// </summary>
    private IAiProvider CreateProviderByType(ProviderType providerType)
    {
        // Eğer seçili provider zaten istenen türdeyse, onu kullan
        if (_settings.ActiveProvider == providerType)
            return AiProviderFactory.CreateProvider(_settings);

        // Aksi takdirde, fallback provider için temporary AppSettings oluştur
        var fallbackSettings = new AppSettings
        {
            ActiveProvider = providerType,
            ApiKey = _settings.ApiKey,
            BaseUrl = _settings.BaseUrl,
            Model = _settings.Model,
            UseLocalModel = _settings.UseLocalModel,
            LocalBaseUrl = _settings.LocalBaseUrl,
            LocalModel = _settings.LocalModel,
            AnthropicApiKey = _settings.AnthropicApiKey,
            GoogleApiKey = _settings.GoogleApiKey,
            OpenAIApiKey = _settings.OpenAIApiKey,
            ApiTimeoutSeconds = _settings.ApiTimeoutSeconds,
        };

        return AiProviderFactory.CreateProvider(fallbackSettings);
    }

    /// <summary>
    /// Provider'ın gerekli credentials'ı var mı?
    /// </summary>
    private bool HasValidCredentials(ProviderType providerType)
    {
        return providerType switch
        {
            ProviderType.LocalModel => 
                // Ollama genellikle localhost:11434'te çalışır
                !string.IsNullOrEmpty(_settings.LocalBaseUrl),
            
            ProviderType.OpenAI => 
                !string.IsNullOrEmpty(_settings.OpenAIApiKey),
            
            ProviderType.ApiService => 
                !string.IsNullOrEmpty(_settings.ApiKey),
            
            ProviderType.Anthropic => 
                !string.IsNullOrEmpty(_settings.AnthropicApiKey),
            
            ProviderType.Google => 
                !string.IsNullOrEmpty(_settings.GoogleApiKey),
            
            _ => false
        };
    }

    /// <summary>
    /// Fallback provider'ın adını döndürür (logging için)
    /// </summary>
    public string GetFallbackProviderName(ProviderCapability capability)
    {
        try
        {
            var fallback = SelectFallback(capability);
            return fallback.ModelName;
        }
        catch
        {
            return "❌ Fallback yok";
        }
    }

    /// <summary>
    /// STT fallback zincirini döndürür (debug/logging için)
    /// </summary>
    public List<string> GetSpeechToTextChain()
    {
        var chain = new List<string>();
        
        // Ana provider
        var selectedCapability = _settings.ActiveProvider.HasCapability(ProviderCapability.SpeechToText)
            ? $"✅ {_settings.ActiveProvider}"
            : $"❌ {_settings.ActiveProvider} (STT desteklemiyor)";
        chain.Add(selectedCapability);

        // Fallback chain
        foreach (var fallback in SpeechToTextFallback)
        {
            if (HasValidCredentials(fallback) && fallback.HasCapability(ProviderCapability.SpeechToText))
            {
                chain.Add($"→ ✅ {fallback}");
            }
            else if (HasValidCredentials(fallback))
            {
                chain.Add($"→ ⚠️ {fallback} (setup var ama STT yok)");
            }
            else
            {
                chain.Add($"→ ❌ {fallback} (setup yok)");
            }
        }

        return chain;
    }

    /// <summary>
    /// Embedding fallback zincirini döndürür (debug/logging için)
    /// </summary>
    public List<string> GetEmbeddingChain()
    {
        var chain = new List<string>();
        
        // Ana provider
        var selectedCapability = _settings.ActiveProvider.HasCapability(ProviderCapability.Embedding)
            ? $"✅ {_settings.ActiveProvider}"
            : $"❌ {_settings.ActiveProvider} (Embedding desteklemiyor)";
        chain.Add(selectedCapability);

        // Fallback chain
        foreach (var fallback in EmbeddingFallback)
        {
            if (HasValidCredentials(fallback) && fallback.HasCapability(ProviderCapability.Embedding))
            {
                chain.Add($"→ ✅ {fallback}");
            }
            else if (HasValidCredentials(fallback))
            {
                chain.Add($"→ ⚠️ {fallback} (setup var ama Embedding yok)");
            }
            else
            {
                chain.Add($"→ ❌ {fallback} (setup yok)");
            }
        }

        return chain;
    }
}
