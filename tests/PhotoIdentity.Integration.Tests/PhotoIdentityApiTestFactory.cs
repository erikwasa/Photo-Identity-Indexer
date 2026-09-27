using System.Net;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using PhotoIdentity.Testing.Postgres;

namespace PhotoIdentity_Integration_Tests;

/// <summary>
/// Compatibility foundation for API integration-test factories in this namespace.
/// Legacy unqualified WebApplicationFactory references resolve here, so generic endpoint hosts
/// share an isolated PostgreSQL catalogue and disable unrelated production workers.
/// Worker-specific tests must explicitly opt back in.
/// </summary>
internal class WebApplicationFactory<TEntryPoint> :
    Microsoft.AspNetCore.Mvc.Testing.WebApplicationFactory<TEntryPoint>
    where TEntryPoint : class
{
    private readonly PostgresTestDatabaseLease _databaseLease = PostgresTestDatabaseLease.Create();

    protected virtual bool DisableBackgroundWorkers => true;

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        // Minimal-hosting applications read web-host settings while Program is being constructed.
        // Give factories that use this base implementation a valid catalogue immediately.
        builder.UseSetting("PhotoIdentity:Postgres:ConnectionString", _databaseLease.ConnectionString);
        builder.UseStaticWebAssets();
    }

    protected override IHost CreateHost(IHostBuilder builder)
    {
        builder.UseEnvironment("IntegrationTestPostgres");
        builder.ConfigureWebHost(webBuilder => webBuilder.UseStaticWebAssets());

        if (DisableBackgroundWorkers)
        {
            builder.ConfigureServices(services =>
            {
                RemoveHostedService<PhotoIdentity.Api.PhotoPlaceEnrichmentHostedService>(services);
                RemoveHostedService<PhotoIdentity.Api.PhotoCaptionEnrichmentHostedService>(services);
                RemoveHostedService<PhotoIdentity.Api.ArchiveAdvancementHostedService>(services);
                RemoveHostedService<PhotoIdentity.Api.IdentityMatchRegenerationHostedService>(services);
            });
        }

        return base.CreateHost(builder);
    }

    protected override void Dispose(bool disposing)
    {
        base.Dispose(disposing);
        if (disposing)
        {
            _databaseLease.Dispose();
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

/// <summary>
/// Shared host foundation for API integration tests.
/// A stable catalogue key lets existing fixtures share one isolated PostgreSQL database with the
/// API host without repeating provider-specific setup in every test.
/// </summary>
internal class PhotoIdentityApiTestFactory : WebApplicationFactory<PhotoIdentity.Api.Program>
{
    private readonly string _catalogueKey;
    private readonly Action<IWebHostBuilder>? _configureWebHost;
    private readonly bool _disableBackgroundWorkers;
    private readonly bool _useSharedTestCatalogue;

    public PhotoIdentityApiTestFactory(
        string catalogueKey,
        Action<IWebHostBuilder>? configureWebHost = null,
        bool disableBackgroundWorkers = true,
        bool useSharedTestCatalogue = true)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(catalogueKey);
        _catalogueKey = catalogueKey;
        _configureWebHost = configureWebHost;
        _disableBackgroundWorkers = disableBackgroundWorkers;
        _useSharedTestCatalogue = useSharedTestCatalogue;
    }

    protected override bool DisableBackgroundWorkers => _disableBackgroundWorkers;

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        base.ConfigureWebHost(builder);

        if (_useSharedTestCatalogue)
        {
            builder.UseSetting(
                "PhotoIdentity:Postgres:ConnectionString",
                PostgresTestCatalogueDatabase.GetCompatibilityConnectionString(_catalogueKey));
        }

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
