using System.Text.Json;
using Npgsql;
using NpgsqlTypes;
using PhotoIdentity.Core.Identifiers;
using PhotoIdentity.Core.Sources;

namespace PhotoIdentity.Persistence.Postgres;

public sealed class PostgresExtendedPhotoMetadataRepository : IExtendedPhotoMetadataRepository
{
    private const int MaximumRawTags = 300;
    private const int MaximumTextLength = 512;
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);
    private readonly PostgresCatalogueDatabase _database;

    public PostgresExtendedPhotoMetadataRepository(PostgresCatalogueDatabase database)
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
        await using NpgsqlConnection connection = await _database.OpenConnectionAsync(cancellationToken);
        await using NpgsqlCommand command = connection.CreateCommand();
        command.CommandText = """
            INSERT INTO photo_extended_metadata (
                asset_revision_id, camera_make, camera_model, lens_model, orientation,
                exposure_time, aperture, iso, focal_length, focal_length_35mm,
                flash, gps_altitude, raw_tags_json)
            VALUES (
                @revision_id, @camera_make, @camera_model, @lens_model, @orientation,
                @exposure_time, @aperture, @iso, @focal_length, @focal_length_35mm,
                @flash, @gps_altitude, @raw_tags_json)
            ON CONFLICT (asset_revision_id) DO UPDATE SET
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
        command.Parameters.AddWithValue("revision_id", Guid.Parse(revisionId.ToString()));
        AddNullable(command, "camera_make", metadata.CameraMake);
        AddNullable(command, "camera_model", metadata.CameraModel);
        AddNullable(command, "lens_model", metadata.LensModel);
        AddNullable(command, "orientation", metadata.Orientation);
        AddNullable(command, "exposure_time", metadata.ExposureTime);
        AddNullable(command, "aperture", metadata.Aperture);
        AddNullable(command, "iso", metadata.Iso);
        AddNullable(command, "focal_length", metadata.FocalLength);
        AddNullable(command, "focal_length_35mm", metadata.FocalLength35Mm);
        AddNullable(command, "flash", metadata.Flash);
        AddNullable(command, "gps_altitude", metadata.GpsAltitude);
        command.Parameters.AddWithValue("raw_tags_json", JsonSerializer.Serialize(BoundTags(metadata.RawTags), JsonOptions));
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    public async Task<CatalogueExtendedPhotoMetadata?> GetAsync(
        AssetRevisionId revisionId,
        CancellationToken cancellationToken = default)
    {
        await using NpgsqlConnection connection = await _database.OpenConnectionAsync(cancellationToken);
        await using NpgsqlCommand command = connection.CreateCommand();
        command.CommandText = """
            SELECT camera_make, camera_model, lens_model, orientation, exposure_time,
                   aperture, iso, focal_length, focal_length_35mm, flash, gps_altitude,
                   raw_tags_json
            FROM photo_extended_metadata
            WHERE asset_revision_id = @revision_id;
            """;
        command.Parameters.AddWithValue("revision_id", Guid.Parse(revisionId.ToString()));
        await using NpgsqlDataReader reader = await command.ExecuteReaderAsync(cancellationToken);
        if (!await reader.ReadAsync(cancellationToken))
        {
            return null;
        }

        return new CatalogueExtendedPhotoMetadata(
            Optional(reader, 0), Optional(reader, 1), Optional(reader, 2),
            Optional(reader, 3), Optional(reader, 4), Optional(reader, 5),
            Optional(reader, 6), Optional(reader, 7), Optional(reader, 8),
            Optional(reader, 9), Optional(reader, 10), DeserializeTags(reader.GetString(11)));
    }

    private static void AddNullable(NpgsqlCommand command, string name, string? value)
    {
        NpgsqlParameter parameter = command.Parameters.Add(name, NpgsqlDbType.Text);
        parameter.Value = value is null ? DBNull.Value : Bound(value, MaximumTextLength);
    }

    private static string? Optional(NpgsqlDataReader reader, int ordinal) =>
        reader.IsDBNull(ordinal) ? null : reader.GetString(ordinal);

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

    private static string Bound(string value, int maximumLength)
    {
        string trimmed = value.Trim();
        return trimmed.Length <= maximumLength ? trimmed : trimmed[..maximumLength];
    }
}
