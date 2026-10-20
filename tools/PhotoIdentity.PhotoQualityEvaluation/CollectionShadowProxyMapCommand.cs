using System.Security.Cryptography;
using System.Text.Json;
using PhotoIdentity.Persistence.Postgres;

namespace PhotoIdentity.PhotoQualityEvaluation;

/// <summary>
/// WI-0195 read-only bridge from private blinded shadow scenarios to existing
/// current, durable proxy locators. No photo originals/catalogue writes.
/// </summary>
internal static class CollectionShadowProxyMapCommand
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web)
    {
        WriteIndented = true
    };
    private const string Protocol = "wi0195-collection-blind-comparison-v1";

    public static async Task<int> RunAsync(string[] args)
    {
        try
        {
            if (args.Length == 1 && args[0] == "--help")
            {
                Console.WriteLine("collection-shadow proxies --blind private/wi0195-shadow-blind.json " +
                                  "--output private/wi0195-shadow-proxies.json");
                return 0;
            }
            Dictionary<string, string> options = new(StringComparer.Ordinal);
            for (int i = 0; i < args.Length; i++)
            {
                if (i + 1 >= args.Length || !options.TryAdd(args[i], args[++i]))
                    throw new ArgumentException("Unknown/duplicate CLI argument.");
            }
            if (options.Count != 2 || !options.ContainsKey("--blind") ||
                !options.ContainsKey("--output"))
                throw new ArgumentException("Required shadow arguments missing.");

            string blindPath = RedundancyCandidateExportCommand.PrivatePath(options["--blind"]);
            string outputPath = RedundancyCandidateExportCommand.PrivatePath(options["--output"]);
            if (blindPath == outputPath || File.Exists(outputPath))
                throw new IOException("Shadow proxy output must be a new private file.");
            byte[] bytes = await File.ReadAllBytesAsync(blindPath);
            using JsonDocument document = JsonDocument.Parse(bytes);
            JsonElement root = document.RootElement;
            if (root.GetProperty("schemaVersion").GetInt32() != 1 ||
                root.GetProperty("protocol").GetString() != Protocol ||
                root.GetProperty("status").GetString() !=
                    "private-blind-shadow-comparison-only")
                throw new InvalidDataException("Expected a blinded shadow scenario.");

            JsonElement comparisons = root.GetProperty("comparisons");
            if (comparisons.ValueKind != JsonValueKind.Array ||
                comparisons.GetArrayLength() is < 1 or > 40)
                throw new InvalidDataException("Shadow comparison count out of bounds.");
            HashSet<string> ids = new(StringComparer.OrdinalIgnoreCase);
            HashSet<string> comparisonsSeen = new(StringComparer.Ordinal);
            foreach (JsonElement comparison in comparisons.EnumerateArray())
            {
                string id = comparison.GetProperty("comparisonId").GetString() ?? "";
                if (!comparisonsSeen.Add(id) || string.IsNullOrWhiteSpace(id))
                    throw new InvalidDataException("Duplicate/missing comparison.");
                foreach (string field in new[] { "leftRevisions", "rightRevisions" })
                {
                    JsonElement revisions = comparison.GetProperty(field);
                    if (revisions.ValueKind != JsonValueKind.Array ||
                        revisions.GetArrayLength() is < 1 or > 100)
                        throw new InvalidDataException("Unsupported shadow candidate count.");
                    foreach (JsonElement photo in revisions.EnumerateArray())
                    {
                        string revision = photo.GetString() ?? "";
                        if (!Guid.TryParse(revision, out _))
                            throw new InvalidDataException("Invalid shadow revision id.");
                        ids.Add(revision);
                    }
                }
            }
            if (ids.Count > 2000)
                throw new InvalidDataException("Too many local proxy identities.");

            string connection = Environment.GetEnvironmentVariable(
                "PhotoIdentity__Postgres__ConnectionString")
                ?? throw new InvalidOperationException("Postgres is not configured.");
            string proxyRoot = Path.GetFullPath(Environment.GetEnvironmentVariable(
                "PhotoIdentity__ReviewProxyRoot")
                ?? throw new InvalidOperationException("Review proxy root is missing."));
            string profile = Environment.GetEnvironmentVariable(
                "PhotoIdentity__ReviewProxyProfileId")
                ?? throw new InvalidOperationException("Review proxy profile is missing.");
            if (!Directory.Exists(proxyRoot))
                throw new DirectoryNotFoundException("Review proxy derivative root missing.");
            await using PostgresCatalogueDatabase db = new(connection);
            PostgresPhotoQualityEvaluationRepository repository = new(db);
            var population = await repository.ReadCurrentProxyPopulationAsync(profile);
            var selected = population
                .Where(row => ids.Contains(row.AssetRevisionId.ToString()))
                .ToDictionary(row => row.AssetRevisionId.ToString(),
                    StringComparer.OrdinalIgnoreCase);
            if (selected.Count != ids.Count)
                throw new InvalidDataException("Missing/stale proxy for captured shadow candidates.");

            var photos = ids.OrderBy(x => x, StringComparer.OrdinalIgnoreCase)
                .Select(id =>
                {
                    var proxy = selected[id].Proxy;
                    string absolute = Path.GetFullPath(Path.Combine(
                        proxyRoot, proxy.RelativePath));
                    string prefix = Path.TrimEndingDirectorySeparator(proxyRoot)
                        + Path.DirectorySeparatorChar;
                    if (Path.IsPathRooted(proxy.RelativePath) ||
                        !absolute.StartsWith(prefix, OperatingSystem.IsWindows()
                            ? StringComparison.OrdinalIgnoreCase
                            : StringComparison.Ordinal))
                        throw new InvalidDataException("Proxy relative path outside root.");
                    return new
                    {
                        revisionId = id,
                        relativePath = proxy.RelativePath,
                        sha256 = proxy.ContentHash.ToString().ToLowerInvariant(),
                        byteLength = proxy.EncodedByteLength
                    };
                }).ToArray();

            var result = new
            {
                schemaVersion = 1,
                protocol = "wi0195-private-shadow-proxies-v1",
                blindFileSha256 = Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant(),
                proxyRoot,
                proxyProfile = profile,
                photos,
                catalogReadOnly = true,
                noOriginalsHydrated = true
            };
            Directory.CreateDirectory(Path.GetDirectoryName(outputPath)!);
            await using (FileStream stream = new(outputPath, FileMode.CreateNew,
                FileAccess.Write, FileShare.None))
            {
                await JsonSerializer.SerializeAsync(stream, result, Json);
            }
            Console.WriteLine(JsonSerializer.Serialize(new
            {
                protocol = "wi0195-private-shadow-proxies-v1",
                status = "read-only-private-shadow-proxy-map",
                comparisonCount = comparisons.GetArrayLength(),
                currentProxiesMatched = selected.Count,
                noOriginalsHydrated = true,
                catalogueReadOnly = true
            }, Json));
            return 0;
        }
        catch (Exception e)
        {
            Console.Error.WriteLine("WI-0195 proxy mapping failed (" +
                e.GetType().Name + ").");
            return 1;
        }
    }
}
