using System.Net;
using System.Text;

namespace Ae.Mistral.Tests;

internal sealed class FakeHttpMessageHandler(Func<HttpRequestMessage, string, HttpResponseMessage> respond) : HttpMessageHandler
{
    public string? LastRequestBody { get; private set; }

    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        LastRequestBody = request.Content is not null
            ? await request.Content.ReadAsStringAsync(cancellationToken)
            : null;

        return respond(request, LastRequestBody ?? string.Empty);
    }

    public static FakeHttpMessageHandler WithJsonResponse(string json) =>
        new((_, _) => new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent(json, Encoding.UTF8, "application/json"),
        });

    public static FakeHttpMessageHandler WithSseResponse(string sseBody) =>
        new((_, _) => new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent(sseBody, Encoding.UTF8, "text/event-stream"),
        });

    public HttpClient ToHttpClient() => new(this) { BaseAddress = new Uri("https://api.mistral.ai/v1/") };
}
