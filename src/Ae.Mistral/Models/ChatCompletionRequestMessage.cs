using System.Text.Json.Serialization;

namespace Ae.Mistral.Models;

[JsonPolymorphic(TypeDiscriminatorPropertyName = "role", UnknownDerivedTypeHandling = JsonUnknownDerivedTypeHandling.FailSerialization)]
[JsonDerivedType(typeof(SystemMessage), "system")]
[JsonDerivedType(typeof(UserMessage), "user")]
[JsonDerivedType(typeof(AssistantMessage), "assistant")]
[JsonDerivedType(typeof(ToolMessage), "tool")]
public abstract class ChatCompletionRequestMessage
{
}

public sealed class SystemMessage : ChatCompletionRequestMessage
{
    public required string Content { get; init; }
}

public sealed class UserMessage : ChatCompletionRequestMessage
{
    public required MessageContent Content { get; init; }
}

public sealed class AssistantMessage : ChatCompletionRequestMessage
{
    public MessageContent? Content { get; init; }

    [JsonPropertyName("tool_calls")]
    public IReadOnlyList<ToolCall>? ToolCalls { get; init; }
}

public sealed class ToolMessage : ChatCompletionRequestMessage
{
    public required string Content { get; init; }

    [JsonPropertyName("tool_call_id")]
    public string? ToolCallId { get; init; }
}
