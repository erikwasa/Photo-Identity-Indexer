using System.Net;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.Configuration;
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

        // A number of mature endpoint fixtures override ConfigureWebHost without calling base and
        // still provide only the historical PhotoIdentity:DatabasePath compatibility key. Host
        // configuration is available before the minimal Program body runs, unlike generic app
        // configuration callbacks, so translate that key here at bootstrap time. This preserves
        // each fixture's isolated seeded PostgreSQL catalogue without restoring SQLite runtime use.
        builder.ConfigureHostConfiguration(configuration =>
        {
            IConfiguration current = configuration.Build();
            string? configuredConnectionString =
                current["PhotoIdentity:Postgres:ConnectionString"];
            if (!string.IsNullOrWhiteSpace(configuredConnectionString))
            {
                return;
            }

            string? compatibilityPath = current["PhotoIdentity:DatabasePath"];
            string connectionString = !string.IsNullOrWhiteSpace(compatibilityPath)
                ? PostgresTestCatalogueDatabase.GetCompatibilityConnectionString(compatibilityPath)
                : _databaseLease.ConnectionString;
            configuration.AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["PhotoIdentity:Postgres:ConnectionString"] = connectionString,
            });
        });

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
/// The historical database-path argument is retained only as a PostgreSQL compatibility key so
/// existing fixtures can share seeded state with the API host while WI-0148 removes SQLite usage.
/// </summary>
internal class PhotoIdentityApiTestFactory : WebApplicationFactory<PhotoIdentity.Api.Program>
{
    private readonly string _databasePath;
    private readonly Action<IWebHostBuilder>? _configureWebHost;
    private readonly bool _disableBackgroundWorkers;
    private readonly bool _useCompatibilityDatabase;

    public PhotoIdentityApiTestFactory(
        string databasePath,
        Action<IWebHostBuilder>? configureWebHost = null,
        bool disableBackgroundWorkers = true,
        bool useSqliteTestCompatibility = true)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(databasePath);
        _databasePath = databasePath;
        _configureWebHost = configureWebHost;
        _disableBackgroundWorkers = disableBackgroundWorkers;
        _useCompatibilityDatabase = useSqliteTestCompatibility;
    }

    protected override bool DisableBackgroundWorkers => _disableBackgroundWorkers;

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        base.ConfigureWebHost(builder);

        if (_useCompatibilityDatabase)
        {
            builder.UseSetting("PhotoIdentity:DatabasePath", _databasePath);
            builder.UseSetting(
                "PhotoIdentity:Postgres:ConnectionString",
                PostgresTestCatalogueDatabase.GetCompatibilityConnectionString(_databasePath));
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
