using System.Globalization;
using Microsoft.Data.Sqlite;
using PhotoIdentity.Core.Identifiers;
using PhotoIdentity.Core.Places;

namespace PhotoIdentity.Persistence.Sqlite;

public sealed record CataloguePlaceEnrichmentCandidate(
    AssetRevisionId RevisionId,
    double Latitude,
    double Longitude);

public sealed record CatalogueReverseGeocodeCacheEntry(
    string PlaceValue,
    string? ProviderResultId,
    string? CountryCode,
    DateTimeOffset ResolvedAtUtc);

public sealed class SqlitePhotoPlaceEnrichmentRepository : IPhotoPlaceEnrichmentStateRepository
{
    private readonly SqliteCatalogueDatabase _database;
    private readonly TimeProvider _timeProvider;

    public SqlitePhotoPlaceEnrichmentRepository(
        SqliteCatalogueDatabase database,
        TimeProvider timeProvider)
    {
        ArgumentNullException.ThrowIfNull(database);
        ArgumentNullException.ThrowIfNull(timeProvider);
        _database = database;
        _timeProvider = timeProvider;
    }

    async Task<IReadOnlyList<PhotoPlaceEnrichmentCandidate>>
        IPhotoPlaceEnrichmentStateRepository.GetCandidatesAsync(
            string provider,
            string contractKey,
            int limit,
            bool refresh,
            CancellationToken cancellationToken)
    {
        IReadOnlyList<CataloguePlaceEnrichmentCandidate> candidates =
            await GetCandidatesAsync(
                provider,
                contractKey,
                limit,
                refresh,
                cancellationToken);
        return candidates
            .Select(ToCoreCandidate)
            .ToArray();
    }

    async Task<ReverseGeocodeCacheEntry?>
        IPhotoPlaceEnrichmentStateRepository.GetCachedAsync(
            string provider,
            string contractKey,
            double latitude,
            double longitude,
            CancellationToken cancellationToken)
    {
        CatalogueReverseGeocodeCacheEntry? cached =
            await GetCachedAsync(
                provider,
                contractKey,
                latitude,
                longitude,
                cancellationToken);
        return cached is null
            ? null
            : new ReverseGeocodeCacheEntry(
                cached.PlaceValue,
                cached.ProviderResultId,
                cached.CountryCode,
                cached.ResolvedAtUtc);
    }

    Task IPhotoPlaceEnrichmentStateRepository.SaveCacheAsync(
        string provider,
        string contractKey,
        PhotoPlaceEnrichmentCandidate candidate,
        string placeValue,
        string? providerResultId,
        string? countryCode,
        CancellationToken cancellationToken) =>
        SaveCacheAsync(
            provider,
            contractKey,
            ToSqliteCandidate(candidate),
            placeValue,
            providerResultId,
            countryCode,
            cancellationToken);

    Task IPhotoPlaceEnrichmentStateRepository.MarkSucceededAsync(
        string provider,
        string contractKey,
        PhotoPlaceEnrichmentCandidate candidate,
        string placeValue,
        string? providerResultId,
        string? countryCode,
        CancellationToken cancellationToken) =>
        MarkSucceededAsync(
            provider,
            contractKey,
            ToSqliteCandidate(candidate),
            placeValue,
            providerResultId,
            countryCode,
            cancellationToken);

    Task IPhotoPlaceEnrichmentStateRepository.MarkSkippedAsync(
        string provider,
        string contractKey,
        PhotoPlaceEnrichmentCandidate candidate,
        string reasonCode,
        string reasonMessage,
        CancellationToken cancellationToken) =>
        MarkSkippedAsync(
            provider,
            contractKey,
            ToSqliteCandidate(candidate),
            reasonCode,
            reasonMessage,
            cancellationToken);

    Task IPhotoPlaceEnrichmentStateRepository.MarkDeferredAsync(
        string provider,
        string contractKey,
        PhotoPlaceEnrichmentCandidate candidate,
        string? errorCode,
        string? errorMessage,
        CancellationToken cancellationToken) =>
        MarkDeferredAsync(
            provider,
            contractKey,
            ToSqliteCandidate(candidate),
            errorCode,
            errorMessage,
            cancellationToken);

    Task IPhotoPlaceEnrichmentStateRepository.MarkFailedAsync(
        string provider,
        string contractKey,
        PhotoPlaceEnrichmentCandidate candidate,
        string? errorCode,
        string? errorMessage,
        CancellationToken cancellationToken) =>
        MarkFailedAsync(
            provider,
            contractKey,
            ToSqliteCandidate(candidate),
            errorCode,
            errorMessage,
            cancellationToken);

    public async Task<IReadOnlyList<CataloguePlaceEnrichmentCandidate>> GetCandidatesAsync(
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
            throw new ArgumentOutOfRangeException(nameof(limit), "Place enrichment batch size must be between 1 and 250.");
        }

        await using SqliteConnection connection = await _database.OpenConnectionAsync(cancellationToken);
        await SqlitePhotoPlaceEnrichmentSchema.EnsureAsync(connection, cancellationToken);
        using SqliteCommand command = connection.CreateCommand();
        command.CommandText = """
            SELECT metadata.asset_revision_id, metadata.latitude, metadata.longitude
            FROM photo_capture_metadata AS metadata
            LEFT JOIN photo_place_enrichment_attempts AS attempt
                ON attempt.asset_revision_id = metadata.asset_revision_id
               AND attempt.provider = $provider
               AND attempt.contract_key = $contract_key
            WHERE metadata.latitude IS NOT NULL
              AND metadata.longitude IS NOT NULL
              AND (
                    attempt.asset_revision_id IS NULL
                    OR attempt.latitude <> metadata.latitude
                    OR attempt.longitude <> metadata.longitude
                    OR (attempt.status <> 'skipped' AND ($refresh = 1 OR attempt.status <> 'succeeded')))
            ORDER BY
                CASE WHEN attempt.asset_revision_id IS NULL THEN 0 ELSE 1 END,
                attempt.last_attempted_at_utc,
                metadata.asset_revision_id
            LIMIT $limit;
            """;
        command.Parameters.AddWithValue("$provider", normalizedProvider);
        command.Parameters.AddWithValue("$contract_key", normalizedContract);
        command.Parameters.AddWithValue("$refresh", refresh ? 1 : 0);
        command.Parameters.AddWithValue("$limit", limit);

        List<CataloguePlaceEnrichmentCandidate> candidates = [];
        await using SqliteDataReader reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            candidates.Add(new CataloguePlaceEnrichmentCandidate(
                AssetRevisionId.From(Guid.Parse(reader.GetString(0))),
                reader.GetDouble(1),
                reader.GetDouble(2)));
        }

        return candidates;
    }

    public async Task<CatalogueReverseGeocodeCacheEntry?> GetCachedAsync(
        string provider,
        string contractKey,
        double latitude,
        double longitude,
        CancellationToken cancellationToken = default)
    {
        await using SqliteConnection connection = await _database.OpenConnectionAsync(cancellationToken);
        await SqlitePhotoPlaceEnrichmentSchema.EnsureAsync(connection, cancellationToken);
        using SqliteCommand command = connection.CreateCommand();
        command.CommandText = """
            SELECT place_value, provider_result_id, country_code, resolved_at_utc
            FROM photo_place_reverse_geocode_cache
            WHERE provider = $provider
              AND contract_key = $contract_key
              AND latitude = $latitude
              AND longitude = $longitude;
            """;
        command.Parameters.AddWithValue("$provider", NormalizeProvider(provider));
        command.Parameters.AddWithValue("$contract_key", NormalizeContractKey(contractKey));
        command.Parameters.AddWithValue("$latitude", latitude);
        command.Parameters.AddWithValue("$longitude", longitude);
        await using SqliteDataReader reader = await command.ExecuteReaderAsync(cancellationToken);
        if (!await reader.ReadAsync(cancellationToken))
        {
            return null;
        }

        return new CatalogueReverseGeocodeCacheEntry(
            reader.GetString(0),
            reader.IsDBNull(1) ? null : reader.GetString(1),
            reader.IsDBNull(2) ? null : reader.GetString(2),
            Parse(reader.GetString(3)));
    }

    public async Task SaveCacheAsync(
        string provider,
        string contractKey,
        CataloguePlaceEnrichmentCandidate candidate,
        string placeValue,
        string? providerResultId,
        string? countryCode,
        CancellationToken cancellationToken = default)
    {
        await using SqliteConnection connection = await _database.OpenConnectionAsync(cancellationToken);
        await SqlitePhotoPlaceEnrichmentSchema.EnsureAsync(connection, cancellationToken);
        using SqliteCommand command = connection.CreateCommand();
        command.CommandText = """
            INSERT INTO photo_place_reverse_geocode_cache (
                provider, contract_key, latitude, longitude, place_value,
                provider_result_id, country_code, resolved_at_utc)
            VALUES (
                $provider, $contract_key, $latitude, $longitude, $place_value,
                $provider_result_id, $country_code, $resolved_at_utc)
            ON CONFLICT(provider, contract_key, latitude, longitude) DO UPDATE SET
                place_value = excluded.place_value,
                provider_result_id = excluded.provider_result_id,
                country_code = excluded.country_code,
                resolved_at_utc = excluded.resolved_at_utc;
            """;
        command.Parameters.AddWithValue("$provider", NormalizeProvider(provider));
        command.Parameters.AddWithValue("$contract_key", NormalizeContractKey(contractKey));
        command.Parameters.AddWithValue("$latitude", candidate.Latitude);
        command.Parameters.AddWithValue("$longitude", candidate.Longitude);
        command.Parameters.AddWithValue("$place_value", placeValue);
        command.Parameters.AddWithValue("$provider_result_id", (object?)providerResultId ?? DBNull.Value);
        command.Parameters.AddWithValue("$country_code", (object?)countryCode ?? DBNull.Value);
        command.Parameters.AddWithValue("$resolved_at_utc", Format(_timeProvider.GetUtcNow()));
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    public Task MarkSucceededAsync(
        string provider,
        string contractKey,
        CataloguePlaceEnrichmentCandidate candidate,
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
        CataloguePlaceEnrichmentCandidate candidate,
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
        CataloguePlaceEnrichmentCandidate candidate,
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
        CataloguePlaceEnrichmentCandidate candidate,
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
        CataloguePlaceEnrichmentCandidate candidate,
        string status,
        string? placeValue,
        string? providerResultId,
        string? countryCode,
        string? errorCode,
        string? errorMessage,
        bool completed,
        CancellationToken cancellationToken)
    {
        string now = Format(_timeProvider.GetUtcNow());
        await using SqliteConnection connection = await _database.OpenConnectionAsync(cancellationToken);
        await SqlitePhotoPlaceEnrichmentSchema.EnsureAsync(connection, cancellationToken);
        using SqliteCommand command = connection.CreateCommand();
        command.CommandText = """
            INSERT INTO photo_place_enrichment_attempts (
                asset_revision_id, provider, contract_key, latitude, longitude,
                status, attempt_count, place_value, provider_result_id, country_code,
                last_error_code, last_error_message, last_attempted_at_utc, completed_at_utc)
            VALUES (
                $revision_id, $provider, $contract_key, $latitude, $longitude,
                $status, 1, $place_value, $provider_result_id, $country_code,
                $error_code, $error_message, $attempted_at_utc, $completed_at_utc)
            ON CONFLICT(asset_revision_id, provider, contract_key) DO UPDATE SET
                latitude = excluded.latitude,
                longitude = excluded.longitude,
                status = excluded.status,
                attempt_count = photo_place_enrichment_attempts.attempt_count + 1,
                place_value = excluded.place_value,
                provider_result_id = excluded.provider_result_id,
                country_code = excluded.country_code,
                last_error_code = excluded.last_error_code,
                last_error_message = excluded.last_error_message,
                last_attempted_at_utc = excluded.last_attempted_at_utc,
                completed_at_utc = excluded.completed_at_utc;
            """;
        command.Parameters.AddWithValue("$revision_id", candidate.RevisionId.ToString());
        command.Parameters.AddWithValue("$provider", NormalizeProvider(provider));
        command.Parameters.AddWithValue("$contract_key", NormalizeContractKey(contractKey));
        command.Parameters.AddWithValue("$latitude", candidate.Latitude);
        command.Parameters.AddWithValue("$longitude", candidate.Longitude);
        command.Parameters.AddWithValue("$status", status);
        command.Parameters.AddWithValue("$place_value", (object?)placeValue ?? DBNull.Value);
        command.Parameters.AddWithValue("$provider_result_id", (object?)providerResultId ?? DBNull.Value);
        command.Parameters.AddWithValue("$country_code", (object?)countryCode ?? DBNull.Value);
        command.Parameters.AddWithValue("$error_code", (object?)Truncate(errorCode, 120) ?? DBNull.Value);
        command.Parameters.AddWithValue("$error_message", (object?)Truncate(errorMessage, 1000) ?? DBNull.Value);
        command.Parameters.AddWithValue("$attempted_at_utc", now);
        command.Parameters.AddWithValue("$completed_at_utc", completed ? now : DBNull.Value);
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    private static PhotoPlaceEnrichmentCandidate ToCoreCandidate(
        CataloguePlaceEnrichmentCandidate candidate) => new(
        candidate.RevisionId,
        candidate.Latitude,
        candidate.Longitude);

    private static CataloguePlaceEnrichmentCandidate ToSqliteCandidate(
        PhotoPlaceEnrichmentCandidate candidate) => new(
        candidate.RevisionId,
        candidate.Latitude,
        candidate.Longitude);

    private static string NormalizeProvider(string provider)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(provider);
        string value = provider.Trim().ToLowerInvariant();
        if (value.Length > 80)
        {
            throw new ArgumentException("Reverse-geocoding provider names cannot exceed 80 characters.", nameof(provider));
        }
        return value;
    }

    private static string NormalizeContractKey(string contractKey)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(contractKey);
        string value = contractKey.Trim();
        if (value.Length > 500)
        {
            throw new ArgumentException("Reverse-geocoding contract keys cannot exceed 500 characters.", nameof(contractKey));
        }
        return value;
    }

    private static string? Truncate(string? value, int maximumLength) =>
        string.IsNullOrWhiteSpace(value)
            ? null
            : value.Trim().Length <= maximumLength
                ? value.Trim()
                : value.Trim()[..maximumLength];

    private static string Format(DateTimeOffset value) =>
        value.ToUniversalTime().ToString("O", CultureInfo.InvariantCulture);

    private static DateTimeOffset Parse(string value) => DateTimeOffset.Parse(
        value,
        CultureInfo.InvariantCulture,
        DateTimeStyles.RoundtripKind);
}
