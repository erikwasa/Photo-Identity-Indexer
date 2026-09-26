using PhotoIdentity.Core.Identifiers;
using PhotoIdentity.Core.Recognition;
using PhotoIdentity.Core.Review;
using PhotoIdentity.Persistence.Postgres;

namespace PhotoIdentity.Testing.Postgres;

/// <summary>
/// Legacy test-fixture entry point that resolves the current PostgreSQL policy before invoking
/// the provider-neutral auto-assignment implementation.
/// </summary>
public sealed class PostgresIdentityAutoAssignmentCompatibilityService
{
    public const string AutomaticActor = PostgresIdentityAutoAssignmentService.AutomaticActor;

    private readonly PostgresIdentityAutoAssignmentService _inner;
    private readonly PostgresIdentitySuggestionPolicyRepository _policies;

    public PostgresIdentityAutoAssignmentCompatibilityService(
        PostgresTestCatalogueDatabase database,
        TimeProvider? timeProvider = null)
    {
        ArgumentNullException.ThrowIfNull(database);
        _inner = new PostgresIdentityAutoAssignmentService(database.Database, timeProvider);
        _policies = new PostgresIdentitySuggestionPolicyRepository(database.Database, timeProvider);
    }

    public async Task<ReviewIdentityAutoAssignmentSummary> ApplyAsync(
        ModelId modelId,
        Sha256Digest modelHash,
        CancellationToken cancellationToken = default)
    {
        ReviewIdentitySuggestionPolicy policy = await _policies.GetAsync(
            modelId,
            modelHash,
            cancellationToken);
        return await _inner.ApplyAsync(modelId, modelHash, policy, cancellationToken);
    }
}
