using Npgsql;
using NpgsqlTypes;
using PhotoIdentity.Core.Identifiers;
using PhotoIdentity.Core.Places;

namespace PhotoIdentity.Persistence.Postgres;

/// <summary>
/// PostgreSQL operational state for reverse-geocoding candidate selection, cache and retry outcomes.
/// </summary>
public sealed class PostgresPhotoPlaceEnrichmentStateRepository :
    IPhotoPlaceEnrichmentStateRepository
{
    private readonly PostgresCatalogueDatabase _database;
    private readonly TimeProvider _timeProvider;

    public PostgresPhotoPlaceEnrichmentStateRepository(
        PostgresCatalogueDatabase database,
        TimeProvider timeProvider)
    {
        ArgumentNullException.ThrowIfNull(database);
        ArgumentNullException.ThrowIfNull(timeProvider);
        _database = database;
        _timeProvider = timeProvider;
    }

    public async Task<IReadOnlyList<PhotoPlaceEnrichmentCandidate>> GetCandidatesAsync(
        string provider,
        string contractKey,
        int limit,
        bool refresh,
        CancellationToken cancellationToken = default)
    {
        string normalizedProvider = NormalizeProvider(provider);
        string normalizedContract = NormalizeContractKey(contractKey);
        if (limit is < 1 or > 250)
        {
            throw new ArgumentOutOfRangeException(
                nameof(limit),
                "Place enrichment batch size must be between 1 and 250.");
        }

        await using NpgsqlConnection connection =
            await _database.OpenConnectionAsync(cancellationToken);
        await using NpgsqlCommand command = connection.CreateCommand();
        command.CommandText =
            """
            SELECT
                metadata.asset_revision_id,
                metadata.latitude,
                metadata.longitude
            FROM photo_capture_metadata AS metadata
            LEFT JOIN photo_place_enrichment_attempts AS attempt
                ON attempt.asset_revision_id = metadata.asset_revision_id
               AND attempt.provider = @provider
               AND attempt.contract_key = @contract_key
            WHERE metadata.latitude IS NOT NULL
              AND metadata.longitude IS NOT NULL
              AND (
                    attempt.asset_revision_id IS NULL
                    OR attempt.latitude <> metadata.latitude
                    OR attempt.longitude <> metadata.longitude
                    OR (
                        attempt.status <> 'skipped'
                        AND (
                            @refresh
                            OR attempt.status <> 'succeeded')))
            ORDER BY
                CASE
                    WHEN attempt.asset_revision_id IS NULL THEN 0
                    ELSE 1
                END,
                attempt.last_attempted_at_utc,
                metadata.asset_revision_id
            LIMIT @limit;
            """;
        command.Parameters.AddWithValue("provider", normalizedProvider);
        command.Parameters.AddWithValue("contract_key", normalizedContract);
        command.Parameters.AddWithValue("refresh", refresh);
        command.Parameters.AddWithValue("limit", limit);

        List<PhotoPlaceEnrichmentCandidate> candidates = [];
        await using NpgsqlDataReader reader =
            await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            candidates.Add(new PhotoPlaceEnrichmentCandidate(
                AssetRevisionId.From(reader.GetGuid(0)),
                reader.GetDouble(1),
                reader.GetDouble(2)));
        }

        return candidates;
    }

    public async Task<ReverseGeocodeCacheEntry?> GetCachedAsync(
        string provider,
        string contractKey,
        double latitude,
        double longitude,
        CancellationToken cancellationToken = default)
    {
        await using NpgsqlConnection connection =
            await _database.OpenConnectionAsync(cancellationToken);
        await using NpgsqlCommand command = connection.CreateCommand();
        command.CommandText =
            """
            SELECT
                place_value,
                provider_result_id,
                country_code,
                resolved_at_utc
            FROM photo_place_reverse_geocode_cache
            WHERE provider = @provider
              AND contract_key = @contract_key
              AND latitude = @latitude
              AND longitude = @longitude;
            """;
        command.Parameters.AddWithValue(
            "provider",
            NormalizeProvider(provider));
        command.Parameters.AddWithValue(
            "contract_key",
            NormalizeContractKey(contractKey));
        command.Parameters.AddWithValue("latitude", latitude);
        command.Parameters.AddWithValue("longitude", longitude);

        await using NpgsqlDataReader reader =
            await command.ExecuteReaderAsync(cancellationToken);
        if (!await reader.ReadAsync(cancellationToken))
        {
            return null;
        }

        return new ReverseGeocodeCacheEntry(
            reader.GetString(0),
            reader.IsDBNull(1) ? null : reader.GetString(1),
            reader.IsDBNull(2) ? null : reader.GetString(2),
            reader.GetFieldValue<DateTimeOffset>(3));
    }

    public async Task SaveCacheAsync(
        string provider,
        string contractKey,
        PhotoPlaceEnrichmentCandidate candidate,
        string placeValue,
        string? providerResultId,
        string? countryCode,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(candidate);
        ArgumentException.ThrowIfNullOrWhiteSpace(placeValue);

        await using NpgsqlConnection connection =
            await _database.OpenConnectionAsync(cancellationToken);
        await using NpgsqlCommand command = connection.CreateCommand();
        command.CommandText =
            """
            INSERT INTO photo_place_reverse_geocode_cache (
                provider,
                contract_key,
                latitude,
                longitude,
                place_value,
                provider_result_id,
                country_code,
                resolved_at_utc)
            VALUES (
                @provider,
                @contract_key,
                @latitude,
                @longitude,
                @place_value,
                @provider_result_id,
                @country_code,
                @resolved_at_utc)
            ON CONFLICT (
                provider,
                contract_key,
                latitude,
                longitude)
            DO UPDATE SET
                place_value = excluded.place_value,
                provider_result_id = excluded.provider_result_id,
                country_code = excluded.country_code,
                resolved_at_utc = excluded.resolved_at_utc;
            """;
        command.Parameters.AddWithValue(
            "provider",
            NormalizeProvider(provider));
        command.Parameters.AddWithValue(
            "contract_key",
            NormalizeContractKey(contractKey));
        command.Parameters.AddWithValue(
            "latitude",
            candidate.Latitude);
        command.Parameters.AddWithValue(
            "longitude",
            candidate.Longitude);
        command.Parameters.AddWithValue(
            "place_value",
            placeValue.Trim());
        AddNullableText(command, "provider_result_id", providerResultId);
        AddNullableText(command, "country_code", countryCode);
        command.Parameters.AddWithValue(
            "resolved_at_utc",
            _timeProvider.GetUtcNow().ToUniversalTime());

        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    public Task MarkSucceededAsync(
        string provider,
        string contractKey,
        PhotoPlaceEnrichmentCandidate candidate,
        string placeValue,
        string? providerResultId,
        string? countryCode,
        CancellationToken cancellationToken = default) =>
        UpsertAttemptAsync(
            provider,
            contractKey,
            candidate,
            "succeeded",
            placeValue,
            providerResultId,
            countryCode,
            errorCode: null,
            errorMessage: null,
            completed: true,
            cancellationToken);

    public Task MarkSkippedAsync(
        string provider,
        string contractKey,
        PhotoPlaceEnrichmentCandidate candidate,
        string reasonCode,
        string reasonMessage,
        CancellationToken cancellationToken = default) =>
        UpsertAttemptAsync(
            provider,
            contractKey,
            candidate,
            "skipped",
            placeValue: null,
            providerResultId: null,
            countryCode: null,
            reasonCode,
            reasonMessage,
            completed: true,
            cancellationToken);

    public Task MarkDeferredAsync(
        string provider,
        string contractKey,
        PhotoPlaceEnrichmentCandidate candidate,
        string? errorCode,
        string? errorMessage,
        CancellationToken cancellationToken = default) =>
        UpsertAttemptAsync(
            provider,
            contractKey,
            candidate,
            "deferred",
            placeValue: null,
            providerResultId: null,
            countryCode: null,
            errorCode,
            errorMessage,
            completed: false,
            cancellationToken);

    public Task MarkFailedAsync(
        string provider,
        string contractKey,
        PhotoPlaceEnrichmentCandidate candidate,
        string? errorCode,
        string? errorMessage,
        CancellationToken cancellationToken = default) =>
        UpsertAttemptAsync(
            provider,
            contractKey,
            candidate,
            "failed",
            placeValue: null,
            providerResultId: null,
            countryCode: null,
            errorCode,
            errorMessage,
            completed: false,
            cancellationToken);

    private async Task UpsertAttemptAsync(
        string provider,
        string contractKey,
        PhotoPlaceEnrichmentCandidate candidate,
        string status,
        string? placeValue,
        string? providerResultId,
        string? countryCode,
        string? errorCode,
        string? errorMessage,
        bool completed,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(candidate);
        DateTimeOffset now =
            _timeProvider.GetUtcNow().ToUniversalTime();

        await using NpgsqlConnection connection =
            await _database.OpenConnectionAsync(cancellationToken);
        await using NpgsqlCommand command = connection.CreateCommand();
        command.CommandText =
            """
            INSERT INTO photo_place_enrichment_attempts (
                asset_revision_id,
                provider,
                contract_key,
                latitude,
                longitude,
                status,
                attempt_count,
                place_value,
                provider_result_id,
                country_code,
                last_error_code,
                last_error_message,
                last_attempted_at_utc,
                completed_at_utc)
            VALUES (
                @asset_revision_id,
                @provider,
                @contract_key,
                @latitude,
                @longitude,
                @status,
                1,
                @place_value,
                @provider_result_id,
                @country_code,
                @last_error_code,
                @last_error_message,
                @last_attempted_at_utc,
                @completed_at_utc)
            ON CONFLICT (
                asset_revision_id,
                provider,
                contract_key)
            DO UPDATE SET
                latitude = excluded.latitude,
                longitude = excluded.longitude,
                status = excluded.status,
                attempt_count =
                    photo_place_enrichment_attempts.attempt_count + 1,
                place_value = excluded.place_value,
                provider_result_id = excluded.provider_result_id,
                country_code = excluded.country_code,
                last_error_code = excluded.last_error_code,
                last_error_message = excluded.last_error_message,
                last_attempted_at_utc = excluded.last_attempted_at_utc,
                completed_at_utc = excluded.completed_at_utc;
            """;
        command.Parameters.AddWithValue(
            "asset_revision_id",
            Guid.Parse(candidate.RevisionId.ToString()));
        command.Parameters.AddWithValue(
            "provider",
            NormalizeProvider(provider));
        command.Parameters.AddWithValue(
            "contract_key",
            NormalizeContractKey(contractKey));
        command.Parameters.AddWithValue(
            "latitude",
            candidate.Latitude);
        command.Parameters.AddWithValue(
            "longitude",
            candidate.Longitude);
        command.Parameters.AddWithValue("status", status);
        AddNullableText(command, "place_value", placeValue);
        AddNullableText(command, "provider_result_id", providerResultId);
        AddNullableText(command, "country_code", countryCode);
        AddNullableText(
            command,
            "last_error_code",
            Truncate(errorCode, 120));
        AddNullableText(
            command,
            "last_error_message",
            Truncate(errorMessage, 1000));
        command.Parameters.AddWithValue(
            "last_attempted_at_utc",
            now);

        NpgsqlParameter completedAt =
            command.Parameters.Add(
                "completed_at_utc",
                NpgsqlDbType.TimestampTz);
        completedAt.Value = completed
            ? now
            : DBNull.Value;

        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    private static void AddNullableText(
        NpgsqlCommand command,
        string name,
        string? value)
    {
        NpgsqlParameter parameter =
            command.Parameters.Add(
                name,
                NpgsqlDbType.Text);
        parameter.Value = string.IsNullOrWhiteSpace(value)
            ? DBNull.Value
            : value.Trim();
    }

    private static string NormalizeProvider(string provider)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(provider);
        string value = provider.Trim().ToLowerInvariant();
        if (value.Length > 80)
        {
            throw new ArgumentException(
                "Reverse-geocoding provider names cannot exceed 80 characters.",
                nameof(provider));
        }

        return value;
    }

    private static string NormalizeContractKey(string contractKey)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(contractKey);
        string value = contractKey.Trim();
        if (value.Length > 500)
        {
            throw new ArgumentException(
                "Reverse-geocoding contract keys cannot exceed 500 characters.",
                nameof(contractKey));
        }

        return value;
    }

    private static string? Truncate(
        string? value,
        int maximumLength) =>
        string.IsNullOrWhiteSpace(value)
            ? null
            : value.Trim().Length <= maximumLength
                ? value.Trim()
                : value.Trim()[..maximumLength];
}
