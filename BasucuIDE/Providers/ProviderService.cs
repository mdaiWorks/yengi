using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace mdaiAgent;

/// <summary>
/// Üst seviye provider yönetim implementasyonu.
/// 
/// Sorumluluğu:
/// 1. Seçili provider'ı tutar ve döndürür
/// 2. Fallback provider seçer (STT ve Embedding için)
/// 3. Provider yeteneklerini kontrol eder
/// 4. Sesli Komut ve RAG'ı provider-aware şekilde yapılandırır
/// </summary>
public class ProviderService : IProviderService
{
    private readonly AppSettings _settings;
    private readonly IAiProvider _selectedProvider;
    private readonly ProviderFallbackStrategy _fallbackStrategy;
    private readonly List<IAiProvider> _allProviders;

    public ProviderService(AppSettings settings, IAiProvider selectedProvider)
    {
        _settings = settings ?? throw new ArgumentNullException(nameof(settings));
        _selectedProvider = selectedProvider ?? throw new ArgumentNullException(nameof(selectedProvider));
        
        // Tüm provider'ları oluştur
        _allProviders = CreateAllProviders(settings);
        
        // Fallback stratejisini başlat
        _fallbackStrategy = new ProviderFallbackStrategy(settings, _allProviders);

        Logger.LogInfo($"ProviderService başlatıldı. Aktif provider: {_selectedProvider.ModelName}");
        LogCapabilities();
    }

    /// <summary>
    /// Seçili provider'ı döndürür
    /// </summary>
    public IAiProvider GetSelected()
    {
        return _selectedProvider;
    }

    /// <summary>
    /// Belirtilen yeteneği destekleyen provider'ı seçer
    /// (Önce seçili provider, yoksa fallback)
    /// </summary>
    public IAiProvider SelectByCapability(ProviderCapability capability)
    {
        if (_selectedProvider.HasCapability(capability))
        {
            Logger.LogInfo($"Provider {_selectedProvider.ModelName} capability {capability} için kullanılacak");
            return _selectedProvider;
        }

        Logger.LogInfo($"Provider {_selectedProvider.ModelName} capability {capability} desteklemiyor, fallback aranıyor...");
        return SelectFallback(capability);
    }

    /// <summary>
    /// Fallback provider seçer
    /// </summary>
    public IAiProvider SelectFallback(ProviderCapability capability)
    {
        try
        {
            var fallback = _fallbackStrategy.SelectFallback(capability);
            Logger.LogInfo($"Fallback provider seçildi: {fallback.ModelName} (capability: {capability})");
            return fallback;
        }
        catch (Exception ex)
        {
            Logger.LogError($"Fallback provider seçilemedi: {ex.Message}");
            throw;
        }
    }

    /// <summary>
    /// Seçili provider'ın belirtilen yeteneği destekleyip desteklemediğini kontrol eder
    /// </summary>
    public bool SupportsCapability(ProviderCapability capability)
    {
        return _selectedProvider.HasCapability(capability);
    }

    /// <summary>
    /// Fallback provider'ın adını döndürür
    /// </summary>
    public string GetFallbackProviderName(ProviderCapability capability)
    {
        return _fallbackStrategy.GetFallbackProviderName(capability);
    }

    /// <summary>
    /// Tüm kullanılabilir provider'ları döndürür
    /// </summary>
    public List<IAiProvider> GetAvailableProviders()
    {
        return _allProviders;
    }

    /// <summary>
    /// Provider'ın yeteneklerini döndürür
    /// </summary>
    public ProviderCapability GetCapabilities(IAiProvider provider)
    {
        return ProviderCapabilities.GetCapabilities(_settings.ActiveProvider);
    }

    /// <summary>
    /// Tüm provider'ları oluştur (fallback için)
    /// </summary>
    private List<IAiProvider> CreateAllProviders(AppSettings settings)
    {
        var providers = new List<IAiProvider>();

        try
        {
            // Ana provider
            providers.Add(AiProviderFactory.CreateProvider(settings));

            // Diğer kullanılabilir provider'ları ekle
            if (!string.IsNullOrEmpty(settings.OpenAIApiKey))
                providers.Add(CreateProviderForType(ProviderType.OpenAI, settings));

            if (!string.IsNullOrEmpty(settings.LocalBaseUrl))
                providers.Add(CreateProviderForType(ProviderType.LocalModel, settings));

            if (!string.IsNullOrEmpty(settings.AnthropicApiKey))
                providers.Add(CreateProviderForType(ProviderType.Anthropic, settings));

            if (!string.IsNullOrEmpty(settings.GoogleApiKey))
                providers.Add(CreateProviderForType(ProviderType.Google, settings));
        }
        catch (Exception ex)
        {
            Logger.LogInfo($"Provider oluşturulurken hata: {ex.Message}");
        }

        return providers;
    }

    /// <summary>
    /// Belirtilen türde provider oluştur
    /// </summary>
    private IAiProvider CreateProviderForType(ProviderType type, AppSettings baseSettings)
    {
        var settings = new AppSettings
        {
            ActiveProvider = type,
            ApiKey = baseSettings.ApiKey,
            BaseUrl = baseSettings.BaseUrl,
            Model = baseSettings.Model,
            UseLocalModel = baseSettings.UseLocalModel,
            LocalBaseUrl = baseSettings.LocalBaseUrl,
            LocalModel = baseSettings.LocalModel,
            AnthropicApiKey = baseSettings.AnthropicApiKey,
            GoogleApiKey = baseSettings.GoogleApiKey,
            OpenAIApiKey = baseSettings.OpenAIApiKey,
            ApiTimeoutSeconds = baseSettings.ApiTimeoutSeconds,
        };

        return AiProviderFactory.CreateProvider(settings);
    }

    /// <summary>
    /// Capabilities'leri logger'a yaz (startup logging)
    /// </summary>
    private void LogCapabilities()
    {
        Logger.LogInfo("=== Provider Capabilities ===");
        Logger.LogInfo($"Sesli Komut (STT) Zinciri:");
        foreach (var item in _fallbackStrategy.GetSpeechToTextChain())
            Logger.LogInfo($"  {item}");

        Logger.LogInfo($"Embedding (RAG) Zinciri:");
        foreach (var item in _fallbackStrategy.GetEmbeddingChain())
            Logger.LogInfo($"  {item}");
    }
}
