using System.Runtime.CompilerServices;
using System.Text;
using System.Text.Json;
using Ae.Mistral.Models;
using Microsoft.Extensions.AI;

namespace Ae.Mistral;

public sealed class MistralChatClient(MistralClient client, string defaultModelId) : IChatClient
{
    public void Dispose() => client.Dispose();

    public object? GetService(Type serviceType, object? serviceKey) =>
        serviceKey is not null ? null :
        serviceType == typeof(ChatClientMetadata) ? new ChatClientMetadata(nameof(MistralChatClient)) :
        serviceType.IsInstanceOfType(this) ? this :
        null;

    public async Task<ChatResponse> GetResponseAsync(
        IEnumerable<ChatMessage> messages, ChatOptions? options = null, CancellationToken cancellationToken = default)
    {
        var request = CreateRequest(messages, options);
        var response = await client.CreateChatCompletionAsync(request, cancellationToken).ConfigureAwait(false);

        var choice = response.Choices.Count > 0 ? response.Choices[0] : null;

        var responseMessage = new ChatMessage { Role = ChatRole.Assistant };
        if (choice is not null)
        {
            AddAssistantContents(responseMessage.Contents, choice.Message);
        }

        return new ChatResponse(responseMessage)
        {
            ResponseId = response.Id,
            ModelId = response.Model,
            FinishReason = choice is not null ? ToFinishReason(choice.FinishReason) : null,
            Usage = response.Usage is { } usage ? ToUsageDetails(usage) : null,
            RawRepresentation = response,
        };
    }

    public async IAsyncEnumerable<ChatResponseUpdate> GetStreamingResponseAsync(
        IEnumerable<ChatMessage> messages, ChatOptions? options = null, [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        var request = CreateRequest(messages, options);
        var toolCallBuilders = new Dictionary<int, ToolCallBuilder>();

        await foreach (var chunk in client.CreateChatCompletionStreamAsync(request, cancellationToken).ConfigureAwait(false))
        {
            var choice = chunk.Choices.Count > 0 ? chunk.Choices[0] : null;

            var update = new ChatResponseUpdate
            {
                ResponseId = chunk.Id,
                ModelId = chunk.Model,
                RawRepresentation = chunk,
                Role = ChatRole.Assistant,
            };

            if (choice is not null)
            {
                if (!string.IsNullOrEmpty(choice.Delta.Content))
                {
                    update.Contents.Add(new TextContent(choice.Delta.Content) { RawRepresentation = choice.Delta });
                }

                if (choice.Delta.ToolCalls is { Count: > 0 } deltaToolCalls)
                {
                    foreach (var tc in deltaToolCalls)
                    {
                        var index = tc.Index ?? 0;
                        if (!toolCallBuilders.TryGetValue(index, out var builder))
                        {
                            builder = new ToolCallBuilder();
                            toolCallBuilders[index] = builder;
                        }

                        if (!string.IsNullOrEmpty(tc.Id)) builder.Id = tc.Id;
                        if (!string.IsNullOrEmpty(tc.Function.Name)) builder.Name = tc.Function.Name;
                        AppendArguments(builder.Arguments, tc.Function.Arguments);
                    }
                }

                update.FinishReason = choice.FinishReason is not null ? ToFinishReason(choice.FinishReason) : null;
                if (choice.FinishReason is not null && toolCallBuilders.Count > 0)
                {
                    foreach (var builder in toolCallBuilders.Values)
                    {
                        var argsStr = builder.Arguments.ToString();
                        update.Contents.Add(new FunctionCallContent(
                            builder.Id ?? string.Empty, builder.Name ?? string.Empty,
                            !string.IsNullOrEmpty(argsStr) ? ParseArguments(argsStr) : null));
                    }
                    toolCallBuilders.Clear();
                }
            }

            if (chunk.Usage is { } usage)
            {
                update.Contents.Add(new UsageContent(ToUsageDetails(usage)) { RawRepresentation = usage });
            }

            yield return update;
        }
    }

    private ChatCompletionRequest CreateRequest(IEnumerable<ChatMessage> messages, ChatOptions? options)
    {
        var requestMessages = new List<ChatCompletionRequestMessage>();

        if (!string.IsNullOrWhiteSpace(options?.Instructions))
        {
            requestMessages.Add(new SystemMessage { Content = options!.Instructions! });
        }

        foreach (var message in messages)
        {
            requestMessages.AddRange(ToMistralMessages(message));
        }

        var rawRequest = options?.RawRepresentationFactory?.Invoke(this) as ChatCompletionRequest;

        var request = (rawRequest ?? new ChatCompletionRequest { Model = "", Messages = [] }) with
        {
            Model = rawRequest?.Model is { Length: > 0 } ? rawRequest.Model : options?.ModelId ?? defaultModelId,
            Messages = [.. rawRequest?.Messages ?? [], .. requestMessages],
            Temperature = rawRequest?.Temperature ?? options?.Temperature,
            TopP = rawRequest?.TopP ?? options?.TopP,
            MaxTokens = rawRequest?.MaxTokens ?? options?.MaxOutputTokens,
            Stop = rawRequest?.Stop ?? (options?.StopSequences is { Count: > 0 } stopSequences ? stopSequences.ToList() : null),
            RandomSeed = rawRequest?.RandomSeed ?? (options?.Seed is { } seed ? checked((int)seed) : null),
        };

        return ApplyTools(request, options);
    }

    private static ChatCompletionRequest ApplyTools(ChatCompletionRequest request, ChatOptions? options)
    {
        if (options is null) return request;

        if (request.Tools is null && options.Tools is { Count: > 0 } aiTools)
        {
            var tools = aiTools.Select(tool =>
            {
                if (tool is not AIFunction function)
                {
                    throw new NotSupportedException($"Tool type '{tool.GetType().Name}' is not supported by Mistral. Only function tools are supported.");
                }

                return new Tool
                {
                    Function = new Function
                    {
                        Name = function.Name,
                        Description = function.Description,
                        Parameters = function.JsonSchema.ValueKind is JsonValueKind.Null or JsonValueKind.Undefined
                            ? JsonDocument.Parse("{}").RootElement
                            : function.JsonSchema,
                    },
                };
            }).ToList();
            request = request with { Tools = tools };
        }

        if (request.ToolChoice is null)
        {
            ToolChoiceValue? toolChoice = options.ToolMode switch
            {
                NoneChatToolMode => ToolChoiceValue.FromEnum("none"),
                RequiredChatToolMode { RequiredFunctionName: { } name } => ToolChoiceValue.FromFunctionName(name),
                RequiredChatToolMode => ToolChoiceValue.FromEnum("any"),
                _ => null,
            };
            if (toolChoice is not null)
            {
                request = request with { ToolChoice = toolChoice };
            }
        }

        return request;
    }

    private static IEnumerable<ChatCompletionRequestMessage> ToMistralMessages(ChatMessage message)
    {
        if (message.Role == ChatRole.Tool)
        {
            var functionResults = message.Contents.OfType<FunctionResultContent>().ToList();
            if (functionResults.Count == 0)
            {
                yield return new ToolMessage { Content = ToResultString(null), ToolCallId = null };
                yield break;
            }

            foreach (var functionResult in functionResults)
            {
                yield return new ToolMessage
                {
                    Content = ToResultString(functionResult),
                    ToolCallId = functionResult.CallId,
                };
            }

            yield break;
        }

        yield return ToMistralMessage(message);
    }

    private static ChatCompletionRequestMessage ToMistralMessage(ChatMessage message)
    {
        if (message.Role == ChatRole.System)
        {
            return new SystemMessage { Content = string.Concat(message.Contents.OfType<TextContent>().Select(tc => tc.Text)) };
        }

        if (message.Role == ChatRole.Assistant)
        {
            var toolCalls = message.Contents.OfType<FunctionCallContent>().ToList();
            var text = string.Concat(message.Contents.OfType<TextContent>().Select(tc => tc.Text));

            return new AssistantMessage
            {
                Content = string.IsNullOrEmpty(text) ? (MessageContent?)null : new MessageContent(text),
                ToolCalls = toolCalls.Count > 0
                    ? toolCalls.Select(tc => new ToolCall
                    {
                        Id = tc.CallId,
                        Function = new FunctionCall
                        {
                            Name = tc.Name,
                            Arguments = tc.Arguments is { } args
                                ? JsonSerializer.SerializeToElement(SerializeArguments(args))
                                : JsonDocument.Parse("\"{}\"").RootElement,
                        },
                    }).ToList()
                    : null,
            };
        }

        var contents = new List<ContentChunk>();
        foreach (var item in message.Contents)
        {
            switch (item)
            {
                case TextContent textContent:
                    contents.Add(new TextChunk { Text = textContent.Text });
                    break;

                case DataContent dataContent when dataContent.HasTopLevelMediaType("image"):
                    if (dataContent.Uri is { } uri)
                    {
                        contents.Add(new ImageUrlChunk { ImageUrl = new ImageUrl { Url = uri.ToString() } });
                    }
                    else if (dataContent.Data is { } data)
                    {
                        var mediaType = dataContent.MediaType ?? "image/png";
                        contents.Add(new ImageUrlChunk
                        {
                            ImageUrl = new ImageUrl { Url = $"data:{mediaType};base64,{Convert.ToBase64String(data.ToArray())}" },
                        });
                    }
                    break;
            }
        }

        if (contents.Count == 1 && contents[0] is TextChunk singleText)
        {
            return new UserMessage { Content = singleText.Text };
        }

        return new UserMessage { Content = new MessageContent(contents) };
    }

    private static void AddAssistantContents(IList<AIContent> contents, AssistantMessage message)
    {
        var text = message.Content is { Text: { } t } ? t : null;
        if (!string.IsNullOrEmpty(text))
        {
            contents.Add(new TextContent(text) { RawRepresentation = message });
        }

        if (message.ToolCalls is { Count: > 0 } toolCalls)
        {
            foreach (var toolCall in toolCalls)
            {
                contents.Add(new FunctionCallContent(
                    callId: toolCall.Id ?? string.Empty,
                    name: toolCall.Function.Name,
                    arguments: ParseArguments(toolCall.Function.Arguments))
                {
                    RawRepresentation = toolCall,
                });
            }
        }
    }

    private static void AppendArguments(StringBuilder builder, JsonElement arguments)
    {
        if (arguments.ValueKind == JsonValueKind.String)
        {
            builder.Append(arguments.GetString());
        }
        else if (arguments.ValueKind is JsonValueKind.Object)
        {
            builder.Append(arguments.GetRawText());
        }
    }

    private static Dictionary<string, object?>? ParseArguments(JsonElement arguments)
    {
        if (arguments.ValueKind == JsonValueKind.Object)
        {
            return ToDictionary(arguments);
        }

        if (arguments.ValueKind == JsonValueKind.String)
        {
            return ParseArguments(arguments.GetString());
        }

        return null;
    }

    private static Dictionary<string, object?>? ParseArguments(string? argsString)
    {
        if (string.IsNullOrWhiteSpace(argsString) || argsString is "{}") return null;

        try
        {
            using var document = JsonDocument.Parse(argsString);
            return document.RootElement.ValueKind == JsonValueKind.Object ? ToDictionary(document.RootElement) : null;
        }
        catch (JsonException)
        {
            return null;
        }
    }

    private static Dictionary<string, object?> ToDictionary(JsonElement objectElement)
    {
        var dict = new Dictionary<string, object?>(StringComparer.Ordinal);
        foreach (var property in objectElement.EnumerateObject())
        {
            dict[property.Name] = property.Value.Clone();
        }
        return dict;
    }

    private static string ToResultString(FunctionResultContent? functionResult)
    {
        if (functionResult is null) return string.Empty;

        return functionResult.Result switch
        {
            JsonElement { ValueKind: JsonValueKind.String } je => je.GetString() ?? string.Empty,
            JsonElement je => je.GetRawText(),
            string text => text,
            not null => JsonSerializer.Serialize(functionResult.Result),
            null => functionResult.Exception?.Message ?? string.Empty,
        };
    }

    private static string SerializeArguments(IEnumerable<KeyValuePair<string, object?>> arguments)
    {
        var dict = new Dictionary<string, object?>(StringComparer.Ordinal);
        foreach (var (key, value) in arguments)
        {
            dict[key] = value;
        }
        return JsonSerializer.Serialize(dict);
    }

    private static ChatFinishReason? ToFinishReason(string? finishReason) =>
        finishReason switch
        {
            "stop" => ChatFinishReason.Stop,
            "length" => ChatFinishReason.Length,
            "model_length" => ChatFinishReason.Length,
            "tool_calls" => ChatFinishReason.ToolCalls,
            "error" => new ChatFinishReason("error"),
            _ => null,
        };

    private static UsageDetails ToUsageDetails(Usage usage) => new()
    {
        InputTokenCount = usage.PromptTokens,
        OutputTokenCount = usage.CompletionTokens,
        TotalTokenCount = usage.TotalTokens,
    };

    private sealed class ToolCallBuilder
    {
        public string? Id { get; set; }
        public string? Name { get; set; }
        public StringBuilder Arguments { get; } = new();
    }
}
