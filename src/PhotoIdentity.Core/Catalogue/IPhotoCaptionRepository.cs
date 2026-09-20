using PhotoIdentity.Core.Identifiers;

namespace PhotoIdentity.Core.Catalogue;

public static class PhotoCaptionLanguages
{
    public const string Swedish = "sv";
    public const string English = "en";
    public const string Default = Swedish;

    public static string Normalize(string? value)
    {
        string normalized = value?.Trim().ToLowerInvariant() ?? Default;
        return normalized switch
        {
            Swedish => Swedish,
            English => English,
            _ => throw new ArgumentException("Caption language must be 'sv' or 'en'.", nameof(value)),
        };
    }

    public static bool TryNormalize(string? value, out string normalized)
    {
        try
        {
            normalized = Normalize(value);
            return true;
        }
        catch (ArgumentException)
        {
            normalized = string.Empty;
            return false;
        }
    }
}

public sealed record PhotoCaptionEnrichmentSettings(
    bool Enabled,
    string Language,
    DateTimeOffset UpdatedAtUtc);

public sealed record PhotoGeneratedCaption(
    AssetRevisionId RevisionId,
    string Language,
    string GenerationVersion,
    string ModelId,
    string ModelDigest,
    string PromptVersion,
    string ImageMode,
    int ContextTokens,
    string? Content,
    IReadOnlyList<string> RiskFlags,
    double GenerationMilliseconds,
    DateTimeOffset GeneratedAtUtc)
{
    public bool IsDisplayable =>
        RiskFlags.Count == 0 &&
        !string.IsNullOrWhiteSpace(Content);

    public string? DisplayableContent =>
        IsDisplayable ? Content : null;
}

/// <summary>
/// Stores optional generated photo-description evidence independently of any collection or
/// presentation surface. Consumers may read this data, but they do not control generation.
/// </summary>
public interface IPhotoCaptionRepository
{
    Task<PhotoCaptionEnrichmentSettings> GetSettingsAsync(
        CancellationToken cancellationToken = default);

    Task<PhotoCaptionEnrichmentSettings> UpdateSettingsAsync(
        bool enabled,
        string language,
        DateTimeOffset updatedAtUtc,
        CancellationToken cancellationToken = default);

    Task<IReadOnlyList<AssetRevisionId>> GetCandidatesAsync(
        string reviewProxyProfileId,
        string language,
        string generationVersion,
        string modelId,
        string modelDigest,
        string promptVersion,
        string imageMode,
        int contextTokens,
        int limit,
        CancellationToken cancellationToken = default);

    Task SaveAsync(
        PhotoGeneratedCaption caption,
        CancellationToken cancellationToken = default);

    Task<PhotoGeneratedCaption?> GetLatestAsync(
        AssetRevisionId revisionId,
        string language,
        CancellationToken cancellationToken = default);
}
