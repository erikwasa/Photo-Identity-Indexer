using System.Globalization;
using Npgsql;
using PhotoIdentity.Core.Identifiers;
using PhotoIdentity.Core.Recognition;

namespace PhotoIdentity.Persistence.Postgres;

public sealed partial class PostgresArchiveAnalysisStateRepository
{
    public Task<IReadOnlyList<AssetRevisionId>> GetPendingCurrentRevisionIdsAsync(
        SourceId sourceId,
        Sha256Digest profileHash,
        CancellationToken cancellationToken = default) =>
        GetPendingCurrentRevisionIdsAsync(sourceId, profileHash, includeHydratable: false, cancellationToken);

    public async Task<IReadOnlyList<AssetRevisionId>> GetPendingCurrentRevisionIdsAsync(
        SourceId sourceId,
        Sha256Digest profileHash,
        bool includeHydratable,
        CancellationToken cancellationToken = default)
    {
        await using NpgsqlConnection connection = await _database.OpenConnectionAsync(cancellationToken);
        using NpgsqlCommand command = connection.CreateCommand();
        command.CommandText = """
            SELECT revision.id
            FROM assets AS asset
            INNER JOIN asset_revisions AS revision
                ON revision.id = (
                    SELECT candidate.id
                    FROM asset_revisions AS candidate
                    WHERE candidate.asset_id = asset.id
                    ORDER BY candidate.observed_at_utc DESC, candidate.id DESC
                    LIMIT 1)
            LEFT JOIN archive_asset_availability AS availability
                ON availability.asset_id = asset.id
            LEFT JOIN archive_source_observations AS source_observation
                ON source_observation.asset_id = asset.id
            LEFT JOIN asset_revision_analysis AS analysis
                ON analysis.asset_revision_id = revision.id
               AND analysis.profile_hash = @profile_hash
            WHERE asset.source_id = @source_id
              AND asset.deleted_at_utc IS NULL
              AND COALESCE(source_observation.verification_state, 'verified') = 'verified'
              AND (
                    COALESCE(availability.availability, 'local') = 'local'
                    OR (@include_hydratable AND availability.availability IN ('online-only', 'downloading'))
                  )
              AND analysis.asset_revision_id IS NULL
            ORDER BY asset.source_key;
            """;
        command.Parameters.AddWithValue("@source_id", Guid.Parse(sourceId.ToString()));
        command.Parameters.AddWithValue("@profile_hash", profileHash.ToString());
        command.Parameters.AddWithValue("@include_hydratable", includeHydratable);

        List<AssetRevisionId> revisions = [];
        await using NpgsqlDataReader reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            revisions.Add(AssetRevisionId.From(reader.GetGuid(0)));
        }

        return revisions;
    }

    public async Task<int> CountCompletedCurrentRevisionsAsync(
        SourceId sourceId,
        Sha256Digest profileHash,
        CancellationToken cancellationToken = default)
    {
        await using NpgsqlConnection connection = await _database.OpenConnectionAsync(cancellationToken);
        using NpgsqlCommand command = connection.CreateCommand();
        command.CommandText = """
            SELECT COUNT(*)
            FROM assets AS asset
            INNER JOIN asset_revisions AS revision
                ON revision.id = (
                    SELECT candidate.id
                    FROM asset_revisions AS candidate
                    WHERE candidate.asset_id = asset.id
                    ORDER BY candidate.observed_at_utc DESC, candidate.id DESC
                    LIMIT 1)
            INNER JOIN asset_revision_analysis AS analysis
                ON analysis.asset_revision_id = revision.id
               AND analysis.profile_hash = @profile_hash
            LEFT JOIN archive_source_observations AS source_observation
                ON source_observation.asset_id = asset.id
            WHERE asset.source_id = @source_id
              AND asset.deleted_at_utc IS NULL
              AND COALESCE(source_observation.verification_state, 'verified') = 'verified';
            """;
        command.Parameters.AddWithValue("@source_id", Guid.Parse(sourceId.ToString()));
        command.Parameters.AddWithValue("@profile_hash", profileHash.ToString());
        object? value = await command.ExecuteScalarAsync(cancellationToken);
        return Convert.ToInt32(value, CultureInfo.InvariantCulture);
    }

}
