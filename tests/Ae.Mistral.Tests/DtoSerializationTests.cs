using System.Text.Json;
using Ae.Mistral.Json;
using Ae.Mistral.Models;
using Xunit;

namespace Ae.Mistral.Tests;

public class DtoSerializationTests
{
    private static string Serialize(ChatCompletionRequestMessage message)
    {
        var request = new ChatCompletionRequest
        {
            Model = "mistral-large-latest",
            Messages = [message],
        };
        return JsonSerializer.Serialize(request, MistralJsonContext.Default.ChatCompletionRequest);
    }

    [Fact]
    public void SystemMessage_AlwaysSerializesRole()
    {
        var json = Serialize(new SystemMessage { Content = "You are a helpful assistant." });
        Assert.Contains("\"role\":\"system\"", json);
    }

    [Fact]
    public void UserMessage_AlwaysSerializesRole()
    {
        var json = Serialize(new UserMessage { Content = "Hello" });
        Assert.Contains("\"role\":\"user\"", json);
    }

    [Fact]
    public void AssistantMessage_AlwaysSerializesRole()
    {
        var json = Serialize(new AssistantMessage { Content = new MessageContent("Hi there") });
        Assert.Contains("\"role\":\"assistant\"", json);
    }

    [Fact]
    public void AssistantMessage_WithNoContent_StillSerializesRole()
    {
        var json = Serialize(new AssistantMessage { Content = null });
        Assert.Contains("\"role\":\"assistant\"", json);
    }

    [Fact]
    public void ToolMessage_AlwaysSerializesRole()
    {
        var json = Serialize(new ToolMessage { Content = "42", ToolCallId = "call_1" });
        Assert.Contains("\"role\":\"tool\"", json);
    }

    [Fact]
    public void UserMessage_WithImageContent_SerializesAsChunkArray()
    {
        var json = Serialize(new UserMessage
        {
            Content = new MessageContent([new ImageUrlChunk { ImageUrl = new ImageUrl { Url = "https://example.com/x.png" } }]),
        });

        Assert.Contains("\"type\":\"image_url\"", json);
        Assert.Contains("https://example.com/x.png", json);
    }

    [Fact]
    public void ToolChoice_NamedFunction_SerializesAsObject()
    {
        var request = new ChatCompletionRequest
        {
            Model = "mistral-large-latest",
            Messages = [new UserMessage { Content = "hi" }],
            ToolChoice = ToolChoiceValue.FromFunctionName("get_weather"),
        };

        var json = JsonSerializer.Serialize(request, MistralJsonContext.Default.ChatCompletionRequest);

        Assert.Contains("\"tool_choice\":{\"type\":\"function\",\"function\":{\"name\":\"get_weather\"}}", json);
    }

    [Fact]
    public void ToolChoice_Enum_SerializesAsBareString()
    {
        var request = new ChatCompletionRequest
        {
            Model = "mistral-large-latest",
            Messages = [new UserMessage { Content = "hi" }],
            ToolChoice = ToolChoiceValue.FromEnum("none"),
        };

        var json = JsonSerializer.Serialize(request, MistralJsonContext.Default.ChatCompletionRequest);

        Assert.Contains("\"tool_choice\":\"none\"", json);
    }
}
