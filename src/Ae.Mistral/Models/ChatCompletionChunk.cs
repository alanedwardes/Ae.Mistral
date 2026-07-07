using System.Text;
using System.Text.Json;
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

    [JsonConverter(typeof(DeltaContentConverter))]
    public string? Content { get; init; }

    [JsonPropertyName("tool_calls")]
    public IReadOnlyList<ToolCall>? ToolCalls { get; init; }
}

public sealed class DeltaContentConverter : JsonConverter<string?>
{
    public override string? Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        if (reader.TokenType == JsonTokenType.Null)
        {
            return null;
        }

        if (reader.TokenType == JsonTokenType.String)
        {
            return reader.GetString();
        }

        var chunks = JsonSerializer.Deserialize<List<ContentChunk>>(ref reader, options) ?? [];

        var builder = new StringBuilder();
        foreach (var chunk in chunks)
        {
            if (chunk is TextChunk textChunk)
            {
                builder.Append(textChunk.Text);
            }
        }

        return builder.ToString();
    }

    public override void Write(Utf8JsonWriter writer, string? value, JsonSerializerOptions options)
    {
        if (value is null)
        {
            writer.WriteNullValue();
        }
        else
        {
            writer.WriteStringValue(value);
        }
    }
}
