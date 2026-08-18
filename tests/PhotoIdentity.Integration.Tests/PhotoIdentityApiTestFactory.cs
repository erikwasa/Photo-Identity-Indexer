using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace PhotoIdentity_Integration_Tests;

/// <summary>
/// Shared host foundation for API integration tests.
/// Generic endpoint tests should not run unrelated production background loops; worker-specific
/// behavior is covered by focused tests that exercise the worker directly or can opt back in.
/// </summary>
internal sealed class PhotoIdentityApiTestFactory : WebApplicationFactory<PhotoIdentity.Api.Program>
{
    private readonly string _databasePath;
    private readonly Action<IWebHostBuilder>? _configureWebHost;
    private readonly bool _disableBackgroundWorkers;

    public PhotoIdentityApiTestFactory(
        string databasePath,
        Action<IWebHostBuilder>? configureWebHost = null,
        bool disableBackgroundWorkers = true)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(databasePath);
        _databasePath = databasePath;
        _configureWebHost = configureWebHost;
        _disableBackgroundWorkers = disableBackgroundWorkers;
    }

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseSetting("PhotoIdentity:DatabasePath", _databasePath);
        builder.UseSetting(WebHostDefaults.DetailedErrorsKey, "true");
        _configureWebHost?.Invoke(builder);

        if (_disableBackgroundWorkers)
        {
            builder.ConfigureTestServices(services =>
            {
                RemoveHostedService<PhotoIdentity.Api.PhotoPlaceEnrichmentHostedService>(services);
                RemoveHostedService<PhotoIdentity.Api.ArchiveAdvancementHostedService>(services);
                RemoveHostedService<PhotoIdentity.Api.IdentityMatchRegenerationHostedService>(services);
            });
        }
    }

    private static void RemoveHostedService<THostedService>(IServiceCollection services)
        where THostedService : class, IHostedService
    {
        for (int index = services.Count - 1; index >= 0; index--)
        {
            ServiceDescriptor descriptor = services[index];
            if (descriptor.ServiceType == typeof(IHostedService) &&
                descriptor.ImplementationType == typeof(THostedService))
            {
                services.RemoveAt(index);
            }
        }
    }
}

internal static class IntegrationTestHttpClientExtensions
{
    private const int MaximumDiagnosticBodyLength = 4_000;

    public static async Task<string> GetRequiredStringAsync(
        this HttpClient client,
        string requestUri,
        CancellationToken cancellationToken = default)
    {
        using HttpResponseMessage response = await client.GetAsync(requestUri, cancellationToken);
        string body = await response.Content.ReadAsStringAsync(cancellationToken);
        if (response.IsSuccessStatusCode)
        {
            return body;
        }

        string boundedBody = body.Length <= MaximumDiagnosticBodyLength
            ? body
            : body[..MaximumDiagnosticBodyLength] + "\n...[response body truncated]";
        throw new HttpRequestException(
            $"GET '{requestUri}' returned {(int)response.StatusCode} ({response.ReasonPhrase}). " +
            $"Response body:\n{boundedBody}",
            inner: null,
            response.StatusCode);
    }
}

internal static class TestCategories
{
    public const string Category = "Category";
    public const string FlakyDiagnostic = "FlakyDiagnostic";
}
