using System.Net;
using System.Text;
using Ae.Mistral.Models;
using Xunit;

namespace Ae.Mistral.Tests;

public class MistralClientTests
{
    private static ChatCompletionRequest SampleRequest() => new()
    {
        Model = "mistral-small-latest",
        Messages = [new UserMessage { Content = "hi" }],
    };

    [Fact]
    public async Task CreateChatCompletionStreamAsync_ParsesUnwrappedChunksAndStopsAtDone()
    {
        const string sse =
            "data: {\"id\":\"abc\",\"object\":\"chat.completion.chunk\",\"model\":\"mistral-small-latest\",\"choices\":[{\"index\":0,\"delta\":{\"role\":\"assistant\",\"content\":\"\"},\"finish_reason\":null}]}\n\n" +
            "data: {\"id\":\"abc\",\"object\":\"chat.completion.chunk\",\"model\":\"mistral-small-latest\",\"choices\":[{\"index\":0,\"delta\":{\"content\":\"Hello\"},\"finish_reason\":null}]}\n\n" +
            "data: {\"id\":\"abc\",\"object\":\"chat.completion.chunk\",\"model\":\"mistral-small-latest\",\"choices\":[{\"index\":0,\"delta\":{\"content\":\" there\"},\"finish_reason\":\"stop\"}],\"usage\":{\"prompt_tokens\":5,\"completion_tokens\":2,\"total_tokens\":7}}\n\n" +
            "data: [DONE]\n\n";

        using var httpClient = FakeHttpMessageHandler.WithSseResponse(sse).ToHttpClient();
        using var client = new MistralClient("test-key", httpClient);

        var chunks = new List<ChatCompletionChunk>();
        await foreach (var chunk in client.CreateChatCompletionStreamAsync(SampleRequest()))
        {
            chunks.Add(chunk);
        }

        Assert.Equal(3, chunks.Count);
        Assert.Equal("Hello", chunks[1].Choices[0].Delta.Content);
        Assert.Equal(" there", chunks[2].Choices[0].Delta.Content);
        Assert.Equal("stop", chunks[2].Choices[0].FinishReason);
        Assert.Equal(7, chunks[2].Usage!.TotalTokens);
    }

    [Fact]
    public async Task CreateChatCompletionStreamAsync_ToleratesUnknownExtraFields()
    {
        const string sse =
            "data: {\"id\":\"abc\",\"model\":\"m\",\"choices\":[{\"index\":0,\"delta\":{\"content\":\"hi\"},\"finish_reason\":null}],\"p\":\"abcdefgh\"}\n\n" +
            "data: [DONE]\n\n";

        using var httpClient = FakeHttpMessageHandler.WithSseResponse(sse).ToHttpClient();
        using var client = new MistralClient("test-key", httpClient);

        var chunks = new List<ChatCompletionChunk>();
        await foreach (var chunk in client.CreateChatCompletionStreamAsync(SampleRequest()))
        {
            chunks.Add(chunk);
        }

        Assert.Single(chunks);
        Assert.Equal("hi", chunks[0].Choices[0].Delta.Content);
    }

    [Fact]
    public async Task CreateChatCompletionStreamAsync_ParsesToolCallDeltaWithStringArguments()
    {
        const string sse =
            "data: {\"id\":\"abc\",\"model\":\"m\",\"choices\":[{\"index\":0,\"delta\":{\"tool_calls\":[{\"id\":\"call_1\",\"type\":\"function\",\"function\":{\"name\":\"get_weather\",\"arguments\":\"{\\\"city\\\": \\\"Paris\\\"}\"},\"index\":0}]},\"finish_reason\":\"tool_calls\"}]}\n\n" +
            "data: [DONE]\n\n";

        using var httpClient = FakeHttpMessageHandler.WithSseResponse(sse).ToHttpClient();
        using var client = new MistralClient("test-key", httpClient);

        var chunks = new List<ChatCompletionChunk>();
        await foreach (var chunk in client.CreateChatCompletionStreamAsync(SampleRequest()))
        {
            chunks.Add(chunk);
        }

        var toolCall = Assert.Single(chunks[0].Choices[0].Delta.ToolCalls!);
        Assert.Equal("get_weather", toolCall.Function.Name);
        Assert.Equal(System.Text.Json.JsonValueKind.String, toolCall.Function.Arguments.ValueKind);
        Assert.Equal("{\"city\": \"Paris\"}", toolCall.Function.Arguments.GetString());
    }

    [Fact]
    public async Task CreateChatCompletionAsync_DeserializesNonStreamingResponse()
    {
        const string json = "{\"id\":\"abc\",\"model\":\"mistral-small-latest\",\"choices\":[{\"index\":0,\"message\":{\"role\":\"assistant\",\"content\":\"Hi!\"},\"finish_reason\":\"stop\"}],\"usage\":{\"prompt_tokens\":3,\"completion_tokens\":2,\"total_tokens\":5}}";

        using var httpClient = FakeHttpMessageHandler.WithJsonResponse(json).ToHttpClient();
        using var client = new MistralClient("test-key", httpClient);

        var response = await client.CreateChatCompletionAsync(SampleRequest());

        Assert.Equal("abc", response.Id);
        Assert.Equal("Hi!", response.Choices[0].Message.Content!.Value.Text);
        Assert.Equal("stop", response.Choices[0].FinishReason);
        Assert.Equal(5, response.Usage!.TotalTokens);
    }

    [Fact]
    public async Task CreateChatCompletionAsync_SendsStreamFalse()
    {
        var handler = FakeHttpMessageHandler.WithJsonResponse(
            "{\"id\":\"abc\",\"model\":\"m\",\"choices\":[{\"index\":0,\"message\":{\"role\":\"assistant\"},\"finish_reason\":\"stop\"}]}");
        using var httpClient = handler.ToHttpClient();
        using var client = new MistralClient("test-key", httpClient);

        await client.CreateChatCompletionAsync(SampleRequest() with { Stream = true });

        Assert.Contains("\"stream\":false", handler.LastRequestBody);
    }

    [Fact]
    public async Task ListModelsAsync_DeserializesModelList()
    {
        const string json = "{\"object\":\"list\",\"data\":[{\"id\":\"mistral-small-latest\",\"object\":\"model\",\"created\":1234567890,\"owned_by\":\"mistralai\",\"capabilities\":{\"completion_chat\":true,\"completion_fim\":false,\"function_calling\":true,\"fine_tuning\":false,\"vision\":false}}]}";

        using var httpClient = FakeHttpMessageHandler.WithJsonResponse(json).ToHttpClient();
        using var client = new MistralClient("test-key", httpClient);

        var models = await client.ListModelsAsync();

        var model = Assert.Single(models);
        Assert.Equal("mistral-small-latest", model.Id);
        Assert.Equal("mistralai", model.OwnedBy);
        Assert.True(model.Capabilities!.CompletionChat);
        Assert.True(model.Capabilities!.FunctionCalling);
    }

    [Fact]
    public async Task CreateChatCompletionAsync_RetriesOnTooManyRequestsThenSucceeds()
    {
        var attempts = 0;
        var handler = new FakeHttpMessageHandler((_, _) =>
        {
            attempts++;
            if (attempts < 3)
            {
                return new HttpResponseMessage(HttpStatusCode.TooManyRequests)
                {
                    Headers = { RetryAfter = new System.Net.Http.Headers.RetryConditionHeaderValue(TimeSpan.Zero) },
                    Content = new StringContent("rate limited", Encoding.UTF8, "text/plain"),
                };
            }

            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(
                    "{\"id\":\"abc\",\"model\":\"m\",\"choices\":[{\"index\":0,\"message\":{\"role\":\"assistant\",\"content\":\"ok\"},\"finish_reason\":\"stop\"}]}",
                    Encoding.UTF8,
                    "application/json"),
            };
        });
        using var httpClient = handler.ToHttpClient();
        using var client = new MistralClient("test-key", httpClient);

        var response = await client.CreateChatCompletionAsync(SampleRequest());

        Assert.Equal(3, attempts);
        Assert.Equal("ok", response.Choices[0].Message.Content!.Value.Text);
    }

    [Fact]
    public async Task CreateChatCompletionAsync_ThrowsAfterExhaustingRetries()
    {
        var attempts = 0;
        var handler = new FakeHttpMessageHandler((_, _) =>
        {
            attempts++;
            return new HttpResponseMessage(HttpStatusCode.ServiceUnavailable)
            {
                Headers = { RetryAfter = new System.Net.Http.Headers.RetryConditionHeaderValue(TimeSpan.Zero) },
                Content = new StringContent("unavailable", Encoding.UTF8, "text/plain"),
            };
        });
        using var httpClient = handler.ToHttpClient();
        using var client = new MistralClient("test-key", httpClient, maxRetries: 2);

        await Assert.ThrowsAsync<MistralApiException>(() => client.CreateChatCompletionAsync(SampleRequest()));
        Assert.Equal(3, attempts);
    }

    [Fact]
    public async Task CreateChatCompletionAsync_DoesNotRetryWhenMaxRetriesIsZero()
    {
        var attempts = 0;
        var handler = new FakeHttpMessageHandler((_, _) =>
        {
            attempts++;
            return new HttpResponseMessage(HttpStatusCode.TooManyRequests)
            {
                Content = new StringContent("rate limited", Encoding.UTF8, "text/plain"),
            };
        });
        using var httpClient = handler.ToHttpClient();
        using var client = new MistralClient("test-key", httpClient, maxRetries: 0);

        await Assert.ThrowsAsync<MistralApiException>(() => client.CreateChatCompletionAsync(SampleRequest()));
        Assert.Equal(1, attempts);
    }
}
