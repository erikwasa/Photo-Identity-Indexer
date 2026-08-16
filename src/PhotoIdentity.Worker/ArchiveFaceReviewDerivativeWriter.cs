using System.Security.Cryptography;
using PhotoIdentity.Core.Identifiers;
using PhotoIdentity.Core.Recognition;
using PhotoIdentity.Imaging.OpenCv;
using PhotoIdentity.Persistence.Sqlite;

namespace PhotoIdentity.Worker;

/// <summary>
/// Generates one durable, high-quality contextual face-review derivative per detected face from
/// the authoritative full-resolution revision while that revision is available locally.
/// </summary>
public sealed class ArchiveFaceReviewDerivativeWriter
{
    public const string ProfileId = "face-review-v1-context2.2-max960-q90";
    public const int MaximumLongEdge = 960;

    private readonly SqliteFaceReviewDerivativeRepository _repository;
    private readonly OpenCvReviewFaceRenderer _renderer;

    public ArchiveFaceReviewDerivativeWriter(SqliteCatalogueDatabase database)
        : this(new SqliteFaceReviewDerivativeRepository(database), new OpenCvReviewFaceRenderer())
    {
    }

    public ArchiveFaceReviewDerivativeWriter(
        SqliteFaceReviewDerivativeRepository repository,
        OpenCvReviewFaceRenderer renderer)
    {
        ArgumentNullException.ThrowIfNull(repository);
        ArgumentNullException.ThrowIfNull(renderer);
        _repository = repository;
        _renderer = renderer;
    }

    public async Task<int> GenerateAsync(
        AssetRevisionId revisionId,
        string sourcePath,
        string sourceRoot,
        string derivativeRoot,
        DateTimeOffset generatedAtUtc,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(sourcePath);
        ArgumentException.ThrowIfNullOrWhiteSpace(sourceRoot);
        ArgumentException.ThrowIfNullOrWhiteSpace(derivativeRoot);

        if (await _repository.IsRevisionCompleteAsync(revisionId, ProfileId, cancellationToken))
        {
            return 0;
        }

        string normalizedSourceRoot = Path.GetFullPath(sourceRoot);
        string normalizedSourcePath = Path.GetFullPath(sourcePath);
        string normalizedDerivativeRoot = Path.GetFullPath(derivativeRoot);
        EnsurePathInsideRoot(normalizedSourcePath, normalizedSourceRoot, nameof(sourcePath));
        EnsureRootsAreSeparate(normalizedSourceRoot, normalizedDerivativeRoot);

        IReadOnlyList<FaceReviewGeometry> faces = await _repository.GetFacesAsync(revisionId, cancellationToken);
        if (faces.Count == 0)
        {
            await _repository.RecordRevisionCompletionAsync(
                revisionId,
                ProfileId,
                [],
                generatedAtUtc,
                cancellationToken);
            return 0;
        }

        byte[] sourceBytes = await File.ReadAllBytesAsync(normalizedSourcePath, cancellationToken);
        IReadOnlyList<EncodedReviewFace?> encodedFaces = _renderer.RenderMany(
            sourceBytes,
            faces.Select(face => face.BoundingBox).ToArray(),
            MaximumLongEdge,
            cancellationToken);

        List<FaceReviewDerivativeRecord> records = new(faces.Count);
        for (int index = 0; index < faces.Count; index++)
        {
            EncodedReviewFace encoded = encodedFaces[index]
                ?? throw new InvalidDataException(
                    $"Face {faces[index].FaceOccurrenceId} could not be rendered from the full-resolution original.");
            Sha256Digest hash = new(
                Convert.ToHexString(SHA256.HashData(encoded.Content)).ToLowerInvariant());
            string relativePath = BuildRelativePath(faces[index].FaceOccurrenceId);
            string destination = ResolveDerivativePath(normalizedDerivativeRoot, relativePath);
            await WriteAtomicallyAsync(destination, encoded.Content, cancellationToken);
            records.Add(new FaceReviewDerivativeRecord(
                faces[index].FaceOccurrenceId,
                ProfileId,
                encoded.Content.LongLength,
                hash,
                encoded.Width,
                encoded.Height,
                generatedAtUtc,
                relativePath));
        }

        await _repository.RecordRevisionCompletionAsync(
            revisionId,
            ProfileId,
            records,
            generatedAtUtc,
            cancellationToken);
        return records.Count;
    }

    private static string BuildRelativePath(FaceOccurrenceId faceOccurrenceId) =>
        Path.Combine("face-review", ProfileId, $"{faceOccurrenceId}.jpg");

    private static string ResolveDerivativePath(string derivativeRoot, string relativePath)
    {
        string fullPath = Path.GetFullPath(Path.Combine(derivativeRoot, relativePath));
        EnsurePathInsideRoot(fullPath, derivativeRoot, nameof(relativePath));
        return fullPath;
    }

    private static void EnsureRootsAreSeparate(string sourceRoot, string derivativeRoot)
    {
        StringComparison comparison = PathComparison;
        if (derivativeRoot.Equals(sourceRoot, comparison) ||
            derivativeRoot.StartsWith(EnsureTrailingSeparator(sourceRoot), comparison))
        {
            throw new ArgumentException(
                "Face review derivative root must be outside the authoritative source root.",
                nameof(derivativeRoot));
        }
    }

    private static void EnsurePathInsideRoot(string path, string root, string parameterName)
    {
        StringComparison comparison = PathComparison;
        if (!path.Equals(root, comparison) &&
            !path.StartsWith(EnsureTrailingSeparator(root), comparison))
        {
            throw new ArgumentException("Path must remain inside its configured root.", parameterName);
        }
    }

    private static async Task WriteAtomicallyAsync(
        string destination,
        byte[] content,
        CancellationToken cancellationToken)
    {
        string? directory = Path.GetDirectoryName(destination);
        if (!string.IsNullOrWhiteSpace(directory))
        {
            Directory.CreateDirectory(directory);
        }

        string temporary = destination + ".tmp-" + Guid.NewGuid().ToString("N");
        try
        {
            await File.WriteAllBytesAsync(temporary, content, cancellationToken);
            File.Move(temporary, destination, overwrite: true);
        }
        finally
        {
            if (File.Exists(temporary))
            {
                File.Delete(temporary);
            }
        }
    }

    private static StringComparison PathComparison => OperatingSystem.IsWindows()
        ? StringComparison.OrdinalIgnoreCase
        : StringComparison.Ordinal;

    private static string EnsureTrailingSeparator(string path) =>
        path.EndsWith(Path.DirectorySeparatorChar)
            ? path
            : path + Path.DirectorySeparatorChar;
}
