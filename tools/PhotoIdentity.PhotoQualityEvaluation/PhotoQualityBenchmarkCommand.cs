using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using PhotoIdentity.Core.Collections;
using PhotoIdentity.Core.Identifiers;
using PhotoIdentity.Core.Imaging;
using PhotoIdentity.Core.Recognition;
using PhotoIdentity.Imaging.OpenCv;
using PhotoIdentity.Persistence.Postgres;

namespace PhotoIdentity.PhotoQualityEvaluation;

internal static class PhotoQualityBenchmarkCommand
{
    private const string ConnectionStringEnvironmentVariable = "PhotoIdentity__Postgres__ConnectionString";
    private const string ProxyRootEnvironmentVariable = "PhotoIdentity__ReviewProxyRoot";
    private const string ProxyProfileEnvironmentVariable = "PhotoIdentity__ReviewProxyProfileId";
    private const int LeakageHammingDistance = 4;
    private const double CalibrationFraction = 0.60d;

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
    };

    public static async Task<int> RunAsync(string[] args)
    {
        try
        {
            if (args.Any(arg => arg is "-h" or "--help"))
            {
                PrintUsage();
                return 0;
            }

            Options options = Parse(args);
            string connectionString = RequiredSetting(
                options.ConnectionString,
                ConnectionStringEnvironmentVariable,
                "PostgreSQL catalogue connection string");
            string proxyRoot = Path.GetFullPath(RequiredSetting(
                options.ProxyRoot,
                ProxyRootEnvironmentVariable,
                "review-proxy derivative root"));
            string profileId = RequiredSetting(
                options.ProfileId,
                ProxyProfileEnvironmentVariable,
                "review-proxy profile id");
            if (!Directory.Exists(proxyRoot))
            {
                throw new DirectoryNotFoundException($"Review-proxy root does not exist: {proxyRoot}");
            }

            string outputPath = ValidateOutputPath(options.OutputPath);
            if (File.Exists(outputPath) && !options.Force)
            {
                throw new IOException($"Output already exists: {outputPath}. Pass --force to replace it.");
            }

            await using PostgresCatalogueDatabase database = new(connectionString);
            PostgresArchiveReviewProxyRepository proxyRepository = new(database);
            ReviewProxyProfile profile = await proxyRepository.GetProfileAsync(profileId)
                ?? throw new InvalidOperationException(
                    $"Review proxy profile '{profileId}' is not registered in the catalogue.");
            PostgresPhotoQualityEvaluationRepository repository = new(database);
            IReadOnlyList<PhotoQualityBenchmarkPopulationRow> population =
                await repository.ReadCurrentProxyPopulationAsync(profile.Id);
            if (population.Count == 0)
            {
                throw new InvalidOperationException(
                    $"No current photo revisions have durable review proxies for profile '{profile.Id}'.");
            }

            PhotoQualityBenchmarkDescriptor[] descriptors = population
                .Select(ToDescriptor)
                .ToArray();
            IReadOnlyList<PhotoQualityBenchmarkSelection> selected =
                PhotoQualityBenchmarkSelector.SelectRepresentative(
                    descriptors,
                    options.RepresentativeCount);

            Dictionary<AssetRevisionId, PhotoQualityBenchmarkPopulationRow> rowByRevision =
                population.ToDictionary(row => row.AssetRevisionId);
            OpenCvPerceptualHashCalculator hashCalculator = new();
            Dictionary<AssetRevisionId, PhotoPerceptualHash64> hashes = [];
            foreach (PhotoQualityBenchmarkSelection selection in selected)
            {
                PhotoQualityBenchmarkPopulationRow row = rowByRevision[selection.Photo.RevisionId];
                string proxyPath = ResolveProxyPath(proxyRoot, row.Proxy.RelativePath);
                byte[] content = await File.ReadAllBytesAsync(proxyPath);
                VerifyProxy(row, content);
                hashes[row.AssetRevisionId] = hashCalculator.Compute(content);
            }

            IReadOnlyDictionary<AssetRevisionId, string> nearDuplicateGroups =
                BuildNearDuplicateGroups(hashes, LeakageHammingDistance);
            IReadOnlyList<PhotoQualityBenchmarkPartitionAssignment> assignments =
                PhotoQualityBenchmarkSelector.AssignPartitions(
                    selected,
                    nearDuplicateGroups,
                    CalibrationFraction);

            Dictionary<AssetRevisionId, PhotoQualityBenchmarkPartition> partitionByRevision =
                assignments.ToDictionary(
                    assignment => assignment.Selection.Photo.RevisionId,
                    assignment => assignment.Partition);

            PhotoQualityBenchmarkPhoto[] photos = selected
                .Select(selection =>
                {
                    PhotoQualityBenchmarkPopulationRow row = rowByRevision[selection.Photo.RevisionId];
                    PhotoQualityBenchmarkPartition partition =
                        partitionByRevision[selection.Photo.RevisionId];
                    return new PhotoQualityBenchmarkPhoto(
                        row.AssetRevisionId.ToString(),
                        row.SourceKey,
                        row.ContentHash.ToString(),
                        row.Proxy.RelativePath,
                        row.Proxy.ContentHash.ToString(),
                        row.MediaType,
                        row.Width,
                        row.Height,
                        row.TakenAtLocal,
                        row.CameraMake,
                        row.CameraModel,
                        row.FaceCount,
                        selection.Stratum,
                        partition == PhotoQualityBenchmarkPartition.Calibration
                            ? "calibration"
                            : "held-out",
                        "representative",
                        null,
                        new PhotoQualityBenchmarkHumanReview(null, null, null));
                })
                .ToArray();

            string benchmarkId = BuildBenchmarkId(profile.Id, selected);
            int nearDuplicateGroupCount = nearDuplicateGroups.Values
                .Distinct(StringComparer.Ordinal)
                .Count();
            PhotoQualityBenchmarkExport export = new(
                SchemaVersion: 2,
                BenchmarkId: benchmarkId,
                GeneratedAtUtc: DateTimeOffset.UtcNow,
                ProxyProfile: profile.ToCanonicalText(),
                SelectionPolicy: PhotoQualityBenchmarkSelector.RepresentativeSelectionPolicyVersion,
                PartitionPolicy: PhotoQualityBenchmarkSelector.PartitionPolicyVersion,
                PopulationCount: population.Count,
                RepresentativeTarget: options.RepresentativeCount,
                RepresentativeCount: photos.Length,
                ChallengeReserve: options.ChallengeReserve,
                ChallengeCount: 0,
                CalibrationCount: photos.Count(photo => photo.Partition == "calibration"),
                HeldOutCount: photos.Count(photo => photo.Partition == "held-out"),
                LeakageControl: new PhotoQualityBenchmarkLeakageSummary(
                    PhotoVisualRedundancyPolicy.AlgorithmVersion,
                    LeakageHammingDistance,
                    nearDuplicateGroupCount,
                    nearDuplicateGroups.Count),
                PopulationCoverage: Coverage(descriptors),
                RepresentativeCoverage: Coverage(selected.Select(item => item.Photo)),
                ChallengeSelectionPolicies: [],
                ChallengeCoverage: null,
                ReviewInstructions:
                    "Review is blinded: do not expose model scores, candidate reasons, strata, partitions, source paths or representative/challenge membership in the review UI. Use Very Low / Low / Normal / High / Very High / Unsure plus personallyImportant and notes.",
                Photos: photos);

            Directory.CreateDirectory(Path.GetDirectoryName(outputPath)!);
            await File.WriteAllTextAsync(
                outputPath,
                JsonSerializer.Serialize(export, JsonOptions));

            Console.WriteLine(
                $"Selected {export.RepresentativeCount} representative photos from {export.PopulationCount} current photos with durable '{profile.Id}' proxies.");
            Console.WriteLine(
                $"Partitions: calibration={export.CalibrationCount}; held-out={export.HeldOutCount}.");
            Console.WriteLine(
                $"Leakage control: {export.LeakageControl.NearDuplicateGroupCount} strict dHash groups covering {export.LeakageControl.NearDuplicatePhotoCount} photos.");
            Console.WriteLine(
                $"Challenge reserve: {export.ChallengeReserve}; challenge photos are intentionally not selected in this checkpoint.");
            Console.WriteLine($"Private benchmark export: {outputPath}");
            Console.WriteLine(
                "Do not commit the export: it contains private source keys, proxy paths and future human review labels.");
            return 0;
        }
        catch (Exception exception)
        {
            Console.Error.WriteLine(exception.Message);
            Console.Error.WriteLine();
            PrintUsage();
            return 1;
        }
    }

    private static PhotoQualityBenchmarkDescriptor ToDescriptor(
        PhotoQualityBenchmarkPopulationRow row) =>
        new(
            row.AssetRevisionId,
            row.ContentHash.ToString(),
            row.SourceKey,
            row.MediaType,
            row.Width,
            row.Height,
            row.TakenAtLocal,
            row.CameraMake,
            row.CameraModel,
            row.FaceCount);

    private static PhotoQualityBenchmarkCoverage Coverage(
        IEnumerable<PhotoQualityBenchmarkDescriptor> source)
    {
        PhotoQualityBenchmarkDescriptor[] photos = source.ToArray();
        PhotoQualityBenchmarkStratum[] strata = photos
            .Select(PhotoQualityBenchmarkSelector.DescribeStratum)
            .ToArray();

        return new PhotoQualityBenchmarkCoverage(
            photos.Length,
            CountBy(strata.Select(item => item.CaptureEra)),
            CountBy(strata.Select(item => item.DeviceFamily)),
            CountBy(strata.Select(item => item.MediaFamily)),
            CountBy(strata.Select(item => item.Resolution)),
            CountBy(strata.Select(item => item.Orientation)),
            CountBy(strata.Select(item => item.Faces)),
            photos.Count(photo => photo.TakenAtLocal.HasValue),
            photos.Count(photo => photo.Width.HasValue && photo.Height.HasValue),
            photos.Count(photo => !string.IsNullOrWhiteSpace(photo.CameraModel)),
            photos
                .Where(photo => !string.IsNullOrWhiteSpace(photo.CameraModel))
                .Select(photo => $"{photo.CameraMake}|{photo.CameraModel}".ToLowerInvariant())
                .Distinct(StringComparer.Ordinal)
                .Count());
    }

    private static IReadOnlyDictionary<string, int> CountBy(IEnumerable<string> values) =>
        values
            .GroupBy(value => value, StringComparer.Ordinal)
            .OrderBy(group => group.Key, StringComparer.Ordinal)
            .ToDictionary(group => group.Key, group => group.Count(), StringComparer.Ordinal);

    private static IReadOnlyDictionary<AssetRevisionId, string> BuildNearDuplicateGroups(
        IReadOnlyDictionary<AssetRevisionId, PhotoPerceptualHash64> hashes,
        int maximumHammingDistance)
    {
        KeyValuePair<AssetRevisionId, PhotoPerceptualHash64>[] items = hashes
            .OrderBy(pair => pair.Key.ToString(), StringComparer.Ordinal)
            .ToArray();
        int[] parent = Enumerable.Range(0, items.Length).ToArray();

        for (int left = 0; left < items.Length; left++)
        {
            for (int right = left + 1; right < items.Length; right++)
            {
                if (items[left].Value.HammingDistance(items[right].Value) <= maximumHammingDistance)
                {
                    Union(parent, left, right);
                }
            }
        }

        Dictionary<int, List<AssetRevisionId>> components = [];
        for (int index = 0; index < items.Length; index++)
        {
            int root = Find(parent, index);
            if (!components.TryGetValue(root, out List<AssetRevisionId>? members))
            {
                members = [];
                components.Add(root, members);
            }

            members.Add(items[index].Key);
        }

        Dictionary<AssetRevisionId, string> groups = [];
        foreach (List<AssetRevisionId> component in components.Values.Where(value => value.Count > 1))
        {
            string groupId = "dhash4:" + component
                .Select(id => id.ToString())
                .OrderBy(value => value, StringComparer.Ordinal)
                .First();
            foreach (AssetRevisionId revisionId in component)
            {
                groups[revisionId] = groupId;
            }
        }

        return groups;
    }

    private static int Find(int[] parent, int value)
    {
        while (parent[value] != value)
        {
            parent[value] = parent[parent[value]];
            value = parent[value];
        }

        return value;
    }

    private static void Union(int[] parent, int left, int right)
    {
        int leftRoot = Find(parent, left);
        int rightRoot = Find(parent, right);
        if (leftRoot != rightRoot)
        {
            parent[rightRoot] = leftRoot;
        }
    }

    private static void VerifyProxy(PhotoQualityBenchmarkPopulationRow row, byte[] content)
    {
        if (content.LongLength != row.Proxy.EncodedByteLength)
        {
            throw new InvalidDataException(
                $"Stored proxy length does not match catalogue metadata for revision {row.AssetRevisionId}.");
        }

        string hash = Convert.ToHexString(SHA256.HashData(content)).ToLowerInvariant();
        if (!string.Equals(hash, row.Proxy.ContentHash.ToString(), StringComparison.Ordinal))
        {
            throw new InvalidDataException(
                $"Stored proxy hash does not match catalogue metadata for revision {row.AssetRevisionId}.");
        }
    }

    private static string ResolveProxyPath(string proxyRoot, string relativePath)
    {
        string path = Path.GetFullPath(Path.Combine(proxyRoot, relativePath));
        StringComparison comparison = OperatingSystem.IsWindows()
            ? StringComparison.OrdinalIgnoreCase
            : StringComparison.Ordinal;
        string rootPrefix = proxyRoot.EndsWith(Path.DirectorySeparatorChar)
            ? proxyRoot
            : proxyRoot + Path.DirectorySeparatorChar;
        if (!path.StartsWith(rootPrefix, comparison))
        {
            throw new InvalidDataException(
                "A stored review-proxy path escapes the configured derivative root.");
        }

        return path;
    }

    private static string BuildBenchmarkId(
        string profileId,
        IEnumerable<PhotoQualityBenchmarkSelection> selected)
    {
        string material = string.Join(
            "\n",
            new[]
            {
                PhotoQualityBenchmarkSelector.RepresentativeSelectionPolicyVersion,
                profileId,
            }.Concat(selected
                .Select(item => item.Photo.RevisionId.ToString())
                .OrderBy(value => value, StringComparer.Ordinal)));
        string digest = Convert.ToHexString(
            SHA256.HashData(Encoding.UTF8.GetBytes(material))).ToLowerInvariant();
        return $"m37-core-{digest[..16]}";
    }

    private static string ValidateOutputPath(string outputPath)
    {
        if (string.IsNullOrWhiteSpace(outputPath))
        {
            throw new ArgumentException(
                "An output path is required. Pass --output <path>.",
                nameof(outputPath));
        }

        if (!Path.IsPathRooted(outputPath))
        {
            string normalized = outputPath.Replace('\\', '/').TrimStart('.', '/');
            bool protectedByRepositoryIgnore =
                normalized.StartsWith("private/", StringComparison.OrdinalIgnoreCase) ||
                normalized.StartsWith("data/", StringComparison.OrdinalIgnoreCase) ||
                normalized.StartsWith("staging/", StringComparison.OrdinalIgnoreCase);
            if (!protectedByRepositoryIgnore)
            {
                throw new ArgumentException(
                    "Relative benchmark exports must be written under private/, data/, or staging/ because they contain private archive paths.",
                    nameof(outputPath));
            }
        }

        return Path.GetFullPath(outputPath);
    }

    private static string RequiredSetting(
        string? optionValue,
        string environmentVariable,
        string description)
    {
        string? value = string.IsNullOrWhiteSpace(optionValue)
            ? Environment.GetEnvironmentVariable(environmentVariable)
            : optionValue;
        if (string.IsNullOrWhiteSpace(value))
        {
            throw new InvalidOperationException(
                $"Provide the {description} with the matching command option or set {environmentVariable}.");
        }

        return value.Trim();
    }

    private static Options Parse(string[] args)
    {
        string? output = null;
        string? proxyRoot = null;
        string? profile = null;
        string? connectionString = null;
        int representativeCount = 400;
        int challengeReserve = 200;
        bool force = false;

        for (int index = 0; index < args.Length; index++)
        {
            string arg = args[index];
            switch (arg)
            {
                case "--output":
                    output = NextValue(args, ref index, arg);
                    break;
                case "--proxy-root":
                    proxyRoot = NextValue(args, ref index, arg);
                    break;
                case "--profile":
                    profile = NextValue(args, ref index, arg);
                    break;
                case "--connection-string":
                    connectionString = NextValue(args, ref index, arg);
                    break;
                case "--representative-count":
                    string representativeText = NextValue(args, ref index, arg);
                    if (!int.TryParse(representativeText, out representativeCount) ||
                        representativeCount is <= 0 or > 5000)
                    {
                        throw new ArgumentException(
                            "--representative-count must be an integer between 1 and 5000.");
                    }
                    break;
                case "--challenge-reserve":
                    string challengeText = NextValue(args, ref index, arg);
                    if (!int.TryParse(challengeText, out challengeReserve) ||
                        challengeReserve is < 0 or > 5000)
                    {
                        throw new ArgumentException(
                            "--challenge-reserve must be an integer between 0 and 5000.");
                    }
                    break;
                case "--force":
                    force = true;
                    break;
                default:
                    throw new ArgumentException($"Unknown benchmark argument: {arg}");
            }
        }

        return new Options(
            output ?? string.Empty,
            proxyRoot,
            profile,
            connectionString,
            representativeCount,
            challengeReserve,
            force);
    }

    private static string NextValue(string[] args, ref int index, string option)
    {
        if (++index >= args.Length || string.IsNullOrWhiteSpace(args[index]))
        {
            throw new ArgumentException($"{option} requires a value.");
        }

        return args[index];
    }

    private static void PrintUsage()
    {
        Console.WriteLine(
            "Usage: dotnet run --project tools/PhotoIdentity.PhotoQualityEvaluation -- benchmark --output private/photo-quality-benchmark.json [--representative-count 400] [--challenge-reserve 200] [--profile <id>] [--proxy-root <path>] [--connection-string <value>] [--force]");
        Console.WriteLine();
        Console.WriteLine(
            $"Defaults can come from {ConnectionStringEnvironmentVariable}, {ProxyRootEnvironmentVariable}, and {ProxyProfileEnvironmentVariable}.");
    }

    private sealed record Options(
        string OutputPath,
        string? ProxyRoot,
        string? ProfileId,
        string? ConnectionString,
        int RepresentativeCount,
        int ChallengeReserve,
        bool Force);
}

internal sealed record PhotoQualityBenchmarkExport(
    int SchemaVersion,
    string BenchmarkId,
    DateTimeOffset GeneratedAtUtc,
    string ProxyProfile,
    string SelectionPolicy,
    string PartitionPolicy,
    int PopulationCount,
    int RepresentativeTarget,
    int RepresentativeCount,
    int ChallengeReserve,
    int ChallengeCount,
    int CalibrationCount,
    int HeldOutCount,
    PhotoQualityBenchmarkLeakageSummary LeakageControl,
    PhotoQualityBenchmarkCoverage PopulationCoverage,
    PhotoQualityBenchmarkCoverage RepresentativeCoverage,
    IReadOnlyList<string>? ChallengeSelectionPolicies,
    PhotoQualityBenchmarkCoverage? ChallengeCoverage,
    string ReviewInstructions,
    IReadOnlyList<PhotoQualityBenchmarkPhoto> Photos);

internal sealed record PhotoQualityBenchmarkLeakageSummary(
    string PerceptualHashAlgorithm,
    int MaximumHammingDistance,
    int NearDuplicateGroupCount,
    int NearDuplicatePhotoCount);

internal sealed record PhotoQualityBenchmarkCoverage(
    int PhotoCount,
    IReadOnlyDictionary<string, int> CaptureEras,
    IReadOnlyDictionary<string, int> DeviceFamilies,
    IReadOnlyDictionary<string, int> MediaFamilies,
    IReadOnlyDictionary<string, int> ResolutionBuckets,
    IReadOnlyDictionary<string, int> Orientations,
    IReadOnlyDictionary<string, int> FaceBuckets,
    int PhotosWithCaptureTime,
    int PhotosWithDimensions,
    int PhotosWithCameraModel,
    int DistinctCameraModelCount);

internal sealed record PhotoQualityBenchmarkPhoto(
    string RevisionId,
    string SourceKey,
    string ContentSha256,
    string ProxyRelativePath,
    string ProxySha256,
    string? MediaType,
    int? Width,
    int? Height,
    DateTime? TakenAtLocal,
    string? CameraMake,
    string? CameraModel,
    int FaceCount,
    PhotoQualityBenchmarkStratum Stratum,
    string Partition,
    string? SampleKind,
    IReadOnlyList<string>? ChallengeReasons,
    PhotoQualityBenchmarkHumanReview Review);

internal sealed record PhotoQualityBenchmarkHumanReview(
    string? QualityLevel,
    bool? PersonallyImportant,
    string? Notes);
