using System.Globalization;
using System.Text.Json;
using Microsoft.Data.Sqlite;
using PhotoIdentity.Core.Geometry;
using PhotoIdentity.Core.Identifiers;
using PhotoIdentity.Core.Recognition;

namespace PhotoIdentity.Persistence.Sqlite;

/// <summary>
/// Persists durable high-resolution contextual face-review derivatives independently from the
/// recognition crop and records revision-level completion so already-analyzed photos can be
/// backfilled without rerunning detector/embedder inference.
/// </summary>
public sealed class SqliteFaceReviewDerivativeRepository
{
    private readonly SqliteCatalogueDatabase _database;

    public SqliteFaceReviewDerivativeRepository(SqliteCatalogueDatabase database)
    {
        ArgumentNullException.ThrowIfNull(database);
        _database = database;
    }

    public async Task EnsureSchemaAsync(CancellationToken cancellationToken = default)
    {
        await _database.InitializeAsync(cancellationToken);
        await using SqliteConnection connection = await _database.OpenConnectionAsync(cancellationToken);
        using SqliteCommand command = connection.CreateCommand();
        command.CommandText = """
            CREATE TABLE IF NOT EXISTS face_review_derivatives (
                face_occurrence_id TEXT NOT NULL,
                profile_id TEXT NOT NULL,
                encoded_byte_length INTEGER NOT NULL CHECK (encoded_byte_length > 0),
                content_sha256 TEXT NOT NULL,
                width INTEGER NOT NULL CHECK (width > 0),
                height INTEGER NOT NULL CHECK (height > 0),
                generated_at_utc TEXT NOT NULL,
                relative_path TEXT NOT NULL,
                PRIMARY KEY (face_occurrence_id, profile_id),
                UNIQUE (relative_path),
                FOREIGN KEY (face_occurrence_id) REFERENCES face_occurrences (id) ON DELETE CASCADE
            );

            CREATE TABLE IF NOT EXISTS asset_revision_face_review_completions (
                asset_revision_id TEXT NOT NULL,
                profile_id TEXT NOT NULL,
                completed_at_utc TEXT NOT NULL,
                PRIMARY KEY (asset_revision_id, profile_id),
                FOREIGN KEY (asset_revision_id) REFERENCES asset_revisions (id) ON DELETE CASCADE
            );

            CREATE INDEX IF NOT EXISTS ix_face_review_derivatives_profile
                ON face_review_derivatives (profile_id, face_occurrence_id);
            CREATE INDEX IF NOT EXISTS ix_face_review_completions_profile
                ON asset_revision_face_review_completions (profile_id, asset_revision_id);
            """;
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    public async Task<FaceReviewDerivativeRecord?> GetAsync(
        FaceOccurrenceId faceOccurrenceId,
        string profileId,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(profileId);
        await EnsureSchemaAsync(cancellationToken);
        await using SqliteConnection connection = await _database.OpenConnectionAsync(cancellationToken);
        using SqliteCommand command = connection.CreateCommand();
        command.CommandText = """
            SELECT face_occurrence_id, profile_id, encoded_byte_length, content_sha256,
                   width, height, generated_at_utc, relative_path
            FROM face_review_derivatives
            WHERE face_occurrence_id = $face_occurrence_id
              AND profile_id = $profile_id;
            """;
        command.Parameters.AddWithValue("$face_occurrence_id", faceOccurrenceId.ToString());
        command.Parameters.AddWithValue("$profile_id", profileId.Trim());

        await using SqliteDataReader reader = await command.ExecuteReaderAsync(cancellationToken);
        return await reader.ReadAsync(cancellationToken)
            ? ReadDerivative(reader)
            : null;
    }

    public async Task<bool> IsRevisionCompleteAsync(
        AssetRevisionId revisionId,
        string profileId,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(profileId);
        await EnsureSchemaAsync(cancellationToken);
        await using SqliteConnection connection = await _database.OpenConnectionAsync(cancellationToken);
        using SqliteCommand command = connection.CreateCommand();
        command.CommandText = """
            SELECT 1
            FROM asset_revision_face_review_completions
            WHERE asset_revision_id = $asset_revision_id
              AND profile_id = $profile_id
            LIMIT 1;
            """;
        command.Parameters.AddWithValue("$asset_revision_id", revisionId.ToString());
        command.Parameters.AddWithValue("$profile_id", profileId.Trim());
        return await command.ExecuteScalarAsync(cancellationToken) is not null;
    }

    public async Task<IReadOnlyList<FaceReviewGeometry>> GetFacesAsync(
        AssetRevisionId revisionId,
        CancellationToken cancellationToken = default)
    {
        await EnsureSchemaAsync(cancellationToken);
        await using SqliteConnection connection = await _database.OpenConnectionAsync(cancellationToken);
        using SqliteCommand command = connection.CreateCommand();
        command.CommandText = """
            WITH latest_observation AS (
                SELECT
                    face_observations.*,
                    ROW_NUMBER() OVER (
                        PARTITION BY face_occurrence_id
                        ORDER BY observed_at_utc DESC, detector_model_id, detector_model_hash) AS row_number
                FROM face_observations
            )
            SELECT
                occurrence.id,
                observation.bounding_box_json,
                revision.width,
                revision.height
            FROM face_occurrences AS occurrence
            INNER JOIN asset_revisions AS revision
                ON revision.id = occurrence.asset_revision_id
            LEFT JOIN latest_observation AS observation
                ON observation.face_occurrence_id = occurrence.id
               AND observation.row_number = 1
            WHERE occurrence.asset_revision_id = $asset_revision_id
            ORDER BY occurrence.ordinal, occurrence.id;
            """;
        command.Parameters.AddWithValue("$asset_revision_id", revisionId.ToString());

        List<FaceReviewGeometry> faces = [];
        await using SqliteDataReader reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            if (reader.IsDBNull(1))
            {
                throw new InvalidDataException(
                    $"Face {reader.GetString(0)} has no observation geometry for review derivative generation.");
            }

            int? photoWidth = reader.IsDBNull(2) ? null : reader.GetInt32(2);
            int? photoHeight = reader.IsDBNull(3) ? null : reader.GetInt32(3);
            if (!TryParseBoundingBox(reader.GetString(1), photoWidth, photoHeight, out NormalizedBoundingBox boundingBox))
            {
                throw new InvalidDataException(
                    $"Face {reader.GetString(0)} has invalid observation geometry for review derivative generation.");
            }

            faces.Add(new FaceReviewGeometry(
                FaceOccurrenceId.From(Guid.Parse(reader.GetString(0))),
                boundingBox));
        }

        return faces;
    }

    public async Task RecordRevisionCompletionAsync(
        AssetRevisionId revisionId,
        string profileId,
        IReadOnlyList<FaceReviewDerivativeRecord> derivatives,
        DateTimeOffset completedAtUtc,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(profileId);
        ArgumentNullException.ThrowIfNull(derivatives);
        await EnsureSchemaAsync(cancellationToken);

        await using SqliteConnection connection = await _database.OpenConnectionAsync(cancellationToken);
        using SqliteTransaction transaction = connection.BeginTransaction();
        foreach (FaceReviewDerivativeRecord derivative in derivatives)
        {
            if (!string.Equals(derivative.ProfileId, profileId.Trim(), StringComparison.Ordinal))
            {
                throw new ArgumentException("All derivative records must use the requested profile.", nameof(derivatives));
            }

            using SqliteCommand command = connection.CreateCommand();
            command.Transaction = transaction;
            command.CommandText = """
                INSERT INTO face_review_derivatives (
                    face_occurrence_id,
                    profile_id,
                    encoded_byte_length,
                    content_sha256,
                    width,
                    height,
                    generated_at_utc,
                    relative_path)
                VALUES (
                    $face_occurrence_id,
                    $profile_id,
                    $encoded_byte_length,
                    $content_sha256,
                    $width,
                    $height,
                    $generated_at_utc,
                    $relative_path)
                ON CONFLICT(face_occurrence_id, profile_id) DO UPDATE SET
                    encoded_byte_length = excluded.encoded_byte_length,
                    content_sha256 = excluded.content_sha256,
                    width = excluded.width,
                    height = excluded.height,
                    generated_at_utc = excluded.generated_at_utc,
                    relative_path = excluded.relative_path;
                """;
            command.Parameters.AddWithValue("$face_occurrence_id", derivative.FaceOccurrenceId.ToString());
            command.Parameters.AddWithValue("$profile_id", derivative.ProfileId);
            command.Parameters.AddWithValue("$encoded_byte_length", derivative.EncodedByteLength);
            command.Parameters.AddWithValue("$content_sha256", derivative.ContentHash.ToString());
            command.Parameters.AddWithValue("$width", derivative.Width);
            command.Parameters.AddWithValue("$height", derivative.Height);
            command.Parameters.AddWithValue("$generated_at_utc", Format(derivative.GeneratedAtUtc));
            command.Parameters.AddWithValue("$relative_path", derivative.RelativePath);
            await command.ExecuteNonQueryAsync(cancellationToken);
        }

        using (SqliteCommand command = connection.CreateCommand())
        {
            command.Transaction = transaction;
            command.CommandText = """
                INSERT INTO asset_revision_face_review_completions (
                    asset_revision_id,
                    profile_id,
                    completed_at_utc)
                VALUES ($asset_revision_id, $profile_id, $completed_at_utc)
                ON CONFLICT(asset_revision_id, profile_id) DO UPDATE SET
                    completed_at_utc = excluded.completed_at_utc;
                """;
            command.Parameters.AddWithValue("$asset_revision_id", revisionId.ToString());
            command.Parameters.AddWithValue("$profile_id", profileId.Trim());
            command.Parameters.AddWithValue("$completed_at_utc", Format(completedAtUtc));
            await command.ExecuteNonQueryAsync(cancellationToken);
        }

        transaction.Commit();
    }

    private static FaceReviewDerivativeRecord ReadDerivative(SqliteDataReader reader) => new(
        FaceOccurrenceId.From(Guid.Parse(reader.GetString(0))),
        reader.GetString(1),
        reader.GetInt64(2),
        new Sha256Digest(reader.GetString(3)),
        reader.GetInt32(4),
        reader.GetInt32(5),
        Parse(reader.GetString(6)),
        reader.GetString(7));

    private static bool TryParseBoundingBox(
        string value,
        int? photoWidth,
        int? photoHeight,
        out NormalizedBoundingBox boundingBox)
    {
        boundingBox = default;
        try
        {
            using JsonDocument document = JsonDocument.Parse(value);
            double[] coordinates;
            if (document.RootElement.ValueKind == JsonValueKind.Array)
            {
                JsonElement[] elements = document.RootElement.EnumerateArray().ToArray();
                if (elements.Length != 4 || elements.Any(element => element.ValueKind != JsonValueKind.Number))
                {
                    return false;
                }

                coordinates = elements.Select(element => element.GetDouble()).ToArray();
            }
            else if (document.RootElement.ValueKind == JsonValueKind.Object &&
                     TryGetNumber(document.RootElement, "x", out double x) &&
                     TryGetNumber(document.RootElement, "y", out double y) &&
                     TryGetNumber(document.RootElement, "width", out double width) &&
                     TryGetNumber(document.RootElement, "height", out double height))
            {
                coordinates = [x, y, width, height];
            }
            else
            {
                return false;
            }

            double normalizedX = coordinates[0];
            double normalizedY = coordinates[1];
            double normalizedWidth = coordinates[2];
            double normalizedHeight = coordinates[3];
            bool alreadyNormalized =
                normalizedX >= 0d && normalizedY >= 0d &&
                normalizedWidth > 0d && normalizedHeight > 0d &&
                normalizedX <= 1d && normalizedY <= 1d &&
                normalizedX + normalizedWidth <= 1d &&
                normalizedY + normalizedHeight <= 1d;

            if (!alreadyNormalized)
            {
                if (photoWidth is not > 0 || photoHeight is not > 0)
                {
                    return false;
                }

                normalizedX /= photoWidth.Value;
                normalizedY /= photoHeight.Value;
                normalizedWidth /= photoWidth.Value;
                normalizedHeight /= photoHeight.Value;
            }

            boundingBox = new NormalizedBoundingBox(
                normalizedX,
                normalizedY,
                normalizedWidth,
                normalizedHeight);
            return true;
        }
        catch (Exception exception) when (
            exception is JsonException or
            InvalidOperationException or
            ArgumentOutOfRangeException)
        {
            return false;
        }
    }

    private static bool TryGetNumber(JsonElement element, string propertyName, out double value)
    {
        value = default;
        return element.TryGetProperty(propertyName, out JsonElement property) &&
               property.ValueKind == JsonValueKind.Number &&
               property.TryGetDouble(out value);
    }

    private static string Format(DateTimeOffset value) =>
        value.ToUniversalTime().ToString("O", CultureInfo.InvariantCulture);

    private static DateTimeOffset Parse(string value) =>
        DateTimeOffset.Parse(value, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind);
}
