using System.Text.Json.Serialization;
using Ae.Mistral.Models;

namespace Ae.Mistral.Json;

[JsonSourceGenerationOptions(
    PropertyNamingPolicy = JsonKnownNamingPolicy.SnakeCaseLower,
    DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull)]
[JsonSerializable(typeof(ChatCompletionRequest))]
[JsonSerializable(typeof(ChatCompletionResponse))]
[JsonSerializable(typeof(ChatCompletionChunk))]
[JsonSerializable(typeof(IReadOnlyList<ContentChunk>))]
[JsonSerializable(typeof(List<ContentChunk>))]
[JsonSerializable(typeof(ModelList))]
internal sealed partial class MistralJsonContext : JsonSerializerContext
{
}
