using System.Text.Json;
using System.Text.Json.Serialization;

namespace Ae.Mistral.Models;

[JsonConverter(typeof(MessageContentConverter))]
public readonly struct MessageContent
{
    public string? Text { get; }
    public IReadOnlyList<ContentChunk>? Chunks { get; }

    public MessageContent(string text)
    {
        Text = text;
        Chunks = null;
    }

    public MessageContent(IReadOnlyList<ContentChunk> chunks)
    {
        Text = null;
        Chunks = chunks;
    }

    public static implicit operator MessageContent(string text) => new(text);
}

public sealed class MessageContentConverter : JsonConverter<MessageContent>
{
    public override MessageContent Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        if (reader.TokenType == JsonTokenType.String)
        {
            return new MessageContent(reader.GetString() ?? string.Empty);
        }

        var chunks = JsonSerializer.Deserialize<List<ContentChunk>>(ref reader, options) ?? [];
        return new MessageContent(chunks);
    }

    public override void Write(Utf8JsonWriter writer, MessageContent value, JsonSerializerOptions options)
    {
        if (value.Chunks is not null)
        {
            JsonSerializer.Serialize(writer, value.Chunks, options);
        }
        else
        {
            writer.WriteStringValue(value.Text ?? string.Empty);
        }
    }
}
