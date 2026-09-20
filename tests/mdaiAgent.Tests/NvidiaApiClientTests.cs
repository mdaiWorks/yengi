using System;
using System.Net;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using System.Text.Json;
using Xunit;

namespace mdaiAgent.Tests;

class FakeHandler : HttpMessageHandler
{
    private int _count = 0;
    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, System.Threading.CancellationToken cancellationToken)
    {
        _count++;
        if (_count < 3)
        {
            var resp = new HttpResponseMessage(HttpStatusCode.InternalServerError)
            {
                Content = new StringContent("{\"error\":\"temp\"}")
            };
            return Task.FromResult(resp);
        }
        else
        {
            var body = JsonSerializer.Serialize(new { choices = new[] { new { index = 0, message = new { role = "assistant", content = "ok" } } } });
            var resp = new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(body)
            };
            return Task.FromResult(resp);
        }
    }
}

public class NvidiaApiClientTests
{
    [Fact]
    public async Task SendChatWithTools_RetriesAndSucceeds()
    {
        var handler = new FakeHandler();
        var client = new HttpClient(handler);
        var settings = new AppSettings { ApiKey = "test", BaseUrl = "https://example.com", Model = "m" };
        var api = new NvidiaApiClient(settings, client);

        var messages = new System.Collections.Generic.List<ExtendedChatMessage>();
        var tools = new System.Collections.Generic.List<ToolDefinition>();

        var res = await api.SendChatWithToolsAsync(messages, tools);
        Assert.NotNull(res);
        Assert.NotEmpty(res.Choices);
        Assert.Equal("ok", res.Choices[0].Message.Content);
    }

    [Fact]
    public async Task SendChatWithTools_UsesLocalModelWhenConfigured()
    {
        var handler = new RecordingHandler(_ => new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent("{\"choices\":[{\"index\":0,\"message\":{\"role\":\"assistant\",\"content\":\"local\"}}]}")
        });
        var client = new HttpClient(handler);
        var settings = new AppSettings
        {
            UseLocalModel = true,
            LocalBaseUrl = "http://localhost:11434/v1",
            LocalModel = "qwen2.5-coder:7b"
        };
        var api = new NvidiaApiClient(settings, client);

        var res = await api.SendChatWithToolsAsync(new System.Collections.Generic.List<ExtendedChatMessage>(), new System.Collections.Generic.List<ToolDefinition>());

        Assert.NotNull(res);
        Assert.Equal("local", res.Choices![0].Message!.Content);
        Assert.Equal("qwen2.5-coder:7b", handler.LastModel);
        Assert.Equal("http://localhost:11434/v1/chat/completions", handler.LastRequestUri);
    }

    [Fact]
    public async Task SendChatWithTools_UsesConfiguredTimeout()
    {
        var handler = new SlowHandler();
        var client = new HttpClient(handler);
        var settings = new AppSettings
        {
            ApiKey = "test",
            BaseUrl = "https://example.com",
            Model = "m",
            ApiTimeoutSeconds = 1
        };
        var api = new NvidiaApiClient(settings, client);

        var ex = await Assert.ThrowsAsync<Exception>(() => api.SendChatWithToolsAsync(new System.Collections.Generic.List<ExtendedChatMessage>(), new System.Collections.Generic.List<ToolDefinition>()));

        Assert.Contains("1 saniye", ex.Message, StringComparison.OrdinalIgnoreCase);
    }
}

internal sealed class RecordingHandler : HttpMessageHandler
{
    private readonly Func<HttpRequestMessage, HttpResponseMessage> _responseFactory;

    public RecordingHandler(Func<HttpRequestMessage, HttpResponseMessage> responseFactory)
    {
        _responseFactory = responseFactory;
    }

    public string? LastModel { get; private set; }
    public string? LastRequestUri { get; private set; }

    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        LastRequestUri = request.RequestUri?.ToString();
        var body = await request.Content!.ReadAsStringAsync(cancellationToken);
        var doc = JsonDocument.Parse(body);
        LastModel = doc.RootElement.GetProperty("model").GetString();
        return _responseFactory(request);
    }
}

internal sealed class SlowHandler : HttpMessageHandler
{
    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        await Task.Delay(TimeSpan.FromSeconds(2), cancellationToken);
        return new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent("{\"choices\":[{\"index\":0,\"message\":{\"role\":\"assistant\",\"content\":\"late\"}}]}")
        };
    }
}
