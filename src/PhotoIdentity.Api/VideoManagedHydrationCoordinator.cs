using Npgsql;
using PhotoIdentity.Core.Catalogue;
using PhotoIdentity.Core.Identifiers;
using PhotoIdentity.Core.Recognition;
using PhotoIdentity.Core.Sources;
using PhotoIdentity.Persistence.Postgres;
using PhotoIdentity.Source.OneDriveSync;

namespace PhotoIdentity.Api;

/// <summary>
/// WI-0207 shared-budget bridge for already-authorized video Archive work.
/// No downloader, background worker or new per-video storage cache.
/// Persist an app-owned revision receipt before any OneDrive +p request,
/// using the same bounded photo/source hydration admission service.
/// </summary>
public sealed class VideoManagedHydrationCoordinator(
    PostgresCatalogueDatabase database,
    IArchiveHydrationRepository ownership,
    IArchiveSourceHydrationRepository sourceOwnership,
    ArchiveHydrationCapacityService capacity,
    IOneDriveFilesOnDemandPlatform oneDrive,
    TimeProvider timeProvider)
{
    public async Task RequestForSelectedAsync(
        VideoArchiveCandidate candidate, string sourcePath,
        CancellationToken token = default)
    {
        ArgumentNullException.ThrowIfNull(candidate);
        ArgumentException.ThrowIfNullOrWhiteSpace(sourcePath);
        // The public service method may never be used as an arbitrary
        // caller-supplied OneDrive path action.
        string expectedPath = Path.GetFullPath(Path.Combine(
            candidate.Root,
            candidate.Key.Replace('/', Path.DirectorySeparatorChar)));
        if (!string.Equals(expectedPath, Path.GetFullPath(sourcePath),
                StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException(
                "Selected video original does not match its catalogue locator.");

        AssetRevisionLookup? revision =
            await ReadCurrentVideoRevisionAsync(candidate, token);
        if (revision is null)
            throw new InvalidOperationException(
                "Current video revision, exclusion or source identity changed.");

        ArchiveManagedSourceHydrationState? pendingSource =
            await sourceOwnership.GetAsync(revision.AssetId, token);
        if (pendingSource is { IsActive: true })
            throw new InvalidOperationException(
                "A source-level managed hydration already owns this asset.");

        ArchiveManagedHydrationState? existing =
            await ownership.GetAsync(revision.RevisionId, token);
        if (existing is { IsActive: true, IsReleaseRequested: true })
            throw new InvalidOperationException(
                "An app-managed release is already in progress.");

        ArchiveHydrationAdmission admitted =
            await capacity.ExecuteHydrationAdmissionAsync(revision, async () =>
            {
                // Recheck the authoritative current/excluded revision and
                // OneDrive state under the shared admission gate, immediately
                // before the app would request bytes. A newly local original
                // may have been pinned by the user since selection.
                if (await ReadCurrentVideoRevisionAsync(candidate, token) is null)
                    throw new InvalidOperationException(
                        "Video source changed before hydration admission.");
                if ((await new PostgresVideoPhotoPriorityProbe(database)
                        .GetAsync(token)).PhotoWorkActive)
                    throw new InvalidOperationException(
                        "Photo work has priority over video hydration.");
                if (await sourceOwnership.GetAsync(
                        revision.AssetId, token) is { IsActive: true })
                    throw new InvalidOperationException(
                        "Source-level managed hydration is already active.");
                OneDriveFilesOnDemandState state = oneDrive.GetState(sourcePath);
                if (state.Availability != AssetAvailability.OnlineOnly ||
                    state.IsPinned)
                    throw new InvalidOperationException(
                        "Source is no longer an eligible unpinned placeholder.");

                ArchiveManagedHydrationState? current =
                    await ownership.GetAsync(revision.RevisionId, token);
                if (current is { IsActive: true, IsReleaseRequested: true })
                    throw new InvalidOperationException(
                        "Managed video source is being released.");

                if (current is { IsActive: true })
                    await capacity.TouchAsync(revision.RevisionId, token);
                else
                    await ownership.ClaimAsync(
                        revision.RevisionId, timeProvider.GetUtcNow(), token);

                // Claim is durable BEFORE requesting +p. A failed request
                // leaves a conservatively tracked lease, not an untracked
                // app-pinned original. The shared budget counts it as soon
                // as it becomes local/downloading.
                await oneDrive.RequestHydrationAsync(sourcePath, token);
            }, token);

        if (!admitted.Allowed)
            throw new InvalidOperationException(
                "Shared managed hydration budget denied video source admission.");
    }

    public Task TouchAsync(Guid revisionId, CancellationToken token = default) =>
        capacity.TouchAsync(AssetRevisionId.From(revisionId), token);

    public async Task MarkRequestedReleaseAsync(
        Guid revisionId, CancellationToken token = default)
    {
        await ownership.MarkReleaseRequestedAsync(
            AssetRevisionId.From(revisionId), timeProvider.GetUtcNow(), token);
    }

    private async Task<AssetRevisionLookup?> ReadCurrentVideoRevisionAsync(
        VideoArchiveCandidate selected, CancellationToken token)
    {
        await using NpgsqlConnection connection =
            await database.OpenConnectionAsync(token);
        await using NpgsqlCommand command = connection.CreateCommand();
        command.CommandText = """
            SELECT revision.asset_id, revision.content_sha256,
                   revision.media_type
            FROM asset_revisions revision
            JOIN assets asset ON asset.id = revision.asset_id
            JOIN sources source ON source.id = asset.source_id
            WHERE revision.id=@revision AND asset.source_id=@source
              AND source.root_locator=@root AND asset.source_key=@key
              AND revision.size_bytes=@bytes
              AND revision.media_kind='video' AND asset.media_kind='video'
              AND source.kind='local-folder'
              AND asset.deleted_at_utc IS NULL
              AND revision.id=(
                SELECT latest.id FROM asset_revisions latest
                WHERE latest.asset_id=asset.id AND latest.media_kind='video'
                ORDER BY latest.observed_at_utc DESC,latest.id DESC LIMIT 1)
              AND NOT EXISTS (
                SELECT 1 FROM source_copy_exclusions excluded
                WHERE excluded.source_id=asset.source_id
                  AND excluded.source_key=asset.source_key);
            """;
        command.Parameters.AddWithValue("revision", selected.RevisionId);
        command.Parameters.AddWithValue("source", selected.SourceId);
        command.Parameters.AddWithValue("root", selected.Root);
        command.Parameters.AddWithValue("key", selected.Key);
        command.Parameters.AddWithValue("bytes", selected.Size);
        await using NpgsqlDataReader reader =
            await command.ExecuteReaderAsync(token);
        if (!await reader.ReadAsync(token)) return null;

        Guid assetId = reader.GetGuid(0);
        string hash = reader.GetString(1);
        string? mediaType = reader.IsDBNull(2)
            ? null : reader.GetString(2);
        return new AssetRevisionLookup(
            AssetRevisionId.From(selected.RevisionId),
            AssetId.From(assetId),
            SourceId.From(selected.SourceId),
            "local-folder", selected.Root, selected.Key,
            new Sha256Digest(hash), selected.Size, mediaType);
    }
}
