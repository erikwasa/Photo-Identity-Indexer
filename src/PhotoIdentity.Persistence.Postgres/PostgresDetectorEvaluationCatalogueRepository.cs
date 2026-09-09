using System.Data;
using System.Globalization;
using System.Text.Json;
using Npgsql;
using NpgsqlTypes;
using PhotoIdentity.Core.Geometry;
using PhotoIdentity.Core.Identifiers;
using PhotoIdentity.Core.Recognition;

namespace PhotoIdentity.Persistence.Postgres;

public sealed class PostgresDetectorEvaluationCatalogueRepository : IDetectorEvaluationCatalogueRepository
{
    private readonly PostgresCatalogueDatabase _database;

    public PostgresDetectorEvaluationCatalogueRepository(PostgresCatalogueDatabase database)
    {
        ArgumentNullException.ThrowIfNull(database);
        _database = database;
    }

    public async Task<IReadOnlyList<CatalogueDetectorEvaluationRun>> GetRunsAsync(
        CancellationToken cancellationToken = default)
    {
        await using NpgsqlConnection connection =
            await _database.OpenConnectionAsync(cancellationToken);
        await using NpgsqlCommand command = connection.CreateCommand();
        command.CommandText = """
            SELECT
                processing_runs.id,
                processing_runs.status,
                processing_runs.started_at_utc,
                processing_runs.completed_at_utc,
                COUNT(DISTINCT processing_jobs.asset_revision_id),
                COUNT(DISTINCT face_occurrences.id)
            FROM processing_runs
            LEFT JOIN processing_jobs
                ON processing_jobs.processing_run_id = processing_runs.id
            LEFT JOIN face_occurrences
                ON face_occurrences.asset_revision_id = processing_jobs.asset_revision_id
            GROUP BY
                processing_runs.id,
                processing_runs.status,
                processing_runs.started_at_utc,
                processing_runs.completed_at_utc
            ORDER BY processing_runs.started_at_utc DESC, processing_runs.id;
            """;

        List<CatalogueDetectorEvaluationRun> runs = [];
        await using NpgsqlDataReader reader =
            await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            runs.Add(new CatalogueDetectorEvaluationRun(
                ProcessingRunId.From(reader.GetGuid(0)),
                reader.GetString(1),
                reader.GetFieldValue<DateTimeOffset>(2),
                reader.IsDBNull(3) ? null : reader.GetFieldValue<DateTimeOffset>(3),
                Convert.ToInt32(reader.GetInt64(4), CultureInfo.InvariantCulture),
                Convert.ToInt32(reader.GetInt64(5), CultureInfo.InvariantCulture)));
        }

        return runs;
    }

    public async Task<CatalogueDetectorEvaluationPhotoPage> GetPhotosAsync(
        ProcessingRunId processingRunId,
        int offset = 0,
        int limit = 8,
        CancellationToken cancellationToken = default)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(offset);
        if (limit is < 1 or > 1000)
        {
            throw new ArgumentOutOfRangeException(nameof(limit), "Photo page size must be between 1 and 1000.");
        }

        await using NpgsqlConnection connection =
            await _database.OpenConnectionAsync(cancellationToken);

        int total = await CountPhotosAsync(connection, processingRunId, cancellationToken);
        await using NpgsqlCommand command = connection.CreateCommand();
        command.CommandText = """
            WITH scoped_photos AS (
                SELECT
                    asset_revisions.id,
                    assets.source_key,
                    COALESCE(asset_revisions.media_type, 'application/octet-stream') AS media_type,
                    asset_revisions.width,
                    asset_revisions.height,
                    asset_revisions.content_sha256,
                    processing_jobs.status AS job_status
                FROM processing_jobs
                INNER JOIN asset_revisions
                    ON asset_revisions.id = processing_jobs.asset_revision_id
                INNER JOIN assets
                    ON assets.id = asset_revisions.asset_id
                WHERE processing_jobs.processing_run_id = @processing_run_id
                ORDER BY assets.source_key, asset_revisions.id
                LIMIT @limit OFFSET @offset
            ),
            latest_observation AS (
                SELECT
                    face_observations.*,
                    ROW_NUMBER() OVER (
                        PARTITION BY face_observations.face_occurrence_id
                        ORDER BY
                            face_observations.observed_at_utc DESC,
                            face_observations.detector_model_id,
                            face_observations.detector_model_hash) AS row_number
                FROM face_observations
            )
            SELECT
                scoped_photos.id,
                scoped_photos.source_key,
                scoped_photos.media_type,
                scoped_photos.width,
                scoped_photos.height,
                scoped_photos.content_sha256,
                scoped_photos.job_status,
                face_occurrences.id,
                face_occurrences.ordinal,
                latest_observation.confidence,
                latest_observation.bounding_box_json
            FROM scoped_photos
            LEFT JOIN face_occurrences
                ON face_occurrences.asset_revision_id = scoped_photos.id
            LEFT JOIN latest_observation
                ON latest_observation.face_occurrence_id = face_occurrences.id
               AND latest_observation.row_number = 1
            ORDER BY scoped_photos.source_key, scoped_photos.id, face_occurrences.ordinal, face_occurrences.id;
            """;
        command.Parameters.AddWithValue("processing_run_id", NpgsqlDbType.Uuid, processingRunId.Value);
        command.Parameters.AddWithValue("limit", NpgsqlDbType.Integer, limit);
        command.Parameters.AddWithValue("offset", NpgsqlDbType.Integer, offset);

        Dictionary<Guid, PhotoBuilder> photosByRevision = [];
        List<PhotoBuilder> orderedPhotos = [];

        await using NpgsqlDataReader reader =
            await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            Guid revisionValue = reader.GetGuid(0);
            if (!photosByRevision.TryGetValue(revisionValue, out PhotoBuilder? photo))
            {
                string sourceKey = reader.GetString(1).Replace('\\', '/');
                string photoName = Path.GetFileName(sourceKey);
                photo = new PhotoBuilder(
                    AssetRevisionId.From(revisionValue),
                    string.IsNullOrWhiteSpace(photoName) ? "Photo" : photoName,
                    reader.GetString(2),
                    reader.IsDBNull(3) ? null : reader.GetInt32(3),
                    reader.IsDBNull(4) ? null : reader.GetInt32(4),
                    new Sha256Digest(reader.GetString(5)),
                    reader.GetString(6));
                photosByRevision.Add(revisionValue, photo);
                orderedPhotos.Add(photo);
            }

            if (!reader.IsDBNull(7) &&
                !reader.IsDBNull(8) &&
                !reader.IsDBNull(9) &&
                !reader.IsDBNull(10))
            {
                photo.Detections.Add(new CatalogueDetectorEvaluationDetection(
                    FaceOccurrenceId.From(reader.GetGuid(7)),
                    reader.GetInt32(8),
                    reader.GetDouble(9),
                    DeserializeBoundingBox(reader.GetString(10))));
            }
        }

        return new CatalogueDetectorEvaluationPhotoPage(
            orderedPhotos.Select(photo => photo.Build()).ToArray(),
            offset,
            limit,
            total);
    }

    private static async Task<int> CountPhotosAsync(
        NpgsqlConnection connection,
        ProcessingRunId processingRunId,
        CancellationToken cancellationToken)
    {
        await using NpgsqlCommand command = connection.CreateCommand();
        command.CommandText = """
            SELECT COUNT(*)
            FROM processing_jobs
            WHERE processing_run_id = @processing_run_id;
            """;
        command.Parameters.AddWithValue("processing_run_id", NpgsqlDbType.Uuid, processingRunId.Value);
        object? result = await command.ExecuteScalarAsync(cancellationToken);
        return Convert.ToInt32(result, CultureInfo.InvariantCulture);
    }

    private static NormalizedBoundingBox DeserializeBoundingBox(string value)
    {
        try
        {
            double[]? coordinates = JsonSerializer.Deserialize<double[]>(value);
            if (coordinates is { Length: 4 })
            {
                return new NormalizedBoundingBox(
                    coordinates[0],
                    coordinates[1],
                    coordinates[2],
                    coordinates[3]);
            }
        }
        catch (JsonException)
        {
            // Fall through to the legacy object-shaped representation used by early test data.
        }

        using JsonDocument document = JsonDocument.Parse(value);
        JsonElement root = document.RootElement;
        if (root.ValueKind != JsonValueKind.Object ||
            !TryReadProperty(root, "x", out double x) ||
            !TryReadProperty(root, "y", out double y) ||
            !TryReadProperty(root, "width", out double width) ||
            !TryReadProperty(root, "height", out double height))
        {
            throw new DataException("Bounding-box JSON must contain four normalised coordinates.");
        }

        return new NormalizedBoundingBox(x, y, width, height);
    }

    private static bool TryReadProperty(JsonElement element, string name, out double value)
    {
        value = 0;
        return element.TryGetProperty(name, out JsonElement property) && property.TryGetDouble(out value);
    }

    private sealed class PhotoBuilder
    {
        public PhotoBuilder(
            AssetRevisionId revisionId,
            string photoName,
            string mediaType,
            int? width,
            int? height,
            Sha256Digest revisionHash,
            string jobStatus)
        {
            RevisionId = revisionId;
            PhotoName = photoName;
            MediaType = mediaType;
            Width = width;
            Height = height;
            RevisionHash = revisionHash;
            JobStatus = jobStatus;
        }

        public AssetRevisionId RevisionId { get; }
        public string PhotoName { get; }
        public string MediaType { get; }
        public int? Width { get; }
        public int? Height { get; }
        public Sha256Digest RevisionHash { get; }
        public string JobStatus { get; }
        public List<CatalogueDetectorEvaluationDetection> Detections { get; } = [];

        public CatalogueDetectorEvaluationPhoto Build() => new(
            RevisionId,
            PhotoName,
            MediaType,
            Width,
            Height,
            RevisionHash,
            JobStatus,
            Detections.ToArray());
    }
}
