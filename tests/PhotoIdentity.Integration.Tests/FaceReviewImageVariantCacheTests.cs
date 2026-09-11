using OpenCvSharp;
using PhotoIdentity.Api;
using PhotoIdentity.Imaging.OpenCv;
using Xunit;

namespace PhotoIdentity_Integration_Tests;

public sealed class FaceReviewImageVariantCacheTests
{
    [Fact]
    public async Task Gallery_variant_is_generated_once_and_reused()
    {
        string root = Path.Combine(
            Path.GetTempPath(),
            "photoidentity-face-variant-" + Guid.NewGuid().ToString("N"));
        try
        {
            string derivativeDirectory = Path.Combine(root, "face-review", "test-profile");
            Directory.CreateDirectory(derivativeDirectory);
            string durablePath = Path.Combine(derivativeDirectory, "face.jpg");

            byte[] source;
            using (Mat image = new(new Size(960, 640), MatType.CV_8UC3, new Scalar(40, 90, 140)))
            {
                Cv2.Rectangle(
                    image,
                    new Rect(240, 120, 360, 360),
                    new Scalar(210, 180, 70),
                    thickness: -1);
                Cv2.ImEncode(
                    ".jpg",
                    image,
                    out source,
                    new ImageEncodingParam(ImwriteFlags.JpegQuality, OpenCvReviewFaceRenderer.JpegQuality));
                Assert.NotEmpty(source);
            }
            await File.WriteAllBytesAsync(durablePath, source);

            FaceReviewDerivativeFile durable = new(durablePath, 960, 640);
            EncodedReviewFace first = Assert.IsType<EncodedReviewFace>(
                await FaceReviewImageVariantCache.RenderAsync(
                    durable,
                    FaceReviewImageVariantCache.GalleryMaximumEdge));

            using (Mat firstImage = Cv2.ImDecode(first.Content, ImreadModes.Color))
            {
                Assert.Equal(360, firstImage.Cols);
                Assert.Equal(240, firstImage.Rows);
            }

            string cachePath = Path.Combine(
                derivativeDirectory,
                "response-cache-v1-q90",
                FaceReviewImageVariantCache.GalleryMaximumEdge.ToString(),
                "face.jpg");
            Assert.True(File.Exists(cachePath));
            Assert.Equal(first.Content, await File.ReadAllBytesAsync(cachePath));

            DateTime pinnedWriteTime = new(2026, 1, 2, 3, 4, 6, DateTimeKind.Utc);
            File.SetLastWriteTimeUtc(cachePath, pinnedWriteTime);
            DateTime observedPinnedWriteTime = File.GetLastWriteTimeUtc(cachePath);

            EncodedReviewFace second = Assert.IsType<EncodedReviewFace>(
                await FaceReviewImageVariantCache.RenderAsync(
                    durable,
                    FaceReviewImageVariantCache.GalleryMaximumEdge));

            Assert.Equal(first.Content, second.Content);
            Assert.Equal(observedPinnedWriteTime, File.GetLastWriteTimeUtc(cachePath));
        }
        finally
        {
            try
            {
                Directory.Delete(root, recursive: true);
            }
            catch
            {
                // Best-effort test cleanup only.
            }
        }
    }
}
