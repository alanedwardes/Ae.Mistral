using Microsoft.Extensions.Configuration;

namespace Ae.Mistral.Tests.Integration;

public static class MistralApiFixture
{
    private static readonly Lazy<string?> LazyApiKey = new(LoadApiKey);

    public static string? ApiKey => LazyApiKey.Value;

    private static string? LoadApiKey()
    {
        var configuration = new ConfigurationBuilder()
            .AddUserSecrets(typeof(MistralApiFixture).Assembly)
            .AddEnvironmentVariables()
            .Build();

        return configuration["mistral:apiKey"] ?? Environment.GetEnvironmentVariable("MISTRAL_API_KEY");
    }

    public static string RequireApiKey() => ApiKey ?? throw new InvalidOperationException(
        """
        No Mistral API key configured. Set it via user-secrets to run integration tests:
          dotnet user-secrets set "mistral:apiKey" "<your-key>" --project tests/Ae.Mistral.Tests
        (or set the MISTRAL_API_KEY environment variable instead).
        """);
}
