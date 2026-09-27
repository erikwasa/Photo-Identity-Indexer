using PhotoIdentity.Persistence.Postgres;

namespace PhotoIdentity.Integration.Tests;

/// <summary>
/// Compatibility shims for mature fixtures that explicitly invoked SQLite schema guards.
/// PostgreSQL catalogue initialization owns these schemas, so no repository-local migration step
/// is required after PostgresTestCatalogueDatabase.InitializeAsync has completed.
/// </summary>
internal static class PostgresLegacySchemaCompatibilityExtensions
{
    public static Task EnsureSchemaAsync(
        this PostgresFaceReviewDerivativeRepository repository,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(repository);
        cancellationToken.ThrowIfCancellationRequested();
        return Task.CompletedTask;
    }
}
