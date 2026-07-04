using Ae.Mistral.Models;
using Microsoft.Extensions.AI;
using Xunit;

namespace Ae.Mistral.Tests;

public class MistralChatClientTests
{
    [Fact]
    public async Task GetResponseAsync_SendsUserMessageWithRole()
    {
        var handler = FakeHttpMessageHandler.WithJsonResponse(
            "{\"id\":\"abc\",\"model\":\"m\",\"choices\":[{\"index\":0,\"message\":{\"role\":\"assistant\",\"content\":\"Hi!\"},\"finish_reason\":\"stop\"}]}");
        using var httpClient = handler.ToHttpClient();
        IChatClient client = new MistralChatClient(new MistralClient("test-key", httpClient), "mistral-small-latest");

        var response = await client.GetResponseAsync([new ChatMessage(ChatRole.User, "Hello")]);

        Assert.Contains("\"role\":\"user\"", handler.LastRequestBody);
        Assert.Equal("Hi!", response.Text);
        Assert.Equal(ChatFinishReason.Stop, response.FinishReason);
    }

    [Fact]
    public async Task GetResponseAsync_ParsesToolCallWithJsonEncodedStringArguments()
    {
        var handler = FakeHttpMessageHandler.WithJsonResponse(
            """
            {"id":"abc","model":"m","choices":[{"index":0,"message":{"role":"assistant","tool_calls":[{"id":"call_1","type":"function","function":{"name":"get_weather","arguments":"{\"city\": \"Paris\"}"}}]},"finish_reason":"tool_calls"}]}
            """);
        using var httpClient = handler.ToHttpClient();
        IChatClient client = new MistralChatClient(new MistralClient("test-key", httpClient), "mistral-small-latest");

        var response = await client.GetResponseAsync([new ChatMessage(ChatRole.User, "weather in paris?")]);

        var call = response.Messages.SelectMany(m => m.Contents).OfType<FunctionCallContent>().Single();
        Assert.Equal("get_weather", call.Name);
        Assert.NotNull(call.Arguments);
        Assert.Equal("Paris", call.Arguments!["city"]!.ToString()!.Trim('"'));
        Assert.Equal(ChatFinishReason.ToolCalls, response.FinishReason);
    }

    [Fact]
    public async Task GetStreamingResponseAsync_YieldsIncrementalTextUpdates()
    {
        const string sse =
            "data: {\"id\":\"abc\",\"model\":\"m\",\"choices\":[{\"index\":0,\"delta\":{\"role\":\"assistant\",\"content\":\"Hel\"},\"finish_reason\":null}]}\n\n" +
            "data: {\"id\":\"abc\",\"model\":\"m\",\"choices\":[{\"index\":0,\"delta\":{\"content\":\"lo\"},\"finish_reason\":\"stop\"}]}\n\n" +
            "data: [DONE]\n\n";

        using var httpClient = FakeHttpMessageHandler.WithSseResponse(sse).ToHttpClient();
        IChatClient client = new MistralChatClient(new MistralClient("test-key", httpClient), "mistral-small-latest");

        var updates = new List<ChatResponseUpdate>();
        await foreach (var update in client.GetStreamingResponseAsync([new ChatMessage(ChatRole.User, "hi")]))
        {
            updates.Add(update);
        }

        Assert.Equal(2, updates.Count);
        Assert.Equal("Hel", updates[0].Text);
        Assert.Equal("lo", updates[1].Text);
        Assert.Equal(ChatFinishReason.Stop, updates[1].FinishReason);
    }

    [Fact]
    public async Task GetStreamingResponseAsync_AccumulatesToolCallAndEmitsOnFinish()
    {
        const string sse =
            "data: {\"id\":\"abc\",\"model\":\"m\",\"choices\":[{\"index\":0,\"delta\":{\"tool_calls\":[{\"id\":\"call_1\",\"type\":\"function\",\"function\":{\"name\":\"get_weather\",\"arguments\":\"{\\\"city\\\": \\\"Paris\\\"}\"},\"index\":0}]},\"finish_reason\":\"tool_calls\"}]}\n\n" +
            "data: [DONE]\n\n";

        using var httpClient = FakeHttpMessageHandler.WithSseResponse(sse).ToHttpClient();
        IChatClient client = new MistralChatClient(new MistralClient("test-key", httpClient), "mistral-small-latest");

        var updates = new List<ChatResponseUpdate>();
        await foreach (var update in client.GetStreamingResponseAsync([new ChatMessage(ChatRole.User, "weather?")]))
        {
            updates.Add(update);
        }

        var call = updates.SelectMany(u => u.Contents).OfType<FunctionCallContent>().Single();
        Assert.Equal("get_weather", call.Name);
        Assert.Equal("Paris", call.Arguments!["city"]!.ToString()!.Trim('"'));
    }

    [Fact]
    public async Task GetResponseAsync_EchoesAssistantToolCallHistoryWithRole()
    {
        var handler = FakeHttpMessageHandler.WithJsonResponse(
            "{\"id\":\"abc\",\"model\":\"m\",\"choices\":[{\"index\":0,\"message\":{\"role\":\"assistant\",\"content\":\"Sunny.\"},\"finish_reason\":\"stop\"}]}");
        using var httpClient = handler.ToHttpClient();
        IChatClient client = new MistralChatClient(new MistralClient("test-key", httpClient), "mistral-small-latest");

        var history = new List<ChatMessage>
        {
            new(ChatRole.User, "weather in paris?"),
            new(ChatRole.Assistant, [new FunctionCallContent("call_1", "get_weather", new Dictionary<string, object?> { ["city"] = "Paris" })]),
            new(ChatRole.Tool, [new FunctionResultContent("call_1", "Sunny, 21C")]),
        };

        await client.GetResponseAsync(history);

        Assert.Contains("\"role\":\"assistant\"", handler.LastRequestBody);
        Assert.Contains("\"role\":\"tool\"", handler.LastRequestBody);
        Assert.Contains("\"tool_call_id\":\"call_1\"", handler.LastRequestBody);
    }

    [Fact]
    public async Task GetResponseAsync_RawRepresentationFactory_FieldsWinOverChatOptions()
    {
        var handler = FakeHttpMessageHandler.WithJsonResponse(
            "{\"id\":\"abc\",\"model\":\"m\",\"choices\":[{\"index\":0,\"message\":{\"role\":\"assistant\",\"content\":\"Hi!\"},\"finish_reason\":\"stop\"}]}");
        using var httpClient = handler.ToHttpClient();
        IChatClient client = new MistralChatClient(new MistralClient("test-key", httpClient), "mistral-small-latest");

        var options = new ChatOptions
        {
            Temperature = 0.9f,
            RawRepresentationFactory = _ => new ChatCompletionRequest
            {
                Model = "mistral-large-latest",
                Messages = [],
                Temperature = 0.1,
            },
        };

        await client.GetResponseAsync([new ChatMessage(ChatRole.User, "Hello")], options);

        Assert.Contains("\"temperature\":0.1", handler.LastRequestBody);
        Assert.Contains("\"model\":\"mistral-large-latest\"", handler.LastRequestBody);
        Assert.Contains("\"role\":\"user\"", handler.LastRequestBody);
        Assert.Contains("Hello", handler.LastRequestBody);
    }
}
