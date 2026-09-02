namespace PhotoIdentity.Core.Sources;

public sealed record ArchiveCoverageState(
    ArchiveCatalogueSource Source,
    IReadOnlyList<string> IncludedFolders);

/// <summary>
/// Persists the single permanent archive source and its recursively included folders.
/// </summary>
public interface IArchiveCoverageRepository
{
    Task<ArchiveCoverageState?> GetAsync(
        CancellationToken cancellationToken = default);

    Task<ArchiveCoverageState> ConfigureAndIncludeAsync(
        ArchiveCatalogueSource source,
        string relativeFolder,
        DateTimeOffset configuredAtUtc,
        CancellationToken cancellationToken = default);

    Task<ArchiveCoverageState> ReplaceIncludedFoldersAsync(
        IEnumerable<string> relativeFolders,
        DateTimeOffset configuredAtUtc,
        CancellationToken cancellationToken = default);
}
