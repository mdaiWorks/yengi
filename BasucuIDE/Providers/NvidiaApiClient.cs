namespace mdaiAgent;

// Backward-compatible name for the OpenAI-compatible provider used by existing integrations.
public sealed class NvidiaApiClient : OpenAiCompatibleClient
{
    public NvidiaApiClient(AppSettings settings, System.Net.Http.HttpClient? httpClient = null)
        : base(settings, httpClient)
    {
    }
}
