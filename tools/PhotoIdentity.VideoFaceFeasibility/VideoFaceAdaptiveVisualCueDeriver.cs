using PhotoIdentity.Core.Imaging;

namespace PhotoIdentity.VideoFaceFeasibility;

/// <summary>
/// Strictly exploratory, source-free visual changes between already-approved
/// coarse stills. A motion/scene cue asks to sample nearby timestamps; it is
/// NOT proof that a face or person exists, nor a tracking or quality filter.
/// No image, fingerprint, identity, or original-video content is persisted.
/// </summary>
public static class VideoFaceAdaptiveVisualCueDeriver
{
    public const int GridSide = 8;
    public const int Cells = GridSide * GridSide;
    public const int SignificantLumaDifference = 32;
    public const int MotionChangedCells = 4;
    public const int SceneChangedCells = 24;

    /// <summary>
    /// Retain a tiny, ephemeral luminance grid from a bounded decoded image.
    /// The caller must have preflighted and approved the already-local still.
    /// </summary>
    public static byte[] Capture(ImageFrame image)
    {
        ArgumentNullException.ThrowIfNull(image);
        if (image.Size.Width < GridSide || image.Size.Height < GridSide ||
            image.Format is not (PixelFormat.Bgr24 or PixelFormat.Rgb24 or
                PixelFormat.Gray8 or PixelFormat.Bgra32 or PixelFormat.Rgba32))
            throw new ArgumentException("Unsupported visual cue image.");

        ReadOnlySpan<byte> pixels = image.Data;
        int bytesPerPixel = ImageFrame.BytesPerPixel(image.Format);
        var cells = new byte[Cells];
        for (int row = 0; row < GridSide; row++)
        {
            int y = (2 * row + 1) * image.Size.Height / (2 * GridSide);
            for (int col = 0; col < GridSide; col++)
            {
                int x = (2 * col + 1) * image.Size.Width / (2 * GridSide);
                int offset = checked(y * image.Stride + x * bytesPerPixel);
                if (image.Format == PixelFormat.Gray8)
                {
                    cells[row * GridSide + col] = pixels[offset];
                    continue;
                }

                bool blueFirst = image.Format is PixelFormat.Bgr24 or PixelFormat.Bgra32;
                int red = pixels[offset + (blueFirst ? 2 : 0)];
                int green = pixels[offset + 1];
                int blue = pixels[offset + (blueFirst ? 0 : 2)];
                cells[row * GridSide + col] =
                    (byte)((77 * red + 150 * green + 29 * blue + 128) >> 8);
            }
        }

        return cells;
    }

    /// <summary>
    /// Adjacent coarse frames with changed luminance cells get cues on BOTH
    /// sides of the interval. The 8x8 proxy cannot distinguish movement,
    /// exposure shifts or scene cuts reliably; the thresholds are only
    /// bounded experimental triggers and never suppress any coarse sample.
    /// </summary>
    public static IReadOnlyList<VideoFaceSamplingCue> Derive(
        VideoFaceSamplingPlan coarse, IReadOnlyList<byte[]> fingerprints)
    {
        ArgumentNullException.ThrowIfNull(coarse);
        ArgumentNullException.ThrowIfNull(fingerprints);
        VideoFaceSamplingPlan expected = VideoFaceAdaptiveSamplingPlanner.PlanCoverage(
            coarse.ClipDurationMs, coarse.WindowStartMs);
        if (coarse.WindowEndMs != expected.WindowEndMs ||
            coarse.CoarseFrameCount != expected.CoarseFrameCount ||
            !coarse.FrameTimestampsMs.SequenceEqual(expected.FrameTimestampsMs) ||
            fingerprints.Count != expected.CoarseFrameCount ||
            fingerprints.Any(p => p is null || p.Length != Cells))
            throw new ArgumentException("Visual evidence requires every unrefined coarse frame.");

        var cues = new SortedDictionary<long, VideoFaceSamplingCueKind>();
        void Add(long timestamp, VideoFaceSamplingCueKind kind)
        {
            if (!cues.TryGetValue(timestamp, out var previous) || kind > previous)
                cues[timestamp] = kind;
        }

        for (int i = 1; i < fingerprints.Count; i++)
        {
            int changed = 0;
            for (int j = 0; j < Cells; j++)
            {
                if (Math.Abs(fingerprints[i][j] - fingerprints[i - 1][j]) >=
                    SignificantLumaDifference)
                    changed++;
            }

            VideoFaceSamplingCueKind kind = changed >= SceneChangedCells
                ? VideoFaceSamplingCueKind.SceneChange
                : VideoFaceSamplingCueKind.Motion;
            if (changed < MotionChangedCells) continue;
            Add(expected.FrameTimestampsMs[i - 1], kind);
            Add(expected.FrameTimestampsMs[i], kind);
        }

        return cues.Select(pair => new VideoFaceSamplingCue(pair.Key, pair.Value))
            .ToArray();
    }
}
