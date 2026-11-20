using PhotoIdentity.Api;
using PhotoIdentity.Cli;
using PhotoIdentity.Core.Geometry;
using PhotoIdentity.Core.Imaging;
using Xunit;

namespace PhotoIdentity_Integration_Tests;

/// <summary>
/// Synthetic pixel/guard tests. No PostgreSQL, originals, video inference,
/// FFmpeg, OneDrive hydration or person-assignment writes.
/// </summary>
public sealed class VideoFaceNativeCropReviewTests
{
    [Fact]
    public void Native_crop_geometry_clamps_context_without_upscaling()
    {
        VideoFaceNativeCropBox box = VideoFaceNativeCropRenderer.CalculateBox(
            1920, 1080, .2, .1, .04, .08);
        Assert.True(box.X >= 0 && box.Y >= 0);
        Assert.True(box.X + box.Width <= 1920);
        Assert.True(box.Y + box.Height <= 1080);
        Assert.InRange(box.Width, 77, 108);
        Assert.InRange(box.Height, 86, 116);

        var corner = VideoFaceNativeCropRenderer.CalculateBox(
            320, 180, 0, 0, .01, .01);
        Assert.Equal(0, corner.X);
        Assert.Equal(0, corner.Y);
        Assert.InRange(corner.Width, 1, 10);
        Assert.InRange(corner.Height, 1, 10);
        Assert.Throws<InvalidDataException>(() =>
            VideoFaceNativeCropRenderer.CalculateBox(3841, 100, .2, .1, .1, .1));
        Assert.Throws<InvalidDataException>(() =>
            VideoFaceNativeCropRenderer.CalculateBox(100, 100,
                double.NaN, .1, .1, .1));
        Assert.Throws<InvalidDataException>(() =>
            VideoFaceNativeCropRenderer.CalculateBox(100, 100, .95, .1, .1, .1));
    }

    [Fact]
    public void Synthetic_bgr_frame_encodes_only_exact_native_box()
    {
        if (!OperatingSystem.IsWindows()) return;
        byte[] pixels = new byte[320 * 180 * 3];
        for (int i = 0; i < pixels.Length; i += 3)
        {
            pixels[i] = 35;
            pixels[i + 1] = 80;
            pixels[i + 2] = 150;
        }
        ImageFrame frame = new(new ImageSize(320, 180),
            PixelFormat.Bgr24, 320 * 3, pixels);
        var box = VideoFaceNativeCropRenderer.CalculateBox(
            320, 180, .2, .1, .15, .25);
        VideoFaceNativeCropJpeg jpeg = VideoFaceNativeCropRenderer.Render(
            frame, 320, 180, .2, .1, .15, .25);
        Assert.InRange(jpeg.Bytes.Length, 100,
            VideoFaceNativeCropRenderer.MaximumEncodedBytes);
        ImageSize declared = VideoFaceNativeFrameProfile.ReadBoundedJpegSize(jpeg.Bytes);
        Assert.Equal(box.Width, jpeg.Width);
        Assert.Equal(box.Height, jpeg.Height);
        Assert.Equal(box.Width, declared.Width);
        Assert.Equal(box.Height, declared.Height);
        Assert.Throws<InvalidDataException>(() =>
            VideoFaceNativeCropRenderer.Render(frame, 640, 180, .2, .1, .15, .25));
    }

    [Fact]
    public void Dedicated_scratch_requires_marker_and_no_unknown_files()
    {
        string path = Path.Combine(Path.GetTempPath(),
            VideoFaceNativeCropScratchGuard.Prefix + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(path);
        try
        {
            Assert.Throws<InvalidOperationException>(() =>
                VideoFaceNativeCropScratchGuard.Validate(path));
            string marker = Path.Combine(path, VideoFaceNativeCropScratchGuard.MarkerName);
            File.WriteAllText(marker, VideoFaceNativeCropScratchGuard.MarkerContents);
            Assert.Equal(Path.GetFullPath(path),
                VideoFaceNativeCropScratchGuard.Validate(path));
            string foreign = Path.Combine(path, "unowned-file.txt");
            File.WriteAllText(foreign, "do not delete");
            Assert.Throws<InvalidOperationException>(() =>
                VideoFaceNativeCropScratchGuard.Validate(path));
            Assert.Equal("do not delete", File.ReadAllText(foreign));
        }
        finally { Directory.Delete(path, recursive: true); }
    }

    [Fact]
    public void Native_crop_http_is_triple_gated_and_browser_does_not_import_raw_sources()
    {
        string root = Root();
        string api = File.ReadAllText(Path.Combine(root, "src", "PhotoIdentity.Api",
            "VideoFaceReviewEndpoints.cs"));
        string service = File.ReadAllText(Path.Combine(root, "src", "PhotoIdentity.Api",
            "VideoFaceNativeCropService.cs"));
        string access = File.ReadAllText(Path.Combine(root,
            "src", "PhotoIdentity.Persistence.Postgres",
            "PostgresVideoFaceNativeCropRepository.cs"));
        string page = File.ReadAllText(Path.Combine(root, "src", "PhotoIdentity.Web",
            "Pages", "VideoFacePersistedReview.razor"));
        string css = File.ReadAllText(Path.Combine(root, "src", "PhotoIdentity.Web",
            "Pages", "VideoFacePersistedReview.razor.css"));

        Assert.Contains("PhotoIdentity:VideoFaceNativeCropEnabled", api);
        Assert.Contains("NativeCropEnabled(settings)", api);
        Assert.Contains("NoStore(context)", api);
        Assert.Contains("enableRangeProcessing: false", api);
        Assert.Contains("publishing_job_id", access);
        Assert.Contains("FOR UPDATE OF source", access);
        Assert.Contains("FOR UPDATE OF asset", access);
        Assert.Contains("job.status='succeeded'", access);
        Assert.Contains("source_copy_exclusions", access);
        Assert.Contains("Plan(", access);
        Assert.Contains("OneDriveVideoLocalStager", service);
        Assert.Contains("VideoToolProcessSupervisor", service);
        Assert.Contains("VideoGuardedToolExecutor", service);
        Assert.Contains("PostgresVideoPhotoPriorityProbe", service);
        Assert.Contains("ReadValidatedFrameAsync", service);
        Assert.Contains("NativeCropUrl", page);
        Assert.Contains("NativeCropReady", page);
        Assert.Contains("native-crop", page);
        Assert.Contains("width: auto", css);
        Assert.DoesNotContain("PublishAsync", service);
        Assert.DoesNotContain("video_person_actions", service);
        Assert.DoesNotContain("localStorage", page);
    }

    private static string Root()
    {
        foreach (string start in new[] { Directory.GetCurrentDirectory(),
                                          AppContext.BaseDirectory })
        {
            DirectoryInfo? current = new(Path.GetFullPath(start));
            for (int depth = 0; current is not null && depth < 12;
                 depth++, current = current.Parent)
                if (File.Exists(Path.Combine(current.FullName, "PhotoIdentity.slnx")))
                    return current.FullName;
        }
        throw new DirectoryNotFoundException("Repository root not found.");
    }
}
