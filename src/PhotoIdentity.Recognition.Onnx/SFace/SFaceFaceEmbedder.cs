using System.Diagnostics;
using PhotoIdentity.Core.Imaging;
using PhotoIdentity.Core.Recognition;
using PhotoIdentity.Recognition.Onnx.Models;

namespace PhotoIdentity.Recognition.Onnx.SFace;

public sealed record SFaceEmbeddingResult(
    ModelDescriptor Descriptor,
    EmbeddingVector Embedding,
    TimeSpan PreprocessingDuration,
    TimeSpan InferenceDuration,
    TimeSpan PostprocessingDuration)
{
    public TimeSpan TotalDuration =>
        PreprocessingDuration + InferenceDuration + PostprocessingDuration;
}

public sealed class SFaceFaceEmbedder : IFaceEmbedder, IDisposable
{
    private readonly ModelManifest _manifest;
    private readonly ISFaceInferenceSession _session;
    private bool _disposed;

    public SFaceFaceEmbedder(ModelManifest manifest, string modelPath)
        : this(manifest, new OnnxSFaceInferenceSession(modelPath))
    {
    }

    internal SFaceFaceEmbedder(
        ModelManifest manifest,
        ISFaceInferenceSession session)
    {
        ArgumentNullException.ThrowIfNull(manifest);
        ArgumentNullException.ThrowIfNull(session);
        ModelManifestValidator.Validate(manifest);

        if (manifest.Role != "faceEmbedding" || manifest.Format != "onnx")
        {
            throw new ModelManifestException(
                $"Model '{manifest.ModelId}' must be an ONNX face-embedding model.");
        }

        if (manifest.Input.DataType != "float32" ||
            manifest.Input.ColourOrder is not ("BGR" or "RGB"))
        {
            throw new ModelManifestException(
                $"Model '{manifest.ModelId}' requires unsupported input preprocessing.");
        }

        if (manifest.Output.Normalisation != "l2-by-adapter")
        {
            throw new ModelManifestException(
                $"Model '{manifest.ModelId}' must declare adapter-owned L2 normalisation.");
        }

        _manifest = manifest;
        _session = session;
        Descriptor = manifest.ToDescriptor();
    }

    public ModelDescriptor Descriptor { get; }

    public Task<EmbeddingVector> EmbedAsync(
        AlignedFace face,
        CancellationToken cancellationToken)
    {
        SFaceEmbeddingResult result = EmbedWithMetrics(face, cancellationToken);
        return Task.FromResult(result.Embedding);
    }

    public Task<SFaceEmbeddingResult> EmbedWithMetricsAsync(
        AlignedFace face,
        CancellationToken cancellationToken = default) =>
        Task.FromResult(EmbedWithMetrics(face, cancellationToken));

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _session.Dispose();
        _disposed = true;
    }

    private SFaceEmbeddingResult EmbedWithMetrics(
        AlignedFace face,
        CancellationToken cancellationToken)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        ArgumentNullException.ThrowIfNull(face);
        cancellationToken.ThrowIfCancellationRequested();

        if (Descriptor.AlignmentProtocol is null ||
            face.Protocol != Descriptor.AlignmentProtocol.Value)
        {
            throw new ArgumentException(
                $"Aligned face protocol '{face.Protocol}' does not match model protocol " +
                $"'{Descriptor.AlignmentProtocol}'.",
                nameof(face));
        }

        if (face.Image.Size != Descriptor.InputSize)
        {
            throw new ArgumentException(
                $"SFace requires aligned images of {Descriptor.InputSize.Width}x{Descriptor.InputSize.Height}, " +
                $"but received {face.Image.Size.Width}x{face.Image.Size.Height}.",
                nameof(face));
        }

        long preprocessingStarted = Stopwatch.GetTimestamp();
        SFacePreprocessedInput input = SFacePreprocessor.Preprocess(
            face.Image,
            _manifest,
            cancellationToken);
        TimeSpan preprocessingDuration = Stopwatch.GetElapsedTime(preprocessingStarted);

        long inferenceStarted = Stopwatch.GetTimestamp();
        SFaceTensor output = _session.Run(input.Data, input.Shape, cancellationToken);
        TimeSpan inferenceDuration = Stopwatch.GetElapsedTime(inferenceStarted);

        cancellationToken.ThrowIfCancellationRequested();
        long postprocessingStarted = Stopwatch.GetTimestamp();
        EmbeddingVector embedding = ParseAndNormalise(output, _manifest);
        TimeSpan postprocessingDuration = Stopwatch.GetElapsedTime(postprocessingStarted);

        return new SFaceEmbeddingResult(
            Descriptor,
            embedding,
            preprocessingDuration,
            inferenceDuration,
            postprocessingDuration);
    }

    private static EmbeddingVector ParseAndNormalise(
        SFaceTensor output,
        ModelManifest manifest)
    {
        int expectedDimensions = manifest.Output.Dimensions ??
            throw new ModelManifestException(
                $"Model '{manifest.ModelId}' does not declare embedding dimensions.");

        bool shapeMatches = output.Shape.Length switch
        {
            1 => output.Shape[0] == expectedDimensions,
            2 => output.Shape[0] == 1 && output.Shape[1] == expectedDimensions,
            _ => false,
        };

        if (!shapeMatches || output.Values.Length != expectedDimensions)
        {
            throw new SFaceOutputException(
                $"SFace output '{output.Name}' must have shape [{expectedDimensions}] or " +
                $"[1,{expectedDimensions}], but returned [{string.Join(',', output.Shape)}] " +
                $"with {output.Values.Length} values.");
        }

        try
        {
            return new EmbeddingVector(output.Values).Normalize();
        }
        catch (ArgumentException exception)
        {
            throw new SFaceOutputException(
                $"SFace output '{output.Name}' is not a valid finite non-zero embedding: " +
                exception.Message);
        }
    }
}

internal sealed record SFacePreprocessedInput(float[] Data, long[] Shape);

internal static class SFacePreprocessor
{
    public static SFacePreprocessedInput Preprocess(
        ImageFrame image,
        ModelManifest manifest,
        CancellationToken cancellationToken)
    {
        int width = manifest.Input.Width;
        int height = manifest.Input.Height;
        int planeSize = checked(width * height);
        float[] tensor = new float[checked(3 * planeSize)];
        ReadOnlySpan<byte> source = image.Data;
        bool modelUsesBgr = manifest.Input.ColourOrder == "BGR";
        double scale = manifest.Input.Normalisation.Scale;
        double[] mean = manifest.Input.Normalisation.Mean;
        int bytesPerPixel = ImageFrame.BytesPerPixel(image.Format);

        for (int y = 0; y < height; y++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            for (int x = 0; x < width; x++)
            {
                int targetOffset = checked((y * width) + x);
                for (int channel = 0; channel < 3; channel++)
                {
                    int semanticBgrChannel = modelUsesBgr ? channel : 2 - channel;
                    int sourceChannel = image.Format switch
                    {
                        PixelFormat.Gray8 => 0,
                        PixelFormat.Bgr24 or PixelFormat.Bgra32 => semanticBgrChannel,
                        PixelFormat.Rgb24 or PixelFormat.Rgba32 => 2 - semanticBgrChannel,
                        _ => throw new ArgumentOutOfRangeException(nameof(image), "Unsupported pixel format."),
                    };

                    int sourceOffset = checked(
                        (y * image.Stride) + (x * bytesPerPixel) + sourceChannel);
                    tensor[(channel * planeSize) + targetOffset] =
                        (float)((source[sourceOffset] - mean[channel]) * scale);
                }
            }
        }

        return new SFacePreprocessedInput(tensor, [1, 3, height, width]);
    }
}
