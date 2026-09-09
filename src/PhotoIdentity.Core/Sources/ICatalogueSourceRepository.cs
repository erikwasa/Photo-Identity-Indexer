namespace PhotoIdentity.Core.Sources;

public interface ICatalogueSourceRepository
{
    Task<ArchiveCatalogueSource> GetOrCreateLocalFolderSourceAsync(string rootLocator,
        DateTimeOffset createdAtUtc, CancellationToken cancellationToken = default);
}
