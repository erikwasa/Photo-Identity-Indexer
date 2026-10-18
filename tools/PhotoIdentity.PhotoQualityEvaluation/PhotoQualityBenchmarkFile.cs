using System.Security.Cryptography;
using System.Text.Json;

namespace PhotoIdentity.PhotoQualityEvaluation;

internal static class PhotoQualityBenchmarkFile
{
    internal static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true,
    };

    public static async Task<PhotoQualityBenchmarkExport> LoadAsync(
        string path,
        CancellationToken cancellationToken = default)
    {
        string fullPath = Path.GetFullPath(path);
        if (!File.Exists(fullPath))
        {
            throw new FileNotFoundException("Benchmark manifest was not found.", fullPath);
        }

        await using FileStream stream = File.OpenRead(fullPath);
        PhotoQualityBenchmarkExport export =
            await JsonSerializer.DeserializeAsync<PhotoQualityBenchmarkExport>(
                stream,
                JsonOptions,
                cancellationToken)
            ?? throw new InvalidDataException("Benchmark manifest JSON is empty or invalid.");

        if (export.SchemaVersion is < 1 or > 2)
        {
            throw new InvalidDataException(
                $"Unsupported benchmark schemaVersion {export.SchemaVersion}; expected 1 or 2.");
        }

        if (string.IsNullOrWhiteSpace(export.BenchmarkId))
        {
            throw new InvalidDataException("Benchmark manifest is missing benchmarkId.");
        }

        if (export.Photos is null || export.Photos.Count == 0)
        {
            throw new InvalidDataException("Benchmark manifest contains no photos.");
        }

        string[] duplicateRevisionIds = export.Photos
            .GroupBy(photo => photo.RevisionId, StringComparer.OrdinalIgnoreCase)
            .Where(group => group.Count() > 1)
            .Select(group => group.Key)
            .ToArray();
        if (duplicateRevisionIds.Length > 0)
        {
            throw new InvalidDataException(
                $"Benchmark manifest contains duplicate revision IDs: {string.Join(", ", duplicateRevisionIds.Take(5))}.");
        }

        return Normalize(export);
    }

    public static async Task SaveAtomicAsync(
        string path,
        PhotoQualityBenchmarkExport export,
        string? backupSuffix = null,
        CancellationToken cancellationToken = default)
    {
        string fullPath = Path.GetFullPath(path);
        string? directory = Path.GetDirectoryName(fullPath);
        if (string.IsNullOrWhiteSpace(directory))
        {
            throw new InvalidOperationException("Benchmark manifest path has no parent directory.");
        }

        Directory.CreateDirectory(directory);
        if (!string.IsNullOrWhiteSpace(backupSuffix) &&
            File.Exists(fullPath))
        {
            string backupPath = fullPath + backupSuffix;
            if (!File.Exists(backupPath))
            {
                File.Copy(fullPath, backupPath, overwrite: false);
            }
        }

        string tempPath = fullPath + $".tmp-{Guid.NewGuid():N}";
        try
        {
            await File.WriteAllTextAsync(
                tempPath,
                JsonSerializer.Serialize(Normalize(export), JsonOptions),
                cancellationToken);
            File.Move(tempPath, fullPath, overwrite: true);
        }
        finally
        {
            if (File.Exists(tempPath))
            {
                File.Delete(tempPath);
            }
        }
    }

    public static string ResolveProxyPath(string proxyRoot, string relativePath)
    {
        string fullRoot = Path.GetFullPath(proxyRoot);
        string path = Path.GetFullPath(Path.Combine(fullRoot, relativePath));
        StringComparison comparison = OperatingSystem.IsWindows()
            ? StringComparison.OrdinalIgnoreCase
            : StringComparison.Ordinal;
        string rootPrefix = fullRoot.EndsWith(Path.DirectorySeparatorChar)
            ? fullRoot
            : fullRoot + Path.DirectorySeparatorChar;
        if (!path.StartsWith(rootPrefix, comparison))
        {
            throw new InvalidDataException(
                "A stored review-proxy path escapes the configured derivative root.");
        }

        return path;
    }

    public static void VerifyProxySha256(PhotoQualityBenchmarkPhoto photo, byte[] content)
    {
        string actual = Convert.ToHexString(SHA256.HashData(content)).ToLowerInvariant();
        if (!string.Equals(actual, photo.ProxySha256, StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidDataException(
                $"Stored proxy hash does not match benchmark metadata for revision {photo.RevisionId}.");
        }
    }

    public static PhotoQualityBenchmarkCoverage Coverage(
        IEnumerable<PhotoQualityBenchmarkPhoto> photos)
    {
        PhotoQualityBenchmarkPhoto[] source = photos.ToArray();
        PhotoIdentity.Core.Imaging.PhotoQualityBenchmarkStratum[] strata =
            source.Select(photo => photo.Stratum).ToArray();

        return new PhotoQualityBenchmarkCoverage(
            source.Length,
            CountBy(strata.Select(item => item.CaptureEra)),
            CountBy(strata.Select(item => item.DeviceFamily)),
            CountBy(strata.Select(item => item.MediaFamily)),
            CountBy(strata.Select(item => item.Resolution)),
            CountBy(strata.Select(item => item.Orientation)),
            CountBy(strata.Select(item => item.Faces)),
            source.Count(photo => photo.TakenAtLocal.HasValue),
            source.Count(photo => photo.Width.HasValue && photo.Height.HasValue),
            source.Count(photo => !string.IsNullOrWhiteSpace(photo.CameraModel)),
            source
                .Where(photo => !string.IsNullOrWhiteSpace(photo.CameraModel))
                .Select(photo => $"{photo.CameraMake}|{photo.CameraModel}".ToLowerInvariant())
                .Distinct(StringComparer.Ordinal)
                .Count());
    }

    public static string StableBlindOrderKey(string benchmarkId, string revisionId)
    {
        byte[] digest = SHA256.HashData(
            System.Text.Encoding.UTF8.GetBytes($"{benchmarkId}\n{revisionId}"));
        return Convert.ToHexString(digest).ToLowerInvariant();
    }

    private static PhotoQualityBenchmarkExport Normalize(PhotoQualityBenchmarkExport export)
    {
        PhotoQualityBenchmarkPhoto[] photos = export.Photos
            .Select(photo => photo with
            {
                SampleKind = string.IsNullOrWhiteSpace(photo.SampleKind)
                    ? (string.Equals(photo.Partition, "challenge", StringComparison.OrdinalIgnoreCase)
                        ? "challenge"
                        : "representative")
                    : photo.SampleKind,
                ChallengeReasons = photo.ChallengeReasons ?? [],
                Review = photo.Review ?? new PhotoQualityBenchmarkHumanReview(null, null, null),
            })
            .ToArray();

        return export with
        {
            SchemaVersion = Math.Max(export.SchemaVersion, 2),
            ChallengeSelectionPolicies = export.ChallengeSelectionPolicies ?? [],
            Photos = photos,
        };
    }

    private static IReadOnlyDictionary<string, int> CountBy(IEnumerable<string> values) =>
        values
            .GroupBy(value => value, StringComparer.Ordinal)
            .OrderBy(group => group.Key, StringComparer.Ordinal)
            .ToDictionary(group => group.Key, group => group.Count(), StringComparer.Ordinal);
}
