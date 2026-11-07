using Npgsql;
using PhotoIdentity.Core.Catalogue;
using PhotoIdentity.Core.Identifiers;
using PhotoIdentity.Core.Recognition;
using PhotoIdentity.Core.Sources;
using PhotoIdentity.Persistence.Postgres;
using PhotoIdentity.Source.OneDriveSync;

namespace PhotoIdentity.Api;

/// <summary>
/// Stable safety decision for local app-owned receipts after restart.
/// </summary>
public enum VideoManagedLocalReuseDisposition
{
    Unowned,
    Reuse,
    ReleasePending,
    NoLongerLocal
}

public static class VideoManagedLocalReusePolicy
{
    public static VideoManagedLocalReuseDisposition Evaluate(
        ArchiveManagedHydrationState? ownership,
        OneDriveFilesOnDemandState state)
    {
        if (ownership is not { IsActive: true })
            return VideoManagedLocalReuseDisposition.Unowned;
        if (ownership.IsReleaseRequested)
            return VideoManagedLocalReuseDisposition.ReleasePending;
        return state.Availability == AssetAvailability.Local
            ? VideoManagedLocalReuseDisposition.Reuse
            : VideoManagedLocalReuseDisposition.NoLongerLocal;
    }
}

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
        ValidateSelectedSourcePath(candidate, sourcePath);

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

                // Claim is durable before a transient Cloud Files hydration.
                // Unlike photo hydration, this video-specific request does
                // NOT set the +p pin bit: it can be safely offered back to
                // OneDrive when no dependent jobs need the bytes.
                await oneDrive.RequestUnpinnedVideoHydrationAsync(
                    sourcePath, token);
            }, token);

        if (!admitted.Allowed)
            throw new InvalidOperationException(
                "Shared managed hydration budget denied video source admission.");
    }


    /// <summary>
    /// First inspection of an online-only video uses durable source identity:
    /// the content revision/hash does not exist yet. Photo hydration never
    /// handles this lane; Claim happens BEFORE unpinned Cloud Files hydration.
    /// </summary>
    public async Task RequestForUninspectedAsync(
        ArchiveSourceObservationSnapshot observation, string sourcePath,
        CancellationToken token = default)
    {
        ArgumentNullException.ThrowIfNull(observation);
        ValidateProvisionalPath(observation, sourcePath);
        if (observation.ObservedSizeBytes is < 1 or > 256L * 1024 * 1024 ||
            observation.MediaType is not ("video/mp4" or "video/quicktime"))
            throw new InvalidOperationException("Uninspected video exceeds admission bounds.");

        ArchiveManagedSourceHydrationState? existing =
            await sourceOwnership.GetAsync(observation.AssetId, token);
        if (existing is { IsActive: true, IsReleaseRequested: true })
            throw new InvalidOperationException("Provisional video source is releasing.");

        ArchiveHydrationAdmission admitted =
            await capacity.ExecuteSourceHydrationAdmissionAsync(
                observation, async () =>
                {
                    if (!await IsCurrentProvisionalAsync(observation, token))
                        throw new InvalidOperationException(
                            "Uninspected source identity, metadata or exclusion changed.");
                    if ((await new PostgresVideoPhotoPriorityProbe(database)
                            .GetAsync(token)).PhotoWorkActive)
                        throw new InvalidOperationException(
                            "Photo work has priority over video inspection.");
                    if (await sourceOwnership.GetAsync(
                            observation.AssetId, token) is
                            { IsActive: true, IsReleaseRequested: true })
                        throw new InvalidOperationException(
                            "Uninspected video is already being released.");

                    OneDriveFilesOnDemandState state = oneDrive.GetState(sourcePath);
                    if (state.Availability != AssetAvailability.OnlineOnly ||
                        state.IsPinned)
                        throw new InvalidOperationException(
                            "Video first inspection requires an unpinned online-only placeholder.");
                    if (!MatchesProvisionalObservation(observation, sourcePath))
                        throw new InvalidOperationException(
                            "Video placeholder changed before inspection admission.");

                    await sourceOwnership.ClaimAsync(
                        observation.AssetId, timeProvider.GetUtcNow(), token);
                    await oneDrive.RequestUnpinnedVideoHydrationAsync(sourcePath, token);
                }, token);
        if (!admitted.Allowed)
            throw new InvalidOperationException(
                "Shared source hydration capacity denied video first inspection.");
    }

    public Task<bool> PromoteInspectedSourceAsync(
        AssetId assetId, AssetRevisionId revisionId,
        CancellationToken token = default) =>
        sourceOwnership.TransferToRevisionAsync(
            assetId, revisionId, timeProvider.GetUtcNow(), token);

    /// <summary>
    /// A failed, interrupted or unsupported pre-revision inspection must not
    /// leak app-owned local bytes. Release is source-locked, revision-empty,
    /// unpinned and protected against source mutation or user-owned copies.
    /// </summary>
    public async Task<bool> ReleaseUninspectedAsync(
        ArchiveSourceObservationSnapshot observation, string path,
        CancellationToken token = default)
    {
        ValidateProvisionalPath(observation, path);
        if (!await IsCurrentProvisionalAsync(observation, token))
            return false;
        ArchiveManagedSourceHydrationState? lease =
            await sourceOwnership.GetAsync(observation.AssetId, token);
        if (lease is not { IsActive: true }) return false;
        if ((await new PostgresVideoPhotoPriorityProbe(database)
                .GetAsync(token)).PhotoWorkActive)
            return false;
        OneDriveFilesOnDemandState state = oneDrive.GetState(path);
        if (state.IsPinned) return false;

        if (state.Availability == AssetAvailability.OnlineOnly)
        {
            // No data to release if hydration was requested but never started.
            await sourceOwnership.MarkReleasedAsync(
                observation.AssetId, timeProvider.GetUtcNow(), token);
            return true;
        }
        if (state.Availability != AssetAvailability.Local ||
            !MatchesProvisionalObservation(observation, path))
            return false;

        await using NpgsqlConnection conn = await database.OpenConnectionAsync(token);
        await using NpgsqlTransaction transaction =
            await conn.BeginTransactionAsync(token);
        // Source-row fencing is shared with exclusion and verified-revision
        // writes; a new revision would transfer this receipt instead.
        await using (NpgsqlCommand check = conn.CreateCommand())
        {
            check.Transaction = transaction;
            check.CommandText = """
                SELECT source.id FROM archive_source_managed_hydrations lease
                JOIN assets asset ON asset.id=lease.asset_id
                JOIN sources source ON source.id=asset.source_id
                JOIN archive_source_observations observed ON observed.asset_id=asset.id
                WHERE lease.asset_id=@asset AND lease.released_at_utc IS NULL
                  AND asset.media_kind='video' AND asset.deleted_at_utc IS NULL
                  AND source.id=@source AND source.root_locator=@root
                  AND asset.source_key=@key
                  AND observed.observed_size_bytes=@size
                  AND observed.observed_last_write_ticks=@ticks
                  AND NOT EXISTS (
                    SELECT 1 FROM asset_revisions revision
                    JOIN video_revision_metadata inspection
                      ON inspection.asset_revision_id=revision.id
                    WHERE revision.asset_id=asset.id AND revision.media_kind='video')
                  AND NOT EXISTS (
                    SELECT 1 FROM source_copy_exclusions exclusion
                    WHERE exclusion.source_id=asset.source_id
                      AND exclusion.source_key=asset.source_key)
                FOR UPDATE OF source, lease;
                """;
            check.Parameters.AddWithValue("asset", observation.AssetId.Value);
            check.Parameters.AddWithValue("source", observation.SourceId.Value);
            check.Parameters.AddWithValue("root", observation.RootLocator);
            check.Parameters.AddWithValue("key", observation.SourceKey);
            check.Parameters.AddWithValue("size", observation.ObservedSizeBytes);
            check.Parameters.AddWithValue("ticks", observation.ObservedLastWriteTimeUtc.UtcTicks);
            if (await check.ExecuteScalarAsync(token) is not Guid)
                return false;
        }
        // Recheck pinned state immediately before durable release request.
        if (oneDrive.GetState(path) is not
            { Availability: AssetAvailability.Local, IsPinned: false })
            return false;
        await using (NpgsqlCommand update = conn.CreateCommand())
        {
            update.Transaction = transaction;
            update.CommandText = """
                UPDATE archive_source_managed_hydrations
                SET release_requested_at_utc=COALESCE(
                    release_requested_at_utc,@now)
                WHERE asset_id=@asset AND released_at_utc IS NULL;
                """;
            update.Parameters.AddWithValue("asset", observation.AssetId.Value);
            update.Parameters.AddWithValue("now", timeProvider.GetUtcNow());
            if (await update.ExecuteNonQueryAsync(token) != 1)
                return false;
        }
        await transaction.CommitAsync(token);
        // Native platform refuses if it is pinned in the meantime.
        await oneDrive.RequestUnpinnedVideoReleaseAsync(path, token);
        return true;
    }

    /// <summary>
    /// Operator-started bounded recovery of unfinished source-level video
    /// hydration; no background scanner and no arbitrary photo source release.
    /// </summary>
    public async Task ReconcileUninspectedAsync(CancellationToken token = default)
    {
        var repository = new PostgresVideoProvisionalRepository(database);
        foreach (ProvisionalVideoHydration pending in
                 await repository.GetActiveAsync(8, token))
        {
            token.ThrowIfCancellationRequested();
            ArchiveSourceObservationSnapshot observation = pending.Observation;
            try
            {
                var selected = new VideoArchiveCandidate(
                    Guid.Empty, observation.SourceId.Value,
                    observation.RootLocator, observation.SourceKey,
                    observation.ObservedSizeBytes);
                string path = ResolveProvisionalPath(observation);
                OneDriveFilesOnDemandState state = oneDrive.GetState(path);
                if (state.IsPinned) continue;
                if (pending.ReleaseRequested)
                {
                    if (pending.LatestRevisionId is null)
                        await ReleaseUninspectedAsync(observation, path, token);
                    continue;
                }
                if (pending.LatestRevisionId is AssetRevisionId revision &&
                    state.Availability == AssetAvailability.Local)
                {
                    // Registration succeeded before crash, but ownership
                    // promotion did not. Never silently lose the source lease.
                    if (await IsCurrentRegisteredAsync(
                            selected, revision, token))
                        await PromoteInspectedSourceAsync(
                            observation.AssetId, revision, token);
                }
                // Still uninspected: the normal bounded discovery lane resumes
                // an already-local or online-only placeholder on this advance.
            }
            catch (OperationCanceledException) when (token.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception)
            {
                // Retain the conservative durable lease for a later advance.
            }
        }
    }

    private static string ResolveProvisionalPath(
        ArchiveSourceObservationSnapshot observation) =>
        Path.GetFullPath(Path.Combine(observation.RootLocator,
            observation.SourceKey.Replace('/', Path.DirectorySeparatorChar)));

    private static void ValidateProvisionalPath(
        ArchiveSourceObservationSnapshot observation, string path)
    {
        var candidate = new VideoArchiveCandidate(
            Guid.Empty, observation.SourceId.Value, observation.RootLocator,
            observation.SourceKey, observation.ObservedSizeBytes);
        ValidateSelectedSourcePath(candidate, path);
        if (observation.VerificationState !=
                ArchiveSourceObservationVerificationState.Unverified)
            throw new InvalidOperationException(
                "Only an unverified video source can enter provisional hydration.");
    }

    private static bool MatchesProvisionalObservation(
        ArchiveSourceObservationSnapshot observation, string path)
    {
        FileInfo file = new(path);
        file.Refresh();
        return file.Exists &&
            file.Length == observation.ObservedSizeBytes &&
            file.LastWriteTimeUtc == observation.ObservedLastWriteTimeUtc.UtcDateTime;
    }

    private async Task<bool> IsCurrentProvisionalAsync(
        ArchiveSourceObservationSnapshot observation, CancellationToken token)
    {
        await using NpgsqlConnection conn = await database.OpenConnectionAsync(token);
        await using NpgsqlCommand cmd = conn.CreateCommand();
        cmd.CommandText = """
            SELECT EXISTS(
                SELECT 1 FROM archive_source_observations observed
                JOIN assets asset ON asset.id=observed.asset_id
                JOIN sources source ON source.id=asset.source_id
                WHERE asset.id=@asset AND asset.media_kind='video'
                  AND asset.deleted_at_utc IS NULL
                  AND source.id=@source AND source.kind='local-folder'
                  AND source.root_locator=@root AND asset.source_key=@key
                  AND observed.observed_size_bytes=@size
                  AND observed.observed_last_write_ticks=@ticks
                  AND observed.observed_media_type=@media
                  AND NOT EXISTS (
                    SELECT 1 FROM asset_revisions revision
                    JOIN video_revision_metadata inspection
                      ON inspection.asset_revision_id=revision.id
                    WHERE revision.asset_id=asset.id AND revision.media_kind='video')
                  AND NOT EXISTS (
                    SELECT 1 FROM source_copy_exclusions exclusion
                    WHERE exclusion.source_id=asset.source_id
                      AND exclusion.source_key=asset.source_key));
            """;
        cmd.Parameters.AddWithValue("asset", observation.AssetId.Value);
        cmd.Parameters.AddWithValue("source", observation.SourceId.Value);
        cmd.Parameters.AddWithValue("root", observation.RootLocator);
        cmd.Parameters.AddWithValue("key", observation.SourceKey);
        cmd.Parameters.AddWithValue("size", observation.ObservedSizeBytes);
        cmd.Parameters.AddWithValue("ticks", observation.ObservedLastWriteTimeUtc.UtcTicks);
        cmd.Parameters.AddWithValue("media", observation.MediaType);
        return await cmd.ExecuteScalarAsync(token) is true;
    }

    private async Task<bool> IsCurrentRegisteredAsync(
        VideoArchiveCandidate selected, AssetRevisionId revision,
        CancellationToken token)
    {
        await using NpgsqlConnection conn = await database.OpenConnectionAsync(token);
        await using NpgsqlCommand cmd = conn.CreateCommand();
        cmd.CommandText = """
            SELECT EXISTS(
                SELECT 1 FROM asset_revisions revision
                JOIN assets asset ON asset.id=revision.asset_id
                JOIN sources source ON source.id=asset.source_id
                WHERE revision.id=@revision AND revision.media_kind='video'
                  AND asset.media_kind='video' AND asset.deleted_at_utc IS NULL
                  AND source.id=@source AND source.root_locator=@root
                  AND asset.source_key=@key
                  AND revision.id=(
                    SELECT newest.id FROM asset_revisions newest
                    WHERE newest.asset_id=asset.id AND newest.media_kind='video'
                    ORDER BY newest.observed_at_utc DESC,newest.id DESC LIMIT 1)
                  AND EXISTS (
                    SELECT 1 FROM video_revision_metadata inspection
                    WHERE inspection.asset_revision_id=revision.id)
                  AND NOT EXISTS (
                    SELECT 1 FROM source_copy_exclusions exclusion
                    WHERE exclusion.source_id=asset.source_id
                      AND exclusion.source_key=asset.source_key));
            """;
        cmd.Parameters.AddWithValue("revision", revision.Value);
        cmd.Parameters.AddWithValue("source", selected.SourceId);
        cmd.Parameters.AddWithValue("root", selected.Root);
        cmd.Parameters.AddWithValue("key", selected.Key);
        return await cmd.ExecuteScalarAsync(token) is true;
    }

    /// <summary>
    /// Recover a prior app-owned local video after a pause, failed attempt
    /// or process restart. This performs no new OneDrive operations. A
    /// previously local user-owned original has no managed receipt and
    /// is never claimed/released by this path. Active release refuses
    /// reuse rather than racing an outstanding +u request.
    /// </summary>
    public async Task<bool> ResumeIfManagedLocalAsync(
        VideoArchiveCandidate candidate, string sourcePath,
        CancellationToken token = default)
    {
        ArgumentNullException.ThrowIfNull(candidate);
        ArgumentException.ThrowIfNullOrWhiteSpace(sourcePath);
        ValidateSelectedSourcePath(candidate, sourcePath);

        AssetRevisionId revisionId = AssetRevisionId.From(candidate.RevisionId);
        ArchiveManagedHydrationState? stored =
            await ownership.GetAsync(revisionId, token);
        if (stored is not { IsActive: true })
            return false; // Pre-existing local user original: no ownership claim.

        AssetRevisionLookup? current =
            await ReadCurrentVideoRevisionAsync(candidate, token);
        if (current is null)
            throw new InvalidOperationException(
                "Managed video source is no longer current or authorized.");

        if (await sourceOwnership.GetAsync(
                current.AssetId, token) is { IsActive: true })
            throw new InvalidOperationException(
                "Source-level hydration already owns this video asset.");

        OneDriveFilesOnDemandState state = oneDrive.GetState(sourcePath);
        VideoManagedLocalReuseDisposition decision =
            VideoManagedLocalReusePolicy.Evaluate(stored, state);
        if (decision != VideoManagedLocalReuseDisposition.Reuse)
            throw new InvalidOperationException(
                "Previously app-hydrated video is releasing or no longer local.");

        await capacity.TouchAsync(revisionId, token);
        return true;
    }

    private static void ValidateSelectedSourcePath(
        VideoArchiveCandidate candidate, string sourcePath)
    {
        // Never treat a caller-provided path as permission to hydrate.
        // Only the exact current catalogue candidate is allowed.
        if (!Path.IsPathFullyQualified(candidate.Root) ||
            candidate.Root.StartsWith(@"\\", StringComparison.Ordinal) ||
            Path.IsPathRooted(candidate.Key) ||
            candidate.Key.Contains(':'))
            throw new InvalidOperationException(
                "Catalogue video path is not a safe local archive locator.");

        string root = Path.TrimEndingDirectorySeparator(
            Path.GetFullPath(candidate.Root));
        string expectedPath = Path.GetFullPath(Path.Combine(
            root, candidate.Key.Replace('/', Path.DirectorySeparatorChar)));
        if (!expectedPath.StartsWith(
                root + Path.DirectorySeparatorChar,
                StringComparison.OrdinalIgnoreCase) ||
            !string.Equals(expectedPath, Path.GetFullPath(sourcePath),
                StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException(
                "Selected video original does not match its catalogue locator.");

        VideoDerivativeCacheGuard.AssertNoLinks(
            Path.GetDirectoryName(expectedPath)!);
    }

    /// <summary>
    /// After derivative completion or on a later operator-started idle
    /// sweep, request release of only a still-authorized, still-unpinned
    /// app-owned video. Source-row locking and the enqueue/restart release
    /// gate ensure new derivative jobs cannot race the committed request.
    /// </summary>
    public async Task<bool> ReleaseIfIdleAsync(
        VideoArchiveCandidate candidate, string path,
        CancellationToken token = default)
    {
        ValidateSelectedSourcePath(candidate, path);
        if (await ReadCurrentVideoRevisionAsync(candidate, token) is null)
            return false;
        return await TryRequestReleaseAsync(
            AssetRevisionId.From(candidate.RevisionId), path, token);
    }

    public async Task<int> ReleaseIdleBatchAsync(
        int maximum = 4, CancellationToken token = default)
    {
        if (maximum is < 1 or > 8)
            throw new ArgumentOutOfRangeException(nameof(maximum));
        var idle = await ownership.GetIdleVideoHydrationsAsync(maximum, token);
        int requested = 0;
        foreach (ArchiveManagedHydrationLeaseState lease in idle)
        {
            token.ThrowIfCancellationRequested();
            string? path = VideoManagedHydrationRecovery.TryResolveMetadataPath(
                lease.RootLocator, lease.SourceKey);
            if (path is null) continue;
            try
            {
                if (await TryRequestReleaseAsync(
                        lease.AssetRevisionId, path, token))
                    requested++;
            }
            catch (OperationCanceledException) when (token.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception)
            {
                // Local bytes and durable receipt remain; the next
                // operator Archive advance may retry a pending release.
                // Do not return private paths or cloud-provider details.
            }
        }
        return requested;
    }

    private async Task<bool> TryRequestReleaseAsync(
        AssetRevisionId revisionId, string path, CancellationToken token)
    {
        ArchiveManagedHydrationState? managed =
            await ownership.GetAsync(revisionId, token);
        if (managed is not { IsActive: true } || managed.IsReleaseRequested)
            return false;
        if ((await new PostgresVideoPhotoPriorityProbe(database)
                .GetAsync(token)).PhotoWorkActive)
            return false;

        OneDriveFilesOnDemandState current = oneDrive.GetState(path);
        if (current.Availability != AssetAvailability.Local ||
            current.IsPinned)
            return false;

        // Source row serialization + a pending-release fence makes
        // new video enqueues/restarts wait and then refuse the release
        // window; queued/running jobs veto release.
        if (!await ownership.TryRequestIdleVideoReleaseAsync(
                revisionId, timeProvider.GetUtcNow(), token))
            return false;

        // Request only +u; never clear an app/user +p pin.
        // If the sync client refuses, leave the pending durable receipt.
        await oneDrive.RequestUnpinnedVideoReleaseAsync(path, token);
        return true;
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
