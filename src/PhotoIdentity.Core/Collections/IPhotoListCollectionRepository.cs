using PhotoIdentity.Core.Identifiers;

namespace PhotoIdentity.Core.Collections;

public sealed class PhotoListCollectionNameConflictException : Exception
{
    public PhotoListCollectionNameConflictException(string name)
        : base($"A photo-list collection named '{name}' already exists.")
    {
    }
}

public sealed class PhotoListCollectionRevisionUnavailableException : Exception
{
    public PhotoListCollectionRevisionUnavailableException(IReadOnlyList<AssetRevisionId> revisionIds)
        : base(
            revisionIds.Count == 1
                ? $"Revision '{revisionIds[0]}' is not available for a photo-list collection."
                : $"{revisionIds.Count} revisions are not available for a photo-list collection.")
    {
        RevisionIds = revisionIds;
    }

    public IReadOnlyList<AssetRevisionId> RevisionIds { get; }
}

public interface IPhotoListCollectionRepository
{
    Task<PhotoListCollectionDefinition> CreateAsync(
        string name,
        IReadOnlyList<AssetRevisionId> revisionIds,
        CancellationToken cancellationToken = default);

    Task<IReadOnlyList<PhotoListCollectionDefinition>> ListAsync(
        CancellationToken cancellationToken = default);

    Task<PhotoListCollectionDefinition?> GetAsync(
        PhotoListCollectionId id,
        CancellationToken cancellationToken = default);

    Task<PhotoListCollectionDefinition?> UpdateAsync(
        PhotoListCollectionId id,
        string name,
        IReadOnlyList<AssetRevisionId> revisionIds,
        CancellationToken cancellationToken = default);

    Task<bool> DeleteAsync(
        PhotoListCollectionId id,
        CancellationToken cancellationToken = default);

    Task<PhotoListCollectionSlideshowSnapshot?> CreateSlideshowSnapshotAsync(
        PhotoListCollectionId id,
        CancellationToken cancellationToken = default);
}
