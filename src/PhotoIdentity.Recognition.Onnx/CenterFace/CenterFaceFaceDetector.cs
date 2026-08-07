using System.Diagnostics;
using PhotoIdentity.Core.Geometry;
using PhotoIdentity.Core.Imaging;
using PhotoIdentity.Core.Recognition;
using PhotoIdentity.Recognition.Onnx.Models;

namespace PhotoIdentity.Recognition.Onnx.CenterFace;

public sealed record CenterFaceDetectorOptions
{
    public double ConfidenceThreshold { get; init; } = 0.5;
    public double NmsThreshold { get; init; } = 0.3;
    public int TopK { get; init; } = 5000;

    internal void Validate()
    {
        if (!double.IsFinite(ConfidenceThreshold) || ConfidenceThreshold < 0 || ConfidenceThreshold > 1)
        {
            throw new ArgumentOutOfRangeException(
                nameof(ConfidenceThreshold),
                "Confidence threshold must be between 0 and 1.");
        }

        if (!double.IsFinite(NmsThreshold) || NmsThreshold < 0 || NmsThreshold > 1)
        {
            throw new ArgumentOutOfRangeException(
                nameof(NmsThreshold),
                "NMS threshold must be between 0 and 1.");
        }

        if (TopK <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(TopK), "TopK must be positive.");
        }
    }
}

public sealed record CenterFaceDetectionResult(
    ModelDescriptor Descriptor,
    ImageSize TensorSize,
    IReadOnlyList<DetectedFaceCandidate> Faces,
    TimeSpan PreprocessingDuration,
    TimeSpan InferenceDuration,
    TimeSpan PostprocessingDuration)
{
    public TimeSpan TotalDuration =>
        PreprocessingDuration + InferenceDuration + PostprocessingDuration;
}

public sealed class CenterFaceFaceDetector : IFaceDetector, IDisposable
{
    private readonly ModelManifest _manifest;
    private readonly ICenterFaceInferenceSession _session;
    private readonly CenterFaceDetectorOptions _options;
    private readonly int _inputMultiple;
    private readonly int _maximumLongEdge;
    private bool _disposed;

    public CenterFaceFaceDetector(
        ModelManifest manifest,
        string modelPath,
        CenterFaceDetectorOptions? options = null)
        : this(manifest, new OnnxCenterFaceInferenceSession(modelPath), options)
    {
    }

    internal CenterFaceFaceDetector(
        ModelManifest manifest,
        ICenterFaceInferenceSession session,
        CenterFaceDetectorOptions? options = null)
    {
        ArgumentNullException.ThrowIfNull(manifest);
        ArgumentNullException.ThrowIfNull(session);
        ModelManifestValidator.Validate(manifest);

        if (manifest.Role != "faceDetection" || manifest.Format != "onnx")
        {
            throw new ModelManifestException(
                $"Model '{manifest.ModelId}' must be an ONNX face-detection model.");
        }

        if (manifest.Input.DataType != "float32" || manifest.Input.ColourOrder != "RGB")
        {
            throw new ModelManifestException(
                $"Model '{manifest.ModelId}' requires unsupported CenterFace input preprocessing.");
        }

        Descriptor = manifest.ToDescriptor();
        if (Descriptor.InputShapePolicy.Kind != ModelInputShapeKind.DynamicMultipleOf ||
            Descriptor.InputShapePolicy.MultipleOf is null ||
            Descriptor.InputShapePolicy.MaximumLongEdge is null)
        {
            throw new ModelManifestException(
                $"Model '{manifest.ModelId}' must declare a bounded dynamic-multiple-of input shape policy.");
        }

        _options = options ?? new CenterFaceDetectorOptions();
        _options.Validate();
        _manifest = manifest;
        _session = session;
        _inputMultiple = Descriptor.InputShapePolicy.MultipleOf.Value;
        _maximumLongEdge = Descriptor.InputShapePolicy.MaximumLongEdge.Value;
    }

    public ModelDescriptor Descriptor { get; }

    public Task<IReadOnlyList<DetectedFaceCandidate>> DetectAsync(
        ImageFrame image,
        CancellationToken cancellationToken)
    {
        CenterFaceDetectionResult result = DetectWithMetrics(image, cancellationToken);
        return Task.FromResult(result.Faces);
    }

    public Task<CenterFaceDetectionResult> DetectWithMetricsAsync(
        ImageFrame image,
        CancellationToken cancellationToken = default) =>
        Task.FromResult(DetectWithMetrics(image, cancellationToken));

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _session.Dispose();
        _disposed = true;
    }

    private CenterFaceDetectionResult DetectWithMetrics(
        ImageFrame image,
        CancellationToken cancellationToken)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        ArgumentNullException.ThrowIfNull(image);
        cancellationToken.ThrowIfCancellationRequested();

        long preprocessingStarted = Stopwatch.GetTimestamp();
        CenterFacePreprocessedInput input = CenterFacePreprocessor.Preprocess(
            image,
            _manifest,
            _inputMultiple,
            _maximumLongEdge,
            cancellationToken);
        TimeSpan preprocessingDuration = Stopwatch.GetElapsedTime(preprocessingStarted);

        long inferenceStarted = Stopwatch.GetTimestamp();
        IReadOnlyDictionary<string, CenterFaceTensor> outputs = _session.Run(
            input.Data,
            input.Shape,
            cancellationToken);
        TimeSpan inferenceDuration = Stopwatch.GetElapsedTime(inferenceStarted);

        cancellationToken.ThrowIfCancellationRequested();
        long postprocessingStarted = Stopwatch.GetTimestamp();
        IReadOnlyList<DetectedFaceCandidate> faces = CenterFaceOutputParser.Parse(
            outputs,
            input.TensorSize,
            _options);
        TimeSpan postprocessingDuration = Stopwatch.GetElapsedTime(postprocessingStarted);

        return new CenterFaceDetectionResult(
            Descriptor,
            input.TensorSize,
            faces,
            preprocessingDuration,
            inferenceDuration,
            postprocessingDuration);
    }
}

internal sealed record CenterFacePreprocessedInput(
    float[] Data,
    long[] Shape,
    ImageSize TensorSize);

internal static class CenterFacePreprocessor
{
    public static CenterFacePreprocessedInput Preprocess(
        ImageFrame image,
        ModelManifest manifest,
        int multipleOf,
        int maxLongEdge,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(image);
        ArgumentNullException.ThrowIfNull(manifest);
        if (multipleOf <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(multipleOf));
        }

        if (maxLongEdge <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(maxLongEdge));
        }

        int sourceLongEdge = Math.Max(image.Size.Width, image.Size.Height);
        double downscale = Math.Min(1.0, (double)maxLongEdge / sourceLongEdge);
        int boundedWidth = Math.Max(1, (int)Math.Ceiling(image.Size.Width * downscale));
        int boundedHeight = Math.Max(1, (int)Math.Ceiling(image.Size.Height * downscale));
        int targetWidth = RoundUp(boundedWidth, multipleOf);
        int targetHeight = RoundUp(boundedHeight, multipleOf);
        ImageSize tensorSize = new(targetWidth, targetHeight);

        float[] tensor = new float[checked(3 * targetWidth * targetHeight)];
        ReadOnlySpan<byte> source = image.Data;
        double normalisationScale = manifest.Input.Normalisation.Scale;
        double[] mean = manifest.Input.Normalisation.Mean;
        int planeSize = checked(targetWidth * targetHeight);

        for (int targetY = 0; targetY < targetHeight; targetY++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            double sourceY = (((targetY + 0.5) * image.Size.Height) / targetHeight) - 0.5;
            int y0 = Math.Clamp((int)Math.Floor(sourceY), 0, image.Size.Height - 1);
            int y1 = Math.Clamp(y0 + 1, 0, image.Size.Height - 1);
            double yWeight = Math.Clamp(sourceY - Math.Floor(sourceY), 0, 1);

            for (int targetX = 0; targetX < targetWidth; targetX++)
            {
                double sourceX = (((targetX + 0.5) * image.Size.Width) / targetWidth) - 0.5;
                int x0 = Math.Clamp((int)Math.Floor(sourceX), 0, image.Size.Width - 1);
                int x1 = Math.Clamp(x0 + 1, 0, image.Size.Width - 1);
                double xWeight = Math.Clamp(sourceX - Math.Floor(sourceX), 0, 1);
                int targetOffset = checked((targetY * targetWidth) + targetX);

                for (int channel = 0; channel < 3; channel++)
                {
                    double top = Lerp(
                        ReadRgbChannel(source, image, x0, y0, channel),
                        ReadRgbChannel(source, image, x1, y0, channel),
                        xWeight);
                    double bottom = Lerp(
                        ReadRgbChannel(source, image, x0, y1, channel),
                        ReadRgbChannel(source, image, x1, y1, channel),
                        xWeight);
                    double value = Lerp(top, bottom, yWeight);
                    tensor[(channel * planeSize) + targetOffset] =
                        (float)((value - mean[channel]) * normalisationScale);
                }
            }
        }

        return new CenterFacePreprocessedInput(
            tensor,
            [1, 3, targetHeight, targetWidth],
            tensorSize);
    }

    private static int RoundUp(int value, int multiple)
    {
        int remainder = value % multiple;
        return remainder == 0
            ? value
            : checked(value + multiple - remainder);
    }

    private static double ReadRgbChannel(
        ReadOnlySpan<byte> data,
        ImageFrame image,
        int x,
        int y,
        int outputChannel)
    {
        int bytesPerPixel = ImageFrame.BytesPerPixel(image.Format);
        int sourceChannel = image.Format switch
        {
            PixelFormat.Gray8 => 0,
            PixelFormat.Rgb24 or PixelFormat.Rgba32 => outputChannel,
            PixelFormat.Bgr24 or PixelFormat.Bgra32 => 2 - outputChannel,
            _ => throw new ArgumentOutOfRangeException(nameof(image), "Unsupported pixel format."),
        };

        int offset = checked((y * image.Stride) + (x * bytesPerPixel) + sourceChannel);
        return data[offset];
    }

    private static double Lerp(double left, double right, double weight) =>
        left + ((right - left) * weight);
}
