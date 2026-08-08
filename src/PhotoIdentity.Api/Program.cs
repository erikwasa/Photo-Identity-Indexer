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

        builder.Services.AddSingleton(new SqliteCatalogueDatabase(databasePath));
        builder.Services.AddSingleton(new ArchiveOperatorConfiguration(
            archiveAnalysisRoot,
            builder.Configuration["PhotoIdentity:RepositoryRoot"],
            builder.Configuration["PhotoIdentity:ModelDirectory"]));
        builder.Services.AddSingleton(new ReviewProxyServingConfiguration(
            builder.Configuration["PhotoIdentity:ReviewProxyRoot"],
            builder.Configuration["PhotoIdentity:ReviewProxyProfileId"]));
        builder.Services.AddSingleton<SqliteReviewRepository>();
        builder.Services.AddSingleton<SqliteReviewFilterRepository>();
        builder.Services.AddSingleton<SqliteReviewSuggestionRepository>();
        builder.Services.AddSingleton<SqliteSuggestionGalleryRepository>();
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
        builder.Services.AddSingleton<SqliteArchiveReviewProxyRepository>();
        builder.Services.AddSingleton<SqliteArchiveHydrationRepository>();
        builder.Services.AddSingleton<ReviewCropFileResolver>();
        builder.Services.AddSingleton<DetectorRolloutCropFileResolver>();
        builder.Services.AddSingleton<CollectionPhotoFileResolver>();
        builder.Services.AddSingleton<CollectionReviewProxyFileResolver>();
        builder.Services.AddSingleton<CollectionOriginalAccessService>();
        builder.Services.AddSingleton<IOneDriveFilesOnDemandPlatform, WindowsOneDriveFilesOnDemandPlatform>();
        builder.Services.AddSingleton<OpenCvThumbnailRenderer>();
        builder.Services.AddSingleton(TimeProvider.System);
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
        app.MapPersonAuditEndpoints();
        app.MapPersonMaintenanceEndpoints();
        app.MapBulkReviewEndpoints();
        app.MapBulkSuggestionReviewEndpoints();
        app.MapCollectionEndpoints();
        app.MapDetectorEvaluationEndpoints();
        app.MapDetectorEvaluationComparisonEndpoints();
        app.MapDetectorRolloutEndpoints();
        app.MapArchiveEndpoints();
        app.MapFallbackToFile("index.html");

        await app.RunAsync();
    }
}
