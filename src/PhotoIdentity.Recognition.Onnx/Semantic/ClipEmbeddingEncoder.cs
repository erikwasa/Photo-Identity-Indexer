using Microsoft.ML.OnnxRuntime;
using Microsoft.ML.OnnxRuntime.Tensors;
using PhotoIdentity.Core.Imaging;

namespace PhotoIdentity.Recognition.Onnx.Semantic;

public sealed record ClipEmbeddingResult(
    IReadOnlyList<float> Vector,
    TimeSpan PreprocessingDuration,
    TimeSpan InferenceDuration,
    TimeSpan PostprocessingDuration)
{
    public int Dimensions => Vector.Count;
    public TimeSpan TotalDuration =>
        PreprocessingDuration + InferenceDuration + PostprocessingDuration;
}

/// <summary>
/// Experimental CLIP dual-encoder adapter for WI-0127. It exposes L2-normalized whole-image
/// and text embeddings without persisting them or assigning controlled-vocabulary labels.
/// </summary>
public sealed class ClipEmbeddingEncoder : IDisposable
{
    public const string ModelId = "openai/clip-vit-base-patch32";

    private readonly ClipBpeTokenizer _tokenizer;
    private readonly IClipEmbeddingSession _session;
    private readonly long[] _dummyInputIds;
    private readonly long[] _dummyAttentionMask;
    private bool _disposed;

    public ClipEmbeddingEncoder(
        string modelPath,
        string vocabularyPath,
        string mergesPath)
        : this(
            ClipBpeTokenizer.Load(vocabularyPath, mergesPath),
            new OnnxClipEmbeddingSession(modelPath))
    {
    }

    internal ClipEmbeddingEncoder(
        ClipBpeTokenizer tokenizer,
        IClipEmbeddingSession session)
    {
        ArgumentNullException.ThrowIfNull(tokenizer);
        ArgumentNullException.ThrowIfNull(session);
        _tokenizer = tokenizer;
        _session = session;
        (_dummyInputIds, _dummyAttentionMask) =
            BuildTextInput("a photo");
    }

    public ClipEmbeddingResult EncodeImage(
        ImageFrame image,
        CancellationToken cancellationToken = default)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        ArgumentNullException.ThrowIfNull(image);
        cancellationToken.ThrowIfCancellationRequested();

        long preprocessingStarted = System.Diagnostics.Stopwatch.GetTimestamp();
        float[] pixelValues =
            ClipImagePreprocessor.Preprocess(image, cancellationToken);
        TimeSpan preprocessingDuration =
            System.Diagnostics.Stopwatch.GetElapsedTime(preprocessingStarted);

        long inferenceStarted = System.Diagnostics.Stopwatch.GetTimestamp();
        float[] raw = _session.RunImage(
            _dummyInputIds,
            _dummyAttentionMask,
            pixelValues,
            cancellationToken);
        TimeSpan inferenceDuration =
            System.Diagnostics.Stopwatch.GetElapsedTime(inferenceStarted);

        long postprocessingStarted = System.Diagnostics.Stopwatch.GetTimestamp();
        float[] normalized = Normalize(raw);
        TimeSpan postprocessingDuration =
            System.Diagnostics.Stopwatch.GetElapsedTime(postprocessingStarted);

        return new(
            normalized,
            preprocessingDuration,
            inferenceDuration,
            postprocessingDuration);
    }

    public ClipEmbeddingResult EncodeText(
        string text,
        CancellationToken cancellationToken = default)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        ArgumentException.ThrowIfNullOrWhiteSpace(text);
        cancellationToken.ThrowIfCancellationRequested();

        long preprocessingStarted = System.Diagnostics.Stopwatch.GetTimestamp();
        (long[] inputIds, long[] attentionMask) =
            BuildTextInput(text.Trim());
        float[] dummyPixels =
            new float[3 * ClipZeroShotTagger.ImageSize * ClipZeroShotTagger.ImageSize];
        TimeSpan preprocessingDuration =
            System.Diagnostics.Stopwatch.GetElapsedTime(preprocessingStarted);

        long inferenceStarted = System.Diagnostics.Stopwatch.GetTimestamp();
        float[] raw = _session.RunText(
            inputIds,
            attentionMask,
            dummyPixels,
            cancellationToken);
        TimeSpan inferenceDuration =
            System.Diagnostics.Stopwatch.GetElapsedTime(inferenceStarted);

        long postprocessingStarted = System.Diagnostics.Stopwatch.GetTimestamp();
        float[] normalized = Normalize(raw);
        TimeSpan postprocessingDuration =
            System.Diagnostics.Stopwatch.GetElapsedTime(postprocessingStarted);

        return new(
            normalized,
            preprocessingDuration,
            inferenceDuration,
            postprocessingDuration);
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _session.Dispose();
        _disposed = true;
    }

    private (long[] InputIds, long[] AttentionMask) BuildTextInput(string text)
    {
        int[] tokens = _tokenizer.Encode(text, ClipZeroShotTagger.ContextLength);
        long[] inputIds = new long[ClipZeroShotTagger.ContextLength];
        long[] attentionMask = new long[ClipZeroShotTagger.ContextLength];
        Array.Fill(inputIds, _tokenizer.EndOfTextId);
        for (int index = 0; index < tokens.Length; index++)
        {
            inputIds[index] = tokens[index];
            attentionMask[index] = 1;
        }

        return (inputIds, attentionMask);
    }

    private static float[] Normalize(IReadOnlyList<float> vector)
    {
        ArgumentNullException.ThrowIfNull(vector);
        if (vector.Count == 0 || vector.Any(value => !float.IsFinite(value)))
        {
            throw new ClipOutputException(
                "CLIP embedding output must contain finite values.");
        }

        double squaredNorm = vector.Sum(value => (double)value * value);
        if (!double.IsFinite(squaredNorm) || squaredNorm <= 0)
        {
            throw new ClipOutputException(
                "CLIP embedding output must have a positive finite norm.");
        }

        double norm = Math.Sqrt(squaredNorm);
        return vector.Select(value => (float)(value / norm)).ToArray();
    }
}

internal interface IClipEmbeddingSession : IDisposable
{
    float[] RunImage(
        long[] inputIds,
        long[] attentionMask,
        float[] pixelValues,
        CancellationToken cancellationToken);

    float[] RunText(
        long[] inputIds,
        long[] attentionMask,
        float[] pixelValues,
        CancellationToken cancellationToken);
}

internal sealed class OnnxClipEmbeddingSession : IClipEmbeddingSession
{
    private readonly InferenceSession _session;
    private readonly bool _usesAttentionMask;
    private readonly bool _usesPositionIds;

    public OnnxClipEmbeddingSession(string modelPath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(modelPath);
        _session = new InferenceSession(modelPath);

        HashSet<string> inputNames =
            _session.InputNames.ToHashSet(StringComparer.Ordinal);
        if (!inputNames.Contains("input_ids") ||
            !inputNames.Contains("pixel_values"))
        {
            _session.Dispose();
            throw new ClipOutputException(
                "The CLIP ONNX model must expose input_ids and pixel_values inputs.");
        }

        _usesAttentionMask = inputNames.Contains("attention_mask");
        _usesPositionIds = inputNames.Contains("position_ids");
        string[] supported = _usesAttentionMask && _usesPositionIds
            ? ["input_ids", "attention_mask", "position_ids", "pixel_values"]
            : _usesAttentionMask
                ? ["input_ids", "attention_mask", "pixel_values"]
                : _usesPositionIds
                    ? ["input_ids", "position_ids", "pixel_values"]
                    : ["input_ids", "pixel_values"];
        string[] unsupported = inputNames
            .Except(supported, StringComparer.Ordinal)
            .OrderBy(value => value, StringComparer.Ordinal)
            .ToArray();
        if (unsupported.Length > 0)
        {
            _session.Dispose();
            throw new ClipOutputException(
                $"The CLIP ONNX model exposes unsupported input(s): {string.Join(", ", unsupported)}.");
        }

        if (!_session.OutputNames.Contains("image_embeds", StringComparer.Ordinal) ||
            !_session.OutputNames.Contains("text_embeds", StringComparer.Ordinal))
        {
            _session.Dispose();
            throw new ClipOutputException(
                "The CLIP ONNX model must expose image_embeds and text_embeds outputs.");
        }
    }

    public float[] RunImage(
        long[] inputIds,
        long[] attentionMask,
        float[] pixelValues,
        CancellationToken cancellationToken) =>
        Run(
            inputIds,
            attentionMask,
            pixelValues,
            "image_embeds",
            cancellationToken);

    public float[] RunText(
        long[] inputIds,
        long[] attentionMask,
        float[] pixelValues,
        CancellationToken cancellationToken) =>
        Run(
            inputIds,
            attentionMask,
            pixelValues,
            "text_embeds",
            cancellationToken);

    public void Dispose() => _session.Dispose();

    private float[] Run(
        long[] inputIds,
        long[] attentionMask,
        float[] pixelValues,
        string outputName,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(inputIds);
        ArgumentNullException.ThrowIfNull(attentionMask);
        ArgumentNullException.ThrowIfNull(pixelValues);
        cancellationToken.ThrowIfCancellationRequested();

        if (inputIds.Length != ClipZeroShotTagger.ContextLength ||
            attentionMask.Length != ClipZeroShotTagger.ContextLength)
        {
            throw new ArgumentException(
                "CLIP text tensors must contain one 77-token row.");
        }
        if (pixelValues.Length !=
            3 * ClipZeroShotTagger.ImageSize * ClipZeroShotTagger.ImageSize)
        {
            throw new ArgumentException(
                "CLIP image tensor must contain one 3x224x224 image.");
        }

        long[] textShape = [1, ClipZeroShotTagger.ContextLength];
        long[] imageShape =
            [1, 3, ClipZeroShotTagger.ImageSize, ClipZeroShotTagger.ImageSize];
        long[] positionIds = _usesPositionIds
            ? Enumerable.Range(0, ClipZeroShotTagger.ContextLength)
                .Select(value => (long)value)
                .ToArray()
            : [];

        using OrtValue idsValue =
            OrtValue.CreateTensorValueFromMemory(inputIds, textShape);
        using OrtValue imageValue =
            OrtValue.CreateTensorValueFromMemory(pixelValues, imageShape);
        using OrtValue? maskValue = _usesAttentionMask
            ? OrtValue.CreateTensorValueFromMemory(attentionMask, textShape)
            : null;
        using OrtValue? positionsValue = _usesPositionIds
            ? OrtValue.CreateTensorValueFromMemory(positionIds, textShape)
            : null;

        List<string> inputNames = ["input_ids"];
        List<OrtValue> inputValues = [idsValue];
        if (_usesAttentionMask)
        {
            inputNames.Add("attention_mask");
            inputValues.Add(maskValue!);
        }
        if (_usesPositionIds)
        {
            inputNames.Add("position_ids");
            inputValues.Add(positionsValue!);
        }
        inputNames.Add("pixel_values");
        inputValues.Add(imageValue);

        using RunOptions runOptions = new();
        using IDisposableReadOnlyCollection<OrtValue> outputs = _session.Run(
            runOptions,
            inputNames.ToArray(),
            inputValues.ToArray(),
            [outputName]);

        cancellationToken.ThrowIfCancellationRequested();
        OrtValue output = outputs.Single();
        OrtTensorTypeAndShapeInfo info = output.GetTensorTypeAndShape();
        if (info.ElementDataType != TensorElementType.Float)
        {
            throw new ClipOutputException(
                $"CLIP output '{outputName}' must contain float32 values.");
        }

        long[] shape = info.Shape;
        if (shape.Length != 2 || shape[0] != 1 || shape[1] < 1)
        {
            throw new ClipOutputException(
                $"CLIP output '{outputName}' must have shape [1,D], but returned [{string.Join(",", shape)}].");
        }

        return output.GetTensorDataAsSpan<float>().ToArray();
    }
}
