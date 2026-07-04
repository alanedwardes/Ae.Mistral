using Ae.Mistral;
using Microsoft.Extensions.AI;
using Xunit;

namespace Ae.Mistral.Tests.Integration;

[Trait("Category", "Integration")]
public class MistralChatClientIntegrationTests
{
    private const string Model = MistralModels.MistralSmallLatest;

    private static IChatClient CreateClient() =>
        new MistralChatClient(new MistralClient(MistralApiFixture.RequireApiKey()), Model);

    [Fact]
    public async Task GetResponseAsync_ReturnsRealAssistantReply()
    {
        var client = CreateClient();

        var response = await client.GetResponseAsync([new ChatMessage(ChatRole.User, "Reply with exactly the word: pong")]);

        Assert.Contains("pong", response.Text, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task GetStreamingResponseAsync_YieldsMultipleIncrementalUpdates()
    {
        var client = CreateClient();

        var updates = new List<ChatResponseUpdate>();
        await foreach (var update in client.GetStreamingResponseAsync(
            [new ChatMessage(ChatRole.User, "Write a sentence with at least ten words about trains.")]))
        {
            updates.Add(update);
        }

        var textUpdates = updates.Where(u => !string.IsNullOrEmpty(u.Text)).ToList();
        Assert.True(textUpdates.Count > 1, "Expected multiple incremental text updates, not one buffered response.");
        Assert.Contains(updates, u => u.FinishReason is not null);

        var fullText = string.Concat(textUpdates.Select(u => u.Text));
        Assert.Contains("train", fullText, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task ToolCallingRoundTrip_ProducesFinalAnswerFromToolResult()
    {
        var client = CreateClient();

        var getWeatherTool = AIFunctionFactory.Create(
            (string city) => $"It is sunny and 21C in {city}.",
            name: "get_weather",
            description: "Gets the current weather for a city.");

        var messages = new List<ChatMessage> { new(ChatRole.User, "What's the weather in Paris? You must use the tool.") };
        var options = new ChatOptions { Tools = [getWeatherTool] };

        var response = await client.GetResponseAsync(messages, options);
        messages.AddRange(response.Messages);

        var functionCall = response.Messages.SelectMany(m => m.Contents).OfType<FunctionCallContent>().Single();
        Assert.Equal("get_weather", functionCall.Name);
        Assert.NotNull(functionCall.Arguments);
        Assert.Contains("paris", functionCall.Arguments!["city"]!.ToString(), StringComparison.OrdinalIgnoreCase);

        var toolResult = await getWeatherTool.InvokeAsync(new AIFunctionArguments(functionCall.Arguments));
        messages.Add(new ChatMessage(ChatRole.Tool, [new FunctionResultContent(functionCall.CallId, toolResult)]));

        var finalResponse = await client.GetResponseAsync(messages, options);

        Assert.Contains("21", finalResponse.Text);
    }
}
