namespace PhotoIdentity.PhotoQualityEvaluation;

internal static class PhotoQualityBenchmarkChallengeCommand
{
    public static async Task<int> RunAsync(string[] args)
    {
        try
        {
            if (args.Length == 0 || args.Any(arg => arg is "-h" or "--help"))
            {
                PrintUsage();
                return args.Length == 0 ? 1 : 0;
            }

            string input = string.Empty;
            string candidates = string.Empty;
            for (int index = 0; index < args.Length; index++)
            {
                switch (args[index])
                {
                    case "--input":
                        input = NextValue(args, ref index, "--input");
                        break;
                    case "--candidates":
                        candidates = NextValue(args, ref index, "--candidates");
                        break;
                    default:
                        throw new ArgumentException($"Unknown challenge argument: {args[index]}");
                }
            }

            PhotoQualityBenchmarkExport benchmark =
                await PhotoQualityBenchmarkFile.LoadAsync(input);
            PhotoQualityBenchmarkChallengeFile challengeFile =
                await LoadChallengeFileAsync(candidates);
            PhotoQualityBenchmarkExport merged =
                PhotoQualityBenchmarkChallengeMerger.Merge(benchmark, challengeFile);

            await PhotoQualityBenchmarkFile.SaveAtomicAsync(
                input,
                merged,
                backupSuffix: ".before-challenge.bak");

            Console.WriteLine(
                $"Added {merged.ChallengeCount - benchmark.ChallengeCount} challenge photos; benchmark now has {merged.Photos.Count} total photos.");
            Console.WriteLine(
                $"Representative core remains {merged.RepresentativeCount}; calibration={merged.CalibrationCount}; held-out={merged.HeldOutCount}.");
            Console.WriteLine(
                "Challenge photos are diagnostic only and do not alter the frozen representative calibration/held-out split.");
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

    private static async Task<PhotoQualityBenchmarkChallengeFile> LoadChallengeFileAsync(
        string path)
    {
        string fullPath = Path.GetFullPath(path);
        if (!File.Exists(fullPath))
        {
            throw new FileNotFoundException("Challenge candidate file was not found.", fullPath);
        }

        await using FileStream stream = File.OpenRead(fullPath);
        PhotoQualityBenchmarkChallengeFile file =
            await System.Text.Json.JsonSerializer.DeserializeAsync<PhotoQualityBenchmarkChallengeFile>(
                stream,
                PhotoQualityBenchmarkFile.JsonOptions)
            ?? throw new InvalidDataException("Challenge candidate JSON is empty or invalid.");
        if (file.SchemaVersion != 1)
        {
            throw new InvalidDataException(
                $"Unsupported challenge schemaVersion {file.SchemaVersion}; expected 1.");
        }

        if (string.IsNullOrWhiteSpace(file.SelectionPolicy))
        {
            throw new InvalidDataException("Challenge candidate file is missing selectionPolicy.");
        }

        return file;
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
            "Usage: dotnet run --project tools/PhotoIdentity.PhotoQualityEvaluation -- benchmark add-challenges --input private/photo-quality-benchmark.json --candidates private/photo-quality-challenges.json");
    }
}

internal static class PhotoQualityBenchmarkChallengeMerger
{
    public static PhotoQualityBenchmarkExport Merge(
        PhotoQualityBenchmarkExport benchmark,
        PhotoQualityBenchmarkChallengeFile challengeFile)
    {
        ArgumentNullException.ThrowIfNull(benchmark);
        ArgumentNullException.ThrowIfNull(challengeFile);

        if (!string.Equals(
                benchmark.BenchmarkId,
                challengeFile.BenchmarkId,
                StringComparison.Ordinal))
        {
            throw new InvalidDataException(
                $"Challenge benchmarkId '{challengeFile.BenchmarkId}' does not match '{benchmark.BenchmarkId}'.");
        }

        if (challengeFile.Photos is null || challengeFile.Photos.Count == 0)
        {
            throw new InvalidDataException("Challenge candidate file contains no photos.");
        }

        string[] duplicateCandidates = challengeFile.Photos
            .GroupBy(photo => photo.RevisionId, StringComparer.OrdinalIgnoreCase)
            .Where(group => group.Count() > 1)
            .Select(group => group.Key)
            .ToArray();
        if (duplicateCandidates.Length > 0)
        {
            throw new InvalidDataException(
                $"Challenge candidate file contains duplicate revision IDs: {string.Join(", ", duplicateCandidates.Take(5))}.");
        }

        HashSet<string> existing = benchmark.Photos
            .Select(photo => photo.RevisionId)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        string[] overlap = challengeFile.Photos
            .Where(photo => existing.Contains(photo.RevisionId))
            .Select(photo => photo.RevisionId)
            .ToArray();
        if (overlap.Length > 0)
        {
            throw new InvalidDataException(
                $"Challenge candidates overlap the frozen benchmark: {string.Join(", ", overlap.Take(5))}.");
        }

        int available = benchmark.ChallengeReserve - benchmark.ChallengeCount;
        if (challengeFile.Photos.Count > available)
        {
            throw new InvalidDataException(
                $"Challenge file contains {challengeFile.Photos.Count} photos but only {available} reserved challenge slots remain.");
        }

        PhotoQualityBenchmarkPhoto[] challengePhotos = challengeFile.Photos
            .Select(photo => photo with
            {
                Partition = "challenge",
                SampleKind = "challenge",
                ChallengeReasons = photo.ChallengeReasons ?? [],
                Review = new PhotoQualityBenchmarkHumanReview(null, null, null),
            })
            .ToArray();

        PhotoQualityBenchmarkPhoto[] all = benchmark.Photos
            .Concat(challengePhotos)
            .ToArray();
        PhotoQualityBenchmarkPhoto[] allChallenge = all
            .Where(photo =>
                string.Equals(photo.SampleKind, "challenge", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(photo.Partition, "challenge", StringComparison.OrdinalIgnoreCase))
            .ToArray();

        string[] policies = (benchmark.ChallengeSelectionPolicies ?? [])
            .Append(challengeFile.SelectionPolicy.Trim())
            .Distinct(StringComparer.Ordinal)
            .ToArray();

        return benchmark with
        {
            SchemaVersion = 2,
            ChallengeCount = allChallenge.Length,
            ChallengeSelectionPolicies = policies,
            ChallengeCoverage = PhotoQualityBenchmarkFile.Coverage(allChallenge),
            ReviewInstructions =
                "Review is blinded: do not expose model scores, candidate reasons, strata, partitions, source paths or representative/challenge membership in the review UI. Use Very Low / Low / Normal / High / Very High / Unsure plus personallyImportant and notes.",
            Photos = all,
        };
    }
}

internal sealed record PhotoQualityBenchmarkChallengeFile(
    int SchemaVersion,
    string BenchmarkId,
    string SelectionPolicy,
    IReadOnlyList<PhotoQualityBenchmarkPhoto> Photos);
