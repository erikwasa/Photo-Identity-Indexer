using System.Security.Cryptography;
using System.Text.Json;
using PhotoIdentity.Persistence.Postgres;

namespace PhotoIdentity.PhotoQualityEvaluation;

/// <summary>
/// WI-0195: read-only proxy resolution for a bounded, existing private
/// collection scenario shortlist. No originals, inference, hydration, or DB writes.
/// </summary>
internal static class CollectionShadowScenarioProxyMapCommand
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web)
    {
        WriteIndented = true
    };
    private const int MaxPhotos = 240;
    private const string Protocol = "wi0195-collection-scenarios-v1";

    public static async Task<int> RunAsync(string[] args)
    {
        try
        {
            if (args.Length == 1 && args[0] == "--help")
            {
                Console.WriteLine("collection-shadow candidate-proxies --scenarios private/wi0195-scenarios.json " +
                                  "--output private/wi0195-candidate-proxies.json");
                return 0;
            }
            Dictionary<string, string> options = new(StringComparer.Ordinal);
            for (int i = 0; i < args.Length; i++)
            {
                if (i + 1 >= args.Length || !options.TryAdd(args[i], args[++i]))
                    throw new ArgumentException("Unexpected or repeated option.");
            }
            if (options.Count != 2 || !options.ContainsKey("--scenarios") ||
                !options.ContainsKey("--output"))
                throw new ArgumentException("Provide both --scenarios and --output.");

            string source = RedundancyCandidateExportCommand.PrivatePath(options["--scenarios"]);
            string output = RedundancyCandidateExportCommand.PrivatePath(options["--output"]);
            if (source == output || File.Exists(output))
                throw new IOException("Private output must be new and distinct.");
            byte[] original = await File.ReadAllBytesAsync(source);
            using JsonDocument doc = JsonDocument.Parse(original);
            JsonElement root = doc.RootElement;
            if (root.GetProperty("schemaVersion").GetInt32() != 1 ||
                root.GetProperty("protocol").GetString() != Protocol)
                throw new InvalidDataException("Unsupported scenario source.");

            JsonElement scenarios = root.GetProperty("scenarios");
            if (scenarios.ValueKind != JsonValueKind.Array ||
                scenarios.GetArrayLength() is < 1 or > 12)
                throw new InvalidDataException("Scenario count must be between 1 and 12.");
            HashSet<string> revisions = new(StringComparer.OrdinalIgnoreCase);
            HashSet<string> scenarioIds = new(StringComparer.Ordinal);
            foreach (JsonElement scenario in scenarios.EnumerateArray())
            {
                string kind = scenario.GetProperty("kind").GetString() ?? "";
                string id = scenario.GetProperty("scenarioId").GetString() ?? "";
                if (kind is not ("smart" or "creative" or "search") ||
                    string.IsNullOrWhiteSpace(id) || !scenarioIds.Add(id))
                    throw new InvalidDataException("Invalid/duplicate scenario.");
                JsonElement candidates = scenario.GetProperty("candidates");
                int target = scenario.GetProperty("targetCount").GetInt32();
                if (candidates.ValueKind != JsonValueKind.Array ||
                    candidates.GetArrayLength() is < 2 or > 2000 ||
                    target < 1 || target >= candidates.GetArrayLength())
                    throw new InvalidDataException("Invalid candidate/target bound.");
                HashSet<string> seenInScenario = new(StringComparer.OrdinalIgnoreCase);
                int expectedRank = 0;
                foreach (JsonElement row in candidates.EnumerateArray())
                {
                    string revision = row.GetProperty("revisionId").GetString() ?? "";
                    int rank = row.GetProperty("rank").GetInt32();
                    string sourceKind = row.GetProperty("kind").GetString() ?? "";
                    _ = row.GetProperty("relevanceScore").GetInt32();
                    if (rank != expectedRank++ || !Guid.TryParse(revision, out _) ||
                        !seenInScenario.Add(revision) ||
                        sourceKind is not ("direct-anchor" or "contextual-addition" or "search"))
                        throw new InvalidDataException("Invalid scenario candidate.");
                    revisions.Add(revision);
                }
            }
            if (revisions.Count > MaxPhotos)
                throw new InvalidDataException("Maximum 240 DISTINCT research photos per batch.");

            string connection = Environment.GetEnvironmentVariable(
                "PhotoIdentity__Postgres__ConnectionString")
                ?? throw new InvalidOperationException("PostgreSQL not configured.");
            string proxyRoot = Path.GetFullPath(Environment.GetEnvironmentVariable(
                "PhotoIdentity__ReviewProxyRoot")
                ?? throw new InvalidOperationException("Review proxy root not configured."));
            string profile = Environment.GetEnvironmentVariable(
                "PhotoIdentity__ReviewProxyProfileId")
                ?? throw new InvalidOperationException("Review proxy profile not configured.");
            if (profile != "jpeg-1600-q78" || !Directory.Exists(proxyRoot))
                throw new InvalidOperationException("Only existing jpeg-1600-q78 proxies are supported.");

            await using PostgresCatalogueDatabase db = new(connection);
            PostgresPhotoQualityEvaluationRepository repository = new(db);
            var population = await repository.ReadCurrentProxyPopulationAsync(profile);
            var selected = population
                .Where(row => revisions.Contains(row.AssetRevisionId.ToString()))
                .ToDictionary(row => row.AssetRevisionId.ToString(),
                    StringComparer.OrdinalIgnoreCase);
            if (selected.Count != revisions.Count)
                throw new InvalidDataException("Some selected current proxies are missing/stale.");

            var photos = revisions.OrderBy(x => x, StringComparer.OrdinalIgnoreCase)
                .Select(revision =>
                {
                    var proxy = selected[revision].Proxy;
                    string absolute = Path.GetFullPath(Path.Combine(proxyRoot, proxy.RelativePath));
                    string prefix = Path.TrimEndingDirectorySeparator(proxyRoot) +
                                    Path.DirectorySeparatorChar;
                    if (Path.IsPathRooted(proxy.RelativePath) ||
                        !absolute.StartsWith(prefix, OperatingSystem.IsWindows()
                            ? StringComparison.OrdinalIgnoreCase
                            : StringComparison.Ordinal))
                        throw new InvalidDataException("Proxy locator outside durable root.");
                    return new
                    {
                        revisionId = revision,
                        relativePath = proxy.RelativePath,
                        sha256 = proxy.ContentHash.ToString().ToLowerInvariant(),
                        byteLength = proxy.EncodedByteLength
                    };
                }).ToArray();
            var result = new
            {
                schemaVersion = 1,
                protocol = "wi0195-private-candidate-proxies-v1",
                scenarioFileSha256 = Convert.ToHexString(SHA256.HashData(original)).ToLowerInvariant(),
                proxyRoot,
                proxyProfile = profile,
                photos,
                catalogReadOnly = true,
                noOriginalsHydrated = true,
                inferencePerformed = false
            };
            Directory.CreateDirectory(Path.GetDirectoryName(output)!);
            await using (FileStream stream = new(output, FileMode.CreateNew,
                FileAccess.Write, FileShare.None))
            {
                await JsonSerializer.SerializeAsync(stream, result, Json);
            }
            Console.WriteLine(JsonSerializer.Serialize(new
            {
                protocol = "wi0195-private-candidate-proxies-v1",
                status = "read-only-existing-review-proxy-map",
                scenarioCount = scenarios.GetArrayLength(),
                distinctCurrentProxiesMatched = selected.Count,
                maximumBatchPhotos = MaxPhotos,
                catalogueReadOnly = true,
                noOriginalsHydrated = true,
                inferencePerformed = false
            }, Json));
            return 0;
        }
        catch (Exception error)
        {
            // Never echo sensitive source paths, catalogue credentials or revision IDs.
            Console.Error.WriteLine("WI-0195 candidate proxy map rejected (" +
                                    error.GetType().Name + ").");
            return 1;
        }
    }
}
