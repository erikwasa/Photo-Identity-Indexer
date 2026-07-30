using PhotoIdentity.Persistence.Sqlite;

namespace PhotoIdentity.Api;

public partial class Program
{
    public static async Task Main(string[] args)
    {
        WebApplicationBuilder builder = WebApplication.CreateBuilder(args);

        string defaultDatabasePath = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "PhotoIdentity",
            "catalogue.db");
        string databasePath = builder.Configuration["PhotoIdentity:DatabasePath"] ?? defaultDatabasePath;

        builder.Services.AddSingleton(new SqliteCatalogueDatabase(databasePath));
        builder.Services.AddSingleton<SqliteReviewRepository>();
        builder.Services.AddSingleton<SqliteReviewFilterRepository>();
        builder.Services.AddSingleton<SqliteReviewSuggestionRepository>();
        builder.Services.AddSingleton<SqliteSuggestionGalleryRepository>();
        builder.Services.AddSingleton<SqlitePersonAuditRepository>();
        builder.Services.AddSingleton<SqlitePersonMaintenanceRepository>();
        builder.Services.AddSingleton<SqliteBulkReviewRepository>();
        builder.Services.AddSingleton<SqliteBulkSuggestionReviewRepository>();
        builder.Services.AddSingleton<ReviewCropFileResolver>();
        builder.Services.AddSingleton(TimeProvider.System);

        WebApplication app = builder.Build();
        await app.Services.GetRequiredService<SqliteCatalogueDatabase>().InitializeAsync();

        app.UseBlazorFrameworkFiles();
        app.UseStaticFiles();
        app.Use(async (context, next) =>
        {
            if (context.Request.Path.StartsWithSegments("/api/review"))
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
        app.MapFallbackToFile("index.html");

        await app.RunAsync();
    }
}
