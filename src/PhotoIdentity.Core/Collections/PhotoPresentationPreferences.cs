using PhotoIdentity.Core.Identifiers;

namespace PhotoIdentity.Core.Collections;

public static class PhotoPresentationPreferenceKinds
{
    public const string Prefer = "prefer";
    public const string Avoid = "avoid";

    public static string Normalize(string value)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(value);
        string normalized = value.Trim().ToLowerInvariant();
        return normalized switch
        {
            Prefer => Prefer,
            Avoid => Avoid,
            _ => throw new ArgumentException(
                "Photo presentation preference must be 'prefer' or 'avoid'.",
                nameof(value)),
        };
    }
}

public static class PhotoPresentationPreferenceActionKinds
{
    public const string Set = "set";
    public const string Clear = "clear";
}

public sealed record PhotoPresentationPreferenceAction(
    long Id,
    AssetRevisionId RevisionId,
    string ActionKind,
    string? Preference,
    string Actor,
    DateTimeOffset CreatedAtUtc);

public sealed record PhotoPresentationPreferenceState(
    AssetRevisionId RevisionId,
    string? Preference,
    IReadOnlyList<PhotoPresentationPreferenceAction> History);

public interface IPhotoPresentationPreferenceRepository
{
    Task<PhotoPresentationPreferenceState> GetStateAsync(
        AssetRevisionId revisionId,
        CancellationToken cancellationToken = default);

    Task<IReadOnlyDictionary<AssetRevisionId, string>> GetEffectiveAsync(
        IEnumerable<AssetRevisionId> revisionIds,
        CancellationToken cancellationToken = default);

    Task<PhotoPresentationPreferenceState> SetAsync(
        AssetRevisionId revisionId,
        string preference,
        string actor,
        CancellationToken cancellationToken = default);

    Task<PhotoPresentationPreferenceState> ClearAsync(
        AssetRevisionId revisionId,
        string actor,
        CancellationToken cancellationToken = default);
}
