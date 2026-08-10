using System.Globalization;
using Microsoft.Extensions.Configuration;
using PhotoIdentity.Imaging.OpenCv;
using PhotoIdentity.Persistence.Sqlite;
using PhotoIdentity.Source.OneDriveSync;

namespace PhotoIdentity.Api;

public partial class Program
{
    public static async Task Main(string[] args)
    {
        WebApplicationBuilder builder = WebApplication.CreateBuilder(args);

        string defaultApplicationRoot = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "PhotoIdentity");
        string defaultDatabasePath = Path.Combine(defaultApplicationRoot, "catalogue.db");
        string defaultDetectorEvaluationRoot = Path.Combine(defaultApplicationRoot, "detector-evaluations");
        string defaultArchiveAnalysisRoot = Path.Combine(defaultApplicationRoot, "archive-analysis");
        string databasePath = builder.Configuration["PhotoIdentity:DatabasePath"] ?? defaultDatabasePath;
        string detectorEvaluationRoot =
            builder.Configuration["PhotoIdentity:DetectorEvaluationRoot"] ?? defaultDetectorEvaluationRoot;
        string archiveAnalysisRoot =
            builder.Configuration["PhotoIdentity:ArchiveAnalysisOutputRoot"] ?? defaultArchiveAnalysisRoot;
        string? reviewProxyRoot = builder.Configuration["PhotoIdentity:ReviewProxyRoot"];
        string? reviewProxyProfileId = builder.Configuration["PhotoIdentity:ReviewProxyProfileId"];

        builder.Services.AddSingleton(new SqliteCatalogueDatabase(databasePath));
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
        builder.Services.AddSingleton<SqliteReviewRepository>();
        builder.Services.AddSingleton<SqliteReviewFilterRepository>();
        builder.Services.AddSingleton<SqliteReviewSuggestionRepository>();
        builder.Services.AddSingleton<SqliteSuggestionGalleryRepository>();
        builder.Services.AddSingleton<SqliteIdentitySuggestionPolicyRepository>();
        builder.Services.AddSingleton<SqliteIdentityMatchRegenerationRepository>();
        builder.Services.AddSingleton<SqliteIdentityMatchRegenerationScorer>();
        builder.Services.AddSingleton<SqliteIdentityAutoAssignmentService>();
        builder.Services.AddSingleton<SqlitePersonAuditRepository>();
        builder.Services.AddSingleton<SqlitePersonMaintenanceRepository>();
        builder.Services.AddSingleton<SqliteBulkReviewRepository>();
        builder.Services.AddSingleton<SqliteBulkSuggestionReviewRepository>();
        builder.Services.AddSingleton<SqliteCollectionQueryRepository>();
        builder.Services.AddSingleton<SqliteDetectorEvaluationRepository>();
        builder.Services.AddSingleton<SqliteLocalBatchRepository>();
        builder.Services.AddSingleton<SqliteProcessingRepository>();
        builder.Services.AddSingleton<SqliteDetectorRolloutReviewRepository>();
        builder.Services.AddSingleton<SqliteDetectorRolloutApplicationRepository>();
        builder.Services.AddSingleton<SqliteArchiveAnalysisRepository>();
        builder.Services.AddSingleton<SqliteArchiveReviewProxyRepository>();
        builder.Services.AddSingleton<SqliteArchivePostAnalysisRepository>();
        builder.Services.AddSingleton<SqliteArchiveHydrationRepository>();
        builder.Services.AddSingleton<SqliteArchiveSourceHydrationRepository>();
        builder.Services.AddSingleton<SqliteArchiveSourceObservationRepository>();
        builder.Services.AddSingleton<SqliteArchiveSourceVerificationStateRepository>();
        builder.Services.AddSingleton<SqliteArchiveAvailabilityRepository>();
        builder.Services.AddSingleton<SqliteArchiveStorageRepository>();
        builder.Services.AddSingleton<SqliteArchiveAdvancementRepository>();
        builder.Services.AddSingleton<ReviewCropFileResolver>();
        builder.Services.AddSingleton<DetectorRolloutCropFileResolver>();
        builder.Services.AddSingleton<CollectionPhotoFileResolver>();
        builder.Services.AddSingleton<CollectionReviewProxyFileResolver>();
        builder.Services.AddSingleton<CollectionOriginalAccessService>();
        builder.Services.AddSingleton<ArchiveHydrationCapacityService>();
        builder.Services.AddSingleton<ArchiveSourceVerificationService>();
        builder.Services.AddSingleton<ArchiveBoundedAnalysisService>();
        builder.Services.AddSingleton<IOneDriveFilesOnDemandPlatform, WindowsOneDriveFilesOnDemandPlatform>();
        builder.Services.AddSingleton<IArchiveStorageProbe, DriveArchiveStorageProbe>();
        builder.Services.AddSingleton<OpenCvThumbnailRenderer>();
        builder.Services.AddSingleton<OpenCvReviewProxyRenderer>();
        builder.Services.AddSingleton(TimeProvider.System);
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
        await app.Services.GetRequiredService<SqliteCatalogueDatabase>().InitializeAsync();

        app.UseBlazorFrameworkFiles();
        app.UseStaticFiles();
        app.Use(async (context, next) =>
        {
            if (context.Request.Path.StartsWithSegments("/api/review") ||
                context.Request.Path.StartsWithSegments("/api/collections") ||
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
            schemaVersion = SqliteCatalogueDatabase.CurrentSchemaVersion,
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
        app.MapCollectionProxyEndpoints();
        app.MapCollectionViewerPreviewEndpoints();
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
}
