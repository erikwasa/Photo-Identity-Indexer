using System.Globalization;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.EventLog;
using PhotoIdentity.Core.Catalogue;
using PhotoIdentity.Core.Collections;
using PhotoIdentity.Core.Imaging;
using PhotoIdentity.Core.People;
using PhotoIdentity.Core.Places;
using PhotoIdentity.Core.Processing;
using PhotoIdentity.Core.Recognition;
using PhotoIdentity.Core.Review;
using PhotoIdentity.Core.Sources;
using PhotoIdentity.Core.Tags;
using PhotoIdentity.Imaging.OpenCv;
using PhotoIdentity.Persistence.Postgres;
using PhotoIdentity.Persistence.Sqlite;
using PhotoIdentity.Source.Local;
using PhotoIdentity.Source.OneDriveSync;
using PhotoIdentity.Worker;

namespace PhotoIdentity.Api;

public partial class Program
{
    public static async Task Main(string[] args)
    {
        WebApplicationBuilder builder = WebApplication.CreateBuilder(args);

        if (OperatingSystem.IsWindows() && builder.Environment.IsDevelopment())
        {
            // WebApplicationFactory uses Development by default. Parallel integration-test hosts
            // can otherwise share the Windows EventLog source lifetime and intermittently attempt
            // to log through an EventLogInternal instance disposed by another completed host.
            builder.Logging.AddFilter<EventLogLoggerProvider>(_ => false);
        }

        string defaultApplicationRoot = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "PhotoIdentity");
        string defaultDatabasePath = Path.Combine(defaultApplicationRoot, "catalogue.db");
        string defaultDetectorEvaluationRoot = Path.Combine(defaultApplicationRoot, "detector-evaluations");
        string defaultArchiveAnalysisRoot = Path.Combine(defaultApplicationRoot, "archive-analysis");
        string databasePath = builder.Configuration["PhotoIdentity:DatabasePath"] ?? defaultDatabasePath;
        string? postgresConnectionString = builder.Configuration["PhotoIdentity:Postgres:ConnectionString"];
        CatalogueProviderKind catalogueProvider = CataloguePersistenceComposition.ResolveProvider(builder.Configuration);
        string catalogueProviderName = catalogueProvider == CatalogueProviderKind.Postgres ? "postgresql" : "sqlite";
        string detectorEvaluationRoot =
            builder.Configuration["PhotoIdentity:DetectorEvaluationRoot"] ?? defaultDetectorEvaluationRoot;
        string archiveAnalysisRoot =
            builder.Configuration["PhotoIdentity:ArchiveAnalysisOutputRoot"] ?? defaultArchiveAnalysisRoot;
        string? reviewProxyRoot = builder.Configuration["PhotoIdentity:ReviewProxyRoot"];
        string? reviewProxyProfileId = builder.Configuration["PhotoIdentity:ReviewProxyProfileId"];
        int? automaticGeoNamesMinimumRequestInterval = ParseOptionalInt(
            builder.Configuration,
            "PhotoIdentity:GeoNames:AutomaticMinimumRequestIntervalMilliseconds");
        int? rawGeoNamesMinimumRequestInterval = ParseOptionalInt(
            builder.Configuration,
            "PhotoIdentity:GeoNames:MinimumRequestIntervalMilliseconds");
        int resolvedGeoNamesMinimumRequestInterval = rawGeoNamesMinimumRequestInterval
            ?? (automaticGeoNamesMinimumRequestInterval is int automaticInterval
                ? Math.Min(
                    automaticInterval,
                    GeoNamesReverseGeocodingConfiguration.DefaultMinimumRequestIntervalMilliseconds)
                : GeoNamesReverseGeocodingConfiguration.DefaultMinimumRequestIntervalMilliseconds);

        PostgresCatalogueDatabase? postgresCatalogueDatabase = null;
        if (catalogueProvider == CatalogueProviderKind.Postgres)
        {
            if (string.IsNullOrWhiteSpace(postgresConnectionString))
            {
                throw new InvalidOperationException(
                    "PhotoIdentity:Postgres:ConnectionString is required when PhotoIdentity:CatalogueProvider is 'postgresql'.");
            }

            postgresCatalogueDatabase = CataloguePersistenceComposition.AddPostgres(
                builder.Services,
                postgresConnectionString);
        }
        else
        {
            CataloguePersistenceComposition.AddSqlite(builder.Services, databasePath);
            if (!string.IsNullOrWhiteSpace(postgresConnectionString))
            {
                // Preserve the migration-foundation health probe while SQLite remains selected.
                // No authoritative service contract is bound to this secondary database.
                postgresCatalogueDatabase = new PostgresCatalogueDatabase(postgresConnectionString);
                builder.Services.AddSingleton(postgresCatalogueDatabase);
            }
        }

        builder.Services.AddSingleton<ArchiveThroughputMetrics>();
        builder.Services.AddSingleton(new ArchiveOperatorConfiguration(
            archiveAnalysisRoot,
            builder.Configuration["PhotoIdentity:RepositoryRoot"],
            builder.Configuration["PhotoIdentity:ModelDirectory"]));
        builder.Services.AddSingleton(new ReviewProxyServingConfiguration(
            reviewProxyRoot,
            reviewProxyProfileId));
        builder.Services.AddSingleton(new ReviewProxyGenerationConfiguration(
            reviewProxyRoot,
            reviewProxyProfileId,
            ParseOptionalInt(builder.Configuration, "PhotoIdentity:ReviewProxyMaximumLongEdge"),
            ParseOptionalInt(builder.Configuration, "PhotoIdentity:ReviewProxyJpegQuality")));
        builder.Services.AddSingleton(new ArchiveHydrationPolicyConfiguration(
            ParseOptionalLong(builder.Configuration, "PhotoIdentity:ArchiveHydration:MinimumFreeSpaceReserveBytes"),
            ParseOptionalLong(builder.Configuration, "PhotoIdentity:ArchiveHydration:MaximumManagedHydrationBytes"),
            ParseOptionalInt(builder.Configuration, "PhotoIdentity:ArchiveHydration:MaximumConcurrentOperations")));
        builder.Services.AddSingleton(new GeoNamesReverseGeocodingConfiguration(
            builder.Configuration["PhotoIdentity:GeoNames:Username"],
            builder.Configuration["PhotoIdentity:GeoNames:BaseUrl"],
            builder.Configuration["PhotoIdentity:GeoNames:Language"],
            resolvedGeoNamesMinimumRequestInterval));
        builder.Services.AddSingleton(new GeoNamesAutomaticEnrichmentConfiguration(
            ParseOptionalBool(builder.Configuration, "PhotoIdentity:GeoNames:AutomaticEnrichmentEnabled"),
            automaticGeoNamesMinimumRequestInterval,
            ParseOptionalInt(builder.Configuration, "PhotoIdentity:GeoNames:AutomaticIdlePollIntervalMilliseconds")));
        builder.Services.AddSingleton<PhotoPlaceEnrichmentWorkerState>();

        builder.Services.AddSingleton<ArchiveSourceCatalogueScanner>();
        builder.Services.AddSingleton<LocalArchiveSyncCoordinator>();
        builder.Services.AddSingleton<ArchiveAnalysisPersistence>(services => new(
            services.GetRequiredService<ICatalogueStoreInitializer>(),
            services.GetRequiredService<IArchiveCoverageRepository>(),
            services.GetRequiredService<IAssetRevisionLookupRepository>(),
            services.GetRequiredService<IFaceInspectionRepository>(),
            services.GetRequiredService<IProcessingRunRepository>(),
            services.GetRequiredService<IProcessingExecutionRepository>(),
            services.GetRequiredService<IArchiveAnalysisStateRepository>()));
        builder.Services.AddSingleton<FaceReviewDerivativeBackfillService>();
        builder.Services.AddSingleton<ReviewCropFileResolver>();
        builder.Services.AddSingleton<DetectorRolloutCropFileResolver>();
        builder.Services.AddSingleton<CollectionPhotoFileResolver>();
        builder.Services.AddSingleton<CollectionReviewProxyFileResolver>();
        builder.Services.AddSingleton<ReviewFaceTargetResolver>();
        builder.Services.AddSingleton<ReviewFaceRevisionResolver>();
        builder.Services.AddSingleton<CollectionOriginalAccessService>();
        builder.Services.AddSingleton<SlideshowOriginalLeaseRegistry>();
        builder.Services.AddSingleton<ArchiveHydrationCapacityService>();
        builder.Services.AddSingleton<SlideshowOriginalPreparationService>();
        builder.Services.AddSingleton<ArchiveSourceVerificationService>();
        builder.Services.AddSingleton<ArchiveBoundedAnalysisService>();
        builder.Services.AddSingleton<IOneDriveFilesOnDemandPlatform, WindowsOneDriveFilesOnDemandPlatform>();
        builder.Services.AddSingleton<IPhotoMetadataReader, MetadataExtractorPhotoMetadataReader>();
        builder.Services.AddSingleton<PhotoMetadataInspectionService>();
        builder.Services.AddSingleton<PhotoMetadataBackfillService>();
        builder.Services.AddSingleton<IArchiveStorageProbe, DriveArchiveStorageProbe>();
        builder.Services.AddSingleton<OpenCvThumbnailRenderer>();
        builder.Services.AddSingleton<OpenCvReviewProxyRenderer>();
        builder.Services.AddSingleton(TimeProvider.System);
        builder.Services.AddHttpClient("GeoNames");
        builder.Services.AddSingleton<IReverseGeocoder, GeoNamesReverseGeocoder>();
        builder.Services.AddSingleton<PhotoPlaceEnrichmentService>();
        builder.Services.AddHostedService<PhotoPlaceEnrichmentHostedService>();
        builder.Services.AddHostedService<ArchiveAdvancementHostedService>();
        builder.Services.AddHostedService<IdentityMatchRegenerationHostedService>();
        builder.Services.AddSingleton(serviceProvider => new DetectorEvaluationSessionStore(
            detectorEvaluationRoot,
            serviceProvider.GetRequiredService<TimeProvider>()));
        builder.Services.AddSingleton(serviceProvider => new DetectorEvaluationGroundTruthStore(
            Path.Combine(detectorEvaluationRoot, "ground-truth"),
            serviceProvider.GetRequiredService<TimeProvider>()));
        builder.Services.AddSingleton(serviceProvider => new DetectorEvaluationComparisonStore(
            Path.Combine(detectorEvaluationRoot, "comparisons"),
            serviceProvider.GetRequiredService<TimeProvider>()));

        WebApplication app = builder.Build();

        PostgresCatalogueHealth postgresHealth = PostgresCatalogueHealth.NotConfigured;
        int? catalogueSchemaVersion;
        if (catalogueProvider == CatalogueProviderKind.Sqlite)
        {
            SqliteCatalogueDatabase catalogueDatabase = app.Services.GetRequiredService<SqliteCatalogueDatabase>();
            await catalogueDatabase.InitializeAsync();
            await SqliteExtendedPhotoMetadataSchema.EnsureAsync(catalogueDatabase);
            await SqlitePhotoMetadataInspectionSchema.EnsureAsync(catalogueDatabase);
            await SqlitePhotoPlaceSchema.EnsureAndMigrateAsync(catalogueDatabase);
            await SqlitePhotoPlaceEnrichmentSchema.EnsureAsync(catalogueDatabase);
            catalogueSchemaVersion = SqliteCatalogueDatabase.CurrentSchemaVersion;

            if (postgresCatalogueDatabase is not null)
            {
                PostgresInitializationResult postgresInitialization =
                    await postgresCatalogueDatabase.TryInitializeAsync();
                postgresHealth = postgresInitialization.Health;

                if (postgresInitialization.Error is null)
                {
                    app.Logger.LogInformation(
                        "PostgreSQL migration foundation is ready at schema version {SchemaVersion}; SQLite remains the selected catalogue provider.",
                        postgresHealth.SchemaVersion);
                }
                else
                {
                    app.Logger.LogWarning(
                        postgresInitialization.Error,
                        "PostgreSQL migration foundation status is {PostgresStatus}; SQLite remains the selected catalogue provider.",
                        postgresHealth.Status);
                }
            }
        }
        else
        {
            PostgresCatalogueDatabase database = postgresCatalogueDatabase
                ?? throw new InvalidOperationException("PostgreSQL catalogue composition was not initialized.");
            PostgresInitializationResult postgresInitialization = await database.TryInitializeAsync();
            postgresHealth = postgresInitialization.Health;
            if (postgresInitialization.Error is not null)
            {
                throw new InvalidOperationException(
                    $"PostgreSQL catalogue initialization failed with status '{postgresHealth.Status}'.",
                    postgresInitialization.Error);
            }

            catalogueSchemaVersion = postgresHealth.SchemaVersion;
            app.Logger.LogInformation(
                "PostgreSQL is the selected catalogue provider at schema version {SchemaVersion}. SQLite catalogue initialization is disabled for this process.",
                catalogueSchemaVersion);
        }

        app.UseBlazorFrameworkFiles();
        app.UseStaticFiles();
        app.Use(async (context, next) =>
        {
            if (!context.Request.Path.StartsWithSegments("/api") ||
                context.Request.Path.StartsWithSegments("/api/archive/diagnostics"))
            {
                await next(context);
                return;
            }

            ArchiveThroughputMetrics metrics = context.RequestServices
                .GetRequiredService<ArchiveThroughputMetrics>();
            string metricName = GetApiRequestMetricName(context.Request.Path);
            using IDisposable timing = metrics.Measure(metricName);
            try
            {
                await next(context);
            }
            finally
            {
                metrics.RecordCounter(
                    context.Response.StatusCode < StatusCodes.Status400BadRequest
                        ? ArchiveThroughputMetricNames.ApiRequestSucceeded
                        : ArchiveThroughputMetricNames.ApiRequestFailed);
            }
        });
        app.Use(async (context, next) =>
        {
            if (context.Request.Path.StartsWithSegments("/api/review") ||
                context.Request.Path.StartsWithSegments("/api/collections") ||
                context.Request.Path.StartsWithSegments("/api/smart-collections") ||
                context.Request.Path.StartsWithSegments("/api/slideshows") ||
                context.Request.Path.StartsWithSegments("/api/photo-metadata") ||
                context.Request.Path.StartsWithSegments("/api/places") ||
                context.Request.Path.StartsWithSegments("/api/place-enrichment") ||
                context.Request.Path.StartsWithSegments("/api/detector-evaluation") ||
                context.Request.Path.StartsWithSegments("/api/detector-rollout") ||
                context.Request.Path.StartsWithSegments("/api/archive"))
            {
                context.Response.OnStarting(() =>
                {
                    context.Response.Headers.CacheControl = "no-store, max-age=0";
                    context.Response.Headers.Pragma = "no-cache";
                    context.Response.Headers.Expires = "0";
                    return Task.CompletedTask;
                });
            }

            await next(context);
        });

        app.MapGet("/health", () => Results.Ok(new
        {
            status = "ok",
            schemaVersion = catalogueSchemaVersion,
            catalogueProvider = catalogueProviderName,
            postgres = postgresHealth,
        }));
        app.MapReviewEndpoints();
        app.MapReviewSuggestionEndpoints();
        app.MapSuggestionGalleryEndpoints();
        app.MapIdentitySuggestionPolicyEndpoints();
        app.MapIdentityMatchRegenerationEndpoints();
        app.MapPersonAuditEndpoints();
        app.MapPersonMaintenanceEndpoints();
        app.MapBulkReviewEndpoints();
        app.MapBulkSuggestionReviewEndpoints();
        app.MapCollectionEndpoints();
        app.MapPhotoDetailsEndpoints();
        app.MapSmartCollectionEndpoints();
        app.MapSlideshowOriginalPreparationEndpoints();
        app.MapPhotoMetadataEndpoints();
        app.MapCollectionProxyEndpoints();
        app.MapCollectionViewerPreviewEndpoints();
        app.MapPhotoTagEndpoints();
        app.MapPhotoPlaceEndpoints();
        app.MapPhotoPlaceEnrichmentEndpoints();
        app.MapDetectorEvaluationEndpoints();
        app.MapDetectorEvaluationComparisonEndpoints();
        app.MapDetectorRolloutEndpoints();
        app.MapArchiveEndpoints();
        app.MapArchiveItemFilterEndpoints();
        app.MapArchiveStorageEndpoints();
        app.MapFallbackToFile("index.html");

        await app.RunAsync();
    }

    private static long? ParseOptionalLong(IConfiguration configuration, string key)
    {
        string? value = configuration[key];
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        if (long.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out long parsed))
        {
            return parsed;
        }

        throw new InvalidOperationException($"Configuration '{key}' must be an integer byte count.");
    }

    private static int? ParseOptionalInt(IConfiguration configuration, string key)
    {
        string? value = configuration[key];
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        if (int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out int parsed))
        {
            return parsed;
        }

        throw new InvalidOperationException($"Configuration '{key}' must be an integer.");
    }

    private static bool? ParseOptionalBool(IConfiguration configuration, string key)
    {
        string? value = configuration[key];
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        if (bool.TryParse(value, out bool parsed))
        {
            return parsed;
        }

        throw new InvalidOperationException($"Configuration '{key}' must be true or false.");
    }

    private static string GetApiRequestMetricName(PathString path)
    {
        if (path.StartsWithSegments("/api/archive"))
        {
            return ArchiveThroughputMetricNames.ApiArchiveRequest;
        }

        if (path.StartsWithSegments("/api/collections") ||
            path.StartsWithSegments("/api/smart-collections"))
        {
            return ArchiveThroughputMetricNames.ApiCollectionRequest;
        }

        if (path.StartsWithSegments("/api/photo-metadata"))
        {
            return ArchiveThroughputMetricNames.ApiMetadataRequest;
        }

        if (path.StartsWithSegments("/api/places") ||
            path.StartsWithSegments("/api/place-enrichment"))
        {
            return ArchiveThroughputMetricNames.ApiPlaceRequest;
        }

        if (path.StartsWithSegments("/api/review") ||
            path.StartsWithSegments("/api/suggestions") ||
            path.StartsWithSegments("/api/people") ||
            path.StartsWithSegments("/api/identity"))
        {
            return ArchiveThroughputMetricNames.ApiReviewRequest;
        }

        if (path.StartsWithSegments("/api/slideshows"))
        {
            return ArchiveThroughputMetricNames.ApiSlideshowRequest;
        }

        return path.StartsWithSegments("/api/detector")
            ? ArchiveThroughputMetricNames.ApiDetectorRequest
            : ArchiveThroughputMetricNames.ApiOtherRequest;
    }
}
