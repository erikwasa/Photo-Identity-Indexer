using Npgsql;
using NpgsqlTypes;
using System.Diagnostics;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using PhotoIdentity.Core.Collections;
using PhotoIdentity.Core.Identifiers;
using PhotoIdentity.Imaging.OpenCv;
using PhotoIdentity.Persistence.Postgres;

namespace PhotoIdentity.PhotoQualityEvaluation;

/// <summary>
/// WI-0194 research only: read existing current-photo review proxies and optionally
/// existing exact-version CLIP vectors. Never generates new search evidence or
/// touches originals, catalogue writes, collections or held-out labels.
/// </summary>
internal static class RedundancyCandidateExportCommand
{
    private const string ConnectionEnvironment = "PhotoIdentity__Postgres__ConnectionString";
    private const string ProxyRootEnvironment = "PhotoIdentity__ReviewProxyRoot";
    private const string ProfileEnvironment = "PhotoIdentity__ReviewProxyProfileId";
    private const string CandidateProtocol = "wi0194-candidate-pairs-v1";
    private const int NearCopyLinkDistance = 4; // Leakage linkage only, not redundancy truth.
    private const int MaximumPairs = 2_000_000; // Downstream sampler input guard.
    private const int MaximumFullPopulationPhotos = 30_000;
    private const long MaximumFullProxyBytes = 16L * 1024 * 1024 * 1024;
    private const long MaximumFullComparisons = 450_000_000;
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = true,
    };

    private sealed record Options(
        string Output, string Report, int Limit, int Neighbors, int ClipSeeds,
        int BorderlinePerSeed, int? RandomNegativeControls,
        string? ModelId, string? ModelSha256, string? PreprocessingVersion,
        string? ProxyRoot, string? ProxyProfile, bool FullProxyPopulation);

    private sealed record Photo(string RevisionId, string ContentSha256,
        ulong PerceptualHash, float[]? ClipVector);

    private sealed record Pair(int Left, int Right, int? DhashDistance,
        double? ClipCosine, string SourceKind);

    internal sealed record Neighbor(int Index, int Distance);

    public static async Task<int> RunAsync(string[] args)
    {
        try
        {
            if (args.Any(x => x is "-h" or "--help"))
            {
                Usage();
                return 0;
            }

            Options options = Parse(args);
            string connectionString = Environment.GetEnvironmentVariable(ConnectionEnvironment)
                ?? throw new ArgumentException("PostgreSQL environment variable is missing.");
            string proxyRoot = options.ProxyRoot
                ?? Environment.GetEnvironmentVariable(ProxyRootEnvironment)
                ?? throw new ArgumentException("Review-proxy root is missing.");
            string profileId = options.ProxyProfile
                ?? Environment.GetEnvironmentVariable(ProfileEnvironment)
                ?? throw new ArgumentException("Review-proxy profile is missing.");
            if (!Directory.Exists(proxyRoot))
                throw new DirectoryNotFoundException("The review-proxy root does not exist.");

            string root = Path.GetFullPath(proxyRoot);
            string output = PrivatePath(options.Output);
            string report = PrivatePath(options.Report);
            if (output == report || File.Exists(output) || File.Exists(report))
                throw new IOException("Private output paths must be distinct, new files.");

            Stopwatch readWatch = Stopwatch.StartNew();
            await using PostgresCatalogueDatabase database = new(connectionString);
            PostgresPhotoQualityEvaluationRepository populationRepository = new(database);
            IReadOnlyList<PhotoQualityBenchmarkPopulationRow> population =
                await populationRepository.ReadCurrentProxyPopulationAsync(profileId);
            if (population.Count < 2)
                throw new InvalidOperationException("At least two current proxy-backed photos are required.");

            // Deterministic archive-spread sample; NOT a representative-stratified cohort
            // and NOT a claim about full-archive retrieval recall.
            IEnumerable<PhotoQualityBenchmarkPopulationRow> ordered = population
                .OrderBy(x => Digest(x.AssetRevisionId.ToString()), StringComparer.Ordinal);
            PhotoQualityBenchmarkPopulationRow[] selected = options.FullProxyPopulation
                ? ordered.ToArray()
                : ordered.Take(options.Limit).ToArray();
            if (options.FullProxyPopulation)
                ValidateFullPopulation(selected.Length, selected.Sum(x => (long)x.Proxy.EncodedByteLength));

            Dictionary<AssetRevisionId, float[]> vectors = [];
            if (options.ModelId is not null)
            {
                Guid[] selectedIds = selected.Select(x => x.AssetRevisionId.Value).ToArray();
                await using NpgsqlConnection connection = await database.OpenConnectionAsync();
                await using NpgsqlTransaction transaction = await connection.BeginTransactionAsync();
                await using (NpgsqlCommand readOnly = new("SET TRANSACTION READ ONLY", connection, transaction))
                    await readOnly.ExecuteNonQueryAsync();
                await using NpgsqlCommand command = new("""
                    SELECT asset_revision_id, vector_values
                    FROM photo_semantic_embeddings
                    WHERE asset_revision_id = ANY(@revision_ids)
                      AND model_id = @model_id
                      AND model_sha256 = @model_sha256
                      AND preprocessing_version = @preprocessing
                      AND vector_encoding = @encoding
                    ORDER BY asset_revision_id;
                    """, connection, transaction);
                command.Parameters.AddWithValue("revision_ids", NpgsqlDbType.Array | NpgsqlDbType.Uuid, selectedIds);
                command.Parameters.AddWithValue("model_id", NpgsqlDbType.Text, options.ModelId);
                command.Parameters.AddWithValue("model_sha256", NpgsqlDbType.Text, options.ModelSha256!);
                command.Parameters.AddWithValue("preprocessing", NpgsqlDbType.Text, options.PreprocessingVersion!);
                command.Parameters.AddWithValue("encoding", NpgsqlDbType.Text,
                    PhotoEmbeddingEvidence.Float32L2NormalizedEncoding);
                await using (NpgsqlDataReader reader = await command.ExecuteReaderAsync())
                {
                    while (await reader.ReadAsync())
                    {
                        AssetRevisionId id = AssetRevisionId.From(reader.GetGuid(0));
                        float[] values = reader.GetFieldValue<float[]>(1);
                        // Reject corrupt/stale evidence rather than silently ranking it.
                        PhotoEmbeddingEvidence checkedEvidence = new(
                            id, options.ModelId, options.ModelSha256!,
                            options.PreprocessingVersion!, values);
                        vectors.Add(id, checkedEvidence.Values.ToArray());
                    }
                }
                await transaction.CommitAsync();
            }
            readWatch.Stop();

            Stopwatch hashWatch = Stopwatch.StartNew();
            OpenCvPerceptualHashCalculator hashCalculator = new();
            List<Photo> photos = [];
            long proxyBytes = 0;
            foreach (PhotoQualityBenchmarkPopulationRow row in selected)
            {
                string relative = row.Proxy.RelativePath;
                string resolved = SafeProxyPath(root, relative);
                byte[] bytes = await File.ReadAllBytesAsync(resolved);
                if (bytes.LongLength != row.Proxy.EncodedByteLength ||
                    !string.Equals(
                        Convert.ToHexString(SHA256.HashData(bytes)),
                        row.Proxy.ContentHash.ToString(),
                        StringComparison.OrdinalIgnoreCase))
                    throw new InvalidDataException("A stored review-proxy failed its content verification.");

                proxyBytes += bytes.LongLength;
                photos.Add(new Photo(row.AssetRevisionId.ToString(),
                    row.ContentHash.ToString(), hashCalculator.Compute(bytes).Value,
                    vectors.GetValueOrDefault(row.AssetRevisionId)));
            }
            hashWatch.Stop();

            Stopwatch dhashWatch = Stopwatch.StartNew();
            Dictionary<(int, int), Pair> pairs = [];
            List<Neighbor>[] nearest = Enumerable.Range(0, photos.Count)
                .Select(_ => new List<Neighbor>()).ToArray();

            int strictDhashNearCopyLinks = 0;
            for (int left = 0; left < photos.Count; left++)
            {
                for (int right = left + 1; right < photos.Count; right++)
                {
                    int distance = System.Numerics.BitOperations.PopCount(
                        photos[left].PerceptualHash ^ photos[right].PerceptualHash);
                    Offer(nearest[left], new Neighbor(right, distance), options.Neighbors);
                    Offer(nearest[right], new Neighbor(left, distance), options.Neighbors);
                    if (distance <= NearCopyLinkDistance)
                    {
                        strictDhashNearCopyLinks++;
                        AddRetrieved(pairs, photos, left, right, distance);
                    }
                }
            }

            for (int left = 0; left < nearest.Length; left++)
                foreach (Neighbor hit in nearest[left])
                    AddRetrieved(pairs, photos, left, hit.Index, hit.Distance);
            dhashWatch.Stop();
            HashSet<(int, int)> dhashRetrieved = pairs.Keys.ToHashSet();
            HashSet<(int, int)> clipRetrieved = [];

            Stopwatch clipWatch = Stopwatch.StartNew();
            int[] seeds = Enumerable.Range(0, photos.Count)
                .Where(i => photos[i].ClipVector is not null)
                .OrderBy(i => Digest("clip-seed|" + photos[i].RevisionId), StringComparer.Ordinal)
                .Take(options.ClipSeeds)
                .ToArray();
            foreach (int seed in seeds)
            {
                float[] query = photos[seed].ClipVector!;
                List<(int Index, double Score)> best = [];
                for (int i = 0; i < photos.Count; i++)
                {
                    float[]? candidate = photos[i].ClipVector;
                    if (i == seed || candidate is null)
                        continue;
                    double score = DotNormalized(query, candidate);
                    best.Add((i, score));
                }

                foreach ((int index, _) in best
                    .OrderByDescending(x => x.Score).ThenBy(x => x.Index)
                    .Take(options.Neighbors))
                {
                    clipRetrieved.Add(SortPair(seed, index));
                    AddRetrieved(pairs, photos, seed, index, null);
                }
            }
            clipWatch.Stop();

            // Deterministic truly random *pair* controls that are not retrieved
            // by either method. High-score controls are excluded when known.
            Stopwatch negativesWatch = Stopwatch.StartNew();
            int randomControls = 0;
            Random random = new(194);
            int controlTarget = options.RandomNegativeControls ??
                Math.Min(200, Math.Max(20, photos.Count / 5));
            for (int attempt = 0; attempt < controlTarget * 100 &&
                randomControls < controlTarget; attempt++)
            {
                int a = random.Next(photos.Count);
                int b = random.Next(photos.Count);
                if (a == b) continue;
                (int left, int right) = SortPair(a, b);
                if (pairs.ContainsKey((left, right))) continue;
                int distance = System.Numerics.BitOperations.PopCount(
                    photos[left].PerceptualHash ^ photos[right].PerceptualHash);
                if (distance < 25) continue;
                if (photos[left].ClipVector is not null &&
                    photos[right].ClipVector is not null &&
                    DotNormalized(photos[left].ClipVector!, photos[right].ClipVector!) >= 0.70)
                    continue;
                pairs.Add((left, right), new Pair(left, right, null, null, "random-negative"));
                randomControls++;
            }
            negativesWatch.Stop();

            var manifest = new
            {
                schemaVersion = 1,
                protocol = CandidateProtocol,
                sampleId = "wi0194-" + Digest(string.Join("|",
                    photos.Select(x => x.RevisionId)) + "|" + options.ModelSha256)[..24],
                photos = photos.Select(x => new
                {
                    revisionId = x.RevisionId,
                    contentSha256 = x.ContentSha256
                }).ToArray(),
                pairs = pairs.OrderBy(x => x.Key.Item1).ThenBy(x => x.Key.Item2)
                    .Select(x => new
                    {
                        leftRevisionId = photos[x.Value.Left].RevisionId,
                        rightRevisionId = photos[x.Value.Right].RevisionId,
                        sourceKind = x.Value.SourceKind,
                        dhashDistance = x.Value.DhashDistance,
                        clipCosine = x.Value.ClipCosine
                    }).ToArray()
            };
            var aggregate = new
            {
                protocol = "wi0194-evidence-export-v1",
                status = "candidate-evidence-only-not-frozen",
                currentProxyBackedPhotos = population.Count,
                sampledPhotos = photos.Count,
                fullProxyArchiveCovered = photos.Count == population.Count,
                fullProxyPopulationRequested = options.FullProxyPopulation,
                strictDhashNearCopyLinks,
                dhashRetrievedPairs = dhashRetrieved.Count,
                clipRetrievedPairs = clipRetrieved.Count,
                clipOnlyRetrievedPairs = clipRetrieved.Count(p => !dhashRetrieved.Contains(p)),
                dhashClipOverlapPairs = clipRetrieved.Count(dhashRetrieved.Contains),
                clipModelId = options.ModelId,
                clipModelSha256 = options.ModelSha256,
                clipPreprocessingVersion = options.PreprocessingVersion,
                clipVectorEncoding = options.ModelId is null ? null :
                    PhotoEmbeddingEvidence.Float32L2NormalizedEncoding,
                clipIndexedPhotosInSample = photos.Count(x => x.ClipVector is not null),
                clipSeedsScanned = seeds.Length,
                exactDhashPairsCompared = (long)photos.Count * (photos.Count - 1) / 2,
                exactClipPairsCompared = seeds.Sum(seed =>
                    photos.Count(x => x.ClipVector is not null) - 1),
                retrievedPairs = pairs.Values.Count(x => x.SourceKind == "retrieved"),
                randomNegativeControls = randomControls,
                verifiedProxyBytesRead = proxyBytes,
                approximateRawClipVectorBytesInSample = photos.Sum(x =>
                    (long)(x.ClipVector?.Length ?? 0) * sizeof(float)),
                timingsMilliseconds = new
                {
                    databaseRead = readWatch.Elapsed.TotalMilliseconds,
                    verifiedProxyHashing = hashWatch.Elapsed.TotalMilliseconds,
                    dhashExactScan = dhashWatch.Elapsed.TotalMilliseconds,
                    clipSeedExactScan = clipWatch.Elapsed.TotalMilliseconds,
                    negativeControls = negativesWatch.Elapsed.TotalMilliseconds
                },
                noOriginalsHydrated = true,
                noCatalogueWrites = true,
                humanReviewLabelsAccessed = false,
                thresholdsCalibrated = false,
                suppressionAuthorized = false,
                limitations = "Subset exact search compares only the selected proxy-backed corpus. " +
                    "CLIP is reused semantic-search evidence, not a validated redundancy model. " +
                    "dHash<=4 links are not exhaustive outside this subset. " +
                    "Random negative controls are filtered by measured similarity. " +
                    "Do not freeze or review held-out pair labels from this export."
            };

            // Compute and validate all evidence before any exclusive private write.
            Directory.CreateDirectory(Path.GetDirectoryName(output)!);
            Directory.CreateDirectory(Path.GetDirectoryName(report)!);
            await using (FileStream stream = new(output, FileMode.CreateNew, FileAccess.Write))
                await JsonSerializer.SerializeAsync(stream, manifest, JsonOptions);
            await using (FileStream stream = new(report, FileMode.CreateNew, FileAccess.Write))
                await JsonSerializer.SerializeAsync(stream, aggregate, JsonOptions);
            Console.WriteLine(JsonSerializer.Serialize(new
            {
                aggregate.status,
                aggregate.currentProxyBackedPhotos,
                aggregate.sampledPhotos,
                aggregate.fullProxyArchiveCovered,
                aggregate.fullProxyPopulationRequested,
                aggregate.strictDhashNearCopyLinks,
                aggregate.dhashRetrievedPairs,
                aggregate.clipRetrievedPairs,
                aggregate.clipOnlyRetrievedPairs,
                aggregate.dhashClipOverlapPairs,
                aggregate.clipIndexedPhotosInSample,
                aggregate.clipSeedsScanned,
                aggregate.retrievedPairs,
                aggregate.randomNegativeControls,
                aggregate.exactDhashPairsCompared,
                aggregate.exactClipPairsCompared,
                aggregate.timingsMilliseconds
            }, JsonOptions));
            return 0;
        }
        catch (Exception exception)
        {
            // Do not log exception paths, revision IDs, credentials or file names.
            Console.Error.WriteLine("WI-0194 private export failed (" +
                exception.GetType().Name + "). No catalogue data was changed.");
            return 1;
        }
    }

    private static void AddRetrieved(
        Dictionary<(int, int), Pair> pairs, IReadOnlyList<Photo> photos,
        int first, int second, int? knownDistance)
    {
        (int left, int right) = SortPair(first, second);
        if (pairs.ContainsKey((left, right))) return;
        if (pairs.Count >= MaximumPairs)
            throw new InvalidOperationException("Candidate-pair bound exceeded.");
        int distance = knownDistance ?? System.Numerics.BitOperations.PopCount(
            photos[left].PerceptualHash ^ photos[right].PerceptualHash);
        double? clip = photos[left].ClipVector is not null &&
            photos[right].ClipVector is not null
                ? DotNormalized(photos[left].ClipVector!, photos[right].ClipVector!) : null;
        pairs.Add((left, right), new Pair(left, right, distance, clip, "retrieved"));
    }

    internal static double DotNormalized(float[] a, float[] b)
    {
        if (a.Length != b.Length || a.Length == 0)
            throw new InvalidDataException("Incompatible embedding dimensions.");
        double score = 0;
        for (int i = 0; i < a.Length; i++)
            score += (double)a[i] * b[i];
        return Math.Clamp(score, -1d, 1d);
    }

    internal static void Offer(List<Neighbor> best, Neighbor candidate, int limit)
    {
        // Most archive-wide comparisons cannot enter an already full top-k.
        // The index tie-break matches the original stable ranking policy.
        if (best.Count == limit && (candidate.Distance > best[^1].Distance ||
            (candidate.Distance == best[^1].Distance && candidate.Index >= best[^1].Index)))
            return;
        best.Add(candidate);
        best.Sort((a, b) =>
        {
            int comparison = a.Distance.CompareTo(b.Distance);
            return comparison == 0 ? a.Index.CompareTo(b.Index) : comparison;
        });
        if (best.Count > limit) best.RemoveAt(best.Count - 1);
    }

    internal static void ValidateFullPopulation(int photos, long expectedProxyBytes)
    {
        long comparisons = (long)photos * (photos - 1) / 2;
        if (photos < 2 || photos > MaximumFullPopulationPhotos ||
            comparisons > MaximumFullComparisons ||
            expectedProxyBytes < 0 || expectedProxyBytes > MaximumFullProxyBytes)
            throw new InvalidOperationException(
                "Full proxy-population survey exceeds bounded photo, comparison or proxy-byte limits.");
    }

    private static (int, int) SortPair(int a, int b) => a < b ? (a, b) : (b, a);

    private static string Digest(string text) => Convert.ToHexString(
        SHA256.HashData(Encoding.UTF8.GetBytes(text))).ToLowerInvariant();

    private static string SafeProxyPath(string root, string relative)
    {
        string path = Path.GetFullPath(Path.Combine(root, relative));
        string prefix = Path.TrimEndingDirectorySeparator(root) + Path.DirectorySeparatorChar;
        if (Path.IsPathRooted(relative) || !path.StartsWith(prefix,
            OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase :
                StringComparison.Ordinal))
            throw new InvalidDataException("A review-proxy path escaped its derivative root.");
        return path;
    }

    internal static string PrivatePath(string raw)
    {
        if (Path.IsPathRooted(raw) || string.IsNullOrWhiteSpace(raw))
            throw new ArgumentException("Exports must be relative paths inside private/.");
        string normalized = raw.Replace('\\', '/');
        if (!normalized.StartsWith("private/", StringComparison.OrdinalIgnoreCase))
            throw new ArgumentException("Exports must be inside the private/ directory.");
        string full = Path.GetFullPath(raw);
        string allowed = Path.GetFullPath("private") + Path.DirectorySeparatorChar;
        if (!full.StartsWith(allowed, OperatingSystem.IsWindows() ?
                StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal))
            throw new ArgumentException("Export path escapes the private directory.");
        return full;
    }

    private static Options Parse(string[] args)
    {
        Dictionary<string, string> arguments = new(StringComparer.Ordinal);
        bool fullProxyPopulation = false;
        for (int i = 0; i < args.Length; i++)
        {
            if (args[i] == "--full-proxy-population")
            {
                if (fullProxyPopulation)
                    throw new ArgumentException("Duplicate full-population option.");
                fullProxyPopulation = true;
                continue;
            }
            if (!args[i].StartsWith("--", StringComparison.Ordinal) ||
                i + 1 >= args.Length || !arguments.TryAdd(args[i], args[++i]))
                throw new ArgumentException("Invalid or duplicate export option.");
        }
        if (fullProxyPopulation && arguments.ContainsKey("--max-photos"))
            throw new ArgumentException("Full-population mode cannot combine with --max-photos.");
        string Arg(string key) => arguments.TryGetValue(key, out string? x) ? x :
            throw new ArgumentException("Required export option missing: " + key);
        int Bound(string key, int fallback, int max) =>
            arguments.TryGetValue(key, out string? raw) ?
                int.TryParse(raw, out int n) && n >= 1 && n <= max ? n :
                throw new ArgumentException("Export option is outside its safe bounds: " + key)
                : fallback;
        int NonnegativeBound(string key, int fallback, int max) =>
            arguments.TryGetValue(key, out string? raw) ?
                int.TryParse(raw, out int n) && n >= 0 && n <= max ? n :
                throw new ArgumentException("Export option is outside its safe bounds: " + key)
                : fallback;
        HashSet<string> allowed = new(StringComparer.Ordinal)
        {
            "--output", "--report", "--max-photos", "--neighbors", "--clip-seeds",
            "--clip-borderline-per-seed", "--random-negative-controls",
            "--clip-model-id", "--clip-model-sha256", "--clip-preprocessing",
            "--proxy-root", "--proxy-profile"
        };
        if (arguments.Keys.Any(key => !allowed.Contains(key)))
            throw new ArgumentException("Unknown export option.");
        bool clip = arguments.ContainsKey("--clip-model-id") ||
            arguments.ContainsKey("--clip-model-sha256") ||
            arguments.ContainsKey("--clip-preprocessing");
        if (clip && (!arguments.ContainsKey("--clip-model-id") ||
            !arguments.ContainsKey("--clip-model-sha256") ||
            !arguments.ContainsKey("--clip-preprocessing")))
            throw new ArgumentException("All CLIP provenance arguments must be supplied together.");
        if (!clip && NonnegativeBound("--clip-borderline-per-seed", 0, 16) > 0)
            throw new ArgumentException("Borderline sampling needs a model fingerprint.");
        string? sha = arguments.GetValueOrDefault("--clip-model-sha256");
        if (sha is not null && (sha.Length != 64 || sha.Any(x => !Uri.IsHexDigit(x))))
            throw new ArgumentException("CLIP model digest must be 64 hexadecimal characters.");

        return new Options(Arg("--output"), Arg("--report"),
            Bound("--max-photos", 500, 5000),
            Bound("--neighbors", 8, 20),
            Bound("--clip-seeds", 100, 500),
            NonnegativeBound("--clip-borderline-per-seed", 0, 16),
            arguments.ContainsKey("--random-negative-controls")
                ? Bound("--random-negative-controls", 200, 1000) : null,
            arguments.GetValueOrDefault("--clip-model-id"), sha,
            arguments.GetValueOrDefault("--clip-preprocessing"),
            arguments.GetValueOrDefault("--proxy-root"),
            arguments.GetValueOrDefault("--proxy-profile"), fullProxyPopulation);
    }

    private static void Usage()
    {
        Console.WriteLine("WI-0194 private evidence export, read-only.");
        Console.WriteLine("redundancy export --output private/wi0194-neighbour-candidates.json " +
            "--report private/wi0194-neighbour-metrics.json " +
            "[--max-photos 500 | --full-proxy-population] [--neighbors 8] [--clip-seeds 100] " +
            "[--clip-borderline-per-seed 0..16] [--random-negative-controls 1..1000] " +
            "[--clip-model-id ID --clip-model-sha256 SHA --clip-preprocessing VERSION] " +
            "[--proxy-root PATH --proxy-profile ID]");
    }
}
