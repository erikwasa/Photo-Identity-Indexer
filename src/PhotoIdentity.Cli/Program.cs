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
                "archive" => await ArchiveCommandRunner.RunAsync(
                    ArchiveCommandOptions.Parse(args.Skip(1).ToArray()),
                    output,
                    cancellationToken),
                "batch" => await BatchCommandRunner.RunAsync(
                    BatchCommandOptions.Parse(args.Skip(1).ToArray()),
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
                "evaluate" when args.Length > 1 && args[1] == "export" =>
                    await CatalogueEvaluationExportCommandRunner.RunAsync(
                        CatalogueEvaluationExportCommandOptions.Parse(args.Skip(2).ToArray()),
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
                "match" => await MatchCommandRunner.RunAsync(
                    MatchCommandOptions.Parse(args.Skip(1).ToArray()),
                    output,
                    cancellationToken),
                "rollout" => await DetectorRolloutCommandRunner.RunAsync(
                    DetectorRolloutCommandOptions.Parse(args.Skip(1).ToArray()),
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

              archive include --database PATH --root DIR --folder RELATIVE_DIR
              archive list --database PATH
              archive sync --database PATH
              archive analyze --database PATH --output DIR
                              [--repository-root PATH] [--model-dir DIR]
                              [--max-attempts COUNT]
              archive resume --database PATH --run RUN_ID [--max-attempts COUNT]
              archive status --database PATH --run RUN_ID

              batch start --database PATH --source DIR [--output DIR]
                          [--root PATH] [--model-dir DIR] [--non-recursive]
                          [--confidence 0..1] [--padding RATIO]
                          [--detector-pipeline single-pass|full-image-plus-tiles]
                          [--tile-size PIXELS] [--tile-overlap 0..<1]
                          [--merge-nms 0..1] [--max-attempts COUNT]
              batch resume --database PATH --run RUN_ID [--max-attempts COUNT]
              batch status --database PATH --run RUN_ID
              batch cancel --database PATH --run RUN_ID

              rollout start --database PATH --output DIR
                            (--revision REVISION_ID [...] | --revision-file PATH)
                            [--root PATH] [--model-dir DIR] [--max-attempts COUNT]
              rollout resume --database PATH --run RUN_ID [--max-attempts COUNT]
              rollout status --database PATH --run RUN_ID
              rollout apply --database PATH --run RUN_ID

              bundle export --database PATH --revision REVISION_ID --job PATH
                            [--profile full-image|reduced-image|face-crops]
                            [--confidence 0..1] [--work DIR]
                            [--max-width PIXELS --max-height PIXELS]
                            [--crop FACE_NUMBER=PATH ...]
              bundle process --job PATH --result PATH [--work DIR]
                             [--root PATH] [--model-dir DIR]
              bundle import --database PATH --job PATH --result PATH
                            --output DIR [--work DIR]

              decode --input PATH --output PATH [--report PATH]
                     [--max-width PIXELS --max-height PIXELS] [--verbose]

              evaluate export --database PATH --output PATH --dataset-id ID
                              --pipeline-version VERSION --detector-id ID
                              --detector-hash SHA256 --embedder-id ID
                              --embedder-hash SHA256 --seed VALUE
                              (--run RUN_ID | --revision REVISION_ID [...])
                              [--gallery-per-person COUNT]
                              [--validation-known-per-person COUNT]
                              [--test-known-per-person COUNT]
                              [--validation-unknown COUNT] [--test-unknown COUNT]
                              [--threshold SCORE ...]
              evaluate --dataset PATH [--output PATH]
                       [--archive-images COUNT]
                       [--hourly-cost AMOUNT] [--currency CODE]

              inspect PATH [--output DIR] [--root PATH] [--model-dir DIR]
                           [--confidence 0..1] [--padding RATIO]
                           [--overwrite] [--verbose]

              match regenerate --database PATH --embedder-id ID
                               --embedder-hash SHA256

            Archive include configures one permanent local archive root and stores a
            recursively included folder relative to that root. Adding a parent folder
            subsumes redundant child inclusions without changing source identity. Archive
            list reports the configured root and normalized coverage. Archive sync scans
            every included folder and discovers new, changed and missing files without
            tombstoning catalogue assets outside the selected coverage.

            Archive analyze runs the governed CenterFace confidence-0.5 single-pass and
            SFace FP32 profile only for current immutable revisions that have not already
            completed that exact profile. The completion marker is independent of detected
            face count, so a successful zero-face image is not repeatedly reprocessed.
            Archive resume continues the same durable run and validates the saved exact
            profile before processing. Archive status reports the registered profile and
            durable job progress.

            Batch start scans a local folder, creates durable jobs for each current
            immutable revision and runs the production inspection pipeline until idle.
            The default detector pipeline is single-pass. The optional
            full-image-plus-tiles pipeline preserves aspect ratio for each pass, maps
            tile detections to original-image coordinates and globally merges duplicates.
            Batch resume reconstructs the saved configuration and continues due work.
            Batch status reports durable progress counts. Batch cancel atomically
            cancels queued and active work and invalidates active leases.

            Rollout start is a separate detector-migration path. It never scans a source
            folder and accepts only explicitly named immutable catalogue revisions. The
            detector is fixed to the governed CenterFace 0.5 single-pass pipeline. Every
            revision persists its reconciliation plan and all candidate payloads before
            any unambiguous result is applied. Ambiguous candidates remain pending human
            review at /detector-rollout/{RUN_ID}; rollout apply persists reviewed choices
            from the saved payload without re-running detector inference. The ordinary
            batch command is not a detector-migration mechanism.

            Bundle export verifies a canonical immutable revision and writes a portable
            full-image, reduced-image or aligned face-crop job. Face-crop exports require
            each human-facing one-based face number, for example --crop 3=C:\Crops\face.png,
            so returned embeddings retain the canonical occurrence ordinal. Bundle process
            runs the database-free OpenCV, YuNet and SFace worker using settings stored in
            the job. Bundle import verifies the exact job/result pair and revision hash
            before replay-safe SQLite persistence. Face-crop jobs treat every 112x112 input
            as an already-aligned face and record explicit crop-input provenance.

            The decode command reads JPEG or PNG content, applies EXIF orientation,
            optionally downsizes it, and writes a normalised PNG without modifying the input.

            Evaluate export reads only human-assigned catalogue faces for exact detector and
            embedder revisions. A required seed assigns whole source revisions to gallery,
            validation or test, preventing photo leakage and recording input provenance.
            The evaluate command selects a threshold from validation data only and reports
            held-out identity metrics, model provenance, throughput and optional projections.

            The inspect command composes decoding, YuNet detection, padded crops,
            five-point SFace alignment and embeddings. It writes an annotated SVG,
            per-face outputs, a reproducibility manifest and detailed timings without
            modifying the source image.

            Match regenerate rebuilds ranked suggestions for one exact embedding model
            revision. It uses current human-confirmed exemplars, preserves rejected
            face-person exclusions and never creates or changes canonical labels.
            """);
    }
}
