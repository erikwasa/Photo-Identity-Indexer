using PhotoIdentity.Transfer.Bundles;
using PhotoIdentity.Worker;

namespace PhotoIdentity.Cli;

internal sealed record BundleCommandOptions(
    string JobBundlePath,
    string ResultBundlePath,
    string WorkingDirectory,
    string? RepositoryRoot,
    string? ModelDirectory)
{
    public static BundleCommandOptions Parse(string[] args)
    {
        if (args.Length == 0 || args[0] != "process")
        {
            throw new ArgumentException("The supported bundle action is 'process'.");
        }

        string? jobPath = null;
        string? resultPath = null;
        string? workingDirectory = null;
        string? repositoryRoot = null;
        string? modelDirectory = null;

        for (int index = 1; index < args.Length; index++)
        {
            string option = args[index];
            string value = index + 1 < args.Length
                ? args[++index]
                : throw new ArgumentException($"Option '{option}' requires a value.");

            switch (option)
            {
                case "--job":
                case "--job-bundle":
                    jobPath = Single(jobPath, value, option);
                    break;
                case "--result":
                case "--result-bundle":
                    resultPath = Single(resultPath, value, option);
                    break;
                case "--work":
                case "--working-dir":
                    workingDirectory = Single(workingDirectory, value, option);
                    break;
                case "--root":
                    repositoryRoot = Single(repositoryRoot, value, option);
                    break;
                case "--model-dir":
                    modelDirectory = Single(modelDirectory, value, option);
                    break;
                default:
                    throw new ArgumentException($"Unknown option '{option}'.");
            }
        }

        if (jobPath is null)
        {
            throw new ArgumentException("Option '--job' is required.");
        }
        if (resultPath is null)
        {
            throw new ArgumentException("Option '--result' is required.");
        }

        workingDirectory ??= Path.Combine(".artifacts", "bundles", "process-work");
        return new BundleCommandOptions(
            jobPath,
            resultPath,
            workingDirectory,
            repositoryRoot,
            modelDirectory);
    }

    private static string Single(string? current, string value, string option)
    {
        if (current is not null)
        {
            throw new ArgumentException($"Option '{option}' may be supplied only once.");
        }

        ArgumentException.ThrowIfNullOrWhiteSpace(value);
        return value;
    }
}

internal static class BundleCommandRunner
{
    public static async Task<int> RunAsync(
        BundleCommandOptions options,
        TextWriter output,
        CancellationToken cancellationToken)
    {
        string repositoryRoot = RepositoryRootLocator.Resolve(options.RepositoryRoot);
        PortableRecognitionProcessor processor = await PortableRecognitionProcessor.CreateAsync(
            repositoryRoot,
            options.ModelDirectory,
            cancellationToken);
        PortableResultManifest result = await new PortableBundleWorker(processor).ProcessAsync(
            options.JobBundlePath,
            options.ResultBundlePath,
            options.WorkingDirectory,
            cancellationToken);

        output.WriteLine($"result: {Path.GetFullPath(options.ResultBundlePath)}");
        output.WriteLine($"bundle-id: {result.BundleId}");
        output.WriteLine($"revision: {result.AssetRevisionId}");
        output.WriteLine($"profile: {ProfileName(result.Profile)}");
        output.WriteLine($"faces: {result.Faces.Count}");
        output.WriteLine($"detector: {result.DetectorModelId}");
        output.WriteLine($"embedder: {result.EmbedderModelId}");
        return 0;
    }

    private static string ProfileName(PortableBundleProfile profile) => profile switch
    {
        PortableBundleProfile.FullImage => "full-image",
        PortableBundleProfile.ReducedImage => "reduced-image",
        PortableBundleProfile.FaceCrops => "face-crops",
        _ => profile.ToString().ToLowerInvariant(),
    };
}
