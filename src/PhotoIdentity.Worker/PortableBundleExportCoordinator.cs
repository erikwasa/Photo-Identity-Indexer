using System.Security.Cryptography;
using PhotoIdentity.Core.Geometry;
using PhotoIdentity.Core.Identifiers;
using PhotoIdentity.Core.Imaging;
using PhotoIdentity.Core.Recognition;
using PhotoIdentity.Imaging.OpenCv;
using PhotoIdentity.Persistence.Sqlite;
using PhotoIdentity.Transfer.Bundles;

namespace PhotoIdentity.Worker;

public sealed record PortableBundleExportOptions(
    AssetRevisionId AssetRevisionId,
    PortableBundleProfile Profile,
    string BundlePath,
    string WorkingDirectory,
    double ConfidenceThreshold = 0.9,
    int ReducedMaximumWidth = 1600,
    int ReducedMaximumHeight = 1600,
    IReadOnlyList<string>? FaceCropPaths = null);

public sealed record PortableBundleExportResult(
    PortableJobManifest Manifest,
    int InputCount);

/// <summary>
/// Exports one canonical immutable revision to a verified portable job bundle.
/// </summary>
public sealed class PortableBundleExportCoordinator
{
    private readonly SqliteCatalogueDatabase _database;
    private readonly IImageDecoder _decoder;
    private readonly OpenCvPngEncoder _encoder;
    private readonly TimeProvider _timeProvider;

    public PortableBundleExportCoordinator(
        SqliteCatalogueDatabase database,
        IImageDecoder? decoder = null,
        OpenCvPngEncoder? encoder = null,
        TimeProvider? timeProvider = null)
    {
        ArgumentNullException.ThrowIfNull(database);
        _database = database;
        _decoder = decoder ?? new OpenCvImageDecoder();
        _encoder = encoder ?? new OpenCvPngEncoder();
        _timeProvider = timeProvider ?? TimeProvider.System;
    }

    public async Task<PortableBundleExportResult> ExportAsync(
        PortableBundleExportOptions options,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(options);
        ValidateOptions(options);
        await _database.InitializeAsync(cancellationToken);

        CatalogueProcessingAssetRevision asset = await new SqliteLocalBatchRepository(_database)
            .GetAssetRevisionAsync(options.AssetRevisionId, cancellationToken)
            ?? throw new KeyNotFoundException($"Asset revision {options.AssetRevisionId} was not found.");

        string bundlePath = Path.GetFullPath(options.BundlePath);
        string workingRoot = Path.GetFullPath(options.WorkingDirectory);
        if (IsWithin(bundlePath, workingRoot))
        {
            throw new ArgumentException(
                "The job bundle path must be outside the disposable export working directory.",
                nameof(options));
        }

        string exportDirectory = Path.Combine(workingRoot, $"export-{Guid.NewGuid():N}");
        Directory.CreateDirectory(exportDirectory);
        try
        {
            IReadOnlyList<PortableJobInput> inputs = options.Profile switch
            {
                PortableBundleProfile.FullImage =>
                    await CreateFullImageInputAsync(asset, cancellationToken),
                PortableBundleProfile.ReducedImage =>
                    await CreateReducedImageInputAsync(asset, options, exportDirectory, cancellationToken),
                PortableBundleProfile.FaceCrops =>
                    await CreateFaceCropInputsAsync(options, exportDirectory, cancellationToken),
                _ => throw new PortableBundleValidationException(
                    $"Portable profile '{options.Profile}' is not supported for export."),
            };

            PortableRecognitionConfiguration configuration = new(options.ConfidenceThreshold);
            PortableJobManifest manifest = await PortableBundleArchive.CreateJobAsync(
                bundlePath,
                new PortableJobBundleRequest(
                    options.AssetRevisionId,
                    asset.ContentHash,
                    options.Profile,
                    configuration.ToJson(),
                    inputs,
                    _timeProvider.GetUtcNow()),
                cancellationToken);
            return new PortableBundleExportResult(manifest, inputs.Count);
        }
        finally
        {
            if (Directory.Exists(exportDirectory))
            {
                Directory.Delete(exportDirectory, recursive: true);
            }
        }
    }

    private async Task<IReadOnlyList<PortableJobInput>> CreateFullImageInputAsync(
        CatalogueProcessingAssetRevision asset,
        CancellationToken cancellationToken)
    {
        string sourcePath = await ResolveAndVerifySourceAsync(asset, cancellationToken);
        string extension = NormalizedImageExtension(sourcePath);
        return [new PortableJobInput(sourcePath, $"inputs/source{extension}", PortableBundleRoles.SourceImage)];
    }

    private async Task<IReadOnlyList<PortableJobInput>> CreateReducedImageInputAsync(
        CatalogueProcessingAssetRevision asset,
        PortableBundleExportOptions options,
        string exportDirectory,
        CancellationToken cancellationToken)
    {
        string sourcePath = await ResolveAndVerifySourceAsync(asset, cancellationToken);
        ImageFrame image;
        await using (FileStream stream = new(
                         sourcePath,
                         FileMode.Open,
                         FileAccess.Read,
                         FileShare.Read,
                         bufferSize: 64 * 1024,
                         useAsync: true))
        {
            image = await _decoder.DecodeAsync(
                stream,
                new DecodeOptions(new ImageSize(options.ReducedMaximumWidth, options.ReducedMaximumHeight)),
                cancellationToken);
        }

        string reducedPath = Path.Combine(exportDirectory, "reduced.png");
        await EncodeAsync(image, reducedPath, cancellationToken);
        return [new PortableJobInput(reducedPath, "inputs/reduced.png", PortableBundleRoles.ReducedImage)];
    }

    private async Task<IReadOnlyList<PortableJobInput>> CreateFaceCropInputsAsync(
        PortableBundleExportOptions options,
        string exportDirectory,
        CancellationToken cancellationToken)
    {
        List<PortableJobInput> inputs = [];
        IReadOnlyList<string> cropPaths = options.FaceCropPaths ?? [];
        for (int index = 0; index < cropPaths.Count; index++)
        {
            string sourcePath = Path.GetFullPath(cropPaths[index]);
            if (!File.Exists(sourcePath))
            {
                throw new FileNotFoundException("Face-crop input was not found.", sourcePath);
            }

            ImageFrame image;
            await using (FileStream stream = new(
                             sourcePath,
                             FileMode.Open,
                             FileAccess.Read,
                             FileShare.Read,
                             bufferSize: 64 * 1024,
                             useAsync: true))
            {
                image = await _decoder.DecodeAsync(stream, new DecodeOptions(), cancellationToken);
            }
            if (image.Size != OpenCvFaceAligner.AlignedSize)
            {
                throw new PortableBundleValidationException(
                    $"Face-crop input {index + 1} must be {OpenCvFaceAligner.AlignedSize.Width}x" +
                    $"{OpenCvFaceAligner.AlignedSize.Height}, but is {image.Size.Width}x{image.Size.Height}.");
            }

            string normalizedPath = Path.Combine(exportDirectory, $"face-{index + 1:000}.png");
            await EncodeAsync(image, normalizedPath, cancellationToken);
            inputs.Add(new PortableJobInput(
                normalizedPath,
                $"inputs/faces/face-{index + 1:000}.png",
                PortableBundleRoles.FaceCrop));
        }

        return inputs;
    }

    private static void ValidateOptions(PortableBundleExportOptions options)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(options.BundlePath);
        ArgumentException.ThrowIfNullOrWhiteSpace(options.WorkingDirectory);
        _ = new PortableRecognitionConfiguration(options.ConfidenceThreshold);
        if (options.ReducedMaximumWidth <= 0 || options.ReducedMaximumHeight <= 0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(options),
                "Reduced-image maximum dimensions must be positive.");
        }

        int cropCount = options.FaceCropPaths?.Count ?? 0;
        if (options.Profile == PortableBundleProfile.FaceCrops && cropCount == 0)
        {
            throw new ArgumentException("Face-crop export requires at least one crop path.", nameof(options));
        }
        if (options.Profile != PortableBundleProfile.FaceCrops && cropCount != 0)
        {
            throw new ArgumentException("Crop paths may be supplied only for the face-crops profile.", nameof(options));
        }
    }

    private static async Task<string> ResolveAndVerifySourceAsync(
        CatalogueProcessingAssetRevision asset,
        CancellationToken cancellationToken)
    {
        string root = Path.GetFullPath(asset.RootLocator);
        string platformKey = asset.SourceKey
            .Replace('/', Path.DirectorySeparatorChar)
            .Replace('\\', Path.DirectorySeparatorChar);
        string resolved = Path.GetFullPath(Path.Combine(root, platformKey));
        if (!IsWithin(resolved, root))
        {
            throw new PortableBundleValidationException(
                "The catalogued source key resolves outside its configured source root.");
        }
        if (!File.Exists(resolved))
        {
            throw new FileNotFoundException("The catalogued source file is unavailable.", resolved);
        }

        Sha256Digest actualHash = await ComputeHashAsync(resolved, cancellationToken);
        if (actualHash != asset.ContentHash)
        {
            throw new PortableBundleValidationException(
                $"The source content changed after revision {asset.RevisionId} was catalogued; rescan before exporting.");
        }

        return resolved;
    }

    private async Task EncodeAsync(
        ImageFrame image,
        string path,
        CancellationToken cancellationToken)
    {
        string? directory = Path.GetDirectoryName(path);
        if (!string.IsNullOrEmpty(directory))
        {
            Directory.CreateDirectory(directory);
        }

        await using FileStream stream = new(
            path,
            FileMode.Create,
            FileAccess.Write,
            FileShare.None,
            bufferSize: 64 * 1024,
            useAsync: true);
        await _encoder.EncodeAsync(image, stream, cancellationToken);
        await stream.FlushAsync(cancellationToken);
    }

    private static bool IsWithin(string path, string root)
    {
        string fullPath = Path.GetFullPath(path);
        string fullRoot = Path.GetFullPath(root);
        StringComparison comparison = OperatingSystem.IsWindows()
            ? StringComparison.OrdinalIgnoreCase
            : StringComparison.Ordinal;
        if (string.Equals(fullPath, fullRoot, comparison))
        {
            return true;
        }

        string prefix = fullRoot.EndsWith(Path.DirectorySeparatorChar)
            ? fullRoot
            : fullRoot + Path.DirectorySeparatorChar;
        return fullPath.StartsWith(prefix, comparison);
    }

    private static string NormalizedImageExtension(string path) =>
        Path.GetExtension(path).ToLowerInvariant() switch
        {
            ".jpeg" => ".jpg",
            ".jpg" => ".jpg",
            ".png" => ".png",
            _ => throw new PortableBundleValidationException(
                "Only JPEG and PNG source revisions can be exported as image bundles."),
        };

    private static async Task<Sha256Digest> ComputeHashAsync(
        string path,
        CancellationToken cancellationToken)
    {
        await using FileStream stream = new(
            path,
            FileMode.Open,
            FileAccess.Read,
            FileShare.Read,
            bufferSize: 64 * 1024,
            useAsync: true);
        byte[] hash = await SHA256.HashDataAsync(stream, cancellationToken);
        return new Sha256Digest(Convert.ToHexString(hash).ToLowerInvariant());
    }
}
