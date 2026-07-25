using System.Security.Cryptography;

namespace PhotoIdentity.Recognition.Onnx.Models;

public sealed record ModelFileVerification(
    bool Exists,
    bool SizeMatches,
    bool HashMatches,
    long? ActualSizeBytes,
    string? ActualSha256)
{
    public bool IsValid => Exists && SizeMatches && HashMatches;
}

public sealed class ModelIntegrityException : Exception
{
    public ModelIntegrityException(string message)
        : base(message)
    {
    }
}

public sealed class ModelFileVerifier
{
    public async Task<ModelFileVerification> VerifyAsync(
        string path,
        ModelManifest manifest,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        ArgumentNullException.ThrowIfNull(manifest);
        ModelManifestValidator.Validate(manifest);

        if (!File.Exists(path))
        {
            return new ModelFileVerification(
                Exists: false,
                SizeMatches: false,
                HashMatches: false,
                ActualSizeBytes: null,
                ActualSha256: null);
        }

        FileInfo file = new(path);
        string actualHash;

        await using (FileStream stream = new(
                         path,
                         FileMode.Open,
                         FileAccess.Read,
                         FileShare.Read,
                         bufferSize: 128 * 1024,
                         useAsync: true))
        {
            using SHA256 sha256 = SHA256.Create();
            byte[] digest = await sha256.ComputeHashAsync(stream, cancellationToken);
            actualHash = Convert.ToHexStringLower(digest);
        }

        return new ModelFileVerification(
            Exists: true,
            SizeMatches: file.Length == manifest.SizeBytes,
            HashMatches: string.Equals(
                actualHash,
                manifest.Sha256,
                StringComparison.OrdinalIgnoreCase),
            ActualSizeBytes: file.Length,
            ActualSha256: actualHash);
    }

    public async Task VerifyOrThrowAsync(
        string path,
        ModelManifest manifest,
        CancellationToken cancellationToken = default)
    {
        ModelFileVerification result = await VerifyAsync(path, manifest, cancellationToken);
        if (result.IsValid)
        {
            return;
        }

        throw new ModelIntegrityException(BuildFailureMessage(path, manifest, result));
    }

    private static string BuildFailureMessage(
        string path,
        ModelManifest manifest,
        ModelFileVerification result)
    {
        if (!result.Exists)
        {
            return $"Model '{manifest.ModelId}' is not installed at '{path}'.";
        }

        return
            $"Model '{manifest.ModelId}' at '{path}' failed integrity verification. " +
            $"Expected {manifest.SizeBytes} bytes and SHA-256 {manifest.Sha256}; " +
            $"found {result.ActualSizeBytes} bytes and SHA-256 {result.ActualSha256}.";
    }
}
