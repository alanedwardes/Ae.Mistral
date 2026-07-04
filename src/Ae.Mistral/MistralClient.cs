using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Net.ServerSentEvents;
using System.Runtime.CompilerServices;
using System.Text.Json;
using Ae.Mistral.Json;
using Ae.Mistral.Models;

namespace Ae.Mistral;

public sealed class MistralClient : IMistralClient
{
    private static readonly Uri DefaultBaseUri = new("https://api.mistral.ai/v1/");

    private static readonly HashSet<HttpStatusCode> RetryableStatusCodes =
    [
        HttpStatusCode.RequestTimeout,
        HttpStatusCode.TooManyRequests,
        HttpStatusCode.InternalServerError,
        HttpStatusCode.BadGateway,
        HttpStatusCode.ServiceUnavailable,
        HttpStatusCode.GatewayTimeout
    ];

    private readonly HttpClient _httpClient;
    private readonly bool _ownsHttpClient;
    private readonly int _maxRetries;

    public MistralClient(string apiKey, HttpClient? httpClient = null, int maxRetries = 2)
    {
        _httpClient = httpClient ?? new HttpClient();
        _ownsHttpClient = httpClient is null;
        _maxRetries = maxRetries;

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
        using var response = await SendWithRetryAsync(
            () => _httpClient.GetAsync("models", cancellationToken),
            cancellationToken).ConfigureAwait(false);

        var responseBody = await response.Content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false);
        var modelList = await JsonSerializer.DeserializeAsync(responseBody, MistralJsonContext.Default.ModelList, cancellationToken).ConfigureAwait(false)
            ?? throw new MistralApiException("Received an empty model list response.");
        return modelList.Data;
    }

    private Task<HttpResponseMessage> SendAsync(ChatCompletionRequest request, CancellationToken cancellationToken)
    {
        return SendWithRetryAsync(async () =>
        {
            using var content = JsonContent.Create(request, MistralJsonContext.Default.ChatCompletionRequest);
            return await _httpClient.PostAsync("chat/completions", content, cancellationToken).ConfigureAwait(false);
        }, cancellationToken);
    }

    private async Task<HttpResponseMessage> SendWithRetryAsync(Func<Task<HttpResponseMessage>> sendRequest, CancellationToken cancellationToken)
    {
        for (var attempt = 0; ; attempt++)
        {
            var response = await sendRequest().ConfigureAwait(false);

            if (response.IsSuccessStatusCode)
            {
                return response;
            }

            if (attempt >= _maxRetries || !RetryableStatusCodes.Contains(response.StatusCode))
            {
                var body = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
                response.Dispose();
                throw new MistralApiException($"Mistral API returned {(int)response.StatusCode} {response.StatusCode}: {body}");
            }

            var delay = GetRetryDelay(response, attempt);
            response.Dispose();
            await Task.Delay(delay, cancellationToken).ConfigureAwait(false);
        }
    }

    private static TimeSpan GetRetryDelay(HttpResponseMessage response, int attempt)
    {
        if (response.Headers.RetryAfter is { } retryAfter)
        {
            if (retryAfter.Delta is { } delta)
            {
                return delta;
            }

            if (retryAfter.Date is { } date)
            {
                var untilDate = date - DateTimeOffset.UtcNow;
                if (untilDate > TimeSpan.Zero)
                {
                    return untilDate;
                }
            }
        }

        var exponentialDelay = TimeSpan.FromMilliseconds(500 * Math.Pow(2, attempt));
        var jitter = TimeSpan.FromMilliseconds(Random.Shared.Next(250));
        return exponentialDelay + jitter;
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
