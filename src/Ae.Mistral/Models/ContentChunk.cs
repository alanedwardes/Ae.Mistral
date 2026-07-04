using System.Text.Json.Serialization;

namespace Ae.Mistral.Models;

[JsonPolymorphic(TypeDiscriminatorPropertyName = "type", UnknownDerivedTypeHandling = JsonUnknownDerivedTypeHandling.FailSerialization)]
[JsonDerivedType(typeof(TextChunk), "text")]
[JsonDerivedType(typeof(ImageUrlChunk), "image_url")]
public abstract class ContentChunk
{
}

public sealed class TextChunk : ContentChunk
{
    public required string Text { get; init; }
}

public sealed class ImageUrlChunk : ContentChunk
{
    [JsonPropertyName("image_url")]
    public required ImageUrl ImageUrl { get; init; }
}

public sealed class ImageUrl
{
    public required string Url { get; init; }
}
