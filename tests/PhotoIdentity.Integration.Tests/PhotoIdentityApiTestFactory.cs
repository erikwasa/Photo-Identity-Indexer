using System.Net;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace PhotoIdentity_Integration_Tests;

/// <summary>
/// Compatibility foundation for API integration-test factories in this namespace.
/// Legacy unqualified WebApplicationFactory references resolve here, so generic endpoint hosts
/// disable unrelated production workers even before they are migrated to PhotoIdentityApiTestFactory.
/// Worker-specific tests must explicitly opt back in.
/// </summary>
internal class WebApplicationFactory<TEntryPoint> :
    Microsoft.AspNetCore.Mvc.Testing.WebApplicationFactory<TEntryPoint>
    where TEntryPoint : class
{
    protected virtual bool DisableBackgroundWorkers => true;

    protected override IHost CreateHost(IHostBuilder builder)
    {
        if (DisableBackgroundWorkers)
        {
            builder.ConfigureServices(services =>
            {
                RemoveHostedService<PhotoIdentity.Api.PhotoPlaceEnrichmentHostedService>(services);
                RemoveHostedService<PhotoIdentity.Api.ArchiveAdvancementHostedService>(services);
                RemoveHostedService<PhotoIdentity.Api.IdentityMatchRegenerationHostedService>(services);
            });
        }

        return base.CreateHost(builder);
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

/// <summary>
/// Shared host foundation for API integration tests.
/// Generic endpoint tests should not run unrelated production background loops; worker-specific
/// behavior is covered by focused tests that exercise the worker directly or can opt back in.
/// </summary>
internal class PhotoIdentityApiTestFactory : WebApplicationFactory<PhotoIdentity.Api.Program>
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

    protected override bool DisableBackgroundWorkers => _disableBackgroundWorkers;

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseSetting("PhotoIdentity:DatabasePath", _databasePath);
        builder.UseSetting(WebHostDefaults.DetailedErrorsKey, "true");
        _configureWebHost?.Invoke(builder);
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

        throw CreateFailureException(response, body, $"GET '{requestUri}'");
    }

    public static async Task EnsureSuccessWithDiagnosticBodyAsync(
        this HttpResponseMessage response,
        string? requestDescription = null,
        CancellationToken cancellationToken = default)
    {
        if (response.IsSuccessStatusCode)
        {
            return;
        }

        string body = await response.Content.ReadAsStringAsync(cancellationToken);
        throw CreateFailureException(
            response,
            body,
            requestDescription ?? response.RequestMessage?.RequestUri?.ToString() ?? "HTTP request");
    }

    public static async Task EnsureStatusCodeWithDiagnosticBodyAsync(
        this HttpResponseMessage response,
        HttpStatusCode expectedStatusCode,
        string? requestDescription = null,
        CancellationToken cancellationToken = default)
    {
        if (response.StatusCode == expectedStatusCode)
        {
            return;
        }

        string body = await response.Content.ReadAsStringAsync(cancellationToken);
        string description = requestDescription ??
            response.RequestMessage?.RequestUri?.ToString() ??
            "HTTP request";
        throw CreateFailureException(
            response,
            body,
            $"{description} expected {(int)expectedStatusCode} ({expectedStatusCode}) but");
    }

    private static HttpRequestException CreateFailureException(
        HttpResponseMessage response,
        string body,
        string requestDescription)
    {
        string boundedBody = body.Length <= MaximumDiagnosticBodyLength
            ? body
            : body[..MaximumDiagnosticBodyLength] + "\n...[response body truncated]";
        return new HttpRequestException(
            $"{requestDescription} returned {(int)response.StatusCode} ({response.ReasonPhrase}). " +
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
