using PhotoIdentity.Core.Geometry;
using PhotoIdentity.Core.Identifiers;
using PhotoIdentity.Core.Imaging;
using PhotoIdentity.Core.Recognition;
using PhotoIdentity.Recognition.Onnx.Models;
using PhotoIdentity.Recognition.Onnx.SFace;
using Xunit;

namespace PhotoIdentity_Recognition_Tests;

public sealed class SFaceEmbedderTests
{
    [Fact]
    public async Task Embedding_has_expected_finite_dimensions_and_unit_norm()
    {
        ModelManifest manifest = await LoadManifestAsync();
        float[] raw = Enumerable.Range(1, 128).Select(value => (float)value).ToArray();
        using FakeSFaceInferenceSession session = new((_, _) =>
            new SFaceTensor("fc1", [1, 128], raw));
        using SFaceFaceEmbedder embedder = new(manifest, session);

        EmbeddingVector result = await embedder.EmbedAsync(
            CreateAlignedFace(CreateUniformFrame(10, 20, 30)),
            CancellationToken.None);

        Assert.Equal(128, result.Dimensions);
        Assert.All(result.ToArray(), value => Assert.True(float.IsFinite(value)));
        Assert.InRange(result.L2Norm, 0.999999, 1.000001);
        Assert.Equal(ModelRole.FaceEmbedding, embedder.Descriptor.Role);
        Assert.Equal(DistanceMetric.Cosine, embedder.Descriptor.DistanceMetric);
        Assert.Equal(new AlignmentProtocolId("sface-five-point-v1"), embedder.Descriptor.AlignmentProtocol);
    }

    [Fact]
    public async Task Preprocessing_uses_manifest_rgb_nchw_without_normalisation()
    {
        ModelManifest manifest = await LoadManifestAsync();
        using FakeSFaceInferenceSession session = new((_, _) =>
            new SFaceTensor("fc1", [1, 128], UnitVector(0)));
        using SFaceFaceEmbedder embedder = new(manifest, session);

        await embedder.EmbedAsync(
            CreateAlignedFace(CreateUniformFrame(blue: 10, green: 20, red: 30)),
            CancellationToken.None);

        Assert.Equal(new long[] { 1, 3, 112, 112 }, session.LastShape);
        Assert.NotNull(session.LastInput);
        int planeSize = 112 * 112;
        Assert.Equal(30f, session.LastInput![0]);
        Assert.Equal(20f, session.LastInput[planeSize]);
        Assert.Equal(10f, session.LastInput[planeSize * 2]);
        Assert.Equal("RGB", manifest.Input.ColourOrder);
        Assert.Equal(1d, manifest.Input.Normalisation.Scale);
        Assert.Equal(new double[] { 0, 0, 0 }, manifest.Input.Normalisation.Mean);
    }

    [Fact]
    public async Task Same_person_fixtures_score_above_different_person_fixture()
    {
        ModelManifest manifest = await LoadManifestAsync();
        using FakeSFaceInferenceSession session = new((input, _) =>
        {
            float[] output = input[0] < 100
                ? Vector(1, (input[0] - 30) * 0.01f)
                : Vector(0, 1);
            return new SFaceTensor("fc1", [1, 128], output);
        });
        using SFaceFaceEmbedder embedder = new(manifest, session);

        EmbeddingVector sameA = await embedder.EmbedAsync(
            CreateAlignedFace(CreateUniformFrame(10, 20, 30)),
            CancellationToken.None);
        EmbeddingVector sameB = await embedder.EmbedAsync(
            CreateAlignedFace(CreateUniformFrame(10, 20, 31)),
            CancellationToken.None);
        EmbeddingVector different = await embedder.EmbedAsync(
            CreateAlignedFace(CreateUniformFrame(10, 20, 200)),
            CancellationToken.None);

        double sameScore = sameA.CosineSimilarity(sameB);
        double differentScore = sameA.CosineSimilarity(different);

        Assert.True(sameScore > differentScore);
        Assert.InRange(sameScore, 0.99, 1.0);
        Assert.InRange(differentScore, -0.000001, 0.000001);
    }

    [Fact]
    public async Task Repeated_cpu_pipeline_is_stable_within_tolerance()
    {
        ModelManifest manifest = await LoadManifestAsync();
        using FakeSFaceInferenceSession session = new((input, _) =>
        {
            float seed = input[0] / 255f;
            return new SFaceTensor(
                "fc1",
                [1, 128],
                Enumerable.Range(0, 128)
                    .Select(index => seed + ((index + 1) * 0.001f))
                    .ToArray());
        });
        using SFaceFaceEmbedder embedder = new(manifest, session);
        AlignedFace face = CreateAlignedFace(CreateUniformFrame(15, 25, 35));

        EmbeddingVector first = await embedder.EmbedAsync(face, CancellationToken.None);
        EmbeddingVector second = await embedder.EmbedAsync(face, CancellationToken.None);

        float[] firstValues = first.ToArray();
        float[] secondValues = second.ToArray();
        Assert.Equal(firstValues.Length, secondValues.Length);
        for (int index = 0; index < firstValues.Length; index++)
        {
            Assert.InRange(Math.Abs(firstValues[index] - secondValues[index]), 0, 1e-7);
        }
    }

    [Fact]
    public async Task Invalid_output_shape_fails_clearly()
    {
        ModelManifest manifest = await LoadManifestAsync();
        using FakeSFaceInferenceSession session = new((_, _) =>
            new SFaceTensor("fc1", [1, 64], new float[64]));
        using SFaceFaceEmbedder embedder = new(manifest, session);

        SFaceOutputException exception = await Assert.ThrowsAsync<SFaceOutputException>(
            () => embedder.EmbedAsync(
                CreateAlignedFace(CreateUniformFrame(10, 20, 30)),
                CancellationToken.None));

        Assert.Contains("128", exception.Message, StringComparison.Ordinal);
        Assert.Contains("[1,64]", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Non_finite_output_fails_clearly()
    {
        ModelManifest manifest = await LoadManifestAsync();
        float[] values = UnitVector(0);
        values[3] = float.NaN;
        using FakeSFaceInferenceSession session = new((_, _) =>
            new SFaceTensor("fc1", [1, 128], values));
        using SFaceFaceEmbedder embedder = new(manifest, session);

        SFaceOutputException exception = await Assert.ThrowsAsync<SFaceOutputException>(
            () => embedder.EmbedAsync(
                CreateAlignedFace(CreateUniformFrame(10, 20, 30)),
                CancellationToken.None));

        Assert.Contains("finite", exception.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Protocol_and_input_size_must_match_descriptor()
    {
        ModelManifest manifest = await LoadManifestAsync();
        using FakeSFaceInferenceSession session = new((_, _) =>
            new SFaceTensor("fc1", [1, 128], UnitVector(0)));
        using SFaceFaceEmbedder embedder = new(manifest, session);

        await Assert.ThrowsAsync<ArgumentException>(
            () => embedder.EmbedAsync(
                new AlignedFace(
                    CreateUniformFrame(10, 20, 30),
                    new AlignmentProtocolId("other-v1")),
                CancellationToken.None));

        ImageFrame wrongSize = CreateUniformFrame(10, 20, 30, width: 64, height: 64);
        await Assert.ThrowsAsync<ArgumentException>(
            () => embedder.EmbedAsync(
                new AlignedFace(wrongSize, new AlignmentProtocolId("sface-five-point-v1")),
                CancellationToken.None));
    }

    private static async Task<ModelManifest> LoadManifestAsync()
    {
        string root = FindRepositoryRoot();
        ModelManifestLoader loader = new();
        return await loader.LoadAsync(
            Path.Combine(root, "models", "manifests", "sface-2021dec-fp32.json"));
    }

    private static string FindRepositoryRoot()
    {
        DirectoryInfo? current = new(AppContext.BaseDirectory);
        while (current is not null)
        {
            if (File.Exists(Path.Combine(current.FullName, "PhotoIdentity.slnx")))
            {
                return current.FullName;
            }

            current = current.Parent;
        }

        throw new DirectoryNotFoundException("Could not locate repository root.");
    }

    private static AlignedFace CreateAlignedFace(ImageFrame image) =>
        new(image, new AlignmentProtocolId("sface-five-point-v1"));

    private static ImageFrame CreateUniformFrame(
        byte blue,
        byte green,
        byte red,
        int width = 112,
        int height = 112)
    {
        int stride = checked(width * 3);
        byte[] data = new byte[checked(stride * height)];
        for (int offset = 0; offset < data.Length; offset += 3)
        {
            data[offset] = blue;
            data[offset + 1] = green;
            data[offset + 2] = red;
        }

        return new ImageFrame(
            new ImageSize(width, height),
            PixelFormat.Bgr24,
            stride,
            data);
    }

    private static float[] UnitVector(int index)
    {
        float[] values = new float[128];
        values[index] = 1;
        return values;
    }

    private static float[] Vector(float first, float second)
    {
        float[] values = new float[128];
        values[0] = first;
        values[1] = second;
        return values;
    }

    private sealed class FakeSFaceInferenceSession : ISFaceInferenceSession
    {
        private readonly Func<float[], long[], SFaceTensor> _run;

        public FakeSFaceInferenceSession(Func<float[], long[], SFaceTensor> run)
        {
            _run = run;
        }

        public float[]? LastInput { get; private set; }
        public long[]? LastShape { get; private set; }

        public SFaceTensor Run(
            float[] input,
            long[] shape,
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            LastInput = (float[])input.Clone();
            LastShape = (long[])shape.Clone();
            return _run(input, shape);
        }

        public void Dispose()
        {
        }
    }
}
