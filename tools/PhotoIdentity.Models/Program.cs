using PhotoIdentity.Recognition.Onnx.Models;

namespace PhotoIdentity.Models;

public static class Program
{
    public static async Task<int> Main(string[] args)
    {
        try
        {
            return await RunAsync(args, Console.Out, Console.Error);
        }
        catch (Exception exception)
        {
            Console.Error.WriteLine($"error: {exception.Message}");
            return 1;
        }
    }

    internal static async Task<int> RunAsync(
        string[] args,
        TextWriter output,
        TextWriter error,
        CancellationToken cancellationToken = default)
    {
        if (args.Length == 0 || args[0] is "-h" or "--help" or "help")
        {
            PrintUsage(output);
            return args.Length == 0 ? 1 : 0;
        }

        string command = args[0];
        Options options = Options.Parse(args.Skip(1).ToArray());
        string root = DiscoverRepositoryRoot(options.Root);
        string manifestDirectory = Path.GetFullPath(
            options.ManifestDirectory ?? Path.Combine(root, "models", "manifests"));
        string modelDirectory = Path.GetFullPath(
            options.ModelDirectory ?? Path.Combine(root, "models", "files"));

        ModelManifestLoader loader = new();
        IReadOnlyList<ModelManifest> all = await loader.LoadDirectoryAsync(
            manifestDirectory,
            cancellationToken);
        IReadOnlyList<ModelManifest> selected = Select(all, options.ModelIds);

        switch (command)
        {
            case "list":
                foreach (ModelManifest manifest in selected)
                {
                    output.WriteLine(
                        $"{manifest.ModelId}\t{manifest.Role}\t{manifest.FileName}\t{manifest.Licences.Weights.Spdx}");
                }

                return 0;

            case "verify":
                return await VerifyAsync(selected, modelDirectory, output, cancellationToken);

            case "install":
                using (HttpClient httpClient = new())
                {
                    httpClient.DefaultRequestHeaders.UserAgent.ParseAdd(
                        "Photo-Identity-Indexer/0.1");
                    ModelInstaller installer = new(httpClient);
                    foreach (ModelManifest manifest in selected)
                    {
                        InstalledModel installed = await installer.InstallAsync(
                            manifest,
                            modelDirectory,
                            cancellationToken);
                        output.WriteLine(
                            $"{installed.Manifest.ModelId}: {installed.Status} ({installed.Path})");
                    }
                }

                return 0;

            default:
                error.WriteLine($"Unknown command '{command}'.");
                PrintUsage(error);
                return 1;
        }
    }

    private static async Task<int> VerifyAsync(
        IReadOnlyList<ModelManifest> manifests,
        string modelDirectory,
        TextWriter output,
        CancellationToken cancellationToken)
    {
        ModelFileVerifier verifier = new();
        bool valid = true;

        foreach (ModelManifest manifest in manifests)
        {
            string path = Path.Combine(modelDirectory, manifest.FileName);
            ModelFileVerification result = await verifier.VerifyAsync(
                path,
                manifest,
                cancellationToken);
            output.WriteLine(
                $"{manifest.ModelId}: {(result.IsValid ? "valid" : "invalid")} ({path})");
            valid &= result.IsValid;
        }

        return valid ? 0 : 1;
    }

    private static IReadOnlyList<ModelManifest> Select(
        IReadOnlyList<ModelManifest> manifests,
        IReadOnlyCollection<string> requestedIds)
    {
        if (requestedIds.Count == 0)
        {
            return manifests;
        }

        Dictionary<string, ModelManifest> map = manifests.ToDictionary(
            manifest => manifest.ModelId,
            StringComparer.Ordinal);
        List<ModelManifest> selected = [];

        foreach (string id in requestedIds)
        {
            if (!map.TryGetValue(id, out ModelManifest? manifest))
            {
                throw new KeyNotFoundException($"Unknown model ID '{id}'.");
            }

            selected.Add(manifest);
        }

        return selected;
    }

    private static string DiscoverRepositoryRoot(string? explicitRoot)
    {
        DirectoryInfo? current = new(
            Path.GetFullPath(explicitRoot ?? Environment.CurrentDirectory));

        while (current is not null)
        {
            if (File.Exists(Path.Combine(current.FullName, "PhotoIdentity.slnx")))
            {
                return current.FullName;
            }

            current = current.Parent;
        }

        throw new DirectoryNotFoundException(
            "Could not find PhotoIdentity.slnx. Supply --root with the repository path.");
    }

    private static void PrintUsage(TextWriter output)
    {
        output.WriteLine("""
            PhotoIdentity.Models

              list [--id MODEL_ID] [--root PATH]
              verify [--id MODEL_ID] [--model-dir PATH] [--root PATH]
              install [--id MODEL_ID] [--model-dir PATH] [--root PATH]

            --id may be supplied more than once.
            """);
    }

    private sealed record Options(
        string? Root,
        string? ManifestDirectory,
        string? ModelDirectory,
        IReadOnlyCollection<string> ModelIds)
    {
        public static Options Parse(string[] args)
        {
            string? root = null;
            string? manifestDirectory = null;
            string? modelDirectory = null;
            List<string> modelIds = [];

            for (int index = 0; index < args.Length; index++)
            {
                string option = args[index];
                string value = index + 1 < args.Length
                    ? args[++index]
                    : throw new ArgumentException($"Option '{option}' requires a value.");

                switch (option)
                {
                    case "--root":
                        root = Single(root, value, option);
                        break;
                    case "--manifest-dir":
                        manifestDirectory = Single(manifestDirectory, value, option);
                        break;
                    case "--model-dir":
                        modelDirectory = Single(modelDirectory, value, option);
                        break;
                    case "--id":
                        modelIds.Add(value);
                        break;
                    default:
                        throw new ArgumentException($"Unknown option '{option}'.");
                }
            }

            return new Options(root, manifestDirectory, modelDirectory, modelIds);
        }

        private static string Single(string? current, string value, string option)
        {
            if (current is not null)
            {
                throw new ArgumentException($"Option '{option}' may be supplied only once.");
            }

            return value;
        }
    }
}
