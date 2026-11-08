using PhotoIdentity.Core.Catalogue;

namespace PhotoIdentity.Api;

/// <summary>
/// One internal source-admission policy for both Standard and Compact output
/// qualities. Approving an additional input class requires its independently
/// verified conversion recipe and a change HERE, not a new operator preset.
/// Keep its PostgreSQL predicate and post-inspection check in sync.
/// </summary>
public static class VideoApprovedInputPolicy
{
    public const long MaximumSourceBytes = 256L * 1024 * 1024;

    // Shared by both queued and not-yet-queued selectors. The exact
    // source/colour checks performed after staging remain independent.
    public const string MetadataPredicate = """
        AND metadata.inspection_status='supported'
        AND metadata.color_transfer='bt709'
        AND metadata.video_codec IN ('h264','hevc')
        AND metadata.pixel_format IN ('yuv420p','yuv420p10le','yuvj420p')
        AND (metadata.audio_codec IS NULL OR
             metadata.audio_codec IN ('aac','pcm_s16le','pcm_s24le'))
        AND metadata.coded_width BETWEEN 16 AND 4096
        AND metadata.coded_height BETWEEN 16 AND 4096
        AND metadata.duration_seconds BETWEEN 0.2 AND 600
        AND metadata.display_rotation_degrees IN (0,90,-90,180,-180)
        """;

    public static bool IsApproved(
        VideoRevisionInspection inspection, long sourceBytes) =>
        inspection.InspectionStatus == "supported" &&
        inspection.ColorTransfer == "bt709" &&
        inspection.VideoCodec is ("h264" or "hevc") &&
        inspection.PixelFormat is ("yuv420p" or "yuv420p10le" or "yuvj420p") &&
        inspection.AudioCodec is (null or "aac" or "pcm_s16le" or "pcm_s24le") &&
        inspection.CodedWidth is >= 16 and <= 4096 &&
        inspection.CodedHeight is >= 16 and <= 4096 &&
        inspection.DisplayRotationDegrees is (0 or 90 or -90 or 180 or -180) &&
        inspection.DurationSeconds is >= 0.2m and <= 600m &&
        sourceBytes is >= 1 and <= MaximumSourceBytes;
}
