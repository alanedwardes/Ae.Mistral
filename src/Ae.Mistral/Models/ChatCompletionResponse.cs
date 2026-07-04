using System.Text.Json.Serialization;

namespace Ae.Mistral.Models;

public sealed class ChatCompletionResponse
{
    public required string Id { get; init; }

    public required string Model { get; init; }

    public required IReadOnlyList<ChatCompletionChoice> Choices { get; init; }

    public Usage? Usage { get; init; }
}

public sealed class ChatCompletionChoice
{
    public int Index { get; init; }

    public required AssistantMessage Message { get; init; }

    [JsonPropertyName("finish_reason")]
    public string? FinishReason { get; init; }
}

public sealed class Usage
{
    [JsonPropertyName("prompt_tokens")]
    public int PromptTokens { get; init; }

    [JsonPropertyName("completion_tokens")]
    public int CompletionTokens { get; init; }

    [JsonPropertyName("total_tokens")]
    public int TotalTokens { get; init; }
}
