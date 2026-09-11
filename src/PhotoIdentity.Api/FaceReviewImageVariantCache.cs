using System.Globalization;
using PhotoIdentity.Core.Imaging;
using PhotoIdentity.Imaging.OpenCv;

namespace PhotoIdentity.Api;

/// <summary>
/// Materializes the fixed gallery-size response from an immutable durable face-review derivative
/// at most once per stored derivative profile. The cache lives beside the durable derivative under
/// the configured private derivative root and is never exposed as a filesystem path.
/// </summary>
public static class FaceReviewImageVariantCache
{
    public const int GalleryMaximumEdge = 360;
    private const string CacheProfileId = "response-cache-v1-q90";
    private const int GateCount = 64;
    private static readonly SemaphoreSlim[] Gates = CreateGates();

    public static async Task<EncodedReviewFace?> RenderAsync(
        FaceReviewDerivativeFile durable,
        int maximumEdge,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(durable);
        cancellationToken.ThrowIfCancellationRequested();

        if (maximumEdge >= Math.Max(durable.Width, durable.Height))
        {
            return await ReadStoredAsync(
                durable.Path,
                durable.Width,
                durable.Height,
                cancellationToken);
        }

        if (maximumEdge != GalleryMaximumEdge)
        {
            return await RenderDirectAsync(durable.Path, maximumEdge, cancellationToken);
        }

        string cachePath = BuildCachePath(durable.Path, maximumEdge);
        (int Width, int Height) cachedSize = ScaleToMaximumEdge(
            durable.Width,
            durable.Height,
            maximumEdge);
        EncodedReviewFace? cached = await TryReadCachedAsync(
            cachePath,
            cachedSize.Width,
            cachedSize.Height,
            cancellationToken);
        if (cached is not null)
        {
            return cached;
        }

        SemaphoreSlim gate = Gates[GetGateIndex(cachePath)];
        await gate.WaitAsync(cancellationToken);
        try
        {
            cached = await TryReadCachedAsync(
                cachePath,
                cachedSize.Width,
                cachedSize.Height,
                cancellationToken);
            if (cached is not null)
            {
                return cached;
            }

            EncodedReviewFace? rendered = await RenderDirectAsync(
                durable.Path,
                maximumEdge,
                cancellationToken);
            if (rendered is null)
            {
                return null;
            }

            await WriteAtomicallyAsync(cachePath, rendered.Content, cancellationToken);
            return rendered;
        }
        finally
        {
            gate.Release();
        }
    }

    private static async Task<EncodedReviewFace?> RenderDirectAsync(
        string path,
        int maximumEdge,
        CancellationToken cancellationToken)
    {
        ReviewProxyProfile responseProfile = new(
            $"face-response-{maximumEdge}",
            maximumEdge,
            OpenCvReviewFaceRenderer.JpegQuality);
        try
        {
            EncodedReviewProxy encoded = await new OpenCvReviewProxyRenderer().RenderAsync(
                path,
                responseProfile,
                cancellationToken);
            return new EncodedReviewFace(
                encoded.Content,
                encoded.ContentType,
                encoded.Width,
                encoded.Height);
        }
        catch (InvalidDataException)
        {
            return null;
        }
    }

    private static Task<EncodedReviewFace?> TryReadCachedAsync(
        string path,
        int width,
        int height,
        CancellationToken cancellationToken)
    {
        try
        {
            FileInfo file = new(path);
            if (!file.Exists ||
                file.Length <= 0 ||
                (file.Attributes & FileAttributes.ReparsePoint) != 0)
            {
                return Task.FromResult<EncodedReviewFace?>(null);
            }
        }
        catch (Exception exception) when (
            exception is IOException or
            UnauthorizedAccessException or
            System.Security.SecurityException)
        {
            return Task.FromResult<EncodedReviewFace?>(null);
        }

        return ReadStoredAsync(path, width, height, cancellationToken);
    }

    private static async Task<EncodedReviewFace?> ReadStoredAsync(
        string path,
        int width,
        int height,
        CancellationToken cancellationToken)
    {
        try
        {
            byte[] content = await File.ReadAllBytesAsync(path, cancellationToken);
            return content.Length == 0
                ? null
                : new EncodedReviewFace(content, ReviewProxyProfile.ContentType, width, height);
        }
        catch (Exception exception) when (
            exception is IOException or
            UnauthorizedAccessException or
            System.Security.SecurityException)
        {
            return null;
        }
    }

    private static string BuildCachePath(string durablePath, int maximumEdge)
    {
        string directory = Path.GetDirectoryName(durablePath)
            ?? throw new InvalidOperationException("The durable face derivative has no parent directory.");
        return Path.Combine(
            directory,
            CacheProfileId,
            maximumEdge.ToString(CultureInfo.InvariantCulture),
            Path.GetFileName(durablePath));
    }

    private static async Task WriteAtomicallyAsync(
        string destination,
        byte[] content,
        CancellationToken cancellationToken)
    {
        string directory = Path.GetDirectoryName(destination)
            ?? throw new InvalidOperationException("The cached face derivative has no parent directory.");
        Directory.CreateDirectory(directory);
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

    private static (int Width, int Height) ScaleToMaximumEdge(
        int width,
        int height,
        int maximumEdge)
    {
        int longest = Math.Max(width, height);
        if (longest <= maximumEdge)
        {
            return (width, height);
        }

        double scale = maximumEdge / (double)longest;
        return (
            Math.Max(1, (int)Math.Round(width * scale, MidpointRounding.AwayFromZero)),
            Math.Max(1, (int)Math.Round(height * scale, MidpointRounding.AwayFromZero)));
    }

    private static int GetGateIndex(string path) =>
        (StringComparer.OrdinalIgnoreCase.GetHashCode(path) & int.MaxValue) % GateCount;

    private static SemaphoreSlim[] CreateGates()
    {
        SemaphoreSlim[] gates = new SemaphoreSlim[GateCount];
        for (int index = 0; index < gates.Length; index++)
        {
            gates[index] = new SemaphoreSlim(1, 1);
        }
        return gates;
    }
}
