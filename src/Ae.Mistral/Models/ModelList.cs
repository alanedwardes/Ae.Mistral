using System.Text.Json.Serialization;

namespace Ae.Mistral.Models;

public sealed class ModelList
{
    public required IReadOnlyList<ModelInfo> Data { get; init; }
}

public sealed class ModelInfo
{
    public required string Id { get; init; }

    public long Created { get; init; }

    [JsonPropertyName("owned_by")]
    public string? OwnedBy { get; init; }

    public string? Name { get; init; }

    public string? Description { get; init; }

    [JsonPropertyName("max_context_length")]
    public int? MaxContextLength { get; init; }

    public ModelCapabilities? Capabilities { get; init; }
}

public sealed class ModelCapabilities
{
    [JsonPropertyName("completion_chat")]
    public bool CompletionChat { get; init; }

    [JsonPropertyName("completion_fim")]
    public bool CompletionFim { get; init; }

    [JsonPropertyName("function_calling")]
    public bool FunctionCalling { get; init; }

    [JsonPropertyName("fine_tuning")]
    public bool FineTuning { get; init; }

    public bool Vision { get; init; }
}
