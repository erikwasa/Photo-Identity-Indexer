using PhotoIdentity.Api;
using PhotoIdentity.Persistence.Sqlite;

var builder = WebApplication.CreateBuilder(args);

string defaultDatabasePath = Path.Combine(
    Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
    "PhotoIdentity",
    "catalogue.db");
string databasePath = builder.Configuration["PhotoIdentity:DatabasePath"] ?? defaultDatabasePath;

builder.Services.AddSingleton(new SqliteCatalogueDatabase(databasePath));
builder.Services.AddSingleton<SqliteReviewRepository>();
builder.Services.AddSingleton(TimeProvider.System);

var app = builder.Build();
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

app.Run();

public partial class Program
{
}
