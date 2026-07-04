using System.Text.Json;
using Ae.Mistral.Models;
using Xunit;

namespace Ae.Mistral.Tests.Integration;

[Trait("Category", "Integration")]
public class MistralClientIntegrationTests
{
    private const string Model = MistralModels.MistralSmallLatest;

    [Fact]
    public async Task CreateChatCompletionAsync_ReturnsRealResponse()
    {
        using var client = new MistralClient(MistralApiFixture.RequireApiKey());

        var response = await client.CreateChatCompletionAsync(new ChatCompletionRequest
        {
            Model = Model,
            MaxTokens = 20,
            Messages = [new UserMessage { Content = "Reply with exactly the word: pong" }],
        });

        Assert.NotEmpty(response.Choices);
        Assert.Contains("pong", response.Choices[0].Message.Content!.Value.Text, StringComparison.OrdinalIgnoreCase);
        Assert.NotNull(response.Usage);
        Assert.True(response.Usage!.TotalTokens > 0);
    }

    [Fact]
    public async Task CreateChatCompletionStreamAsync_YieldsMultipleChunksEndingInDone()
    {
        using var client = new MistralClient(MistralApiFixture.RequireApiKey());

        var request = new ChatCompletionRequest
        {
            Model = Model,
            MaxTokens = 40,
            Messages = [new UserMessage { Content = "Count from one to five, one number per word." }],
        };

        var chunks = new List<ChatCompletionChunk>();
        await foreach (var chunk in client.CreateChatCompletionStreamAsync(request))
        {
            chunks.Add(chunk);
        }

        Assert.True(chunks.Count > 1, "Expected more than one streamed chunk.");
        Assert.Contains(chunks, c => c.Choices[0].FinishReason is not null);
        Assert.Contains(chunks, c => c.Usage is not null);
    }

    [Fact]
    public async Task CreateChatCompletionAsync_ToolCall_ReturnsJsonEncodedStringArguments()
    {
        using var client = new MistralClient(MistralApiFixture.RequireApiKey());

        var response = await client.CreateChatCompletionAsync(new ChatCompletionRequest
        {
            Model = Model,
            Messages = [new UserMessage { Content = "What's the weather in Paris? You must use the get_weather tool." }],
            Tools =
            [
                new Tool
                {
                    Function = new Function
                    {
                        Name = "get_weather",
                        Description = "Gets the current weather for a city.",
                        Parameters = JsonSerializer.SerializeToElement(new
                        {
                            type = "object",
                            properties = new { city = new { type = "string" } },
                            required = new[] { "city" },
                        }),
                    },
                },
            ],
            ToolChoice = ToolChoiceValue.FromEnum("any"),
        });

        var toolCall = response.Choices[0].Message.ToolCalls?.SingleOrDefault();
        Assert.NotNull(toolCall);
        Assert.Equal("get_weather", toolCall!.Function.Name);

        Assert.Equal(JsonValueKind.String, toolCall.Function.Arguments.ValueKind);
        using var parsedArgs = JsonDocument.Parse(toolCall.Function.Arguments.GetString()!);
        Assert.Contains("paris", parsedArgs.RootElement.GetProperty("city").GetString(), StringComparison.OrdinalIgnoreCase);
    }
}
