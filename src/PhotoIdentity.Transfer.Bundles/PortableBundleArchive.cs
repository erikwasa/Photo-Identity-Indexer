using System.IO.Compression;
using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Json.Serialization;
using PhotoIdentity.Core.Identifiers;

namespace PhotoIdentity.Transfer.Bundles;

public static class PortableBundleArchive
{
    public const int CurrentSchemaVersion = 1;
    public const string ManifestEntryName = "manifest.json";

    private static readonly DateTimeOffset ArchiveTimestamp = new(1980, 1, 1, 0, 0, 0, TimeSpan.Zero);

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = false,
        Converters = { new JsonStringEnumConverter(JsonNamingPolicy.CamelCase) },
    };

    public static async Task<PortableJobManifest> CreateJobAsync(
        string bundlePath,
        PortableJobBundleRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(bundlePath);
        ArgumentNullException.ThrowIfNull(request);
        ValidateJobInputs(request.Profile, request.Inputs);

        List<(PortableBundleFile Descriptor, string SourcePath)> files = [];
        foreach (PortableJobInput input in request.Inputs)
        {
            string sourcePath = Path.GetFullPath(input.SourcePath);
            if (!File.Exists(sourcePath))
            {
                throw new FileNotFoundException("Bundle input was not found.", sourcePath);
            }

            string bundleFilePath = PortableBundlePath.Normalize(input.BundlePath);
            FileInfo info = new(sourcePath);
            Sha256Digest hash = await ComputeHashAsync(sourcePath, cancellationToken);
            files.Add((new PortableBundleFile(bundleFilePath, Required(input.Role, nameof(input.Role)), info.Length, hash.ToString()), sourcePath));
        }

        EnsureDistinctPaths(files.Select(file => file.Descriptor.Path));
        PortableJobManifest manifest = new(
            CurrentSchemaVersion,
            request.BundleId is null ? Guid.NewGuid().ToString("D") : Required(request.BundleId, nameof(request.BundleId)),
            request.AssetRevisionId.ToString(),
            request.SourceContentHash.ToString(),
            request.Profile,
            string.IsNullOrWhiteSpace(request.ConfigurationJson) ? "{}" : request.ConfigurationJson,
            request.CreatedAtUtc.ToUniversalTime(),
            files.Select(file => file.Descriptor).OrderBy(file => file.Path, StringComparer.Ordinal).ToArray());
        ValidateJobManifest(manifest);

        byte[] manifestBytes = JsonSerializer.SerializeToUtf8Bytes(manifest, JsonOptions);
        await WriteArchiveAsync(bundlePath, manifestBytes, files, cancellationToken);
        return manifest;
    }

    public static async Task<ExtractedPortableJob> ExtractJobAsync(
        string bundlePath,
        string destinationDirectory,
        CancellationToken cancellationToken = default)
    {
        (PortableJobManifest manifest, Sha256Digest manifestHash) = await ReadAndExtractAsync<PortableJobManifest>(
            bundlePath,
            destinationDirectory,
            static manifest => manifest.Files,
            ValidateJobManifest,
            cancellationToken);
        return new ExtractedPortableJob(manifest, manifestHash, Path.GetFullPath(destinationDirectory));
    }

    public static async Task<PortableResultManifest> CreateResultAsync(
        string bundlePath,
        ExtractedPortableJob job,
        PortableProcessingOutput output,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(bundlePath);
        ArgumentNullException.ThrowIfNull(job);
        ArgumentNullException.ThrowIfNull(output);

        List<(PortableBundleFile Descriptor, string SourcePath)> files = [];
        List<PortableFaceResult> faces = [];
        foreach (PortableProcessedFace face in output.Faces.OrderBy(face => face.Ordinal))
        {
            ArgumentOutOfRangeException.ThrowIfNegative(face.Ordinal);
            if (!double.IsFinite(face.Confidence) || face.Confidence is < 0 or > 1)
            {
                throw new PortableBundleValidationException("Face confidence must be between zero and one.");
            }
            if (face.Embedding.Count == 0 || face.Embedding.Any(value => !float.IsFinite(value)))
            {
                throw new PortableBundleValidationException("Face embeddings must contain finite components.");
            }

            string sourcePath = Path.GetFullPath(face.CropPath);
            if (!File.Exists(sourcePath))
            {
                throw new FileNotFoundException("A processed face crop was not found.", sourcePath);
            }
            string archivePath = $"results/faces/face-{face.Ordinal + 1:000}/crop{Path.GetExtension(sourcePath).ToLowerInvariant()}";
            FileInfo info = new(sourcePath);
            Sha256Digest hash = await ComputeHashAsync(sourcePath, cancellationToken);
            PortableBundleFile descriptor = new(archivePath, PortableBundleRoles.ResultCrop, info.Length, hash.ToString());
            files.Add((descriptor, sourcePath));
            faces.Add(new PortableFaceResult(
                face.Ordinal,
                face.Confidence,
                PortableBoundingBox.FromCore(face.BoundingBox),
                PortableLandmarks.FromCore(face.Landmarks),
                archivePath,
                face.CropWidth,
                face.CropHeight,
                face.Embedding.ToArray()));
        }

        EnsureDistinctPaths(files.Select(file => file.Descriptor.Path));
        PortableResultManifest manifest = new(
            CurrentSchemaVersion,
            job.Manifest.BundleId,
            job.ManifestHash.ToString(),
            job.Manifest.AssetRevisionId,
            job.Manifest.SourceContentSha256,
            job.Manifest.Profile,
            output.DetectorModelId.ToString(),
            output.DetectorModelHash.ToString(),
            output.EmbedderModelId.ToString(),
            output.EmbedderModelHash.ToString(),
            output.AlignmentProtocol.ToString(),
            output.CompletedAtUtc.ToUniversalTime(),
            files.Select(file => file.Descriptor).OrderBy(file => file.Path, StringComparer.Ordinal).ToArray(),
            faces);
        ValidateResultManifest(manifest);

        byte[] manifestBytes = JsonSerializer.SerializeToUtf8Bytes(manifest, JsonOptions);
        await WriteArchiveAsync(bundlePath, manifestBytes, files, cancellationToken);
        return manifest;
    }

    public static async Task<ExtractedPortableResult> ExtractResultAsync(
        string bundlePath,
        string destinationDirectory,
        CancellationToken cancellationToken = default)
    {
        (PortableResultManifest manifest, Sha256Digest manifestHash) = await ReadAndExtractAsync<PortableResultManifest>(
            bundlePath,
            destinationDirectory,
            static manifest => manifest.Files,
            ValidateResultManifest,
            cancellationToken);
        return new ExtractedPortableResult(manifest, manifestHash, Path.GetFullPath(destinationDirectory));
    }

    private static async Task WriteArchiveAsync(
        string bundlePath,
        byte[] manifestBytes,
        IReadOnlyList<(PortableBundleFile Descriptor, string SourcePath)> files,
        CancellationToken cancellationToken)
    {
        string fullPath = Path.GetFullPath(bundlePath);
        string? directory = Path.GetDirectoryName(fullPath);
        if (!string.IsNullOrEmpty(directory))
        {
            Directory.CreateDirectory(directory);
        }

        string temporaryPath = fullPath + $".{Guid.NewGuid():N}.tmp";
        try
        {
            await using FileStream stream = new(temporaryPath, FileMode.CreateNew, FileAccess.ReadWrite, FileShare.None, 64 * 1024, useAsync: true);
            using (ZipArchive archive = new(stream, ZipArchiveMode.Create, leaveOpen: true))
            {
                ZipArchiveEntry manifestEntry = archive.CreateEntry(ManifestEntryName, CompressionLevel.Optimal);
                manifestEntry.LastWriteTime = ArchiveTimestamp;
                await using (Stream entryStream = manifestEntry.Open())
                {
                    await entryStream.WriteAsync(manifestBytes, cancellationToken);
                }

                foreach ((PortableBundleFile descriptor, string sourcePath) in files.OrderBy(file => file.Descriptor.Path, StringComparer.Ordinal))
                {
                    ZipArchiveEntry entry = archive.CreateEntry(descriptor.Path, CompressionLevel.Optimal);
                    entry.LastWriteTime = ArchiveTimestamp;
                    await using Stream input = new FileStream(sourcePath, FileMode.Open, FileAccess.Read, FileShare.Read, 64 * 1024, useAsync: true);
                    await using Stream output = entry.Open();
                    await input.CopyToAsync(output, cancellationToken);
                }
            }
            await stream.FlushAsync(cancellationToken);
            await stream.DisposeAsync();
            File.Move(temporaryPath, fullPath, overwrite: true);
        }
        finally
        {
            if (File.Exists(temporaryPath))
            {
                File.Delete(temporaryPath);
            }
        }
    }

    private static async Task<(TManifest Manifest, Sha256Digest ManifestHash)> ReadAndExtractAsync<TManifest>(
        string bundlePath,
        string destinationDirectory,
        Func<TManifest, IReadOnlyList<PortableBundleFile>> getFiles,
        Action<TManifest> validateManifest,
        CancellationToken cancellationToken)
        where TManifest : class
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(bundlePath);
        ArgumentException.ThrowIfNullOrWhiteSpace(destinationDirectory);
        string fullBundlePath = Path.GetFullPath(bundlePath);
        if (!File.Exists(fullBundlePath))
        {
            throw new FileNotFoundException("Portable bundle was not found.", fullBundlePath);
        }

        ResetDirectory(destinationDirectory);
        await using FileStream stream = new(fullBundlePath, FileMode.Open, FileAccess.Read, FileShare.Read, 64 * 1024, useAsync: true);
        using ZipArchive archive = new(stream, ZipArchiveMode.Read, leaveOpen: true);
        ZipArchiveEntry[] manifestEntries = archive.Entries
            .Where(entry => string.Equals(entry.FullName, ManifestEntryName, StringComparison.Ordinal))
            .ToArray();
        if (manifestEntries.Length != 1)
        {
            throw new PortableBundleValidationException("Bundle must contain exactly one manifest.");
        }
        ZipArchiveEntry manifestEntry = manifestEntries[0];
        byte[] manifestBytes;
        await using (Stream manifestStream = manifestEntry.Open())
        await using (MemoryStream buffer = new())
        {
            await manifestStream.CopyToAsync(buffer, cancellationToken);
            manifestBytes = buffer.ToArray();
        }

        TManifest manifest;
        try
        {
            manifest = JsonSerializer.Deserialize<TManifest>(manifestBytes, JsonOptions)
                ?? throw new PortableBundleValidationException("Bundle manifest is empty.");
        }
        catch (JsonException exception)
        {
            throw new PortableBundleValidationException("Bundle manifest is invalid JSON.", exception);
        }
        validateManifest(manifest);
        IReadOnlyList<PortableBundleFile> files = getFiles(manifest);
        EnsureDistinctPaths(files.Select(file => file.Path));

        HashSet<string> expectedEntries = new(files.Select(file => file.Path), StringComparer.Ordinal)
        {
            ManifestEntryName,
        };
        if (archive.Entries.GroupBy(entry => entry.FullName, StringComparer.Ordinal).Any(group => group.Count() != 1))
        {
            throw new PortableBundleValidationException("Bundle contains duplicate archive entries.");
        }
        foreach (ZipArchiveEntry entry in archive.Entries)
        {
            if (!expectedEntries.Contains(entry.FullName))
            {
                throw new PortableBundleValidationException($"Bundle contains undeclared entry '{entry.FullName}'.");
            }
        }

        foreach (PortableBundleFile file in files)
        {
            ZipArchiveEntry entry = archive.GetEntry(file.Path)
                ?? throw new PortableBundleValidationException($"Bundle entry '{file.Path}' is missing.");
            string destinationPath = PortableBundlePath.ResolveWithinRoot(destinationDirectory, file.Path);
            string? outputDirectory = Path.GetDirectoryName(destinationPath);
            if (!string.IsNullOrEmpty(outputDirectory))
            {
                Directory.CreateDirectory(outputDirectory);
            }

            await using Stream input = entry.Open();
            await using FileStream output = new(destinationPath, FileMode.CreateNew, FileAccess.Write, FileShare.None, 64 * 1024, useAsync: true);
            using IncrementalHash hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
            byte[] copyBuffer = new byte[64 * 1024];
            long length = 0;
            while (true)
            {
                int read = await input.ReadAsync(copyBuffer, cancellationToken);
                if (read == 0)
                {
                    break;
                }
                await output.WriteAsync(copyBuffer.AsMemory(0, read), cancellationToken);
                hash.AppendData(copyBuffer, 0, read);
                length += read;
            }
            await output.FlushAsync(cancellationToken);
            string actualHash = Convert.ToHexString(hash.GetHashAndReset()).ToLowerInvariant();
            if (length != file.Length || !string.Equals(actualHash, file.Sha256, StringComparison.OrdinalIgnoreCase))
            {
                throw new PortableBundleValidationException($"Bundle entry '{file.Path}' failed length or SHA-256 verification.");
            }
        }

        return (manifest, Digest(manifestBytes));
    }

    private static void ValidateJobInputs(PortableBundleProfile profile, IReadOnlyList<PortableJobInput> inputs)
    {
        ArgumentNullException.ThrowIfNull(inputs);
        if (inputs.Count == 0)
        {
            throw new PortableBundleValidationException("A portable job must contain at least one input.");
        }

        int sourceImages = inputs.Count(input => input.Role == PortableBundleRoles.SourceImage);
        int reducedImages = inputs.Count(input => input.Role == PortableBundleRoles.ReducedImage);
        int faceCrops = inputs.Count(input => input.Role == PortableBundleRoles.FaceCrop);
        bool valid = profile switch
        {
            PortableBundleProfile.FullImage => sourceImages == 1 && inputs.Count == 1,
            PortableBundleProfile.ReducedImage => reducedImages == 1 && inputs.Count == 1,
            PortableBundleProfile.FaceCrops => faceCrops == inputs.Count && faceCrops > 0,
            _ => false,
        };
        if (!valid)
        {
            throw new PortableBundleValidationException($"Input roles do not match the '{profile}' bundle profile.");
        }
    }

    private static void ValidateJobManifest(PortableJobManifest manifest)
    {
        if (manifest.SchemaVersion != CurrentSchemaVersion || !Guid.TryParse(manifest.BundleId, out Guid bundleId) || bundleId == Guid.Empty)
        {
            throw new PortableBundleValidationException("Job manifest schema or bundle identifier is invalid.");
        }
        _ = ParseRevisionId(manifest.AssetRevisionId);
        Sha256Digest sourceHash = new(manifest.SourceContentSha256);
        try
        {
            using JsonDocument _ = JsonDocument.Parse(manifest.ConfigurationJson);
        }
        catch (JsonException exception)
        {
            throw new PortableBundleValidationException("Job configuration is not valid JSON.", exception);
        }
        ValidateFiles(manifest.Files);
        ValidateJobInputs(manifest.Profile, manifest.Files.Select(file => new PortableJobInput(file.Path, file.Path, file.Role)).ToArray());
        if (manifest.Profile == PortableBundleProfile.FullImage &&
            !string.Equals(manifest.Files[0].Sha256, sourceHash.ToString(), StringComparison.OrdinalIgnoreCase))
        {
            throw new PortableBundleValidationException("Full-image payload hash does not match the immutable source content hash.");
        }
    }

    private static void ValidateResultManifest(PortableResultManifest manifest)
    {
        if (manifest.SchemaVersion != CurrentSchemaVersion || !Guid.TryParse(manifest.BundleId, out Guid bundleId) || bundleId == Guid.Empty)
        {
            throw new PortableBundleValidationException("Result manifest schema or bundle identifier is invalid.");
        }
        _ = ParseRevisionId(manifest.AssetRevisionId);
        _ = new Sha256Digest(manifest.SourceContentSha256);
        _ = new Sha256Digest(manifest.JobManifestSha256);
        _ = new Sha256Digest(manifest.DetectorModelSha256);
        _ = new Sha256Digest(manifest.EmbedderModelSha256);
        _ = new ModelId(manifest.DetectorModelId);
        _ = new ModelId(manifest.EmbedderModelId);
        _ = new AlignmentProtocolId(manifest.AlignmentProtocol);
        ValidateFiles(manifest.Files);
        if (manifest.Files.Any(file => file.Role != PortableBundleRoles.ResultCrop))
        {
            throw new PortableBundleValidationException("Result bundles may contain only declared result crops.");
        }
        HashSet<string> filePaths = new(manifest.Files.Select(file => file.Path), StringComparer.Ordinal);
        HashSet<string> referencedPaths = new(StringComparer.Ordinal);
        HashSet<int> ordinals = [];
        foreach (PortableFaceResult face in manifest.Faces)
        {
            if (!ordinals.Add(face.Ordinal) || face.Ordinal < 0 ||
                !filePaths.Contains(face.CropPath) || !referencedPaths.Add(face.CropPath))
            {
                throw new PortableBundleValidationException("Result faces contain duplicate ordinals, crop references or undeclared crops.");
            }
            _ = face.BoundingBox.ToCore();
            _ = face.Landmarks.ToCore();
            if (!double.IsFinite(face.Confidence) || face.Confidence is < 0 or > 1 || face.CropWidth <= 0 || face.CropHeight <= 0)
            {
                throw new PortableBundleValidationException("Result face metadata is invalid.");
            }
            if (face.Embedding.Count == 0 || face.Embedding.Any(value => !float.IsFinite(value)))
            {
                throw new PortableBundleValidationException("Result embedding is invalid.");
            }
        }
        if (filePaths.Count != manifest.Faces.Count)
        {
            throw new PortableBundleValidationException("Every result crop must belong to exactly one face result.");
        }
    }

    private static void ValidateFiles(IReadOnlyList<PortableBundleFile> files)
    {
        ArgumentNullException.ThrowIfNull(files);
        foreach (PortableBundleFile file in files)
        {
            string normalizedPath = PortableBundlePath.Normalize(file.Path);
            if (!string.Equals(normalizedPath, file.Path, StringComparison.Ordinal))
            {
                throw new PortableBundleValidationException($"Bundle path '{file.Path}' is not canonical.");
            }
            _ = Required(file.Role, nameof(file.Role));
            if (file.Length < 0)
            {
                throw new PortableBundleValidationException("Bundle file length cannot be negative.");
            }
            _ = new Sha256Digest(file.Sha256);
        }
        EnsureDistinctPaths(files.Select(file => file.Path));
    }

    private static void EnsureDistinctPaths(IEnumerable<string> paths)
    {
        HashSet<string> unique = new(StringComparer.Ordinal);
        HashSet<string> portableUnique = new(StringComparer.OrdinalIgnoreCase);
        foreach (string path in paths.Select(PortableBundlePath.Normalize))
        {
            if (path == ManifestEntryName || !unique.Add(path) || !portableUnique.Add(path))
            {
                throw new PortableBundleValidationException($"Bundle path '{path}' is duplicated, non-portable or reserved.");
            }
        }
    }

    private static AssetRevisionId ParseRevisionId(string value) =>
        Guid.TryParse(value, out Guid parsed) && parsed != Guid.Empty
            ? AssetRevisionId.From(parsed)
            : throw new PortableBundleValidationException("Asset revision identifier is invalid.");

    private static async Task<Sha256Digest> ComputeHashAsync(string path, CancellationToken cancellationToken)
    {
        await using FileStream stream = new(path, FileMode.Open, FileAccess.Read, FileShare.Read, 64 * 1024, useAsync: true);
        byte[] hash = await SHA256.HashDataAsync(stream, cancellationToken);
        return new Sha256Digest(Convert.ToHexString(hash).ToLowerInvariant());
    }

    private static Sha256Digest Digest(ReadOnlySpan<byte> bytes) =>
        new(Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant());

    private static string Required(string value, string parameterName)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(value, parameterName);
        return value.Trim();
    }

    private static void ResetDirectory(string directory)
    {
        string fullPath = Path.GetFullPath(directory);
        if (Directory.Exists(fullPath))
        {
            Directory.Delete(fullPath, recursive: true);
        }
        Directory.CreateDirectory(fullPath);
    }
}
