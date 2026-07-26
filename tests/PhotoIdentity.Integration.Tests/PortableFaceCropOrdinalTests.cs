using System.Security.Cryptography;
using PhotoIdentity.Cli;
using PhotoIdentity.Core.Geometry;
using PhotoIdentity.Core.Identifiers;
using PhotoIdentity.Core.Imaging;
using PhotoIdentity.Core.Recognition;
using PhotoIdentity.Imaging.OpenCv;
using PhotoIdentity.Transfer.Bundles;
using PhotoIdentity.Worker;
using Xunit;

namespace PhotoIdentity_Integration_Tests;

public sealed class PortableFaceCropOrdinalTests
{
    [Fact]
    public async Task Face_crop_path_preserves_nonfirst_canonical_occurrence_ordinal()
    {
        string directory = CreateTemporaryDirectory();
        try
        {
            string cropPath = Path.Combine(directory, "aligned.png");
            await WritePngAsync(cropPath, CreateFrame(112, 112));
            string jobPath = Path.Combine(directory, "job.photoid-job");
            string resultPath = Path.Combine(directory, "result.photoid-result");
            await PortableBundleArchive.CreateJobAsync(
                jobPath,
                new PortableJobBundleRequest(
                    AssetRevisionId.New(),
                    Digest("canonical-source"u8),
                    PortableBundleProfile.FaceCrops,
                    new PortableRecognitionConfiguration().ToJson(),
                    [new PortableJobInput(cropPath, "inputs/faces/face-003.png", PortableBundleRoles.FaceCrop)],
                    DateTimeOffset.UtcNow));

            PortableRecognitionProcessor processor = new(
                new OpenCvImageDecoder(),
                new OpenCvPngEncoder(),
                new OpenCvFaceAligner(),
                _ => throw new InvalidOperationException("A crop-only job must not create a detector."),
                () => new FakeEmbedder());
            PortableResultManifest result = await new PortableBundleWorker(processor).ProcessAsync(
                jobPath,
                resultPath,
                Path.Combine(directory, "work"));

            PortableFaceResult face = Assert.Single(result.Faces);
            Assert.Equal(2, face.Ordinal);
            Assert.Equal("results/faces/face-003/crop.png", face.CropPath);
        }
        finally
        {
            DeleteTemporaryDirectory(directory);
        }
    }

    [Fact]
    public async Task Bundle_cli_rejects_duplicate_numbered_crop_arguments_before_database_access()
    {
        StringWriter output = new();
        StringWriter error = new();
        int exitCode = await Program.RunAsync(
            [
                "bundle", "export",
                "--database", "unused.db",
                "--revision", Guid.NewGuid().ToString(),
                "--job", "unused.photoid-job",
                "--profile", "face-crops",
                "--crop", "3=first.png",
                "--crop", "3=second.png",
            ],
            output,
            error);

        Assert.Equal(2, exitCode);
        Assert.Contains("Face number 3", error.ToString(), StringComparison.Ordinal);
    }

    private static async Task WritePngAsync(string path, ImageFrame frame)
    {
        await using FileStream stream = new(path, FileMode.Create, FileAccess.Write, FileShare.None);
        await new OpenCvPngEncoder().EncodeAsync(frame, stream, CancellationToken.None);
    }

    private static ImageFrame CreateFrame(int width, int height)
    {
        int stride = checked(width * 3);
        byte[] data = new byte[checked(stride * height)];
        for (int y = 0; y < height; y++)
        {
            for (int x = 0; x < width; x++)
            {
                int offset = (y * stride) + (x * 3);
                data[offset] = (byte)((x * 3 + y) % 256);
                data[offset + 1] = (byte)((x + y * 5) % 256);
                data[offset + 2] = (byte)((x * 7 + y * 2) % 256);
            }
        }
        return new ImageFrame(new ImageSize(width, height), PixelFormat.Bgr24, stride, data);
    }

    private static Sha256Digest Digest(ReadOnlySpan<byte> bytes) =>
        new(Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant());

    private static string CreateTemporaryDirectory()
    {
        string directory = Path.Combine(
            Path.GetTempPath(),
            "PhotoIdentity.Integration.Tests",
            Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        return directory;
    }

    private static void DeleteTemporaryDirectory(string directory)
    {
        if (Directory.Exists(directory))
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    private sealed class FakeEmbedder : IFaceEmbedder
    {
        public ModelDescriptor Descriptor { get; } = new(
            new ModelId("fake-crop-embedder"),
            ModelRole.FaceEmbedding,
            ModelFormat.Onnx,
            new Sha256Digest(new string('b', 64)),
            new ImageSize(112, 112),
            "fake-runtime",
            "Apache-2.0",
            "test",
            outputDimensions: 2,
            distanceMetric: DistanceMetric.Cosine,
            alignmentProtocol: OpenCvFaceAligner.SFaceFivePointV1);

        public Task<EmbeddingVector> EmbedAsync(
            AlignedFace face,
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return Task.FromResult(new EmbeddingVector([1f, 0f]));
        }
    }
}
