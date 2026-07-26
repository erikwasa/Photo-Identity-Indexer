using System.Security.Cryptography;
using PhotoIdentity.Core.Identifiers;
using PhotoIdentity.Core.Recognition;
using PhotoIdentity.Transfer.Bundles;

namespace PhotoIdentity.Persistence.Sqlite;

public sealed record PortableBundleImportResult(
    string BundleId,
    AssetRevisionId AssetRevisionId,
    int ImportedFaceCount);

/// <summary>
/// Imports verified portable result bundles without changing people or human-label tables.
/// </summary>
public sealed class SqliteBundleResultImporter
{
    private readonly SqliteLocalBatchRepository _assetRepository;
    private readonly SqliteFaceCatalogueRepository _faceRepository;

    public SqliteBundleResultImporter(SqliteCatalogueDatabase database)
    {
        ArgumentNullException.ThrowIfNull(database);
        _assetRepository = new SqliteLocalBatchRepository(database);
        _faceRepository = new SqliteFaceCatalogueRepository(database);
    }

    public async Task<PortableBundleImportResult> ImportAsync(
        string jobBundlePath,
        string resultBundlePath,
        string outputRoot,
        string workingRoot,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(jobBundlePath);
        ArgumentException.ThrowIfNullOrWhiteSpace(resultBundlePath);
        ArgumentException.ThrowIfNullOrWhiteSpace(outputRoot);
        ArgumentException.ThrowIfNullOrWhiteSpace(workingRoot);

        string importDirectory = Path.Combine(
            Path.GetFullPath(workingRoot),
            $"import-{Guid.NewGuid():N}");
        string jobDirectory = Path.Combine(importDirectory, "job");
        string resultDirectory = Path.Combine(importDirectory, "result");
        try
        {
            ExtractedPortableJob job = await PortableBundleArchive.ExtractJobAsync(
                jobBundlePath,
                jobDirectory,
                cancellationToken);
            ExtractedPortableResult extracted = await PortableBundleArchive.ExtractResultAsync(
                resultBundlePath,
                resultDirectory,
                cancellationToken);
            PortableResultManifest manifest = extracted.Manifest;
            ValidateJobLink(job, manifest);

            AssetRevisionId revisionId = ParseRevisionId(manifest.AssetRevisionId);
            CatalogueProcessingAssetRevision canonicalRevision = await _assetRepository.GetAssetRevisionAsync(
                revisionId,
                cancellationToken)
                ?? throw new PortableBundleValidationException(
                    $"Result bundle targets unknown asset revision {revisionId}.");
            Sha256Digest expectedContentHash = new(manifest.SourceContentSha256);
            if (canonicalRevision.ContentHash != expectedContentHash)
            {
                throw new PortableBundleValidationException(
                    $"Result bundle is stale for asset revision {revisionId}; the canonical content hash differs.");
            }

            Dictionary<string, PortableBundleFile> files = manifest.Files.ToDictionary(
                file => file.Path,
                StringComparer.Ordinal);
            foreach (PortableFaceResult face in manifest.Faces.OrderBy(face => face.Ordinal))
            {
                PortableBundleFile cropFile = files.GetValueOrDefault(face.CropPath)
                    ?? throw new PortableBundleValidationException(
                        $"Face {face.Ordinal} does not reference a declared result crop.");
                if (cropFile.Role != PortableBundleRoles.ResultCrop)
                {
                    throw new PortableBundleValidationException(
                        $"Face {face.Ordinal} does not reference a declared result crop.");
                }

                string extractedCropPath = extracted.ResolveFile(cropFile);
                string extension = Path.GetExtension(extractedCropPath).ToLowerInvariant();
                string storagePath = Path.Combine(
                    Path.GetFullPath(outputRoot),
                    "bundle-imports",
                    manifest.BundleId,
                    revisionId.ToString(),
                    "faces",
                    $"face-{face.Ordinal + 1:000}",
                    $"{cropFile.Sha256}{extension}");
                await CopyAtomicallyAsync(extractedCropPath, storagePath, cropFile, cancellationToken);

                DateTimeOffset createdAt = manifest.CompletedAtUtc.ToUniversalTime();
                FaceOccurrenceId occurrenceId = FaceOccurrenceId.New();
                FaceCropId cropId = FaceCropId.New();
                _ = await _faceRepository.SaveInspectionAsync(
                    new CatalogueFaceOccurrence(
                        occurrenceId,
                        revisionId,
                        face.Ordinal,
                        createdAt),
                    new CatalogueFaceObservation(
                        occurrenceId,
                        new ModelId(manifest.DetectorModelId),
                        new Sha256Digest(manifest.DetectorModelSha256),
                        face.Confidence,
                        face.BoundingBox.ToCore(),
                        face.Landmarks.ToCore(),
                        createdAt),
                    new CatalogueFaceCrop(
                        cropId,
                        occurrenceId,
                        new AlignmentProtocolId(manifest.AlignmentProtocol),
                        new Sha256Digest(cropFile.Sha256),
                        storagePath,
                        face.CropWidth,
                        face.CropHeight,
                        createdAt),
                    new CatalogueFaceEmbedding(
                        cropId,
                        new ModelId(manifest.EmbedderModelId),
                        new Sha256Digest(manifest.EmbedderModelSha256),
                        new EmbeddingVector(face.Embedding.ToArray()),
                        createdAt),
                    cancellationToken);
            }

            return new PortableBundleImportResult(
                manifest.BundleId,
                revisionId,
                manifest.Faces.Count);
        }
        finally
        {
            if (Directory.Exists(importDirectory))
            {
                Directory.Delete(importDirectory, recursive: true);
            }
        }
    }

    private static void ValidateJobLink(
        ExtractedPortableJob job,
        PortableResultManifest result)
    {
        if (!string.Equals(result.BundleId, job.Manifest.BundleId, StringComparison.Ordinal) ||
            !string.Equals(result.JobManifestSha256, job.ManifestHash.ToString(), StringComparison.OrdinalIgnoreCase) ||
            !string.Equals(result.AssetRevisionId, job.Manifest.AssetRevisionId, StringComparison.Ordinal) ||
            !string.Equals(result.SourceContentSha256, job.Manifest.SourceContentSha256, StringComparison.OrdinalIgnoreCase) ||
            result.Profile != job.Manifest.Profile)
        {
            throw new PortableBundleValidationException(
                "Result bundle does not match the supplied verified job manifest.");
        }
    }

    private static AssetRevisionId ParseRevisionId(string value) =>
        Guid.TryParse(value, out Guid parsed) && parsed != Guid.Empty
            ? AssetRevisionId.From(parsed)
            : throw new PortableBundleValidationException("Result asset revision identifier is invalid.");

    private static async Task CopyAtomicallyAsync(
        string sourcePath,
        string destinationPath,
        PortableBundleFile descriptor,
        CancellationToken cancellationToken)
    {
        string? directory = Path.GetDirectoryName(destinationPath);
        if (!string.IsNullOrEmpty(directory))
        {
            Directory.CreateDirectory(directory);
        }

        if (File.Exists(destinationPath))
        {
            FileInfo existingInfo = new(destinationPath);
            if (existingInfo.Length == descriptor.Length &&
                await ComputeHashAsync(destinationPath, cancellationToken) == new Sha256Digest(descriptor.Sha256))
            {
                return;
            }
            throw new PortableBundleValidationException(
                $"Existing imported crop '{destinationPath}' does not match the result bundle.");
        }

        string temporaryPath = destinationPath + $".{Guid.NewGuid():N}.tmp";
        try
        {
            await using FileStream input = new(sourcePath, FileMode.Open, FileAccess.Read, FileShare.Read, 64 * 1024, useAsync: true);
            await using FileStream output = new(temporaryPath, FileMode.CreateNew, FileAccess.Write, FileShare.None, 64 * 1024, useAsync: true);
            await input.CopyToAsync(output, cancellationToken);
            await output.FlushAsync(cancellationToken);
            File.Move(temporaryPath, destinationPath);
        }
        finally
        {
            if (File.Exists(temporaryPath))
            {
                File.Delete(temporaryPath);
            }
        }
    }

    private static async Task<Sha256Digest> ComputeHashAsync(
        string path,
        CancellationToken cancellationToken)
    {
        await using FileStream stream = new(path, FileMode.Open, FileAccess.Read, FileShare.Read, 64 * 1024, useAsync: true);
        byte[] hash = await SHA256.HashDataAsync(stream, cancellationToken);
        return new Sha256Digest(Convert.ToHexString(hash).ToLowerInvariant());
    }
}
