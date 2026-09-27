namespace PhotoIdentity.Cli;

public static class Program
{
    public static async Task<int> Main(string[] args)
    {
        try
        {
            return await RunAsync(args, Console.Out, Console.Error);
        }
        catch (OperationCanceledException)
        {
            Console.Error.WriteLine("error: operation cancelled");
            return 130;
        }
        catch (Exception exception)
        {
            Console.Error.WriteLine($"error: {exception.Message}");
            return 1;
        }
    }

    public static async Task<int> RunAsync(
        string[] args,
        TextWriter output,
        TextWriter error,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(args);
        ArgumentNullException.ThrowIfNull(output);
        ArgumentNullException.ThrowIfNull(error);

        if (args.Length == 0 || args[0] is "help" or "-h" or "--help")
        {
            PrintUsage(output);
            return args.Length == 0 ? 2 : 0;
        }

        try
        {
            return args[0] switch
            {
                "archive" when args.Length > 1 && args[1] == "proxy" =>
                    await ArchiveProxyMeasureCommandRunner.RunAsync(
                        ArchiveProxyMeasureCommandOptions.Parse(args.Skip(2).ToArray()),
                        output,
                        cancellationToken),
                "bundle" => await BundleCommandRunner.RunAsync(
                    BundleCommandOptions.Parse(args.Skip(1).ToArray()),
                    output,
                    cancellationToken),
                "decode" => await DecodeCommandRunner.RunAsync(
                    DecodeCommandOptions.Parse(args.Skip(1).ToArray()),
                    output,
                    error,
                    cancellationToken),
                "evaluate" => await EvaluationCommandRunner.RunAsync(
                    EvaluationCommandOptions.Parse(args.Skip(1).ToArray()),
                    output,
                    error,
                    cancellationToken),
                "inspect" => await InspectCommandRunner.RunAsync(
                    InspectCommandOptions.Parse(args.Skip(1).ToArray()),
                    output,
                    error,
                    cancellationToken),
                "metadata" when args.Length > 1 && args[1] == "enrich" =>
                    await MetadataEnrichmentCommandRunner.RunAsync(
                        MetadataEnrichmentCommandOptions.Parse(args.Skip(2).ToArray()),
                        output,
                        cancellationToken),
                "rollout" => await DetectorRolloutCommandRunner.RunAsync(
                    DetectorRolloutCommandOptions.Parse(args.Skip(1).ToArray()),
                    output,
                    cancellationToken),
                "semantic-tags" when args.Length > 1 && args[1] == "evaluate" =>
                    await SemanticTagEvaluationCommandRunner.RunAsync(
                        SemanticTagEvaluationCommandOptions.Parse(args.Skip(2).ToArray()),
                        output,
                        cancellationToken),
                "image-embeddings" when args.Length > 1 && args[1] == "evaluate" =>
                    await ImageEmbeddingEvaluationCommandRunner.RunAsync(
                        ImageEmbeddingEvaluationCommandOptions.Parse(args.Skip(2).ToArray()),
                        output,
                        cancellationToken),
                "narration" when args.Length > 1 && args[1] == "evaluate" =>
                    await NarrationEvaluationCommandRunner.RunAsync(
                        NarrationEvaluationCommandOptions.Parse(args.Skip(2).ToArray()),
                        output,
                        cancellationToken),
                _ => UnknownCommand(args[0], error),
            };
        }
        catch (ArgumentException exception)
        {
            error.WriteLine($"error: {exception.Message}");
            PrintUsage(error);
            return 2;
        }
    }

    private static int UnknownCommand(string command, TextWriter error)
    {
        error.WriteLine($"Unknown command '{command}'.");
        PrintUsage(error);
        return 2;
    }

    private static void PrintUsage(TextWriter output)
    {
        output.WriteLine("""
            Photo Identity Indexer CLI

              archive proxy measure --source DIR --output DIR
                                    --profile ID:MAX_LONG_EDGE:JPEG_QUALITY
                                    [--profile ID:MAX_LONG_EDGE:JPEG_QUALITY ...]
                                    [--non-recursive]

              rollout start --postgres-connection-env NAME --output DIR
                            (--revision REVISION_ID [...] | --revision-file PATH)
                            [--root PATH] [--model-dir DIR] [--max-attempts COUNT]
              rollout resume --postgres-connection-env NAME --run RUN_ID [--max-attempts COUNT]
              rollout status --postgres-connection-env NAME --run RUN_ID
              rollout apply --postgres-connection-env NAME --run RUN_ID

              bundle process --job PATH --result PATH [--work DIR]
                             [--root PATH] [--model-dir DIR]

              decode --input PATH --output PATH [--report PATH]
                     [--max-width PIXELS --max-height PIXELS] [--verbose]

              evaluate --dataset PATH [--output PATH]
                       [--archive-images COUNT]
                       [--hourly-cost AMOUNT] [--currency CODE]

              inspect PATH [--output DIR] [--root PATH] [--model-dir DIR]
                           [--confidence 0..1] [--padding RATIO]
                           [--overwrite] [--verbose]

              metadata enrich --postgres-connection-env NAME --rules PATH
                              [--report PATH] [--apply]

              semantic-tags evaluate --postgres-connection-env NAME
                                     --collection COLLECTION_ID
                                     --proxy-root DIR --proxy-profile ID
                                     --model PATH
                                     --tokenizer-vocab PATH
                                     --tokenizer-merges PATH
                                     --concept-vocabulary PATH
                                     [--target-count COUNT]
                                     [--moment-gap-minutes MINUTES]
                                     [--max-candidates COUNT]
                                     [--concepts-per-photo COUNT]
                                     [--compare-originals COUNT]
                                     [--report PATH]
                                     [--review-output DIR]

              image-embeddings evaluate --postgres-connection-env NAME
                                        --collection COLLECTION_ID
                                        --proxy-root DIR --proxy-profile ID
                                        --model PATH
                                        --tokenizer-vocab PATH
                                        --tokenizer-merges PATH
                                        --query TEXT [--query TEXT ...]
                                        [--target-count COUNT]
                                        [--moment-gap-minutes MINUTES]
                                        [--max-candidates COUNT]
                                        [--retrieval-count COUNT]
                                        [--similar-seeds COUNT]
                                        [--neighbors-per-seed COUNT]
                                        [--report PATH]
                                        [--review-output DIR]

              narration evaluate --postgres-connection-env NAME
                                 --collection COLLECTION_ID
                                 --proxy-root DIR --proxy-profile ID
                                 [--ollama-base-url LOOPBACK_URL]
                                 [--model NAME]
                                 [--caption-image-mode proxy|thumbnail]
                                 [--ollama-context TOKENS]
                                 [--target-count COUNT]
                                 [--moment-gap-minutes MINUTES]
                                 [--sample-count COUNT]
                                 [--timeout-seconds SECONDS]
                                 [--report PATH]
                                 [--review-output DIR]

            PostgreSQL is the only supported catalogue. Migration-era commands that opened
            SQLite catalogues directly were retired by WI-0149 after PostgreSQL became the
            unconditional application catalogue and their accepted migration evidence was
            preserved in historical work-item records.

            Bundle process remains database-free: it runs the portable recognition worker
            using settings stored in the job bundle and writes a checksummed result bundle.

            Metadata enrich is dry-run by default and writes only when --apply is supplied.
            Catalogue commands that need a live database read the PostgreSQL connection string
            only from the named environment variable and never print it.
            """);
    }
}
