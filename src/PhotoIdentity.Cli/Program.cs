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
                "batch" => await BatchCommandRunner.RunAsync(
                    BatchCommandOptions.Parse(args.Skip(1).ToArray()),
                    output,
                    cancellationToken),
                "decode" => await DecodeCommandRunner.RunAsync(
                    DecodeCommandOptions.Parse(args.Skip(1).ToArray()),
                    output,
                    error,
                    cancellationToken),
                "inspect" => await InspectCommandRunner.RunAsync(
                    InspectCommandOptions.Parse(args.Skip(1).ToArray()),
                    output,
                    error,
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

              batch start --database PATH --source DIR [--output DIR]
                          [--root PATH] [--model-dir DIR] [--non-recursive]
                          [--confidence 0..1] [--padding RATIO]
                          [--max-attempts COUNT]
              batch resume --database PATH --run RUN_ID [--max-attempts COUNT]
              batch status --database PATH --run RUN_ID
              batch cancel --database PATH --run RUN_ID

              decode --input PATH --output PATH [--report PATH]
                     [--max-width PIXELS --max-height PIXELS] [--verbose]

              inspect PATH [--output DIR] [--root PATH] [--model-dir DIR]
                           [--confidence 0..1] [--padding RATIO]
                           [--overwrite] [--verbose]

            Batch start scans a local folder, creates durable jobs for each current
            immutable revision and runs the production inspection pipeline until idle.
            Batch resume reconstructs the saved configuration and continues due work.
            Batch status reports durable progress counts. Batch cancel atomically
            cancels queued and active work and invalidates active leases.

            The decode command reads JPEG or PNG content, applies EXIF orientation,
            optionally downsizes it, and writes a normalised PNG without modifying the input.

            The inspect command composes decoding, YuNet detection, padded crops,
            five-point SFace alignment and embeddings. It writes an annotated SVG,
            per-face outputs, a reproducibility manifest and detailed timings without
            modifying the source image.
            """);
    }
}
