namespace PhotoIdentity.Core.Sources;

/// <summary>
/// Versions the persisted photo-metadata extraction contract. Increment CurrentVersion only when
/// already-inspected revisions must be read again to populate materially new or changed persisted
/// metadata semantics.
/// </summary>
public static class PhotoMetadataExtractionContract
{
    /// <summary>
    /// Capture-time/GPS-only metadata persisted before WI-0072 introduced the richer metadata set.
    /// Legacy rows without an explicit inspection-version marker are treated as this version.
    /// </summary>
    public const int LegacyVersion = 1;

    /// <summary>
    /// WI-0072 richer metadata contract: capture time/GPS plus camera, lens, exposure, GPS altitude
    /// and bounded sanitized raw metadata tags.
    /// </summary>
    public const int CurrentVersion = 2;
}
