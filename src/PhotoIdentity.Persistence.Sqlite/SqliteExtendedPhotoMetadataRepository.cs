using System.Text.Json;
using Microsoft.Data.Sqlite;
using PhotoIdentity.Core.Identifiers;
using PhotoIdentity.Core.Sources;

namespace PhotoIdentity.Persistence.Sqlite;

public sealed record CatalogueExtendedPhotoMetadata(
    string? CameraMake,
    string? CameraModel,
    string? LensModel,
    string? Orientation,
    string? ExposureTime,
    string? Aperture,
    string? Iso,
    string? FocalLength,
    string? FocalLength35Mm,
    string? Flash,
    string? GpsAltitude,
    IReadOnlyList<PhotoMetadataTag> RawTags);

public static class SqliteExtendedPhotoMetadataSchema
{
    public static async Task EnsureAsync(
        SqliteCatalogueDatabase database,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(database);
        await using SqliteConnection connection = await database.OpenConnectionAsync(cancellationToken);
        await EnsureAsync(connection, cancellationToken);
    }

    internal static async Task EnsureAsync(
        SqliteConnection connection,
        CancellationToken cancellationToken)
    {
        using SqliteCommand command = connection.CreateCommand();
        command.CommandText = """
            CREATE TABLE IF NOT EXISTS photo_extended_metadata (
                asset_revision_id TEXT NOT NULL PRIMARY KEY,
                camera_make TEXT NULL,
                camera_model TEXT NULL,
                lens_model TEXT NULL,
                orientation TEXT NULL,
                exposure_time TEXT NULL,
                aperture TEXT NULL,
                iso TEXT NULL,
                focal_length TEXT NULL,
                focal_length_35mm TEXT NULL,
                flash TEXT NULL,
                gps_altitude TEXT NULL,
                raw_tags_json TEXT NOT NULL DEFAULT '[]',
                FOREIGN KEY (asset_revision_id) REFERENCES asset_revisions (id) ON DELETE CASCADE);
            """;
        await command.ExecuteNonQueryAsync(cancellationToken);
    }
}

/// <summary>
/// Persists non-query-critical photo metadata separately from the stable WI-0050
/// capture-time/GPS table. This keeps existing collection/location contracts intact while
/// allowing richer inspection data to evolve independently.
/// </summary>
public sealed class SqliteExtendedPhotoMetadataRepository
{
    private const int MaximumRawTags = 300;
    private const int MaximumTextLength = 512;
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    private readonly SqliteCatalogueDatabase _database;

    public SqliteExtendedPhotoMetadataRepository(SqliteCatalogueDatabase database)
    {
        ArgumentNullException.ThrowIfNull(database);
        _database = database;
    }

    public async Task SaveAsync(
        AssetRevisionId revisionId,
        PhotoCaptureMetadata metadata,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(metadata);
        await using SqliteConnection connection = await _database.OpenConnectionAsync(cancellationToken);
        await SqliteExtendedPhotoMetadataSchema.EnsureAsync(connection, cancellationToken);

        using SqliteCommand command = connection.CreateCommand();
        command.CommandText = """
            INSERT INTO photo_extended_metadata (
                asset_revision_id,
                camera_make,
                camera_model,
                lens_model,
                orientation,
                exposure_time,
                aperture,
                iso,
                focal_length,
                focal_length_35mm,
                flash,
                gps_altitude,
                raw_tags_json)
            VALUES (
                $revision_id,
                $camera_make,
                $camera_model,
                $lens_model,
                $orientation,
                $exposure_time,
                $aperture,
                $iso,
                $focal_length,
                $focal_length_35mm,
                $flash,
                $gps_altitude,
                $raw_tags_json)
            ON CONFLICT(asset_revision_id) DO UPDATE SET
                camera_make = excluded.camera_make,
                camera_model = excluded.camera_model,
                lens_model = excluded.lens_model,
                orientation = excluded.orientation,
                exposure_time = excluded.exposure_time,
                aperture = excluded.aperture,
                iso = excluded.iso,
                focal_length = excluded.focal_length,
                focal_length_35mm = excluded.focal_length_35mm,
                flash = excluded.flash,
                gps_altitude = excluded.gps_altitude,
                raw_tags_json = excluded.raw_tags_json;
            """;
        command.Parameters.AddWithValue("$revision_id", revisionId.ToString());
        AddOptional(command, "$camera_make", metadata.CameraMake);
        AddOptional(command, "$camera_model", metadata.CameraModel);
        AddOptional(command, "$lens_model", metadata.LensModel);
        AddOptional(command, "$orientation", metadata.Orientation);
        AddOptional(command, "$exposure_time", metadata.ExposureTime);
        AddOptional(command, "$aperture", metadata.Aperture);
        AddOptional(command, "$iso", metadata.Iso);
        AddOptional(command, "$focal_length", metadata.FocalLength);
        AddOptional(command, "$focal_length_35mm", metadata.FocalLength35Mm);
        AddOptional(command, "$flash", metadata.Flash);
        AddOptional(command, "$gps_altitude", metadata.GpsAltitude);
        command.Parameters.AddWithValue("$raw_tags_json", JsonSerializer.Serialize(BoundTags(metadata.RawTags), JsonOptions));
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    public async Task<CatalogueExtendedPhotoMetadata?> GetAsync(
        AssetRevisionId revisionId,
        CancellationToken cancellationToken = default)
    {
        await using SqliteConnection connection = await _database.OpenConnectionAsync(cancellationToken);
        await SqliteExtendedPhotoMetadataSchema.EnsureAsync(connection, cancellationToken);

        using SqliteCommand command = connection.CreateCommand();
        command.CommandText = """
            SELECT
                camera_make,
                camera_model,
                lens_model,
                orientation,
                exposure_time,
                aperture,
                iso,
                focal_length,
                focal_length_35mm,
                flash,
                gps_altitude,
                raw_tags_json
            FROM photo_extended_metadata
            WHERE asset_revision_id = $revision_id;
            """;
        command.Parameters.AddWithValue("$revision_id", revisionId.ToString());
        await using SqliteDataReader reader = await command.ExecuteReaderAsync(cancellationToken);
        if (!await reader.ReadAsync(cancellationToken))
        {
            return null;
        }

        IReadOnlyList<PhotoMetadataTag> tags = DeserializeTags(reader.GetString(11));
        return new CatalogueExtendedPhotoMetadata(
            Optional(reader, 0),
            Optional(reader, 1),
            Optional(reader, 2),
            Optional(reader, 3),
            Optional(reader, 4),
            Optional(reader, 5),
            Optional(reader, 6),
            Optional(reader, 7),
            Optional(reader, 8),
            Optional(reader, 9),
            Optional(reader, 10),
            tags);
    }

    private static IReadOnlyList<PhotoMetadataTag> BoundTags(IReadOnlyList<PhotoMetadataTag> tags) =>
        tags.Take(MaximumRawTags)
            .Select(tag => new PhotoMetadataTag(
                Bound(tag.Directory, 120),
                Bound(tag.Name, 160),
                Bound(tag.Value, MaximumTextLength)))
            .Where(tag => tag.Directory.Length > 0 && tag.Name.Length > 0 && tag.Value.Length > 0)
            .ToArray();

    private static IReadOnlyList<PhotoMetadataTag> DeserializeTags(string json)
    {
        try
        {
            return JsonSerializer.Deserialize<PhotoMetadataTag[]>(json, JsonOptions) ?? [];
        }
        catch (JsonException)
        {
            return [];
        }
    }

    private static void AddOptional(SqliteCommand command, string parameterName, string? value) =>
        command.Parameters.AddWithValue(parameterName, value is null ? DBNull.Value : Bound(value, MaximumTextLength));

    private static string? Optional(SqliteDataReader reader, int ordinal) =>
        reader.IsDBNull(ordinal) ? null : reader.GetString(ordinal);

    private static string Bound(string value, int maximumLength)
    {
        string trimmed = value.Trim();
        return trimmed.Length <= maximumLength ? trimmed : trimmed[..maximumLength];
    }
}
