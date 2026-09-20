using System;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Xunit;

namespace mdaiAgent.Tests;

public class NativeProviderTests
{
    [Fact]
    public async Task AnthropicStreaming_EmitsTextDeltas()
    {
        var handler = new StubHandler(_ => new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent(
                "event: content_block_delta\ndata: {\"type\":\"content_block_delta\",\"delta\":{\"type\":\"text_delta\",\"text\":\"Mer\"}}\n\n" +
                "data: {\"type\":\"content_block_delta\",\"delta\":{\"type\":\"text_delta\",\"text\":\"haba\"}}\n\n",
                Encoding.UTF8,
                "text/event-stream")
        });
        var settings = new AppSettings { AnthropicApiKey = "test-key", AnthropicBaseUrl = "https://api.test/v1", AnthropicModel = "claude-test" };
        var client = new AnthropicApiClient(settings, new HttpClient(handler));
        var output = new StringBuilder();

        var response = await client.SendChatWithToolsStreamAsync(new(), new(), value => output.Append(value));

        Assert.Equal("Merhaba", output.ToString());
        Assert.Equal("Merhaba", response!.Choices![0].Message!.Content);
        Assert.Equal("test-key", handler.LastRequest!.Headers.GetValues("x-api-key").Single());
        Assert.EndsWith("/v1/messages", handler.LastRequest.RequestUri!.ToString());
    }

    [Fact]
    public async Task AnthropicStreaming_ParsesToolUseBlocks()
    {
        var handler = new StubHandler(_ => new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent(
                "event: content_block_start\n" +
                "data: {\"type\":\"content_block_start\",\"index\":0,\"content_block\":{\"type\":\"tool_use\",\"id\":\"toolu_123\",\"name\":\"SearchCode\",\"input\":{}}}\n\n" +
                "event: content_block_delta\n" +
                "data: {\"type\":\"content_block_delta\",\"index\":0,\"delta\":{\"type\":\"input_json_delta\",\"partial_json\":\"{\\\"query\\\":\\\"test\\\"}\"}}\n\n" +
                "event: content_block_stop\n" +
                "data: {\"type\":\"content_block_stop\",\"index\":0}\n\n",
                Encoding.UTF8,
                "text/event-stream")
        });
        var settings = new AppSettings { AnthropicApiKey = "test-key", AnthropicBaseUrl = "https://api.test/v1", AnthropicModel = "claude-test" };
        var client = new AnthropicApiClient(settings, new HttpClient(handler));

        var response = await client.SendChatWithToolsStreamAsync(new(), new());

        var toolCall = Assert.Single(response!.Choices![0].Message!.ToolCalls!);
        Assert.Equal("toolu_123", toolCall.Id);
        Assert.Equal("SearchCode", toolCall.Function.Name);
        Assert.Equal("{\"query\":\"test\"}", toolCall.Function.Arguments);
    }

    [Fact]
    public async Task GeminiStreaming_EmitsTextDeltas()
    {
        var handler = new StubHandler(_ => new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent(
                "data: {\"candidates\":[{\"content\":{\"parts\":[{\"text\":\"Mer\"}]}}]}\n\n" +
                "data: {\"candidates\":[{\"content\":{\"parts\":[{\"text\":\"haba\"}]}}]}\n\n",
                Encoding.UTF8,
                "text/event-stream")
        });
        var settings = new AppSettings { GoogleApiKey = "test-key", GoogleBaseUrl = "https://generativelanguage.googleapis.com/v1beta/openai", GoogleModel = "gemini-test" };
        var client = new GeminiApiClient(settings, new HttpClient(handler));
        var output = new StringBuilder();

        var response = await client.SendChatWithToolsStreamAsync(new(), new(), value => output.Append(value));

        Assert.Equal("Merhaba", output.ToString());
        Assert.Equal("Merhaba", response!.Choices![0].Message!.Content);
        Assert.Contains("key=test-key", handler.LastRequest!.RequestUri!.Query);
        Assert.EndsWith(":streamGenerateContent?alt=sse&key=test-key", handler.LastRequest.RequestUri.ToString());
    }

    private sealed class StubHandler : HttpMessageHandler
    {
        private readonly Func<HttpRequestMessage, HttpResponseMessage> _factory;
        public HttpRequestMessage? LastRequest { get; private set; }

        public StubHandler(Func<HttpRequestMessage, HttpResponseMessage> factory) => _factory = factory;

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            LastRequest = request;
            return Task.FromResult(_factory(request));
        }
    }
}
