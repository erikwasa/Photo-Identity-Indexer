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
        builder.Services.AddSingleton(TimeProvider.System);

        WebApplication app = builder.Build();
        await app.Services.GetRequiredService<SqliteCatalogueDatabase>().InitializeAsync();

        app.UseBlazorFrameworkFiles();
        app.UseStaticFiles();

        app.MapGet("/health", () => Results.Ok(new
        {
            status = "ok",
            schemaVersion = SqliteCatalogueDatabase.CurrentSchemaVersion,
        }));
        app.MapReviewEndpoints();
        app.MapFallbackToFile("index.html");

        await app.RunAsync();
    }
}
