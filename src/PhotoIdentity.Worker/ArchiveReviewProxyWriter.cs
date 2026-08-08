using System.Security.Cryptography;
using PhotoIdentity.Core.Identifiers;
using PhotoIdentity.Core.Imaging;
using PhotoIdentity.Core.Recognition;
using PhotoIdentity.Imaging.OpenCv;
using PhotoIdentity.Persistence.Sqlite;

namespace PhotoIdentity.Worker;

/// <summary>
/// Generates and durably records one permanent review proxy for an immutable source revision.
/// Existing verified proxy completion is reused without reopening the authoritative original.
/// </summary>
public sealed class ArchiveReviewProxyWriter
{
    private readonly SqliteArchiveReviewProxyRepository _repository;
    private readonly OpenCvReviewProxyRenderer _renderer;

    public ArchiveReviewProxyWriter(SqliteCatalogueDatabase database)
        : this(new SqliteArchiveReviewProxyRepository(database), new OpenCvReviewProxyRenderer())
    {
    }

    public ArchiveReviewProxyWriter(
        SqliteArchiveReviewProxyRepository repository,
        OpenCvReviewProxyRenderer renderer)
    {
        ArgumentNullException.ThrowIfNull(repository);
        ArgumentNullException.ThrowIfNull(renderer);
        _repository = repository;
        _renderer = renderer;
    }

    public async Task<ArchiveReviewProxyRecord> GenerateAsync(
        AssetRevisionId revisionId,
        string sourcePath,
        string sourceRoot,
        string derivativeRoot,
        ReviewProxyProfile profile,
        DateTimeOffset generatedAtUtc,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(sourcePath);
        ArgumentException.ThrowIfNullOrWhiteSpace(sourceRoot);
        ArgumentException.ThrowIfNullOrWhiteSpace(derivativeRoot);
        ArgumentNullException.ThrowIfNull(profile);

        string normalizedSourceRoot = Path.GetFullPath(sourceRoot);
        string normalizedSourcePath = Path.GetFullPath(sourcePath);
        string normalizedDerivativeRoot = Path.GetFullPath(derivativeRoot);
        EnsurePathInsideRoot(normalizedSourcePath, normalizedSourceRoot, nameof(sourcePath));
        EnsureRootsAreSeparate(normalizedSourceRoot, normalizedDerivativeRoot);

        ArchiveReviewProxyRecord? existing = await _repository.GetAsync(
            revisionId,
            profile.Id,
            cancellationToken);
        if (existing is not null &&
            await IsStoredProxyValidAsync(existing, normalizedDerivativeRoot, cancellationToken))
        {
            return existing;
        }

        await _repository.RegisterProfileAsync(profile, generatedAtUtc, cancellationToken);
        EncodedReviewProxy encoded = await _renderer.RenderAsync(
            normalizedSourcePath,
            profile,
            cancellationToken);
        Sha256Digest hash = ComputeHash(encoded.Content);
        string relativePath = existing?.RelativePath ?? BuildRelativePath(revisionId, profile.Id);
        string destination = ResolveDerivativePath(normalizedDerivativeRoot, relativePath);

        await WriteAtomicallyAsync(destination, encoded.Content, cancellationToken);
        ArchiveReviewProxyRecord requested = new(
            revisionId,
            profile.Id,
            encoded.Content.LongLength,
            hash,
            encoded.Width,
            encoded.Height,
            generatedAtUtc,
            relativePath);

        return await _repository.RecordCompletionAsync(requested, cancellationToken);
    }

    private static async Task<bool> IsStoredProxyValidAsync(
        ArchiveReviewProxyRecord proxy,
        string derivativeRoot,
        CancellationToken cancellationToken)
    {
        string path = ResolveDerivativePath(derivativeRoot, proxy.RelativePath);
        FileInfo file = new(path);
        if (!file.Exists || file.Length != proxy.EncodedByteLength)
        {
            return false;
        }

        byte[] content = await File.ReadAllBytesAsync(path, cancellationToken);
        return ComputeHash(content) == proxy.ContentHash;
    }

    private static string BuildRelativePath(AssetRevisionId revisionId, string profileId) =>
        Path.Combine("review-proxies", profileId, $"{revisionId}.jpg");

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
                "Review proxy derivative root must be outside the authoritative source root.",
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

    private static Sha256Digest ComputeHash(byte[] content) =>
        new(Convert.ToHexString(SHA256.HashData(content)).ToLowerInvariant());

    private static StringComparison PathComparison => OperatingSystem.IsWindows()
        ? StringComparison.OrdinalIgnoreCase
        : StringComparison.Ordinal;

    private static string EnsureTrailingSeparator(string path) =>
        path.EndsWith(Path.DirectorySeparatorChar)
            ? path
            : path + Path.DirectorySeparatorChar;
}
