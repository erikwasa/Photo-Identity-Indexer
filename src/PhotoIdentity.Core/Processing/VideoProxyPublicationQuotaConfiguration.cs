using System.Globalization;

namespace PhotoIdentity.Core.Processing;

/// <summary>
/// WI-0189: opt-in registered-byte publication fence for the shared Archive
/// video lane. This does not measure scratch/orphan files or enforce a disk
/// quota; it deliberately performs no filesystem operations.
/// </summary>
public static class VideoProxyPublicationQuotaConfiguration
{
    public static VideoProxyRetentionLimits? ParseOptional(
        string? maximumCacheMiB, string? maximumProxyMiB)
    {
        // Backwards compatible: old installations keep WI-0188 publication
        // behavior until BOTH settings are deliberately supplied.
        if (maximumCacheMiB is null && maximumProxyMiB is null)
            return null;

        static long Parse(string? input, long minimum, long maximum)
        {
            if (input is null ||
                !long.TryParse(input, NumberStyles.None,
                    CultureInfo.InvariantCulture, out long value) ||
                value < minimum || value > maximum)
                throw new ArgumentException(
                    "Configured video publication quota is incomplete or out of bounds.");
            return value;
        }

        long cache = Parse(maximumCacheMiB, 64, 16384);
        long proxy = Parse(maximumProxyMiB, 1, 256);
        if (proxy > cache)
            throw new ArgumentException(
                "Video proxy byte limit cannot exceed the cache byte limit.");

        var limits = new VideoProxyRetentionLimits(
            checked(cache * 1024L * 1024), checked(proxy * 1024L * 1024));
        limits.Validate();
        return limits;
    }
}
