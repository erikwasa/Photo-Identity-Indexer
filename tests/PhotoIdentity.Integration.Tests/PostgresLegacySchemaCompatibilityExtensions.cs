using PhotoIdentity.Persistence.Postgres;

namespace PhotoIdentity.Integration.Tests;

/// <summary>
/// Compatibility shim for mature fixtures that explicitly invoked repository-local schema guards.
/// PostgreSQL catalogue initialization owns these schemas, so no additional migration step is
/// required after PostgresTestCatalogueDatabase.InitializeAsync has completed.
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
