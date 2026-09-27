using System.Buffers.Binary;
using System.IO.Compression;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Npgsql;
using PhotoIdentity.Core.Geometry;
using PhotoIdentity.Core.Identifiers;
using PhotoIdentity.Core.Recognition;
using PhotoIdentity.Core.Review;
using PhotoIdentity.Persistence.Postgres;

namespace PhotoIdentity.ReviewVerification;

public static class Program
{
    private const int FaceCount = 64;
    private const string AdminConnectionEnvironmentVariable =
        "PHOTOIDENTITY_REVIEW_VERIFICATION_POSTGRES_ADMIN_CONNECTION_STRING";
    private static readonly ModelId EmbedderModelId = new("review-verification-embedder");
    private static readonly Sha256Digest EmbedderModelHash = new(new string('e', 64));
    private static readonly ModelId DetectorModelId = new("review-verification-detector");
    private static readonly Sha256Digest DetectorModelHash = new(new string('b', 64));

    public static async Task<int> Main(string[] args)
    {
        try
        {
            VerificationOptions options = VerificationOptions.Parse(args);
            string adminConnectionString = GetRequiredAdminConnectionString();
            if (!string.IsNullOrWhiteSpace(options.DropDatabaseName))
            {
                await DropDatabaseAsync(adminConnectionString, options.DropDatabaseName);
                return 0;
            }

            VerificationManifest manifest = await PrepareAsync(
                options.OutputDirectory!,
                adminConnectionString);
            Console.WriteLine(JsonSerializer.Serialize(manifest, JsonOptions));
            return 0;
        }
        catch (Exception exception)
        {
            Console.Error.WriteLine($"error: {exception.Message}");
            return 1;
        }
    }

    private static async Task<VerificationManifest> PrepareAsync(
        string outputDirectory,
        string adminConnectionString)
    {
        string root = Path.GetFullPath(outputDirectory);
        string cropDirectory = Path.Combine(root, "crops");
        string sourceDirectory = Path.Combine(root, "demo-source");
        string databaseName = $"photoidentity_review_verification_{Guid.NewGuid():N}";
        string connectionString = await CreateDatabaseAsync(adminConnectionString, databaseName);

        try
        {
            ResetDirectory(root);
            Directory.CreateDirectory(cropDirectory);
            Directory.CreateDirectory(sourceDirectory);

            await using PostgresCatalogueDatabase database = new(connectionString);
            PostgresInitializationResult initialization = await database.TryInitializeAsync();
            if (initialization.Error is not null)
            {
                throw new InvalidOperationException(
                    $"PostgreSQL review-verification catalogue initialization failed with status '{initialization.Health.Status}'.",
                    initialization.Error);
            }

            DateTimeOffset now = DateTimeOffset.UtcNow;
            SourceId sourceId = SourceId.New();
            await SeedSourceAsync(database, sourceId, sourceDirectory, now);
            PostgresFaceInspectionRepository faceRepository = new(database);
            List<FaceOccurrenceId> faceIds = [];

            for (int index = 0; index < FaceCount; index++)
            {
                int number = index + 1;
                string sourceName = $"demo-photo-{number:D2}.png";
                string sourcePath = Path.Combine(sourceDirectory, sourceName);
                byte[] sourceBytes = Encoding.UTF8.GetBytes($"Photo Identity review verification fixture {number}");
                await File.WriteAllBytesAsync(sourcePath, sourceBytes);

                AssetId assetId = AssetId.New();
                AssetRevisionId revisionId = AssetRevisionId.New();
                await SeedRevisionAsync(
                    database,
                    sourceId,
                    assetId,
                    revisionId,
                    $"verification/{sourceName}",
                    Digest(sourceBytes),
                    sourceBytes.LongLength,
                    now.AddSeconds(index));

                byte[] cropBytes = DemoPng.Create(112, 112, index);
                string cropPath = Path.Combine(cropDirectory, $"face-{number:D2}.png");
                await File.WriteAllBytesAsync(cropPath, cropBytes);

                FaceOccurrenceId faceId = FaceOccurrenceId.New();
                FaceCropId cropId = FaceCropId.New();
                await faceRepository.SaveInspectionAsync(
                    new FaceInspectionWrite(
                        faceId,
                        revisionId,
                        0,
                        now.AddSeconds(index),
                        DetectorModelId,
                        DetectorModelHash,
                        0.91 + (index * 0.001),
                        new NormalizedBoundingBox(0.14, 0.12, 0.72, 0.75),
                        CreateLandmarks(),
                        cropId,
                        new AlignmentProtocolId("review-verification-v1"),
                        Digest(cropBytes),
                        cropPath,
                        112,
                        112,
                        EmbedderModelId,
                        EmbedderModelHash,
                        new EmbeddingVector(EmbeddingFor(index))));
                faceIds.Add(faceId);
            }

            PostgresReviewActionRepository reviewRepository = new(database);
            ReviewPerson primaryPerson = await reviewRepository.CreatePersonAsync(
                "Demo Person",
                now.AddMinutes(1));
            ReviewPerson secondaryPerson = await reviewRepository.CreatePersonAsync(
                "Second Demo Person",
                now.AddMinutes(1).AddSeconds(1));
            ReviewPerson mergeSourcePerson = await reviewRepository.CreatePersonAsync(
                "Merge Source Person",
                now.AddMinutes(1).AddSeconds(2));

            await reviewRepository.AssignAsync(
                faceIds[0],
                primaryPerson.Id,
                "verification:seed",
                now.AddMinutes(2),
                "Primary matcher exemplar.");
            await reviewRepository.AssignAsync(
                faceIds[1],
                secondaryPerson.Id,
                "verification:seed",
                now.AddMinutes(2).AddSeconds(1),
                "Secondary matcher exemplar.");
            await reviewRepository.AssignAsync(
                faceIds[8],
                mergeSourcePerson.Id,
                "verification:seed",
                now.AddMinutes(2).AddSeconds(2),
                "Disposable person-merge source assignment.");
            await reviewRepository.RejectAsync(
                faceIds[2],
                "verification:seed",
                now.AddMinutes(3),
                "Seeded rejection for the Rejected filter.");

            int suggestedTargetCount = await RegenerateSuggestionsAsync(database, now.AddMinutes(4));
            if (suggestedTargetCount < 2)
            {
                throw new InvalidOperationException(
                    "The PostgreSQL verification fixture did not generate enough ranked suggestions.");
            }

            return new VerificationManifest(
                SchemaVersion: 3,
                DatabaseName: databaseName,
                PostgresConnectionString: connectionString,
                ArtifactDirectory: root,
                FaceCount: FaceCount,
                UnreviewedCount: FaceCount - 4,
                AssignedCount: 3,
                RejectedCount: 1,
                MutationFaceId: faceIds[3].ToString(),
                BulkFaceIds: [faceIds[4].ToString(), faceIds[5].ToString()],
                SuggestionAcceptFaceId: faceIds[6].ToString(),
                SuggestionRejectFaceId: faceIds[7].ToString(),
                MergeSourceFaceId: faceIds[8].ToString(),
                RejectionFaceId: faceIds[9].ToString(),
                RenamePersonId: secondaryPerson.Id.ToString(),
                RenameOriginalDisplayName: secondaryPerson.DisplayName,
                MergeSourcePersonId: mergeSourcePerson.Id.ToString(),
                MergeTargetPersonId: primaryPerson.Id.ToString(),
                EmbedderModelId: EmbedderModelId.ToString(),
                EmbedderModelHash: EmbedderModelHash.ToString(),
                GeneratedAtUtc: DateTimeOffset.UtcNow);
        }
        catch
        {
            await DropDatabaseAsync(adminConnectionString, databaseName);
            throw;
        }
    }

    private static async Task<int> RegenerateSuggestionsAsync(
        PostgresCatalogueDatabase database,
        DateTimeOffset startedAtUtc)
    {
        PostgresIdentitySuggestionPolicyRepository policies = new(database);
        ReviewIdentitySuggestionPolicy policy = await policies.GetAsync(
            EmbedderModelId,
            EmbedderModelHash);
        PostgresIdentityMatchRegenerationRepository runs = new(database);
        ReviewIdentityMatchRegenerationRun run = await runs.StartAsync(
            EmbedderModelId,
            EmbedderModelHash,
            policy.Version,
            "verification:seed",
            startedAtUtc);
        PostgresIdentityMatchRegenerationScorer scorer = new(database);
        await scorer.PrepareRunAsync(run);

        int suggestedTargets = 0;
        DateTimeOffset cursor = startedAtUtc;
        try
        {
            while (true)
            {
                ReviewIdentityMatchRegenerationTarget? target = await runs.ClaimNextTargetAsync(
                    run.Id,
                    cursor = cursor.AddMilliseconds(1));
                if (target is null)
                {
                    break;
                }

                int suggestionCount = await scorer.ScoreTargetAsync(
                    EmbedderModelId,
                    EmbedderModelHash,
                    target.FaceOccurrenceId);
                if (suggestionCount > 0)
                {
                    suggestedTargets++;
                }
                await runs.CompleteTargetAsync(
                    run.Id,
                    target.FaceOccurrenceId,
                    suggestionCount,
                    cursor = cursor.AddMilliseconds(1));
            }

            await scorer.RemoveObsoleteRankingsAsync(
                EmbedderModelId,
                EmbedderModelHash,
                run.Id);
            await runs.CompleteRunAsync(
                run.Id,
                automaticallyAssignedCount: 0,
                cursor.AddMilliseconds(1));
        }
        finally
        {
            await scorer.ReleaseRunAsync(run.Id);
        }

        return suggestedTargets;
    }

    private static async Task SeedSourceAsync(
        PostgresCatalogueDatabase database,
        SourceId sourceId,
        string rootLocator,
        DateTimeOffset createdAtUtc)
    {
        await using NpgsqlConnection connection = await database.OpenConnectionAsync();
        await using NpgsqlCommand command = connection.CreateCommand();
        command.CommandText = """
            INSERT INTO sources (id, kind, root_locator, created_at_utc)
            VALUES (@id, 'review-verification', @root_locator, @created_at_utc);
            """;
        command.Parameters.AddWithValue("id", Guid.Parse(sourceId.ToString()));
        command.Parameters.AddWithValue("root_locator", rootLocator);
        command.Parameters.AddWithValue("created_at_utc", createdAtUtc.ToUniversalTime());
        await command.ExecuteNonQueryAsync();
    }

    private static async Task SeedRevisionAsync(
        PostgresCatalogueDatabase database,
        SourceId sourceId,
        AssetId assetId,
        AssetRevisionId revisionId,
        string sourceKey,
        Sha256Digest contentHash,
        long sizeBytes,
        DateTimeOffset observedAtUtc)
    {
        await using NpgsqlConnection connection = await database.OpenConnectionAsync();
        await using NpgsqlCommand command = connection.CreateCommand();
        command.CommandText = """
            INSERT INTO assets (id, source_id, source_key, created_at_utc)
            VALUES (@asset_id, @source_id, @source_key, @observed_at_utc);

            INSERT INTO asset_revisions (
                id,
                asset_id,
                content_sha256,
                size_bytes,
                observed_at_utc,
                media_type,
                width,
                height)
            VALUES (
                @revision_id,
                @asset_id,
                @content_sha256,
                @size_bytes,
                @observed_at_utc,
                'image/png',
                1200,
                800);
            """;
        command.Parameters.AddWithValue("asset_id", Guid.Parse(assetId.ToString()));
        command.Parameters.AddWithValue("source_id", Guid.Parse(sourceId.ToString()));
        command.Parameters.AddWithValue("source_key", sourceKey);
        command.Parameters.AddWithValue("revision_id", Guid.Parse(revisionId.ToString()));
        command.Parameters.AddWithValue("content_sha256", contentHash.ToString());
        command.Parameters.AddWithValue("size_bytes", sizeBytes);
        command.Parameters.AddWithValue("observed_at_utc", observedAtUtc.ToUniversalTime());
        await command.ExecuteNonQueryAsync();
    }

    private static async Task<string> CreateDatabaseAsync(
        string adminConnectionString,
        string databaseName)
    {
        NpgsqlConnectionStringBuilder adminBuilder = new(adminConnectionString)
        {
            Pooling = false,
        };
        await using NpgsqlConnection connection = new(adminBuilder.ConnectionString);
        await connection.OpenAsync();
        await using NpgsqlCommand command = connection.CreateCommand();
        command.CommandText = $"CREATE DATABASE {QuoteIdentifier(databaseName)};";
        await command.ExecuteNonQueryAsync();

        NpgsqlConnectionStringBuilder targetBuilder = new(adminConnectionString)
        {
            Database = databaseName,
            Pooling = false,
        };
        return targetBuilder.ConnectionString;
    }

    private static async Task DropDatabaseAsync(
        string adminConnectionString,
        string databaseName)
    {
        if (!databaseName.StartsWith("photoidentity_review_verification_", StringComparison.Ordinal))
        {
            throw new InvalidOperationException(
                $"Refusing to drop non-verification database '{databaseName}'.");
        }

        NpgsqlConnectionStringBuilder adminBuilder = new(adminConnectionString)
        {
            Pooling = false,
        };
        await using NpgsqlConnection connection = new(adminBuilder.ConnectionString);
        await connection.OpenAsync();
        await using NpgsqlCommand command = connection.CreateCommand();
        command.CommandText = $"DROP DATABASE IF EXISTS {QuoteIdentifier(databaseName)} WITH (FORCE);";
        await command.ExecuteNonQueryAsync();
    }

    private static string GetRequiredAdminConnectionString()
    {
        string? value = Environment.GetEnvironmentVariable(AdminConnectionEnvironmentVariable);
        if (string.IsNullOrWhiteSpace(value))
        {
            value = Environment.GetEnvironmentVariable(
                "PHOTOIDENTITY_TEST_POSTGRES_ADMIN_CONNECTION_STRING");
        }
        return !string.IsNullOrWhiteSpace(value)
            ? value
            : throw new InvalidOperationException(
                $"Set {AdminConnectionEnvironmentVariable} to a PostgreSQL admin connection string before running review verification.");
    }

    private static string QuoteIdentifier(string value) =>
        '"' + value.Replace("\"", "\"\"", StringComparison.Ordinal) + '"';

    private static float[] EmbeddingFor(int index) => index switch
    {
        0 => [1f, 0f, 0f],
        1 => [0f, 1f, 0f],
        2 => [0f, 0f, 1f],
        3 => [0.98f, 0.15f, 0f],
        4 => [0.95f, 0.2f, 0f],
        5 => [0.2f, 0.95f, 0f],
        6 => [0.99f, 0.1f, 0f],
        7 => [0.1f, 0.99f, 0f],
        8 => [0f, 0f, 1f],
        9 => [0.7f, 0.7f, 0f],
        10 => [0.85f, 0.25f, 0f],
        _ => index % 2 == 0 ? [0.9f, 0.3f, 0f] : [0.3f, 0.9f, 0f],
    };

    private static NormalizedFaceLandmarks CreateLandmarks() =>
        new(
            new NormalizedPoint(0.32, 0.34),
            new NormalizedPoint(0.68, 0.34),
            new NormalizedPoint(0.50, 0.50),
            new NormalizedPoint(0.37, 0.69),
            new NormalizedPoint(0.63, 0.69));

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
        string DatabaseName,
        string PostgresConnectionString,
        string ArtifactDirectory,
        int FaceCount,
        int UnreviewedCount,
        int AssignedCount,
        int RejectedCount,
        string MutationFaceId,
        IReadOnlyList<string> BulkFaceIds,
        string SuggestionAcceptFaceId,
        string SuggestionRejectFaceId,
        string MergeSourceFaceId,
        string RejectionFaceId,
        string RenamePersonId,
        string RenameOriginalDisplayName,
        string MergeSourcePersonId,
        string MergeTargetPersonId,
        string EmbedderModelId,
        string EmbedderModelHash,
        DateTimeOffset GeneratedAtUtc);

    private sealed record VerificationOptions(
        string? OutputDirectory,
        string? DropDatabaseName)
    {
        public static VerificationOptions Parse(string[] args)
        {
            string? output = null;
            string? dropDatabaseName = null;
            for (int index = 0; index < args.Length; index++)
            {
                if (args[index] == "--output" && index + 1 < args.Length)
                {
                    output = args[++index];
                    continue;
                }
                if (args[index] == "--drop-database" && index + 1 < args.Length)
                {
                    dropDatabaseName = args[++index];
                    continue;
                }

                throw new ArgumentException($"Unknown or incomplete argument '{args[index]}'.");
            }

            if (string.IsNullOrWhiteSpace(dropDatabaseName))
            {
                ArgumentException.ThrowIfNullOrWhiteSpace(output);
            }
            else if (!string.IsNullOrWhiteSpace(output))
            {
                throw new ArgumentException("--output and --drop-database cannot be combined.");
            }

            return new VerificationOptions(output, dropDatabaseName);
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
