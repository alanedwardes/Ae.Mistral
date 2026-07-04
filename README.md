# Ae.Mistral

A [`Microsoft.Extensions.AI`](https://learn.microsoft.com/en-us/dotnet/ai/microsoft-extensions-ai) `IChatClient` implementation for the [Mistral](https://mistral.ai) chat completions API: hand-written DTOs, real token-by-token SSE streaming, and tool calling.

## Usage

```csharp
using Ae.Mistral;
using Microsoft.Extensions.AI;

IChatClient client = new MistralChatClient(new MistralClient(apiKey), MistralModels.MistralLargeLatest);

await foreach (var update in client.GetStreamingResponseAsync(
    [new ChatMessage(ChatRole.User, "Say hello in one short sentence.")]))
{
    Console.Write(update.Text);
}
```

See `samples/ChatSample` for a fuller example including tool calling.

## Testing

`dotnet test` runs the unit test suite (no network access required). A separate set of integration
tests under `tests/Ae.Mistral.Tests/Integration/` hits the live Mistral API and needs an API key,
set via user-secrets, and run with the `Integration` category included:

```sh
dotnet user-secrets set "mistral:apiKey" "<your-key>" --project tests/Ae.Mistral.Tests
dotnet test --filter "Category=Integration"
```

CI excludes the `Integration` category since it has no secret configured.

## Status

Early, built for internal use, not yet published to NuGet.
