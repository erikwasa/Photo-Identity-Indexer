using System.Text.Json;
using Microsoft.Data.Sqlite;
using PhotoIdentity.Core.Geometry;
using PhotoIdentity.Core.Identifiers;
using PhotoIdentity.Core.Imaging;
using PhotoIdentity.Imaging.OpenCv;
using PhotoIdentity.Persistence.Sqlite;

namespace PhotoIdentity.Api;

/// <summary>
/// Resolves a face occurrence to privacy-safe review pixels without exposing source paths.
/// Durable face-review derivatives are preferred for both Gallery and Details; if a catalogue is
/// still being backfilled, the durable whole-photo review proxy remains the safe fallback.
/// Authoritative originals are never opened by this runtime review path.
/// </summary>
public sealed class ReviewFacePreviewResolver
{
    private readonly SqliteCatalogueDatabase _database;
    private readonly CollectionReviewProxyFileResolver _proxyFileResolver;
    private readonly OpenCvReviewFaceRenderer _renderer;

    public ReviewFacePreviewResolver(
        SqliteCatalogueDatabase database,
        CollectionReviewProxyFileResolver proxyFileResolver,
        CollectionOriginalAccessService originalAccessService,
        OpenCvReviewFaceRenderer renderer)
    {
        ArgumentNullException.ThrowIfNull(database);
        ArgumentNullException.ThrowIfNull(proxyFileResolver);
        ArgumentNullException.ThrowIfNull(originalAccessService);
        ArgumentNullException.ThrowIfNull(renderer);
        _database = database;
        _proxyFileResolver = proxyFileResolver;
        _renderer = renderer;
    }

    public async Task<EncodedReviewFace?> RenderAsync(
        FaceOccurrenceId faceOccurrenceId,
        int maximumEdge,
        bool preferVerifiedOriginal = false,
        CancellationToken cancellationToken = default)
    {
        _ = preferVerifiedOriginal;
        FaceReviewDerivativeFile? durable = await _proxyFileResolver.ResolveFaceReviewAsync(
            faceOccurrenceId,
            cancellationToken);
        if (durable is not null)
        {
            EncodedReviewFace? rendered = await RenderStoredDerivativeAsync(
                durable,
                maximumEdge,
                cancellationToken);
            if (rendered is not null)
            {
                return rendered;
            }
        }

        ReviewFaceGeometry? geometry = await GetGeometryAsync(faceOccurrenceId, cancellationToken);
        if (geometry is null)
        {
            return null;
        }

        CollectionPhotoFile? proxy = await _proxyFileResolver.ResolveAsync(
            geometry.AssetRevisionId,
            cancellationToken);
        if (proxy is null)
        {
            return null;
        }

        return await _renderer.RenderAsync(
            proxy.Path,
            geometry.BoundingBox,
            maximumEdge,
            cancellationToken);
    }

    private static async Task<EncodedReviewFace?> RenderStoredDerivativeAsync(
        FaceReviewDerivativeFile durable,
        int maximumEdge,
        CancellationToken cancellationToken)
    {
        if (maximumEdge >= Math.Max(durable.Width, durable.Height))
        {
            try
            {
                byte[] content = await File.ReadAllBytesAsync(durable.Path, cancellationToken);
                return new EncodedReviewFace(
                    content,
                    "image/jpeg",
                    durable.Width,
                    durable.Height);
            }
            catch (Exception exception) when (
                exception is IOException or
                UnauthorizedAccessException or
                System.Security.SecurityException)
            {
                return null;
            }
        }

        ReviewProxyProfile responseProfile = new(
            $"face-response-{maximumEdge}",
            maximumEdge,
            OpenCvReviewFaceRenderer.JpegQuality);
        try
        {
            EncodedReviewProxy encoded = await new OpenCvReviewProxyRenderer().RenderAsync(
                durable.Path,
                responseProfile,
                cancellationToken);
            return new EncodedReviewFace(
                encoded.Content,
                encoded.ContentType,
                encoded.Width,
                encoded.Height);
        }
        catch (InvalidDataException)
        {
            return null;
        }
    }

    private async Task<ReviewFaceGeometry?> GetGeometryAsync(
        FaceOccurrenceId faceOccurrenceId,
        CancellationToken cancellationToken)
    {
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
                face_occurrences.asset_revision_id,
                latest_observation.bounding_box_json,
                asset_revisions.width,
                asset_revisions.height
            FROM face_occurrences
            INNER JOIN asset_revisions
                ON asset_revisions.id = face_occurrences.asset_revision_id
            LEFT JOIN latest_observation
                ON latest_observation.face_occurrence_id = face_occurrences.id
               AND latest_observation.row_number = 1
            WHERE face_occurrences.id = $face_occurrence_id;
            """;
        command.Parameters.AddWithValue("$face_occurrence_id", faceOccurrenceId.ToString());

        await using SqliteDataReader reader = await command.ExecuteReaderAsync(cancellationToken);
        if (!await reader.ReadAsync(cancellationToken) || reader.IsDBNull(1))
        {
            return null;
        }

        if (!Guid.TryParse(reader.GetString(0), out Guid revisionGuid) || revisionGuid == Guid.Empty)
        {
            return null;
        }

        int? photoWidth = reader.IsDBNull(2) ? null : reader.GetInt32(2);
        int? photoHeight = reader.IsDBNull(3) ? null : reader.GetInt32(3);
        if (!TryParseBoundingBox(reader.GetString(1), photoWidth, photoHeight, out NormalizedBoundingBox boundingBox))
        {
            return null;
        }

        return new ReviewFaceGeometry(AssetRevisionId.From(revisionGuid), boundingBox);
    }

    internal static NormalizedBoundingBox? CalculateTargetBoundingBox(
        string? value,
        int? photoWidth,
        int? photoHeight)
    {
        if (string.IsNullOrWhiteSpace(value) ||
            photoWidth is not > 0 ||
            photoHeight is not > 0 ||
            !TryParseBoundingBox(value, photoWidth, photoHeight, out NormalizedBoundingBox boundingBox))
        {
            return null;
        }

        return OpenCvReviewFaceRenderer.CalculateTargetBoundingBox(
            photoWidth.Value,
            photoHeight.Value,
            boundingBox);
    }

    internal static NormalizedBoundingBox? CalculateTargetBoundingBoxFromNormalizedObservation(
        string? value,
        int geometryWidth,
        int geometryHeight)
    {
        if (string.IsNullOrWhiteSpace(value) ||
            geometryWidth <= 0 ||
            geometryHeight <= 0 ||
            !TryParseBoundingBox(value, photoWidth: null, photoHeight: null, out NormalizedBoundingBox boundingBox))
        {
            return null;
        }

        return OpenCvReviewFaceRenderer.CalculateTargetBoundingBox(
            geometryWidth,
            geometryHeight,
            boundingBox);
    }

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

    private sealed record ReviewFaceGeometry(
        AssetRevisionId AssetRevisionId,
        NormalizedBoundingBox BoundingBox);
}
