using System.Text.Json;
using System.Text.Json.Serialization;

namespace Ae.Mistral.Models;

public sealed class Tool
{
    public string Type { get; init; } = "function";

    public required Function Function { get; init; }
}

public sealed class Function
{
    public required string Name { get; init; }

    public string Description { get; init; } = "";

    public required JsonElement Parameters { get; init; }
}

public sealed class ToolCall
{
    public string? Id { get; init; }

    public string Type { get; init; } = "function";

    public required FunctionCall Function { get; init; }

    public int? Index { get; init; }
}

public sealed class FunctionCall
{
    public required string Name { get; init; }

    public required JsonElement Arguments { get; init; }
}

public sealed class FunctionName
{
    public required string Name { get; init; }
}

[JsonConverter(typeof(ToolChoiceConverter))]
public readonly struct ToolChoiceValue
{
    public string? Enum { get; }
    public string? FunctionName { get; }

    private ToolChoiceValue(string? enumValue, string? functionName)
    {
        Enum = enumValue;
        FunctionName = functionName;
    }

    public static ToolChoiceValue FromEnum(string value) => new(value, null);
    public static ToolChoiceValue FromFunctionName(string name) => new(null, name);
}

public sealed class ToolChoiceConverter : JsonConverter<ToolChoiceValue>
{
    public override ToolChoiceValue Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        if (reader.TokenType == JsonTokenType.String)
        {
            return ToolChoiceValue.FromEnum(reader.GetString() ?? "auto");
        }

        using var document = JsonDocument.ParseValue(ref reader);
        var name = document.RootElement.GetProperty("function").GetProperty("name").GetString()!;
        return ToolChoiceValue.FromFunctionName(name);
    }

    public override void Write(Utf8JsonWriter writer, ToolChoiceValue value, JsonSerializerOptions options)
    {
        if (value.FunctionName is not null)
        {
            writer.WriteStartObject();
            writer.WriteString("type", "function");
            writer.WritePropertyName("function");
            writer.WriteStartObject();
            writer.WriteString("name", value.FunctionName);
            writer.WriteEndObject();
            writer.WriteEndObject();
        }
        else
        {
            writer.WriteStringValue(value.Enum ?? "auto");
        }
    }
}
