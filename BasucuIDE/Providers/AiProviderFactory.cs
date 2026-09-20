namespace mdaiAgent;

public static class AiProviderFactory
{
    public static IAiProvider CreateProvider(AppSettings settings)
    {
        return settings.ActiveProvider switch
        {
            ProviderType.Anthropic => new AnthropicApiClient(settings),
            ProviderType.Google => new GeminiApiClient(settings),
            _ => new OpenAiCompatibleClient(settings)
        };
    }
}
