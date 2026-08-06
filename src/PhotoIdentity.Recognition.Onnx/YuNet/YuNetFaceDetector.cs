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
    public YuNetDetectorPipelineMode PipelineMode { get; init; } = YuNetDetectorPipelineMode.SinglePass;
    public int TileSize { get; init; } = 1024;
    public double TileOverlap { get; init; } = 0.2;
    public double MergeNmsThreshold { get; init; } = 0.3;

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

        if (!Enum.IsDefined(PipelineMode))
        {
            throw new ArgumentOutOfRangeException(nameof(PipelineMode), "The detector pipeline mode is unsupported.");
        }

        if (TileSize <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(TileSize), "The tile size must be positive.");
        }

        if (!double.IsFinite(TileOverlap) || TileOverlap < 0 || TileOverlap >= 1)
        {
            throw new ArgumentOutOfRangeException(
                nameof(TileOverlap),
                "The tile overlap must be at least zero and less than one.");
        }

        if (!double.IsFinite(MergeNmsThreshold) || MergeNmsThreshold < 0 || MergeNmsThreshold > 1)
        {
            throw new ArgumentOutOfRangeException(
                nameof(MergeNmsThreshold),
                "The merge NMS threshold must be between 0 and 1.");
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

        return _options.PipelineMode == YuNetDetectorPipelineMode.MultiScale
            ? DetectMultiScale(image, cancellationToken)
            : DetectSinglePass(image, cancellationToken);
    }

    private YuNetDetectionResult DetectSinglePass(
        ImageFrame image,
        CancellationToken cancellationToken)
    {
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

    private YuNetDetectionResult DetectMultiScale(
        ImageFrame image,
        CancellationToken cancellationToken)
    {
        List<YuNetSourceRegion> passes =
        [
            YuNetSourceRegion.FullImage(image.Size),
            .. YuNetTilePlanner.CreateTiles(image.Size, _options.TileSize, _options.TileOverlap),
        ];
        List<DetectedFaceCandidate> mappedDetections = [];
        TimeSpan preprocessingDuration = TimeSpan.Zero;
        TimeSpan inferenceDuration = TimeSpan.Zero;
        TimeSpan postprocessingDuration = TimeSpan.Zero;

        foreach (YuNetSourceRegion pass in passes)
        {
            cancellationToken.ThrowIfCancellationRequested();

            long preprocessingStarted = Stopwatch.GetTimestamp();
            YuNetPreprocessedInput input = YuNetPreprocessor.Preprocess(
                image,
                _manifest,
                pass,
                preserveAspectRatio: true,
                cancellationToken);
            preprocessingDuration += Stopwatch.GetElapsedTime(preprocessingStarted);

            long inferenceStarted = Stopwatch.GetTimestamp();
            IReadOnlyDictionary<string, YuNetTensor> outputs = _session.Run(
                input.Data,
                input.Shape,
                cancellationToken);
            inferenceDuration += Stopwatch.GetElapsedTime(inferenceStarted);

            cancellationToken.ThrowIfCancellationRequested();
            long postprocessingStarted = Stopwatch.GetTimestamp();
            IReadOnlyList<DetectedFaceCandidate> passDetections = YuNetOutputParser.Parse(
                outputs,
                Descriptor.InputSize,
                _options);
            foreach (DetectedFaceCandidate detection in passDetections)
            {
                DetectedFaceCandidate? mapped = input.Transform.MapToSource(detection);
                if (mapped is not null)
                {
                    mappedDetections.Add(mapped);
                }
            }

            postprocessingDuration += Stopwatch.GetElapsedTime(postprocessingStarted);
        }

        long mergeStarted = Stopwatch.GetTimestamp();
        IReadOnlyList<DetectedFaceCandidate> faces = YuNetDetectionMerger.Merge(
            mappedDetections,
            _options.MergeNmsThreshold,
            _options.TopK);
        postprocessingDuration += Stopwatch.GetElapsedTime(mergeStarted);

        return new YuNetDetectionResult(
            Descriptor,
            faces,
            preprocessingDuration,
            inferenceDuration,
            postprocessingDuration);
    }
}

internal sealed record YuNetPreprocessedInput(
    float[] Data,
    long[] Shape,
    YuNetPreprocessingTransform Transform);

internal static class YuNetPreprocessor
{
    public static YuNetPreprocessedInput Preprocess(
        ImageFrame image,
        ModelManifest manifest,
        CancellationToken cancellationToken) =>
        Preprocess(
            image,
            manifest,
            YuNetSourceRegion.FullImage(image.Size),
            preserveAspectRatio: false,
            cancellationToken);

    public static YuNetPreprocessedInput Preprocess(
        ImageFrame image,
        ModelManifest manifest,
        YuNetSourceRegion sourceRegion,
        bool preserveAspectRatio,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(image);
        ArgumentNullException.ThrowIfNull(manifest);
        sourceRegion.ValidateWithin(image.Size);

        int targetWidth = manifest.Input.Width;
        int targetHeight = manifest.Input.Height;
        float[] tensor = new float[checked(3 * targetWidth * targetHeight)];
        ReadOnlySpan<byte> source = image.Data;
        bool modelUsesBgr = manifest.Input.ColourOrder == "BGR";
        double normalisationScale = manifest.Input.Normalisation.Scale;
        double[] mean = manifest.Input.Normalisation.Mean;
        int planeSize = checked(targetWidth * targetHeight);

        double scaleX;
        double scaleY;
        double offsetX;
        double offsetY;
        if (preserveAspectRatio)
        {
            double scale = Math.Min(
                (double)targetWidth / sourceRegion.Width,
                (double)targetHeight / sourceRegion.Height);
            scaleX = scale;
            scaleY = scale;
            offsetX = (targetWidth - (sourceRegion.Width * scale)) / 2;
            offsetY = (targetHeight - (sourceRegion.Height * scale)) / 2;
        }
        else
        {
            scaleX = (double)targetWidth / sourceRegion.Width;
            scaleY = (double)targetHeight / sourceRegion.Height;
            offsetX = 0;
            offsetY = 0;
        }

        for (int channel = 0; channel < 3; channel++)
        {
            float paddingValue = (float)((0 - mean[channel]) * normalisationScale);
            Array.Fill(tensor, paddingValue, channel * planeSize, planeSize);
        }

        double contentRight = offsetX + (sourceRegion.Width * scaleX);
        double contentBottom = offsetY + (sourceRegion.Height * scaleY);
        for (int targetY = 0; targetY < targetHeight; targetY++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            double targetCentreY = targetY + 0.5;
            if (targetCentreY < offsetY || targetCentreY >= contentBottom)
            {
                continue;
            }

            double localSourceY = ((targetCentreY - offsetY) / scaleY) - 0.5;
            int y0 = Math.Clamp((int)Math.Floor(localSourceY), 0, sourceRegion.Height - 1);
            int y1 = Math.Clamp(y0 + 1, 0, sourceRegion.Height - 1);
            double yWeight = Math.Clamp(localSourceY - Math.Floor(localSourceY), 0, 1);

            for (int targetX = 0; targetX < targetWidth; targetX++)
            {
                double targetCentreX = targetX + 0.5;
                if (targetCentreX < offsetX || targetCentreX >= contentRight)
                {
                    continue;
                }

                double localSourceX = ((targetCentreX - offsetX) / scaleX) - 0.5;
                int x0 = Math.Clamp((int)Math.Floor(localSourceX), 0, sourceRegion.Width - 1);
                int x1 = Math.Clamp(x0 + 1, 0, sourceRegion.Width - 1);
                double xWeight = Math.Clamp(localSourceX - Math.Floor(localSourceX), 0, 1);
                int targetOffset = checked((targetY * targetWidth) + targetX);

                for (int channel = 0; channel < 3; channel++)
                {
                    double top = Lerp(
                        ReadChannel(
                            source,
                            image,
                            sourceRegion.X + x0,
                            sourceRegion.Y + y0,
                            channel,
                            modelUsesBgr),
                        ReadChannel(
                            source,
                            image,
                            sourceRegion.X + x1,
                            sourceRegion.Y + y0,
                            channel,
                            modelUsesBgr),
                        xWeight);
                    double bottom = Lerp(
                        ReadChannel(
                            source,
                            image,
                            sourceRegion.X + x0,
                            sourceRegion.Y + y1,
                            channel,
                            modelUsesBgr),
                        ReadChannel(
                            source,
                            image,
                            sourceRegion.X + x1,
                            sourceRegion.Y + y1,
                            channel,
                            modelUsesBgr),
                        xWeight);
                    double value = Lerp(top, bottom, yWeight);
                    tensor[(channel * planeSize) + targetOffset] =
                        (float)((value - mean[channel]) * normalisationScale);
                }
            }
        }

        return new YuNetPreprocessedInput(
            tensor,
            [1, 3, targetHeight, targetWidth],
            new YuNetPreprocessingTransform(
                image.Size,
                sourceRegion,
                new ImageSize(targetWidth, targetHeight),
                scaleX,
                scaleY,
                offsetX,
                offsetY));
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
