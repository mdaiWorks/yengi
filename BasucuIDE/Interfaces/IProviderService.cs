using System.Threading.Tasks;

namespace mdaiAgent;

/// <summary>
/// Üst seviye provider yönetimi sağlar.
/// Seçili provider'ı döndürür, fallback seçer, yetenekleri kontrol eder.
/// 
/// Tasarım Felsefesi:
/// - Sesli Komut ve RAG, bu service'i kullanarak otomatik provider seçer
/// - Kullanıcı extra API key yazmasına gerek yok
/// - Smart fallback: Groq (STT) → OpenAI; Ollama (Embedding) → OpenAI
/// </summary>
public interface IProviderService
{
    /// <summary>
    /// Kullanıcının seçtiği aktif provider'ı döndürür
    /// </summary>
    IAiProvider GetSelected();

    /// <summary>
    /// Belirtilen yeteneği destekleyen provider'ı seçer
    /// </summary>
    IAiProvider SelectByCapability(ProviderCapability capability);

    /// <summary>
    /// Fallback chain'de bir sonraki provider'ı seçer
    /// </summary>
    IAiProvider SelectFallback(ProviderCapability capability);

    /// <summary>
    /// Seçili provider'ın belirtilen yeteneği destekleyip desteklemediğini kontrol eder
    /// </summary>
    bool SupportsCapability(ProviderCapability capability);

    /// <summary>
    /// Fallback provider'ın adını döndürür (logging için)
    /// </summary>
    string GetFallbackProviderName(ProviderCapability capability);

    /// <summary>
    /// Tüm kullanılabilir provider'ları döndürür
    /// </summary>
    List<IAiProvider> GetAvailableProviders();

    /// <summary>
    /// Provider'ın yeteneklerini döndürür
    /// </summary>
    ProviderCapability GetCapabilities(IAiProvider provider);
}
