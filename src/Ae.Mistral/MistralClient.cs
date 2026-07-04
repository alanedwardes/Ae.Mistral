using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Net.ServerSentEvents;
using System.Runtime.CompilerServices;
using System.Text.Json;
using Ae.Mistral.Json;
using Ae.Mistral.Models;

namespace Ae.Mistral;

public sealed class MistralClient : IDisposable
{
    private static readonly Uri DefaultBaseUri = new("https://api.mistral.ai/v1/");

    private readonly HttpClient _httpClient;
    private readonly bool _ownsHttpClient;

    public MistralClient(string apiKey, HttpClient? httpClient = null)
    {
        _httpClient = httpClient ?? new HttpClient();
        _ownsHttpClient = httpClient is null;

        _httpClient.BaseAddress ??= DefaultBaseUri;
        _httpClient.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", apiKey);
    }

    public async Task<ChatCompletionResponse> CreateChatCompletionAsync(ChatCompletionRequest request, CancellationToken cancellationToken = default)
    {
        var nonStreamingRequest = request with { Stream = false };
        using var response = await SendAsync(nonStreamingRequest, cancellationToken).ConfigureAwait(false);
        var responseBody = await response.Content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false);
        return await JsonSerializer.DeserializeAsync(responseBody, MistralJsonContext.Default.ChatCompletionResponse, cancellationToken).ConfigureAwait(false)
            ?? throw new MistralApiException("Received an empty chat completion response.");
    }

    public async IAsyncEnumerable<ChatCompletionChunk> CreateChatCompletionStreamAsync(
        ChatCompletionRequest request,
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        var streamingRequest = request with { Stream = true };
        var response = await SendAsync(streamingRequest, cancellationToken).ConfigureAwait(false);
        try
        {
            var responseStream = await response.Content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false);

            await foreach (var item in SseParser.Create(responseStream).EnumerateAsync(cancellationToken).ConfigureAwait(false))
            {
                if (item.Data == "[DONE]")
                {
                    yield break;
                }

                var chunk = JsonSerializer.Deserialize(item.Data, MistralJsonContext.Default.ChatCompletionChunk);
                if (chunk is not null)
                {
                    yield return chunk;
                }
            }
        }
        finally
        {
            response.Dispose();
        }
    }

    public async Task<IReadOnlyList<ModelInfo>> ListModelsAsync(CancellationToken cancellationToken = default)
    {
        using var response = await _httpClient.GetAsync("models", cancellationToken).ConfigureAwait(false);

        if (!response.IsSuccessStatusCode)
        {
            var body = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
            throw new MistralApiException($"Mistral API returned {(int)response.StatusCode} {response.StatusCode}: {body}");
        }

        var responseBody = await response.Content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false);
        var modelList = await JsonSerializer.DeserializeAsync(responseBody, MistralJsonContext.Default.ModelList, cancellationToken).ConfigureAwait(false)
            ?? throw new MistralApiException("Received an empty model list response.");
        return modelList.Data;
    }

    private async Task<HttpResponseMessage> SendAsync(ChatCompletionRequest request, CancellationToken cancellationToken)
    {
        using var content = JsonContent.Create(request, MistralJsonContext.Default.ChatCompletionRequest);
        var response = await _httpClient.PostAsync("chat/completions", content, cancellationToken).ConfigureAwait(false);

        if (!response.IsSuccessStatusCode)
        {
            var body = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
            response.Dispose();
            throw new MistralApiException($"Mistral API returned {(int)response.StatusCode} {response.StatusCode}: {body}");
        }

        return response;
    }

    public void Dispose()
    {
        if (_ownsHttpClient)
        {
            _httpClient.Dispose();
        }
    }
}

public sealed class MistralApiException(string message) : Exception(message);
