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
                "catalogue" when args.Length > 1 && args[1] == "rehearse-v33" =>
                    await VideoSchemaRehearsalCommandRunner.RunAsync(
                        VideoSchemaRehearsalOptions.Parse(args.Skip(2).ToArray()),
                        output, cancellationToken, expectedRestoredSchemaVersion: 32),
                "catalogue" when args.Length > 1 && args[1] == "rehearse-v34" =>
                    await VideoSchemaRehearsalCommandRunner.RunAsync(
                        VideoSchemaRehearsalOptions.Parse(args.Skip(2).ToArray()),
                        output, cancellationToken, expectedRestoredSchemaVersion: 33),
                "catalogue" when args.Length > 1 && args[1] == "rehearse-v35" =>
                    await VideoSchemaRehearsalCommandRunner.RunAsync(
                        VideoSchemaRehearsalOptions.Parse(args.Skip(2).ToArray()),
                        output, cancellationToken, expectedRestoredSchemaVersion: 34),
                "video" when args.Length > 1 && args[1] == "scratch-cleanup" =>
                    await VideoScratchCleanupCommandRunner.RunAsync(
                        VideoScratchCleanupOptions.Parse(args.Skip(2).ToArray()),
                        output, cancellationToken),
                "video" when args.Length > 2 && args[1] == "jobs" =>
                    await VideoJobsCommandRunner.RunAsync(
                        VideoJobsCommandOptions.Parse(args[2], args.Skip(3).ToArray()),
                        output, cancellationToken),
                "video" when args.Length > 1 && args[1] == "posters-cleanup" =>
                    await VideoPosterCleanupCommandRunner.RunAsync(
                        VideoPosterCleanupOptions.Parse(args.Skip(2).ToArray()),
                        output, cancellationToken),
                "video" when args.Length > 1 && args[1] == "posters" =>
                    await VideoPosterCommandRunner.RunAsync(
                        VideoPosterOptions.Parse(args.Skip(2).ToArray()),
                        output, cancellationToken),
                "video" when args.Length > 1 && args[1] == "ingest" =>
                    await VideoIngestCommandRunner.RunAsync(
                        VideoIngestOptions.Parse(args.Skip(2).ToArray()),
                        output, cancellationToken),
                "video" when args.Length > 1 && args[1] == "pilot" =>
                    await VideoPilotCommandRunner.RunAsync(
                        VideoPilotOptions.Parse(args.Skip(2).ToArray()),
                        output, cancellationToken),
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

              catalogue rehearse-v33 --postgres-connection-env NAME
                                     --restore-report PATH
                          (legacy v32 restore; applies v33 and the current v34)
              catalogue rehearse-v34 --postgres-connection-env NAME
                                     --restore-report PATH
                          (verified v33 restore only; applies v34 job-queue schema)
                          (both commands require a successful restore report and
                           modify ONLY its matched local photoidentity_restore_* database)

              catalogue rehearse-v35 --postgres-connection-env NAME
                                     --restore-report ABSOLUTE_REPORT_PATH
                                     (v34 isolated restore only; video control defaults off)

              video scratch-cleanup --scratch-root DIR
                                    [--min-age-hours HOURS (24..720)]
                                    [--max-directories COUNT (1..256)] [--apply]
                                    (dry-run default; deletes only old,
                                     strictly owned video scratch directories)

              video jobs status --postgres-connection-env NAME --database DB
                                 (read-only; bounded sanitized queue summary
                                  and at most 20 recent opaque job IDs)
              video jobs pause|resume|stop --postgres-connection-env NAME --database DB
                                           [--apply]
                                           (dry-run default; durable v35 pause/stop;
                                            resume never enables video processing)
              video jobs cancel --postgres-connection-env NAME --database DB
                                --job-id UUID [--apply]
                                (dry-run default; explicit cancellation request;
                                 does not start video workers)

              video ingest --enable-video --source DIR --ffprobe ABSOLUTE_EXE
                           [--max-videos COUNT (1..25)] [--timeout-seconds SECONDS (1..120)]
                           [--postgres-connection-env NAME --database DB --apply]
                           (dry-run by default; --apply writes only bounded local video
                            revisions and technical metadata; no posters or photo analysis)

              video posters-cleanup --enable-video --source DIR --derivatives DIR
                                    --postgres-connection-env NAME --database DB
                                    [--max-files COUNT (1..25)] [--min-age-hours HOURS (24..720)]
                                    [--apply]
                                    (dry-run default; deletes only old, source-proven
                                     stale SDR JPEG posters, never photo derivatives)

              video posters --enable-video --source DIR --derivatives DIR
                            --ffmpeg ABSOLUTE_EXE
                            --postgres-connection-env NAME --database DB
                            [--max-videos COUNT (1..25)] [--timeout-seconds SECONDS (1..120)]
                            [--apply]
                            (dry-run by default; only creates bounded revision-bound
                             SDR JPEG posters for previously indexed local videos)

              video pilot --enable-video --source DIR --derivatives DIR
                          --ffprobe ABSOLUTE_EXE --ffmpeg ABSOLUTE_EXE
                          [--max-videos COUNT (1..50)] [--timeout-seconds SECONDS (1..120)]
                          [--preview-hlg]
                          (HLG previews require explicit BT.2020 HLG stream evidence;
                           evaluation-only, no catalogue or OneDrive hydration)

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
