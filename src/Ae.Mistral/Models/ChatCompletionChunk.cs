using System.Text.Json.Serialization;

namespace Ae.Mistral.Models;

public sealed class ChatCompletionChunk
{
    public required string Id { get; init; }

    public required string Model { get; init; }

    public required IReadOnlyList<ChatCompletionStreamChoice> Choices { get; init; }

    public Usage? Usage { get; init; }
}

public sealed class ChatCompletionStreamChoice
{
    public int Index { get; init; }

    public required DeltaMessage Delta { get; init; }

    [JsonPropertyName("finish_reason")]
    public string? FinishReason { get; init; }
}

public sealed class DeltaMessage
{
    public string? Role { get; init; }

    public string? Content { get; init; }

    [JsonPropertyName("tool_calls")]
    public IReadOnlyList<ToolCall>? ToolCalls { get; init; }
}
