using System.Diagnostics;
using PhotoIdentity.Core.Geometry;
using PhotoIdentity.Core.Imaging;
using PhotoIdentity.Core.Recognition;
using PhotoIdentity.Recognition.Onnx.Models;

namespace PhotoIdentity.Recognition.Onnx.YuNet;

public sealed record YuNetDetectorOptions
{
    public double ConfidenceThreshold { get; init; } = 0.9;
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

public sealed record YuNetDetectionResult(
    ModelDescriptor Descriptor,
    IReadOnlyList<DetectedFaceCandidate> Faces,
    TimeSpan PreprocessingDuration,
    TimeSpan InferenceDuration,
    TimeSpan PostprocessingDuration)
{
    public TimeSpan TotalDuration =>
        PreprocessingDuration + InferenceDuration + PostprocessingDuration;
}

public sealed class YuNetFaceDetector : IFaceDetector, IDisposable
{
    private readonly ModelManifest _manifest;
    private readonly IYuNetInferenceSession _session;
    private readonly YuNetDetectorOptions _options;
    private bool _disposed;

    public YuNetFaceDetector(
        ModelManifest manifest,
        string modelPath,
        YuNetDetectorOptions? options = null)
        : this(manifest, new OnnxYuNetInferenceSession(modelPath), options)
    {
    }

    internal YuNetFaceDetector(
        ModelManifest manifest,
        IYuNetInferenceSession session,
        YuNetDetectorOptions? options = null)
    {
        ArgumentNullException.ThrowIfNull(manifest);
        ArgumentNullException.ThrowIfNull(session);
        ModelManifestValidator.Validate(manifest);

        if (manifest.Role != "faceDetection" || manifest.Format != "onnx")
        {
            throw new ModelManifestException(
                $"Model '{manifest.ModelId}' must be an ONNX face-detection model.");
        }

        if (manifest.Input.DataType != "float32" ||
            manifest.Input.ColourOrder is not ("BGR" or "RGB"))
        {
            throw new ModelManifestException(
                $"Model '{manifest.ModelId}' requires unsupported input preprocessing.");
        }

        _options = options ?? new YuNetDetectorOptions();
        _options.Validate();
        _manifest = manifest;
        _session = session;
        Descriptor = manifest.ToDescriptor();
    }

    public ModelDescriptor Descriptor { get; }

    public Task<IReadOnlyList<DetectedFaceCandidate>> DetectAsync(
        ImageFrame image,
        CancellationToken cancellationToken)
    {
        YuNetDetectionResult result = DetectWithMetrics(image, cancellationToken);
        return Task.FromResult(result.Faces);
    }

    public Task<YuNetDetectionResult> DetectWithMetricsAsync(
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

    private YuNetDetectionResult DetectWithMetrics(
        ImageFrame image,
        CancellationToken cancellationToken)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        ArgumentNullException.ThrowIfNull(image);
        cancellationToken.ThrowIfCancellationRequested();

        long preprocessingStarted = Stopwatch.GetTimestamp();
        YuNetPreprocessedInput input = YuNetPreprocessor.Preprocess(
            image,
            _manifest,
            cancellationToken);
        TimeSpan preprocessingDuration = Stopwatch.GetElapsedTime(preprocessingStarted);

        long inferenceStarted = Stopwatch.GetTimestamp();
        IReadOnlyDictionary<string, YuNetTensor> outputs = _session.Run(
            input.Data,
            input.Shape,
            cancellationToken);
        TimeSpan inferenceDuration = Stopwatch.GetElapsedTime(inferenceStarted);

        cancellationToken.ThrowIfCancellationRequested();
        long postprocessingStarted = Stopwatch.GetTimestamp();
        IReadOnlyList<DetectedFaceCandidate> faces = YuNetOutputParser.Parse(
            outputs,
            Descriptor.InputSize,
            _options);
        TimeSpan postprocessingDuration = Stopwatch.GetElapsedTime(postprocessingStarted);

        return new YuNetDetectionResult(
            Descriptor,
            faces,
            preprocessingDuration,
            inferenceDuration,
            postprocessingDuration);
    }
}

internal sealed record YuNetPreprocessedInput(float[] Data, long[] Shape);

internal static class YuNetPreprocessor
{
    public static YuNetPreprocessedInput Preprocess(
        ImageFrame image,
        ModelManifest manifest,
        CancellationToken cancellationToken)
    {
        int targetWidth = manifest.Input.Width;
        int targetHeight = manifest.Input.Height;
        float[] tensor = new float[checked(3 * targetWidth * targetHeight)];
        ReadOnlySpan<byte> source = image.Data;
        bool modelUsesBgr = manifest.Input.ColourOrder == "BGR";
        double scale = manifest.Input.Normalisation.Scale;
        double[] mean = manifest.Input.Normalisation.Mean;
        int planeSize = checked(targetWidth * targetHeight);

        for (int targetY = 0; targetY < targetHeight; targetY++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            double sourceY = ((targetY + 0.5) * image.Size.Height / targetHeight) - 0.5;
            int y0 = Math.Clamp((int)Math.Floor(sourceY), 0, image.Size.Height - 1);
            int y1 = Math.Clamp(y0 + 1, 0, image.Size.Height - 1);
            double yWeight = Math.Clamp(sourceY - Math.Floor(sourceY), 0, 1);

            for (int targetX = 0; targetX < targetWidth; targetX++)
            {
                double sourceX = ((targetX + 0.5) * image.Size.Width / targetWidth) - 0.5;
                int x0 = Math.Clamp((int)Math.Floor(sourceX), 0, image.Size.Width - 1);
                int x1 = Math.Clamp(x0 + 1, 0, image.Size.Width - 1);
                double xWeight = Math.Clamp(sourceX - Math.Floor(sourceX), 0, 1);
                int targetOffset = checked((targetY * targetWidth) + targetX);

                for (int channel = 0; channel < 3; channel++)
                {
                    double top = Lerp(
                        ReadChannel(source, image, x0, y0, channel, modelUsesBgr),
                        ReadChannel(source, image, x1, y0, channel, modelUsesBgr),
                        xWeight);
                    double bottom = Lerp(
                        ReadChannel(source, image, x0, y1, channel, modelUsesBgr),
                        ReadChannel(source, image, x1, y1, channel, modelUsesBgr),
                        xWeight);
                    double value = Lerp(top, bottom, yWeight);
                    tensor[(channel * planeSize) + targetOffset] =
                        (float)((value - mean[channel]) * scale);
                }
            }
        }

        return new YuNetPreprocessedInput(
            tensor,
            [1, 3, targetHeight, targetWidth]);
    }

    private static double ReadChannel(
        ReadOnlySpan<byte> data,
        ImageFrame image,
        int x,
        int y,
        int outputChannel,
        bool modelUsesBgr)
    {
        int semanticBgrChannel = modelUsesBgr ? outputChannel : 2 - outputChannel;
        int bytesPerPixel = ImageFrame.BytesPerPixel(image.Format);
        int sourceChannel = image.Format switch
        {
            PixelFormat.Gray8 => 0,
            PixelFormat.Bgr24 or PixelFormat.Bgra32 => semanticBgrChannel,
            PixelFormat.Rgb24 or PixelFormat.Rgba32 => 2 - semanticBgrChannel,
            _ => throw new ArgumentOutOfRangeException(nameof(image), "Unsupported pixel format."),
        };

        int offset = checked((y * image.Stride) + (x * bytesPerPixel) + sourceChannel);
        return data[offset];
    }

    private static double Lerp(double left, double right, double weight) =>
        left + ((right - left) * weight);
}
