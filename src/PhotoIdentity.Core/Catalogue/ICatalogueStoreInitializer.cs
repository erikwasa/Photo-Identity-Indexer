namespace PhotoIdentity.Core.Catalogue;

/// <summary>
/// Provider-neutral readiness boundary for the configured catalogue store.
/// </summary>
public interface ICatalogueStoreInitializer
{
    Task InitializeAsync(CancellationToken cancellationToken = default);
}
