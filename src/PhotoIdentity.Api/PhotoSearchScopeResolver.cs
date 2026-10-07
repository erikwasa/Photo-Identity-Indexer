using PhotoIdentity.Core.Collections;
using PhotoIdentity.Core.Identifiers;

namespace PhotoIdentity.Api;

public sealed record PhotoSearchScope(
    SmartCollectionId CollectionId,
    string CollectionName,
    IReadOnlySet<AssetRevisionId> RevisionIds)
{
    public int EligiblePhotoCount => RevisionIds.Count;
}

public sealed record PhotoSearchScopeSummary(
    string CollectionId,
    string CollectionName,
    int EligiblePhotoCount);

public sealed class PhotoSearchScopeNotFoundException : Exception
{
    public PhotoSearchScopeNotFoundException(SmartCollectionId collectionId)
        : base($"Smart Collection '{collectionId}' was not found.")
    {
        CollectionId = collectionId;
    }

    public SmartCollectionId CollectionId { get; }
}

/// <summary>
/// Resolves a saved Smart Collection into one server-side eligibility set. Structured Smart
/// Collection predicates remain authoritative; ranked semantic/caption search only sees revisions
/// that survived this exact scope.
/// </summary>
public sealed class PhotoSearchScopeResolver
{
    private readonly ISmartCollectionRepository _definitions;
    private readonly ISmartCollectionQueryRepository _query;

    public PhotoSearchScopeResolver(
        ISmartCollectionRepository definitions,
        ISmartCollectionQueryRepository query)
    {
        ArgumentNullException.ThrowIfNull(definitions);
        ArgumentNullException.ThrowIfNull(query);
        _definitions = definitions;
        _query = query;
    }

    public async Task<PhotoSearchScope?> ResolveAsync(
        string? collectionId,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(collectionId))
        {
            return null;
        }

        if (!Guid.TryParse(collectionId.Trim(), out Guid parsed) || parsed == Guid.Empty)
        {
            throw new ArgumentException(
                $"Smart Collection identifier '{collectionId}' is not a valid non-empty GUID.",
                nameof(collectionId));
        }

        return await ResolveAsync(SmartCollectionId.From(parsed), cancellationToken);
    }

    public async Task<PhotoSearchScope> ResolveAsync(
        SmartCollectionId collectionId,
        CancellationToken cancellationToken = default)
    {
        SmartCollectionDefinition? definition =
            await _definitions.GetAsync(collectionId, cancellationToken);
        if (definition is null)
        {
            throw new PhotoSearchScopeNotFoundException(collectionId);
        }

        IReadOnlyList<SmartCollectionPhoto> photos =
            await _query.QueryAllAsync(definition.Filter, cancellationToken);
        HashSet<AssetRevisionId> revisionIds =
            photos.Select(photo => photo.RevisionId).ToHashSet();

        return new PhotoSearchScope(
            definition.Id,
            definition.Name,
            revisionIds);
    }
}
