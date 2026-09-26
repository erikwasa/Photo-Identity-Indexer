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
                "archive" when args.Length > 1 && args[1] == "inventory" =>
                    await ArchiveMediaInventoryCommandRunner.RunAsync(
                        ArchiveMediaInventoryCommandOptions.Parse(args.Skip(2).ToArray()),
                        output,
                        cancellationToken),
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
                "catalogue" when args.Length > 1 && args[1] == "backup" =>
                    await CatalogueBackupCommandRunner.RunAsync(
                        CatalogueBackupCommandOptions.Parse(args.Skip(1).ToArray()),
                        output,
                        cancellationToken),
                "catalogue" => await CatalogueMigrationCommandRunner.RunAsync(
                    CatalogueMigrationCommandOptions.Parse(args.Skip(1).ToArray()),
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

              catalogue backup --database PATH --output PATH --application-stopped
              catalogue migrate --sqlite-backup PATH
                                --postgres-connection-env NAME
                                [--report PATH]

              archive include --database PATH --root DIR --folder RELATIVE_DIR
              archive list --database PATH
              archive sync --database PATH
              archive inventory --database PATH
              archive analyze --database PATH --output DIR
                              [--repository-root PATH] [--model-dir DIR]
                              [--max-attempts COUNT]
              archive resume --database PATH --run RUN_ID [--max-attempts COUNT]
              archive status --database PATH --run RUN_ID
              archive proxy measure --source DIR --output DIR
                                    --profile ID:MAX_LONG_EDGE:JPEG_QUALITY
                                    [--profile ID:MAX_LONG_EDGE:JPEG_QUALITY ...]
                                    [--non-recursive]

              batch start --database PATH --source DIR [--output DIR]
                          [--root PATH] [--model-dir DIR] [--non-recursive]
                          [--confidence 0..1] [--padding RATIO]
                          [--detector-pipeline single-pass|full-image-plus-tiles]
                          [--tile-size PIXELS] [--tile-overlap 0..<1]
                          [--merge-nms 0..1] [--max-attempts COUNT]
              batch resume --database PATH --run RUN_ID [--max-attempts COUNT]
              batch status --database PATH --run RUN_ID
              batch cancel --database PATH --run RUN_ID

              rollout start --postgres-connection-env NAME --output DIR
                            (--revision REVISION_ID [...] | --revision-file PATH)
                            [--root PATH] [--model-dir DIR] [--max-attempts COUNT]
              rollout resume --postgres-connection-env NAME --run RUN_ID [--max-attempts COUNT]
              rollout status --postgres-connection-env NAME --run RUN_ID
              rollout apply --postgres-connection-env NAME --run RUN_ID

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
                               [--auto-assign on|off]
                               [--high-score-threshold 0..1]
                               [--high-margin-threshold 0..2]
                               [--medium-score-threshold 0..1]

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

            Catalogue backup is the WI-0102 stopped-source snapshot path. It requires the
            operator to explicitly confirm that Photo Identity has been stopped, opens the
            source SQLite catalogue read-only, verifies the current schema and foreign keys,
            creates a consistent SQLite backup through the SQLite backup API, then verifies
            backup integrity and foreign keys. It refuses to overwrite an existing backup and
            prints only the backup filename, hashes, size and schema version rather than the
            private catalogue path.

            Catalogue migrate is the offline WI-0102 SQLite-to-PostgreSQL import path.
            It reads an already-created SQLite backup in read-only/query-only mode, requires
            a PostgreSQL database with no existing public tables, initializes the current
            PostgreSQL schema, copies all compatible authoritative tables in foreign-key
            dependency order, preserves explicit stable IDs, repairs generated integer
            sequences and verifies source/target row counts before commit. Populated SQLite
            tables or columns without a PostgreSQL destination fail the migration rather
            than being silently dropped. The PostgreSQL connection string is read only from
            the named environment variable and is never written to output or the report.
            Stop the application before taking the final SQLite backup; never migrate from
            one writable catalogue while another writable authoritative catalogue is active.

            Archive include configures one permanent local archive root and stores a
            recursively included folder relative to that root. Adding a parent folder
            subsumes redundant child inclusions without changing source identity. Archive
            list reports the configured root and normalized coverage. Archive sync scans
            every included folder and discovers new, changed and missing supported files
            without tombstoning catalogue assets outside the selected coverage.

            Archive inventory scans only configured coverage and reports aggregate counts
            by file extension, media family and current support state. It does not open image
            content or print paths, so it can reveal future HEIC/RAW variants without hydrating
            OneDrive placeholders or leaking private filenames.

            Archive analyze runs the governed CenterFace confidence-0.5 single-pass and
            SFace FP32 profile only for current immutable revisions that have not already
            completed that exact profile. The completion marker is independent of detected
            face count, so a successful zero-face image is not repeatedly reprocessed.
            Archive resume continues the same durable run and validates the saved exact
            profile before processing. Archive status reports the registered profile and
            durable job progress.

            Archive proxy measure is a pre-default measurement path. It renders every
            supported JPEG/PNG/HEIC/HEIF source image with one or more exact profiles, writes
            those derivatives outside the source root, and reports only aggregate logical
            source bytes plus total/mean/median/p95 proxy bytes and source-to-proxy compression.
            Candidate syntax is ID:MAX_LONG_EDGE:JPEG_QUALITY. Use at least two profiles for
            the 100-image tuning comparison and the one selected profile for the later
            560-image scale validation. The command never chooses or registers a permanent
            catalogue default; that remains a maintainer decision after visual inspection.

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
            Every rollout action requires --postgres-connection-env NAME. The named
            environment variable contains the PostgreSQL connection string; SQLite is
            not a supported rollout catalogue.

            Bundle export verifies a canonical immutable revision and writes a portable
            full-image, reduced-image or aligned face-crop job. Face-crop exports require
            each human-facing one-based face number, for example --crop 3=C:\Crops\face.png,
            so returned embeddings retain the canonical occurrence ordinal. Bundle process
            runs the database-free OpenCV, YuNet and SFace worker using settings stored in
            the job. Bundle import verifies the exact job/result pair and revision hash
            before replay-safe SQLite persistence. Face-crop jobs treat every 112x112 input
            as an already-aligned face and record explicit crop-input provenance.

            The decode command reads JPEG, PNG, HEIC or HEIF content, applies orientation,
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

            Metadata enrich is the WI-0163 catalogue-only bulk correction path. It is dry-run
            by default, reads explicit JSON rules, proposes dates only for currently undated
            photos and Places only for photos without a named Place or valid non-zero GPS.
            Directory inference preserves month precision and excludes the configured catch-all
            directories such as 1970. Place rules apply only when the photo's entire effective
            date range is contained by the rule. Ambiguities are reported rather than guessed.
            --apply is required for writes, which use the existing append-only capture-date and
            Place repositories. Originals and extracted metadata are never modified.

            Semantic-tags evaluate is the WI-0126 bounded local visible-content experiment.
            It reads one saved Smart Collection from PostgreSQL, generates the same timestamp-first
            Creative candidate set, scores only existing durable review proxies with an operator-
            supplied CLIP-compatible ONNX model plus tokenizer files, and compares the ordinary
            selector with an experimental semantic-diversity bonus. It never writes automatic tags
            or model evidence into the catalogue. --compare-originals explicitly opts into opening
            at most the requested number of safely resolved source originals for proxy/original
            agreement measurement; zero is the default. Output and reports contain aggregate counts,
            public vocabulary concept ids, hashes and runtime evidence only, never private paths,
            filenames, Smart Collection names or revision ids.

            Image-embeddings evaluate is the WI-0127 bounded whole-image embedding
            experiment. It reuses the same local CLIP model/tokenizer assets and durable review
            proxies, emits L2-normalized model-versioned image vectors in memory only, and measures
            exact text-to-image retrieval, exact image-to-image nearest neighbors and an opt-in
            embedding-diversity Creative selector. It does not install pgvector or any ANN index,
            persist vectors, alter canonical metadata or write catalogue evidence. The JSON report
            contains aggregate runtime/storage/retrieval/selection measurements but omits query
            text and revision ids; the optional private review page contains the operator's query
            text and copied review proxies for qualitative retrieval and selection review.

            Narration evaluate is the WI-0128 bounded local caption experiment. It
            sends either existing durable review proxies or explicitly requested temporary
            in-memory thumbnails to an operator-controlled Ollama endpoint
            that must resolve to loopback, compares model captions with deterministic catalogue-
            derived text, and applies a conservative guard against names/locations, relationships,
            ages, dates and event-identity claims. Generated captions remain in memory and the
            optional private review page only; the aggregate JSON report contains model provenance,
            package size, runtime and guard counts but no captions, filenames, collection names or
            revision ids. It never persists generated text or writes catalogue evidence.

            Match regenerate rebuilds ranked suggestions for one exact embedding model
            revision from the current canonical exemplar snapshot while preserving rejected
            face-person exclusions. Confidence groups come from the persisted versioned
            suggestion policy. High requires both the configured rank-1 score threshold and
            the configured minimum rank-1/rank-2 score gap. Automatic assignment is disabled
            by default; when enabled, qualifying High rank-1 suggestions are promoted only
            after all targets have been scored, so newly automatic exemplars cannot cascade
            through the same regeneration. Optional policy arguments update the persisted
            policy before the run.
            """);
    }
}
