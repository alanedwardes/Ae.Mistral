using Ae.Mistral;
using Microsoft.Extensions.AI;

var apiKey = Environment.GetEnvironmentVariable("MISTRAL_API_KEY")
    ?? throw new InvalidOperationException("Set the MISTRAL_API_KEY environment variable.");

IChatClient client = new MistralChatClient(new MistralClient(apiKey), MistralModels.MistralSmallLatest);

Console.WriteLine("--- Plain chat (non-streaming) ---");
var plainResponse = await client.GetResponseAsync([new ChatMessage(ChatRole.User, "Say hello in one short sentence.")]);
Console.WriteLine(plainResponse.Text);

Console.WriteLine();
Console.WriteLine("--- Streaming chat (tokens print as they arrive) ---");
await foreach (var update in client.GetStreamingResponseAsync([new ChatMessage(ChatRole.User, "Count from one to five, one number per word.")]))
{
    Console.Write(update.Text);
}
Console.WriteLine();

Console.WriteLine();
Console.WriteLine("--- Tool calling round trip ---");
var getWeatherTool = AIFunctionFactory.Create(
    (string city) => $"It's sunny and 21C in {city}.",
    name: "get_weather",
    description: "Gets the current weather for a city.");

var messages = new List<ChatMessage> { new(ChatRole.User, "What's the weather in Paris?") };
var options = new ChatOptions { Tools = [getWeatherTool] };

var response = await client.GetResponseAsync(messages, options);
messages.AddRange(response.Messages);

var functionCall = response.Messages.SelectMany(m => m.Contents).OfType<FunctionCallContent>().FirstOrDefault();
if (functionCall is not null)
{
    var argsText = functionCall.Arguments is { } callArgs ? string.Join(", ", callArgs.Select(kv => $"{kv.Key}={kv.Value}")) : "";
    Console.WriteLine($"Model called: {functionCall.Name}({argsText})");
    var result = await getWeatherTool.InvokeAsync(new AIFunctionArguments(functionCall.Arguments));
    messages.Add(new ChatMessage(ChatRole.Tool, [new FunctionResultContent(functionCall.CallId, result)]));

    var finalResponse = await client.GetResponseAsync(messages, options);
    Console.WriteLine(finalResponse.Text);
}
else
{
    Console.WriteLine("Model answered directly: " + response.Text);
}

Console.WriteLine();
Console.WriteLine("Done.");
