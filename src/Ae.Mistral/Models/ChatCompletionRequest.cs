using System.Text.Json.Serialization;

namespace Ae.Mistral.Models;

public sealed record ChatCompletionRequest
{
    public required string Model { get; init; }

    public required IReadOnlyList<ChatCompletionRequestMessage> Messages { get; init; }

    public double? Temperature { get; init; }

    [JsonPropertyName("top_p")]
    public double? TopP { get; init; }

    [JsonPropertyName("max_tokens")]
    public int? MaxTokens { get; init; }

    public bool Stream { get; init; }

    public IReadOnlyList<string>? Stop { get; init; }

    public IReadOnlyList<Tool>? Tools { get; init; }

    [JsonPropertyName("tool_choice")]
    public ToolChoiceValue? ToolChoice { get; init; }

    [JsonPropertyName("random_seed")]
    public int? RandomSeed { get; init; }
}
