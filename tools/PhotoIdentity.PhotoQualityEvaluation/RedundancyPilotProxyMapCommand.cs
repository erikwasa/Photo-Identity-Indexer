using System.Security.Cryptography;
using System.Text.Json;
using PhotoIdentity.Persistence.Postgres;

namespace PhotoIdentity.PhotoQualityEvaluation;

/// <summary>
/// WI-0194: map a private, calibration-only blind pilot to current durable
/// local review proxies. Never opens originals, generates proxies, or writes
/// catalogue data. Proxy hashes are checked by the loopback reviewer.
/// </summary>
internal static class RedundancyPilotProxyMapCommand
{
    private const string ConnectionEnv = "PhotoIdentity__Postgres__ConnectionString";
    private const string ProxyRootEnv = "PhotoIdentity__ReviewProxyRoot";
    private const string ProfileEnv = "PhotoIdentity__ReviewProxyProfileId";
    private const string PilotProtocol = "wi0194-calibration-blind-pilot-v1";
    private static readonly JsonSerializerOptions Options = new(JsonSerializerDefaults.Web)
    {
        WriteIndented = true
    };

    private sealed record BlindPair(string PairId, string LeftRevisionId, string RightRevisionId);
    private sealed record BlindPilot(int SchemaVersion, string Protocol,
        string Purpose, string[] Labels, BlindPair[] Pairs);

    public static async Task<int> RunAsync(string[] args)
    {
        try
        {
            if (args.Length == 1 && args[0] == "--help")
            {
                Console.WriteLine(
                    "redundancy pilot-proxies --input private/wi0194-pilot-blind.json " +
                    "--output private/wi0194-pilot-proxies.json " +
                    "[--validation-audit private/wi0194-heldout-audit.json]");
                return 0;
            }
            Dictionary<string, string> values = new(StringComparer.Ordinal);
            for (int i = 0; i < args.Length; i++)
            {
                if (i + 1 >= args.Length || !values.TryAdd(args[i], args[++i]))
                    throw new ArgumentException("Unknown or duplicate proxy-map argument.");
            }
            if (values.Count != (values.ContainsKey("--validation-audit") ? 3 : 2) ||
                !values.ContainsKey("--input") || !values.ContainsKey("--output"))
                throw new ArgumentException("Required proxy-map arguments are missing.");

            string input = RedundancyCandidateExportCommand.PrivatePath(values["--input"]);
            string output = RedundancyCandidateExportCommand.PrivatePath(values["--output"]);
            if (input == output || File.Exists(output))
                throw new IOException("Proxy-map output must be a new, distinct private file.");

            byte[] pilotBytes = await File.ReadAllBytesAsync(input);
            BlindPilot pilot = JsonSerializer.Deserialize<BlindPilot>(pilotBytes, Options)
                ?? throw new InvalidDataException("Invalid blind pilot.");
            bool isHeldOut = pilot.Protocol == "wi0194-heldout-blind-validation-v1";
            if (pilot.SchemaVersion != 1 ||
                !(pilot.Protocol == PilotProtocol || isHeldOut) ||
                pilot.Purpose != (isHeldOut
                    ? "heldout-independent-pair-validation-frozen-before-labels"
                    : "calibration-only-pilot-unfrozen-heldout-excluded") ||
                pilot.Pairs is null || pilot.Pairs.Length is < 1 or > 200 ||
                pilot.Labels is null || !pilot.Labels.SequenceEqual(new[]
                {
                    "redundant", "similar-keep-both", "not-similar", "unsure"
                }))
                throw new InvalidDataException("Input must be an approved blind review protocol.");

            // Require the SEPARATE frozen audit before resolving any held-out photos.
            if (isHeldOut != values.ContainsKey("--validation-audit"))
                throw new InvalidDataException("Held-out proxy mapping requires its frozen audit.");
            if (isHeldOut)
            {
                string auditPath = RedundancyCandidateExportCommand.PrivatePath(
                    values["--validation-audit"]);
                if (auditPath == input || auditPath == output)
                    throw new InvalidDataException("Private held-out paths must be distinct.");
                using JsonDocument audit = JsonDocument.Parse(
                    await File.ReadAllBytesAsync(auditPath));
                JsonElement root = audit.RootElement;
                string sha = Convert.ToHexString(SHA256.HashData(pilotBytes))
                    .ToLowerInvariant();
                if (root.GetProperty("protocol").GetString() !=
                        "wi0194-heldout-validation-audit-v1" ||
                    root.GetProperty("status").GetString() !=
                        "heldout-validation-frozen-before-human-labels" ||
                    root.GetProperty("pilotSha256").GetString() != sha ||
                    root.GetProperty("suppressionThresholdFrozen").GetBoolean() ||
                    root.GetProperty("productionSuppressionApproved").GetBoolean())
                    throw new InvalidDataException("Held-out audit failed pilot binding.");
            }
            HashSet<string> pairIds = new(StringComparer.Ordinal);
            HashSet<string> photoIds = new(StringComparer.OrdinalIgnoreCase);
            foreach (BlindPair pair in pilot.Pairs)
            {
                if (string.IsNullOrWhiteSpace(pair.PairId) ||
                    !pairIds.Add(pair.PairId) ||
                    !Guid.TryParse(pair.LeftRevisionId, out _) ||
                    !Guid.TryParse(pair.RightRevisionId, out _) ||
                    pair.LeftRevisionId.Equals(pair.RightRevisionId,
                        StringComparison.OrdinalIgnoreCase))
                    throw new InvalidDataException("Malformed or duplicate pilot pair.");
                photoIds.Add(pair.LeftRevisionId);
                photoIds.Add(pair.RightRevisionId);
            }

            string connection = Environment.GetEnvironmentVariable(ConnectionEnv)
                ?? throw new InvalidOperationException("PostgreSQL connection is missing.");
            string proxyRoot = Path.GetFullPath(
                Environment.GetEnvironmentVariable(ProxyRootEnv)
                ?? throw new InvalidOperationException("Review-proxy root is missing."));
            string profile = Environment.GetEnvironmentVariable(ProfileEnv)
                ?? throw new InvalidOperationException("Review-proxy profile is missing.");
            if (!Directory.Exists(proxyRoot))
                throw new DirectoryNotFoundException("Review-proxy root is unavailable.");
            await using PostgresCatalogueDatabase db = new(connection);
            PostgresPhotoQualityEvaluationRepository repository = new(db);
            var population = await repository.ReadCurrentProxyPopulationAsync(profile);
            var selected = population
                .Where(row => photoIds.Contains(row.AssetRevisionId.ToString()))
                .ToDictionary(row => row.AssetRevisionId.ToString(),
                    StringComparer.OrdinalIgnoreCase);
            if (selected.Count != photoIds.Count)
                throw new InvalidDataException("The pilot includes missing/stale current proxies.");

            var photos = photoIds.OrderBy(x => x, StringComparer.OrdinalIgnoreCase)
                .Select(revisionId =>
                {
                    var proxy = selected[revisionId].Proxy;
                    // Check containment *before* persisting any relative locator.
                    string full = Path.GetFullPath(Path.Combine(proxyRoot, proxy.RelativePath));
                    string prefix = Path.TrimEndingDirectorySeparator(proxyRoot)
                        + Path.DirectorySeparatorChar;
                    if (Path.IsPathRooted(proxy.RelativePath) ||
                        !full.StartsWith(prefix,
                            OperatingSystem.IsWindows()
                                ? StringComparison.OrdinalIgnoreCase
                                : StringComparison.Ordinal))
                        throw new InvalidDataException("Proxy locator escapes its root.");
                    return new
                    {
                        revisionId,
                        relativePath = proxy.RelativePath,
                        sha256 = proxy.ContentHash.ToString().ToLowerInvariant(),
                        byteLength = proxy.EncodedByteLength
                    };
                }).ToArray();

            var manifest = new
            {
                schemaVersion = 1,
                protocol = "wi0194-private-pilot-proxies-v1",
                pilotSha256 = Convert.ToHexString(SHA256.HashData(pilotBytes))
                    .ToLowerInvariant(),
                proxyRoot,
                proxyProfile = profile,
                photos,
                catalogReadOnly = true,
                noOriginalsHydrated = true,
            };
            Directory.CreateDirectory(Path.GetDirectoryName(output)!);
            await using (FileStream stream = new(output, FileMode.CreateNew,
                             FileAccess.Write, FileShare.None))
            {
                await JsonSerializer.SerializeAsync(stream, manifest, Options);
            }
            Console.WriteLine(JsonSerializer.Serialize(new
            {
                status = isHeldOut
                    ? "local-heldout-validation-proxy-map-only"
                    : "local-pilot-proxy-map-only",
                currentProxiesMatched = selected.Count,
                blindPilotPairs = pilot.Pairs.Length,
                noOriginalsHydrated = true,
                catalogueReadOnly = true
            }, Options));
            return 0;
        }
        catch (Exception exception)
        {
            // Never log private paths, source IDs or connection strings.
            Console.Error.WriteLine("WI-0194 proxy mapping failed (" +
                                    exception.GetType().Name + ").");
            return 1;
        }
    }
}
