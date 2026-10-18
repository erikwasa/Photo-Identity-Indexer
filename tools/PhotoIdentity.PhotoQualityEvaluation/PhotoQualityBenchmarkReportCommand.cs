namespace PhotoIdentity.PhotoQualityEvaluation;

internal static class PhotoQualityBenchmarkReportCommand
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
            for (int index = 0; index < args.Length; index++)
            {
                if (args[index] == "--input")
                {
                    input = NextValue(args, ref index, "--input");
                }
                else
                {
                    throw new ArgumentException(
                        $"Unknown report argument: {args[index]}");
                }
            }

            PhotoQualityBenchmarkExport benchmark =
                await PhotoQualityBenchmarkFile.LoadAsync(input);
            Print(benchmark);
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

    internal static void Print(PhotoQualityBenchmarkExport benchmark)
    {
        PhotoQualityBenchmarkPhoto[] representative = benchmark.Photos
            .Where(photo =>
                !string.Equals(
                    photo.SampleKind,
                    "challenge",
                    StringComparison.OrdinalIgnoreCase) &&
                !string.Equals(
                    photo.Partition,
                    "challenge",
                    StringComparison.OrdinalIgnoreCase))
            .ToArray();
        PhotoQualityBenchmarkPhoto[] challenge = benchmark.Photos
            .Except(representative)
            .ToArray();

        Console.WriteLine($"Benchmark: {benchmark.BenchmarkId}");
        Console.WriteLine(
            $"Population with durable proxy: {benchmark.PopulationCount}");
        Console.WriteLine(
            $"Representative: {representative.Length} (calibration={benchmark.CalibrationCount}; held-out={benchmark.HeldOutCount})");
        Console.WriteLine(
            $"Challenge: {challenge.Length}/{benchmark.ChallengeReserve}");
        Console.WriteLine(
            $"Total benchmark photos: {benchmark.Photos.Count}");
        Console.WriteLine();

        PrintCoverage("Representative coverage", benchmark.RepresentativeCoverage);
        if (challenge.Length > 0)
        {
            PrintCoverage(
                "Challenge coverage",
                benchmark.ChallengeCoverage ??
                    PhotoQualityBenchmarkFile.Coverage(challenge));
        }

        Console.WriteLine("Human review progress");
        Console.WriteLine("---------------------");
        int reviewed = benchmark.Photos.Count(
            photo => !string.IsNullOrWhiteSpace(photo.Review.QualityLevel));
        Console.WriteLine(
            $"Reviewed: {reviewed}/{benchmark.Photos.Count}; remaining={benchmark.Photos.Count - reviewed}");
        foreach (IGrouping<string, PhotoQualityBenchmarkPhoto> group in benchmark.Photos
                     .Where(photo => !string.IsNullOrWhiteSpace(photo.Review.QualityLevel))
                     .GroupBy(
                         photo => photo.Review.QualityLevel!,
                         StringComparer.Ordinal)
                     .OrderBy(group => group.Key, StringComparer.Ordinal))
        {
            Console.WriteLine($"  {group.Key}: {group.Count()}");
        }
        Console.WriteLine(
            $"Personally important: {benchmark.Photos.Count(photo => photo.Review.PersonallyImportant == true)}");

        if (challenge.Length > 0)
        {
            Console.WriteLine();
            Console.WriteLine("Challenge reason coverage");
            Console.WriteLine("-------------------------");
            foreach (IGrouping<string, PhotoQualityBenchmarkPhoto> group in challenge
                         .SelectMany(photo =>
                             (photo.ChallengeReasons ?? [])
                                 .Select(reason => (reason, photo)))
                         .GroupBy(pair => pair.reason, pair => pair.photo)
                         .OrderBy(group => group.Key, StringComparer.Ordinal))
            {
                Console.WriteLine($"  {group.Key}: {group.Count()}");
            }
        }
    }

    private static void PrintCoverage(
        string title,
        PhotoQualityBenchmarkCoverage coverage)
    {
        Console.WriteLine(title);
        Console.WriteLine(new string('-', title.Length));
        Console.WriteLine($"Photos: {coverage.PhotoCount}");
        PrintCounts("Capture eras", coverage.CaptureEras);
        PrintCounts("Media families", coverage.MediaFamilies);
        PrintCounts("Resolution buckets", coverage.ResolutionBuckets);
        PrintCounts("Orientations", coverage.Orientations);
        PrintCounts("Face buckets", coverage.FaceBuckets);
        Console.WriteLine(
            $"Camera metadata: {coverage.PhotosWithCameraModel}/{coverage.PhotoCount}; distinct camera models={coverage.DistinctCameraModelCount}");
        Console.WriteLine(
            $"Capture time metadata: {coverage.PhotosWithCaptureTime}/{coverage.PhotoCount}");
        Console.WriteLine(
            $"Dimensions: {coverage.PhotosWithDimensions}/{coverage.PhotoCount}");
        Console.WriteLine();
    }

    private static void PrintCounts(
        string label,
        IReadOnlyDictionary<string, int> counts)
    {
        Console.WriteLine(
            $"  {label}: {string.Join(", ", counts.OrderBy(pair => pair.Key, StringComparer.Ordinal).Select(pair => $"{pair.Key}={pair.Value}"))}");
    }

    private static string NextValue(
        string[] args,
        ref int index,
        string option)
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
            "Usage: dotnet run --project tools/PhotoIdentity.PhotoQualityEvaluation -- benchmark report --input private/photo-quality-benchmark.json");
    }
}
