using PhotoIdentity.Core.Geometry;
using PhotoIdentity.Core.Imaging;
using PhotoIdentity.Recognition.Onnx.Semantic;
using Xunit;

namespace PhotoIdentity_Recognition_Tests;

public sealed class ClipEmbeddingEncoderTests
{
    [Fact]
    public void Encoder_normalizes_image_and_text_vectors()
    {
        ClipBpeTokenizer tokenizer = CreateTokenizer();
        using FakeEmbeddingSession session = new(
            image: [3f, 4f],
            text: [0f, 5f]);
        using ClipEmbeddingEncoder encoder = new(tokenizer, session);
        ImageFrame image = new(
            new ImageSize(1, 1),
            PixelFormat.Bgr24,
            stride: 3,
            data: new byte[] { 0, 0, 0 });

        ClipEmbeddingResult imageResult = encoder.EncodeImage(image);
        ClipEmbeddingResult textResult = encoder.EncodeText("cat");

        Assert.Equal([0.6f, 0.8f], imageResult.Vector);
        Assert.Equal([0f, 1f], textResult.Vector);
        Assert.Equal(2, imageResult.Dimensions);
        Assert.Equal(2, textResult.Dimensions);
        Assert.Equal(1, session.ImageRuns);
        Assert.Equal(1, session.TextRuns);
    }

    [Fact]
    public void Encoder_rejects_non_finite_or_zero_model_output()
    {
        ClipBpeTokenizer tokenizer = CreateTokenizer();
        using FakeEmbeddingSession zeroSession = new(
            image: [0f, 0f],
            text: [1f, 0f]);
        using ClipEmbeddingEncoder zeroEncoder = new(tokenizer, zeroSession);
        ImageFrame image = new(
            new ImageSize(1, 1),
            PixelFormat.Bgr24,
            stride: 3,
            data: new byte[] { 0, 0, 0 });

        Assert.Throws<ClipOutputException>(() => zeroEncoder.EncodeImage(image));

        using FakeEmbeddingSession invalidSession = new(
            image: [1f, 0f],
            text: [float.NaN, 1f]);
        using ClipEmbeddingEncoder invalidEncoder = new(tokenizer, invalidSession);
        Assert.Throws<ClipOutputException>(() => invalidEncoder.EncodeText("cat"));
    }

    private static ClipBpeTokenizer CreateTokenizer() =>
        ClipBpeTokenizer.CreateForTests(
            new Dictionary<string, int>(StringComparer.Ordinal)
            {
                ["<|startoftext|>"] = 100,
                ["<|endoftext|>"] = 101,
                ["a</w>"] = 1,
                ["p"] = 2,
                ["h"] = 3,
                ["o"] = 4,
                ["t"] = 5,
                ["o</w>"] = 6,
                ["c"] = 7,
                ["a"] = 8,
                ["t</w>"] = 9,
            },
            [
                ("x", "y"),
            ]);

    private sealed class FakeEmbeddingSession(
        float[] image,
        float[] text) : IClipEmbeddingSession
    {
        public int ImageRuns { get; private set; }
        public int TextRuns { get; private set; }

        public float[] RunImage(
            long[] inputIds,
            long[] attentionMask,
            float[] pixelValues,
            CancellationToken cancellationToken)
        {
            ImageRuns++;
            Assert.Equal(ClipZeroShotTagger.ContextLength, inputIds.Length);
            Assert.Equal(ClipZeroShotTagger.ContextLength, attentionMask.Length);
            Assert.Equal(
                3 * ClipZeroShotTagger.ImageSize * ClipZeroShotTagger.ImageSize,
                pixelValues.Length);
            return image;
        }

        public float[] RunText(
            long[] inputIds,
            long[] attentionMask,
            float[] pixelValues,
            CancellationToken cancellationToken)
        {
            TextRuns++;
            Assert.Equal(ClipZeroShotTagger.ContextLength, inputIds.Length);
            Assert.Equal(ClipZeroShotTagger.ContextLength, attentionMask.Length);
            Assert.Equal(
                3 * ClipZeroShotTagger.ImageSize * ClipZeroShotTagger.ImageSize,
                pixelValues.Length);
            return text;
        }

        public void Dispose()
        {
        }
    }
}
