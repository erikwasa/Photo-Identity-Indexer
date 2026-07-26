using System.Buffers.Binary;
using System.IO.Compression;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.Data.Sqlite;
using PhotoIdentity.Core.Identifiers;
using PhotoIdentity.Core.Recognition;
using PhotoIdentity.Persistence.Sqlite;

namespace PhotoIdentity.ReviewVerification;

public static class Program
{
    private const int FaceCount = 8;

    public static async Task<int> Main(string[] args)
    {
        try
        {
            VerificationOptions options = VerificationOptions.Parse(args);
            VerificationManifest manifest = await PrepareAsync(options.OutputDirectory);
            Console.WriteLine(JsonSerializer.Serialize(manifest, JsonOptions));
            return 0;
        }
        catch (Exception exception)
        {
            Console.Error.WriteLine($"error: {exception.Message}");
            return 1;
        }
    }

    private static async Task<VerificationManifest> PrepareAsync(string outputDirectory)
    {
        string root = Path.GetFullPath(outputDirectory);
        string databasePath = Path.Combine(root, "catalogue.db");
        string cropDirectory = Path.Combine(root, "crops");
        string sourceDirectory = Path.Combine(root, "demo-source");

        ResetDirectory(root);
        Directory.CreateDirectory(cropDirectory);
        Directory.CreateDirectory(sourceDirectory);

        SqliteCatalogueDatabase database = new(databasePath);
        await database.InitializeAsync();

        DateTimeOffset now = DateTimeOffset.UtcNow;
        SourceId sourceId = SourceId.New();
        CatalogueSource source = new(sourceId, "review-verification", sourceDirectory, now);
        SqliteAssetCatalogueRepository assetRepository = new(database);
        List<FaceOccurrenceId> faceIds = [];

        for (int index = 0; index < FaceCount; index++)
        {
            int number = index + 1;
            string sourceName = $"demo-photo-{number:D2}.png";
            string sourcePath = Path.Combine(sourceDirectory, sourceName);
            byte[] sourceBytes = Encoding.UTF8.GetBytes($"Photo Identity review verification fixture {number}");
            await File.WriteAllBytesAsync(sourcePath, sourceBytes);

            AssetId assetId = AssetId.New();
            CatalogueAsset asset = new(assetId, sourceId, $"verification/{sourceName}", now.AddSeconds(index));
            CatalogueAssetRevision revision = new(
                AssetRevisionId.New(),
                assetId,
                Digest(sourceBytes),
                sourceBytes.LongLength,
                now.AddSeconds(index),
                "image/png",
                1200,
                800);
            CatalogueAssetRevision persistedRevision = await assetRepository.SaveRevisionAsync(source, asset, revision);

            FaceOccurrenceId faceId = FaceOccurrenceId.New();
            faceIds.Add(faceId);
            byte[] cropBytes = DemoPng.Create(112, 112, index);
            string cropPath = Path.Combine(cropDirectory, $"face-{number:D2}.png");
            await File.WriteAllBytesAsync(cropPath, cropBytes);

            await InsertFaceAsync(
                database,
                faceId,
                persistedRevision.Id,
                index,
                cropPath,
                Digest(cropBytes),
                now.AddSeconds(index));
        }

        SqliteReviewRepository reviewRepository = new(database);
        CatalogueReviewPerson demoPerson = await reviewRepository.CreatePersonAsync(
            "Demo Person",
            now.AddMinutes(1));
        await reviewRepository.AssignAsync(
            faceIds[^2],
            demoPerson.Id,
            "verification:seed",
            now.AddMinutes(2),
            "Seeded assignment for the Assigned filter.");
        await reviewRepository.RejectAsync(
            faceIds[^1],
            "verification:seed",
            now.AddMinutes(3),
            "Seeded rejection for the Rejected filter.");

        return new VerificationManifest(
            SchemaVersion: 1,
            DatabasePath: databasePath,
            ArtifactDirectory: root,
            FaceCount: FaceCount,
            UnreviewedCount: FaceCount - 2,
            AssignedCount: 1,
            RejectedCount: 1,
            GeneratedAtUtc: DateTimeOffset.UtcNow);
    }

    private static async Task InsertFaceAsync(
        SqliteCatalogueDatabase database,
        FaceOccurrenceId faceId,
        AssetRevisionId revisionId,
        int ordinal,
        string cropPath,
        Sha256Digest cropHash,
        DateTimeOffset createdAtUtc)
    {
        await using SqliteConnection connection = await database.OpenConnectionAsync();
        using SqliteTransaction transaction = connection.BeginTransaction();
        using SqliteCommand command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = """
            INSERT INTO face_occurrences (id, asset_revision_id, ordinal, created_at_utc)
            VALUES ($face_id, $revision_id, $ordinal, $created_at_utc);

            INSERT INTO face_observations (
                face_occurrence_id,
                detector_model_id,
                detector_model_hash,
                confidence,
                bounding_box_json,
                landmarks_json,
                observed_at_utc)
            VALUES (
                $face_id,
                'review-verification-detector',
                $model_hash,
                $confidence,
                '{"x":16,"y":14,"width":80,"height":84}',
                '[{"x":38,"y":44},{"x":74,"y":44},{"x":56,"y":62},{"x":42,"y":82},{"x":70,"y":82}]',
                $created_at_utc);

            INSERT INTO face_crops (
                id,
                face_occurrence_id,
                crop_protocol,
                content_sha256,
                storage_path,
                width,
                height,
                created_at_utc)
            VALUES (
                $crop_id,
                $face_id,
                'review-verification-v1',
                $crop_hash,
                $crop_path,
                112,
                112,
                $created_at_utc);
            """;
        command.Parameters.AddWithValue("$face_id", faceId.ToString());
        command.Parameters.AddWithValue("$revision_id", revisionId.ToString());
        command.Parameters.AddWithValue("$ordinal", ordinal);
        command.Parameters.AddWithValue("$created_at_utc", createdAtUtc.ToUniversalTime().ToString("O"));
        command.Parameters.AddWithValue("$model_hash", new string('b', 64));
        command.Parameters.AddWithValue("$confidence", 0.91 + (ordinal * 0.01));
        command.Parameters.AddWithValue("$crop_id", FaceCropId.New().ToString());
        command.Parameters.AddWithValue("$crop_hash", cropHash.ToString());
        command.Parameters.AddWithValue("$crop_path", cropPath);
        await command.ExecuteNonQueryAsync();
        transaction.Commit();
    }

    private static Sha256Digest Digest(ReadOnlySpan<byte> bytes) =>
        new(Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant());

    private static void ResetDirectory(string directory)
    {
        if (Directory.Exists(directory))
        {
            Directory.Delete(directory, recursive: true);
        }

        Directory.CreateDirectory(directory);
    }

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
    };

    private sealed record VerificationManifest(
        int SchemaVersion,
        string DatabasePath,
        string ArtifactDirectory,
        int FaceCount,
        int UnreviewedCount,
        int AssignedCount,
        int RejectedCount,
        DateTimeOffset GeneratedAtUtc);

    private sealed record VerificationOptions(string OutputDirectory)
    {
        public static VerificationOptions Parse(string[] args)
        {
            string? output = null;
            for (int index = 0; index < args.Length; index++)
            {
                if (args[index] == "--output" && index + 1 < args.Length)
                {
                    output = args[++index];
                    continue;
                }

                throw new ArgumentException($"Unknown or incomplete argument '{args[index]}'.");
            }

            ArgumentException.ThrowIfNullOrWhiteSpace(output);
            return new VerificationOptions(output);
        }
    }

    private static class DemoPng
    {
        private static readonly uint[] CrcTable = BuildCrcTable();

        public static byte[] Create(int width, int height, int variant)
        {
            using MemoryStream png = new();
            png.Write([137, 80, 78, 71, 13, 10, 26, 10]);

            Span<byte> header = stackalloc byte[13];
            BinaryPrimitives.WriteUInt32BigEndian(header[..4], (uint)width);
            BinaryPrimitives.WriteUInt32BigEndian(header.Slice(4, 4), (uint)height);
            header[8] = 8;
            header[9] = 6;
            WriteChunk(png, "IHDR", header);

            byte[] raw = new byte[(width * 4 + 1) * height];
            int offset = 0;
            for (int y = 0; y < height; y++)
            {
                raw[offset++] = 0;
                for (int x = 0; x < width; x++)
                {
                    double dx = x - (width / 2.0);
                    double dy = y - (height / 2.0);
                    bool centre = (dx * dx) + (dy * dy) < (width * 0.28) * (width * 0.28);
                    raw[offset++] = (byte)((x * 2 + variant * 29) % 256);
                    raw[offset++] = (byte)((y * 2 + variant * 47) % 256);
                    raw[offset++] = centre ? (byte)235 : (byte)(80 + variant * 17);
                    raw[offset++] = 255;
                }
            }

            using MemoryStream compressed = new();
            using (ZLibStream zlib = new(compressed, CompressionLevel.SmallestSize, leaveOpen: true))
            {
                zlib.Write(raw);
            }

            WriteChunk(png, "IDAT", compressed.ToArray());
            WriteChunk(png, "IEND", ReadOnlySpan<byte>.Empty);
            return png.ToArray();
        }

        private static void WriteChunk(Stream output, string type, ReadOnlySpan<byte> data)
        {
            Span<byte> length = stackalloc byte[4];
            BinaryPrimitives.WriteUInt32BigEndian(length, (uint)data.Length);
            output.Write(length);

            byte[] typeBytes = Encoding.ASCII.GetBytes(type);
            output.Write(typeBytes);
            output.Write(data);

            uint crc = 0xffffffff;
            foreach (byte value in typeBytes)
            {
                crc = CrcTable[(crc ^ value) & 0xff] ^ (crc >> 8);
            }
            foreach (byte value in data)
            {
                crc = CrcTable[(crc ^ value) & 0xff] ^ (crc >> 8);
            }

            Span<byte> crcBytes = stackalloc byte[4];
            BinaryPrimitives.WriteUInt32BigEndian(crcBytes, crc ^ 0xffffffff);
            output.Write(crcBytes);
        }

        private static uint[] BuildCrcTable()
        {
            uint[] table = new uint[256];
            for (uint index = 0; index < table.Length; index++)
            {
                uint value = index;
                for (int bit = 0; bit < 8; bit++)
                {
                    value = (value & 1) == 1 ? 0xedb88320 ^ (value >> 1) : value >> 1;
                }
                table[index] = value;
            }
            return table;
        }
    }
}
