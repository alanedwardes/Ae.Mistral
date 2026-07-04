using Ae.Mistral.Models;

namespace Ae.Mistral;

public interface IMistralClient : IDisposable
{
    Task<ChatCompletionResponse> CreateChatCompletionAsync(ChatCompletionRequest request, CancellationToken cancellationToken = default);

    IAsyncEnumerable<ChatCompletionChunk> CreateChatCompletionStreamAsync(ChatCompletionRequest request, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<ModelInfo>> ListModelsAsync(CancellationToken cancellationToken = default);
}
