using System.Globalization;
using PhotoIdentity.Core.Identifiers;
using PhotoIdentity.Persistence.Sqlite;
using PhotoIdentity.Transfer.Bundles;
using PhotoIdentity.Worker;

namespace PhotoIdentity.Cli;

internal enum BundleCommandAction
{
    Export,
    Process,
    Import,
}

internal sealed record BundleCommandOptions(
    BundleCommandAction Action,
    string? DatabasePath,
    AssetRevisionId? RevisionId,
    PortableBundleProfile Profile,
    string JobBundlePath,
    string? ResultBundlePath,
    string? OutputRoot,
    string WorkingDirectory,
    string? RepositoryRoot,
    string? ModelDirectory,
    double ConfidenceThreshold,
    int ReducedMaximumWidth,
    int ReducedMaximumHeight,
    IReadOnlyList<string> FaceCropPaths)
{
    public static BundleCommandOptions Parse(string[] args)
    {
        if (args.Length == 0)
        {
            throw new ArgumentException("The bundle command requires 'export', 'process' or 'import'.");
        }

        BundleCommandAction action = args[0] switch
        {
            "export" => BundleCommandAction.Export,
            "process" => BundleCommandAction.Process,
            "import" => BundleCommandAction.Import,
            _ => throw new ArgumentException($"Unknown bundle action '{args[0]}'."),
        };

        string? databasePath = null;
        AssetRevisionId? revisionId = null;
        PortableBundleProfile profile = PortableBundleProfile.FullImage;
        bool profileSpecified = false;
        string? jobPath = null;
        string? resultPath = null;
        string? outputRoot = null;
        string? workingDirectory = null;
        string? repositoryRoot = null;
        string? modelDirectory = null;
        double confidenceThreshold = 0.9;
        bool confidenceSpecified = false;
        int reducedMaximumWidth = 1600;
        int reducedMaximumHeight = 1600;
        bool maximumWidthSpecified = false;
        bool maximumHeightSpecified = false;
        List<string> cropPaths = [];

        for (int index = 1; index < args.Length; index++)
        {
            string option = args[index];
            string value = index + 1 < args.Length
                ? args[++index]
                : throw new ArgumentException($"Option '{option}' requires a value.");

            switch (option)
            {
                case "--database":
                    databasePath = Single(databasePath, value, option);
                    break;
                case "--revision":
                case "--revision-id":
                    if (revisionId is not null)
                    {
                        throw new ArgumentException($"Option '{option}' may be supplied only once.");
                    }
                    if (!Guid.TryParse(value, out Guid parsedRevision) || parsedRevision == Guid.Empty)
                    {
                        throw new ArgumentException($"Option '{option}' requires a non-empty GUID.");
                    }
                    revisionId = AssetRevisionId.From(parsedRevision);
                    break;
                case "--profile":
                    if (profileSpecified)
                    {
                        throw new ArgumentException($"Option '{option}' may be supplied only once.");
                    }
                    profile = ParseProfile(value);
                    profileSpecified = true;
                    break;
                case "--job":
                case "--job-bundle":
                    jobPath = Single(jobPath, value, option);
                    break;
                case "--result":
                case "--result-bundle":
                    resultPath = Single(resultPath, value, option);
                    break;
                case "--output":
                case "--output-root":
                    outputRoot = Single(outputRoot, value, option);
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
                case "--confidence":
                    if (confidenceSpecified)
                    {
                        throw new ArgumentException($"Option '{option}' may be supplied only once.");
                    }
                    confidenceThreshold = UnitInterval(value, option);
                    confidenceSpecified = true;
                    break;
                case "--max-width":
                    if (maximumWidthSpecified)
                    {
                        throw new ArgumentException($"Option '{option}' may be supplied only once.");
                    }
                    reducedMaximumWidth = PositiveInteger(value, option);
                    maximumWidthSpecified = true;
                    break;
                case "--max-height":
                    if (maximumHeightSpecified)
                    {
                        throw new ArgumentException($"Option '{option}' may be supplied only once.");
                    }
                    reducedMaximumHeight = PositiveInteger(value, option);
                    maximumHeightSpecified = true;
                    break;
                case "--crop":
                    ArgumentException.ThrowIfNullOrWhiteSpace(value);
                    cropPaths.Add(value);
                    break;
                default:
                    throw new ArgumentException($"Unknown option '{option}'.");
            }
        }

        jobPath ??= throw new ArgumentException("Option '--job' is required.");
        workingDirectory ??= Path.Combine(
            ".artifacts",
            "bundles",
            action.ToString().ToLowerInvariant() + "-work");

        switch (action)
        {
            case BundleCommandAction.Export:
                Require(databasePath, "--database", action);
                if (revisionId is null)
                {
                    throw new ArgumentException("Option '--revision' is required for bundle export.");
                }
                if (resultPath is not null || outputRoot is not null || repositoryRoot is not null || modelDirectory is not null)
                {
                    throw new ArgumentException(
                        "Bundle export does not accept --result, --output, --root or --model-dir.");
                }
                if (profile != PortableBundleProfile.ReducedImage &&
                    (maximumWidthSpecified || maximumHeightSpecified))
                {
                    throw new ArgumentException("--max-width and --max-height require the reduced-image profile.");
                }
                if (profile == PortableBundleProfile.FaceCrops && cropPaths.Count == 0)
                {
                    throw new ArgumentException("The face-crops profile requires at least one --crop path.");
                }
                if (profile != PortableBundleProfile.FaceCrops && cropPaths.Count != 0)
                {
                    throw new ArgumentException("--crop may be used only with the face-crops profile.");
                }
                break;
            case BundleCommandAction.Process:
                Require(resultPath, "--result", action);
                if (databasePath is not null || revisionId is not null || outputRoot is not null ||
                    profileSpecified || confidenceSpecified || maximumWidthSpecified || maximumHeightSpecified || cropPaths.Count != 0)
                {
                    throw new ArgumentException(
                        "Bundle process accepts only --job, --result, --work, --root and --model-dir.");
                }
                break;
            case BundleCommandAction.Import:
                Require(databasePath, "--database", action);
                Require(resultPath, "--result", action);
                Require(outputRoot, "--output", action);
                if (revisionId is not null || repositoryRoot is not null || modelDirectory is not null ||
                    profileSpecified || confidenceSpecified || maximumWidthSpecified || maximumHeightSpecified || cropPaths.Count != 0)
                {
                    throw new ArgumentException(
                        "Bundle import accepts only --database, --job, --result, --output and --work.");
                }
                break;
            default:
                throw new ArgumentOutOfRangeException(nameof(action));
        }

        return new BundleCommandOptions(
            action,
            databasePath,
            revisionId,
            profile,
            jobPath,
            resultPath,
            outputRoot,
            workingDirectory,
            repositoryRoot,
            modelDirectory,
            confidenceThreshold,
            reducedMaximumWidth,
            reducedMaximumHeight,
            cropPaths);
    }

    private static PortableBundleProfile ParseProfile(string value) => value.ToLowerInvariant() switch
    {
        "full" or "full-image" => PortableBundleProfile.FullImage,
        "reduced" or "reduced-image" => PortableBundleProfile.ReducedImage,
        "crops" or "face-crops" => PortableBundleProfile.FaceCrops,
        _ => throw new ArgumentException(
            "Option '--profile' must be full-image, reduced-image or face-crops."),
    };

    private static string Single(string? current, string value, string option)
    {
        if (current is not null)
        {
            throw new ArgumentException($"Option '{option}' may be supplied only once.");
        }
        ArgumentException.ThrowIfNullOrWhiteSpace(value);
        return value;
    }

    private static double UnitInterval(string value, string option)
    {
        double parsed = double.TryParse(
            value,
            NumberStyles.Float,
            CultureInfo.InvariantCulture,
            out double result) && double.IsFinite(result)
            ? result
            : throw new ArgumentException($"Option '{option}' requires a finite number.");
        return parsed is >= 0 and <= 1
            ? parsed
            : throw new ArgumentException($"Option '{option}' must be between zero and one.");
    }

    private static int PositiveInteger(string value, string option) =>
        int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out int parsed) && parsed > 0
            ? parsed
            : throw new ArgumentException($"Option '{option}' requires a positive integer.");

    private static void Require(string? value, string option, BundleCommandAction action)
    {
        if (value is null)
        {
            throw new ArgumentException(
                $"Option '{option}' is required for bundle {action.ToString().ToLowerInvariant()}.");
        }
    }
}

internal static class BundleCommandRunner
{
    public static async Task<int> RunAsync(
        BundleCommandOptions options,
        TextWriter output,
        CancellationToken cancellationToken)
    {
        return options.Action switch
        {
            BundleCommandAction.Export => await ExportAsync(options, output, cancellationToken),
            BundleCommandAction.Process => await ProcessAsync(options, output, cancellationToken),
            BundleCommandAction.Import => await ImportAsync(options, output, cancellationToken),
            _ => throw new ArgumentOutOfRangeException(nameof(options)),
        };
    }

    private static async Task<int> ExportAsync(
        BundleCommandOptions options,
        TextWriter output,
        CancellationToken cancellationToken)
    {
        SqliteCatalogueDatabase database = new(options.DatabasePath!);
        PortableBundleExportCoordinator coordinator = new(database);
        PortableBundleExportResult result = await coordinator.ExportAsync(
            new PortableBundleExportOptions(
                options.RevisionId!.Value,
                options.Profile,
                options.JobBundlePath,
                options.WorkingDirectory,
                options.ConfidenceThreshold,
                options.ReducedMaximumWidth,
                options.ReducedMaximumHeight,
                options.FaceCropPaths),
            cancellationToken);

        output.WriteLine($"bundle: {Path.GetFullPath(options.JobBundlePath)}");
        output.WriteLine($"bundle-id: {result.Manifest.BundleId}");
        output.WriteLine($"revision: {result.Manifest.AssetRevisionId}");
        output.WriteLine($"profile: {ProfileName(result.Manifest.Profile)}");
        output.WriteLine($"inputs: {result.InputCount}");
        return 0;
    }

    private static async Task<int> ProcessAsync(
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
            options.ResultBundlePath!,
            options.WorkingDirectory,
            cancellationToken);

        output.WriteLine($"result: {Path.GetFullPath(options.ResultBundlePath!)}");
        output.WriteLine($"bundle-id: {result.BundleId}");
        output.WriteLine($"revision: {result.AssetRevisionId}");
        output.WriteLine($"profile: {ProfileName(result.Profile)}");
        output.WriteLine($"faces: {result.Faces.Count}");
        output.WriteLine($"detector: {result.DetectorModelId}");
        output.WriteLine($"embedder: {result.EmbedderModelId}");
        return 0;
    }

    private static async Task<int> ImportAsync(
        BundleCommandOptions options,
        TextWriter output,
        CancellationToken cancellationToken)
    {
        SqliteCatalogueDatabase database = new(options.DatabasePath!);
        await database.InitializeAsync(cancellationToken);
        PortableBundleImportResult result = await new SqliteBundleResultImporter(database).ImportAsync(
            options.JobBundlePath,
            options.ResultBundlePath!,
            options.OutputRoot!,
            options.WorkingDirectory,
            cancellationToken);

        output.WriteLine($"bundle-id: {result.BundleId}");
        output.WriteLine($"revision: {result.AssetRevisionId}");
        output.WriteLine($"imported-faces: {result.ImportedFaceCount}");
        output.WriteLine($"output: {Path.GetFullPath(options.OutputRoot!)}");
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
