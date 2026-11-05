using System.Text.Json;
using PhotoIdentity.Imaging.OpenCv;
using PhotoIdentity.Recognition.Onnx.Models;
using PhotoIdentity.Recognition.Onnx.SFace;
using PhotoIdentity.Recognition.Onnx.YuNet;

namespace PhotoIdentity.VideoFaceFeasibility;

/// <summary>
/// Standalone, opt-in exploratory tool. No Photo Identity database, app settings,
/// OneDrive client, original video or writable identity services are referenced.
/// </summary>
public static class Program
{
    private static readonly JsonSerializerOptions OutputJson = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = true,
    };

    public static async Task<int> Main(string[] args)
    {
        if (args.Length == 1 && args[0] is "--help" or "-h")
        {
            Console.WriteLine(
                "Usage: dotnet run --project tools/PhotoIdentity.VideoFaceFeasibility -- " +
                "--manifest <private-frames.json> [--root <repo>] [--model-dir <installed-model-dir>]");
            return 0;
        }

        try
        {
            Options options = Options.Parse(args);
            // All inputs are pre-extracted, operator-selected local image files.
            VideoFaceFrameSet set = await VideoFaceFrameSetLoader.LoadAsync(options.Manifest);
            string repositoryRoot = Path.GetFullPath(options.RepositoryRoot ?? Directory.GetCurrentDirectory());
            string modelDirectory = Path.GetFullPath(options.ModelDirectory ??
                Path.Combine(repositoryRoot, "models", "files"));
            string manifestDirectory = Path.GetDirectoryName(Path.GetFullPath(options.Manifest))!;

            ModelManifestLoader loader = new();
            string manifests = Path.Combine(repositoryRoot, "models", "manifests");
            ModelManifest detectorManifest = await loader.LoadAsync(
                Path.Combine(manifests, "yunet-2023mar-fp32.json"), CancellationToken.None);
            ModelManifest embedderManifest = await loader.LoadAsync(
                Path.Combine(manifests, "sface-2021dec-fp32.json"), CancellationToken.None);
            string detectorPath = Path.Combine(modelDirectory, detectorManifest.FileName);
            string embedderPath = Path.Combine(modelDirectory, embedderManifest.FileName);
            ModelFileVerifier verifier = new();
            await verifier.VerifyOrThrowAsync(detectorPath, detectorManifest);
            await verifier.VerifyOrThrowAsync(embedderPath, embedderManifest);

            using YuNetFaceDetector detector = new(
                detectorManifest, detectorPath, new YuNetDetectorOptions { ConfidenceThreshold = 0.9 });
            using SFaceFaceEmbedder embedder = new(embedderManifest, embedderPath);
            VideoFaceExperiment experiment = new(
                new OpenCvImageDecoder(),
                detector,
                new OpenCvFaceAligner(),
                embedder);

            VideoFaceAggregate result = await experiment.RunAsync(set, manifestDirectory);
            Console.WriteLine(JsonSerializer.Serialize(result, OutputJson));
            return 0;
        }
        catch (Exception exception) when (exception is ArgumentException or JsonException)
        {
            // Never print private frame paths or contents.
            Console.Error.WriteLine("error: invalid offline frame manifest or options (see --help).");
            return 2;
        }
        catch (Exception exception) when (
            exception is ModelIntegrityException or ModelManifestException or FileNotFoundException)
        {
            Console.Error.WriteLine("error: required approved face model is unavailable or failed integrity validation.");
            return 3;
        }
        catch (Exception)
        {
            Console.Error.WriteLine("error: offline frame feasibility run failed; private inputs were not logged.");
            return 1;
        }
    }

    private sealed record Options(string Manifest, string? RepositoryRoot, string? ModelDirectory)
    {
        public static Options Parse(string[] args)
        {
            string? manifest = null;
            string? root = null;
            string? models = null;
            for (int i = 0; i < args.Length; i++)
            {
                string name = args[i];
                if (++i >= args.Length || string.IsNullOrWhiteSpace(args[i]))
                {
                    throw new ArgumentException("Every option requires a nonempty value.");
                }

                string value = args[i];
                switch (name)
                {
                    case "--manifest" when manifest is null:
                        manifest = value;
                        break;
                    case "--root" when root is null:
                        root = value;
                        break;
                    case "--model-dir" when models is null:
                        models = value;
                        break;
                    default:
                        throw new ArgumentException("Unknown or repeated option.");
                }
            }

            return new Options(
                manifest ?? throw new ArgumentException("--manifest is required."),
                root,
                models);
        }
    }
}
