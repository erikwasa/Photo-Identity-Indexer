using System.Net;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using Microsoft.ML.OnnxRuntime;
using Microsoft.ML.OnnxRuntime.Tensors;
using OpenCvSharp;
using PhotoIdentity.Core.Imaging;

namespace PhotoIdentity.Recognition.Onnx.Semantic;

public sealed record ClipVisibleContentConcept(
    string Id,
    string Prompt);

public sealed record ClipVisibleContentScore(
    string ConceptId,
    string Prompt,
    float Probability);

public sealed record ClipZeroShotResult(
    IReadOnlyList<ClipVisibleContentScore> Scores,
    TimeSpan PreprocessingDuration,
    TimeSpan InferenceDuration,
    TimeSpan PostprocessingDuration)
{
    public TimeSpan TotalDuration =>
        PreprocessingDuration + InferenceDuration + PostprocessingDuration;
}

public sealed class ClipZeroShotTagger : IDisposable
{
    public const int ContextLength = 77;
    public const int ImageSize = 224;
    public const string TokenizerVersion = "openai-clip-byte-bpe-v1";
    public const string ImagePreprocessingVersion =
        "clip-rgb-opencv-cubic-shortest-edge-center-crop-224-v1";

    private readonly ClipBpeTokenizer _tokenizer;
    private readonly IClipInferenceSession _session;
    private bool _disposed;

    public ClipZeroShotTagger(
        string modelPath,
        string vocabularyPath,
        string mergesPath)
        : this(
            ClipBpeTokenizer.Load(vocabularyPath, mergesPath),
            new OnnxClipInferenceSession(modelPath))
    {
    }

    internal ClipZeroShotTagger(
        ClipBpeTokenizer tokenizer,
        IClipInferenceSession session)
    {
        ArgumentNullException.ThrowIfNull(tokenizer);
        ArgumentNullException.ThrowIfNull(session);
        _tokenizer = tokenizer;
        _session = session;
    }

    public ClipZeroShotResult Score(
        ImageFrame image,
        IReadOnlyList<ClipVisibleContentConcept> concepts,
        CancellationToken cancellationToken = default)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        ArgumentNullException.ThrowIfNull(image);
        ArgumentNullException.ThrowIfNull(concepts);
        cancellationToken.ThrowIfCancellationRequested();
        if (concepts.Count < 2)
        {
            throw new ArgumentException(
                "CLIP visible-content evaluation requires at least two concepts.",
                nameof(concepts));
        }

        HashSet<string> ids = new(StringComparer.Ordinal);
        foreach (ClipVisibleContentConcept concept in concepts)
        {
            if (string.IsNullOrWhiteSpace(concept.Id) ||
                string.IsNullOrWhiteSpace(concept.Prompt))
            {
                throw new ArgumentException(
                    "Every visible-content concept requires a non-empty id and prompt.",
                    nameof(concepts));
            }

            if (!ids.Add(concept.Id.Trim()))
            {
                throw new ArgumentException(
                    $"Visible-content concept id '{concept.Id}' is duplicated.",
                    nameof(concepts));
            }
        }

        long preprocessingStarted = System.Diagnostics.Stopwatch.GetTimestamp();
        (long[] inputIds, long[] attentionMask) = BuildTextInputs(
            concepts,
            cancellationToken);
        float[] pixelValues = ClipImagePreprocessor.Preprocess(
            image,
            cancellationToken);
        TimeSpan preprocessingDuration =
            System.Diagnostics.Stopwatch.GetElapsedTime(preprocessingStarted);

        long inferenceStarted = System.Diagnostics.Stopwatch.GetTimestamp();
        float[] logits = _session.Run(
            inputIds,
            attentionMask,
            concepts.Count,
            pixelValues,
            cancellationToken);
        TimeSpan inferenceDuration =
            System.Diagnostics.Stopwatch.GetElapsedTime(inferenceStarted);

        cancellationToken.ThrowIfCancellationRequested();
        long postprocessingStarted = System.Diagnostics.Stopwatch.GetTimestamp();
        if (logits.Length != concepts.Count)
        {
            throw new ClipOutputException(
                $"CLIP returned {logits.Length} image/text logits for {concepts.Count} concepts.");
        }

        float[] probabilities = Softmax(logits);
        ClipVisibleContentScore[] scores = concepts
            .Select((concept, index) => new ClipVisibleContentScore(
                concept.Id.Trim(),
                concept.Prompt.Trim(),
                probabilities[index]))
            .OrderByDescending(score => score.Probability)
            .ThenBy(score => score.ConceptId, StringComparer.Ordinal)
            .ToArray();
        TimeSpan postprocessingDuration =
            System.Diagnostics.Stopwatch.GetElapsedTime(postprocessingStarted);

        return new ClipZeroShotResult(
            scores,
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

    private (long[] InputIds, long[] AttentionMask) BuildTextInputs(
        IReadOnlyList<ClipVisibleContentConcept> concepts,
        CancellationToken cancellationToken)
    {
        long[] inputIds = new long[checked(concepts.Count * ContextLength)];
        long[] attentionMask = new long[inputIds.Length];
        Array.Fill(inputIds, _tokenizer.EndOfTextId);

        for (int conceptIndex = 0; conceptIndex < concepts.Count; conceptIndex++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            int[] tokens = _tokenizer.Encode(concepts[conceptIndex].Prompt, ContextLength);
            int offset = checked(conceptIndex * ContextLength);
            for (int tokenIndex = 0; tokenIndex < tokens.Length; tokenIndex++)
            {
                inputIds[offset + tokenIndex] = tokens[tokenIndex];
                attentionMask[offset + tokenIndex] = 1;
            }
        }

        return (inputIds, attentionMask);
    }

    private static float[] Softmax(float[] logits)
    {
        if (logits.Length == 0)
        {
            return [];
        }

        float maximum = logits.Max();
        double sum = 0;
        double[] exponentials = new double[logits.Length];
        for (int index = 0; index < logits.Length; index++)
        {
            if (!float.IsFinite(logits[index]))
            {
                throw new ClipOutputException("CLIP returned a non-finite logit.");
            }

            double value = Math.Exp(logits[index] - maximum);
            exponentials[index] = value;
            sum += value;
        }

        if (!double.IsFinite(sum) || sum <= 0)
        {
            throw new ClipOutputException("CLIP logits could not be normalized.");
        }

        return exponentials.Select(value => (float)(value / sum)).ToArray();
    }
}

internal interface IClipInferenceSession : IDisposable
{
    float[] Run(
        long[] inputIds,
        long[] attentionMask,
        int promptCount,
        float[] pixelValues,
        CancellationToken cancellationToken);
}

internal sealed class OnnxClipInferenceSession : IClipInferenceSession
{
    private readonly InferenceSession _session;
    private readonly bool _usesAttentionMask;
    private readonly bool _usesPositionIds;
    private readonly string _outputName;

    public OnnxClipInferenceSession(string modelPath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(modelPath);
        _session = new InferenceSession(modelPath);

        HashSet<string> inputNames = _session.InputNames.ToHashSet(StringComparer.Ordinal);
        if (!inputNames.Contains("input_ids") || !inputNames.Contains("pixel_values"))
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

        _outputName = _session.OutputNames
            .FirstOrDefault(name =>
                string.Equals(name, "logits_per_image", StringComparison.Ordinal))
            ?? throw DisposeAndCreate(
                "The CLIP ONNX model must expose a logits_per_image output.");
    }

    public float[] Run(
        long[] inputIds,
        long[] attentionMask,
        int promptCount,
        float[] pixelValues,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(inputIds);
        ArgumentNullException.ThrowIfNull(attentionMask);
        ArgumentNullException.ThrowIfNull(pixelValues);
        cancellationToken.ThrowIfCancellationRequested();

        if (promptCount < 1 ||
            inputIds.Length != checked(promptCount * ClipZeroShotTagger.ContextLength) ||
            attentionMask.Length != inputIds.Length)
        {
            throw new ArgumentException("CLIP text tensors do not match the prompt count.");
        }

        if (pixelValues.Length != 3 * ClipZeroShotTagger.ImageSize * ClipZeroShotTagger.ImageSize)
        {
            throw new ArgumentException("CLIP image tensor must contain one 3x224x224 image.");
        }

        long[] textShape = [promptCount, ClipZeroShotTagger.ContextLength];
        long[] imageShape = [1, 3, ClipZeroShotTagger.ImageSize, ClipZeroShotTagger.ImageSize];
        long[] positionIds = _usesPositionIds
            ? Enumerable.Range(0, promptCount)
                .SelectMany(_ => Enumerable.Range(0, ClipZeroShotTagger.ContextLength))
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
            [_outputName]);

        cancellationToken.ThrowIfCancellationRequested();
        OrtValue output = outputs.Single();
        OrtTensorTypeAndShapeInfo info = output.GetTensorTypeAndShape();
        if (info.ElementDataType != TensorElementType.Float)
        {
            throw new ClipOutputException(
                $"CLIP output '{_outputName}' must contain float32 values.");
        }

        long[] shape = info.Shape;
        bool shapeMatches =
            shape.Length == 2 &&
            shape[0] == 1 &&
            shape[1] == promptCount;
        if (!shapeMatches)
        {
            throw new ClipOutputException(
                $"CLIP output '{_outputName}' must have shape [1,{promptCount}], but returned [{string.Join(",", shape)}].");
        }

        return output.GetTensorDataAsSpan<float>().ToArray();
    }

    public void Dispose() => _session.Dispose();

    private ClipOutputException DisposeAndCreate(string message)
    {
        _session.Dispose();
        return new ClipOutputException(message);
    }
}

public sealed class ClipOutputException : Exception
{
    public ClipOutputException(string message)
        : base(message)
    {
    }
}

internal sealed class ClipBpeTokenizer
{
    private const string StartToken = "<|startoftext|>";
    private const string EndToken = "<|endoftext|>";
    private static readonly Regex TokenPattern = new(
        @"<\|startoftext\|>|<\|endoftext\|>|'s|'t|'re|'ve|'m|'ll|'d|[\p{L}]+|[\p{N}]|[^\s\p{L}\p{N}]+",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);
    private static readonly Regex Whitespace = new(
        @"\s+",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);

    private readonly IReadOnlyDictionary<string, int> _vocabulary;
    private readonly IReadOnlyDictionary<(string Left, string Right), int> _mergeRanks;
    private readonly IReadOnlyDictionary<byte, char> _byteEncoder;
    private readonly Dictionary<string, string[]> _cache = new(StringComparer.Ordinal);

    private ClipBpeTokenizer(
        IReadOnlyDictionary<string, int> vocabulary,
        IReadOnlyDictionary<(string Left, string Right), int> mergeRanks)
    {
        _vocabulary = vocabulary;
        _mergeRanks = mergeRanks;
        _byteEncoder = BuildByteEncoder();

        if (!_vocabulary.TryGetValue(StartToken, out int start) ||
            !_vocabulary.TryGetValue(EndToken, out int end))
        {
            throw new InvalidDataException(
                "CLIP vocabulary must contain start-of-text and end-of-text tokens.");
        }

        StartOfTextId = start;
        EndOfTextId = end;
    }

    public int StartOfTextId { get; }
    public int EndOfTextId { get; }

    public static ClipBpeTokenizer Load(
        string vocabularyPath,
        string mergesPath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(vocabularyPath);
        ArgumentException.ThrowIfNullOrWhiteSpace(mergesPath);

        Dictionary<string, int> vocabulary =
            JsonSerializer.Deserialize<Dictionary<string, int>>(
                File.ReadAllText(vocabularyPath))
            ?? throw new InvalidDataException("CLIP vocabulary JSON is empty.");

        Dictionary<(string Left, string Right), int> ranks = [];
        int rank = 0;
        foreach (string rawLine in File.ReadLines(mergesPath))
        {
            string line = rawLine.Trim();
            if (line.Length == 0 || line.StartsWith("#", StringComparison.Ordinal))
            {
                continue;
            }

            string[] pair = line.Split(
                ' ',
                StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
            if (pair.Length != 2)
            {
                throw new InvalidDataException(
                    $"Invalid CLIP BPE merge line '{rawLine}'.");
            }

            ranks.TryAdd((pair[0], pair[1]), rank++);
        }

        if (ranks.Count == 0)
        {
            throw new InvalidDataException("CLIP BPE merges file contains no merge pairs.");
        }

        return new ClipBpeTokenizer(vocabulary, ranks);
    }

    internal static ClipBpeTokenizer CreateForTests(
        IReadOnlyDictionary<string, int> vocabulary,
        IReadOnlyList<(string Left, string Right)> merges) =>
        new(
            new Dictionary<string, int>(vocabulary, StringComparer.Ordinal),
            merges
                .Select((pair, rank) => (pair, rank))
                .ToDictionary(value => value.pair, value => value.rank));

    public int[] Encode(string text, int maximumLength)
    {
        ArgumentNullException.ThrowIfNull(text);
        if (maximumLength < 2)
        {
            throw new ArgumentOutOfRangeException(nameof(maximumLength));
        }

        string cleaned = CleanText(text);
        List<int> tokens = [StartOfTextId];
        foreach (Match match in TokenPattern.Matches(cleaned))
        {
            string encoded = EncodeBytes(match.Value);
            foreach (string piece in ApplyBpe(encoded))
            {
                if (!_vocabulary.TryGetValue(piece, out int id))
                {
                    throw new InvalidDataException(
                        $"CLIP vocabulary does not contain BPE token '{piece}'.");
                }

                tokens.Add(id);
            }
        }

        int contentLimit = maximumLength - 1;
        if (tokens.Count > contentLimit)
        {
            tokens.RemoveRange(contentLimit, tokens.Count - contentLimit);
        }
        tokens.Add(EndOfTextId);
        return tokens.ToArray();
    }

    private static string CleanText(string value)
    {
        string decoded = WebUtility.HtmlDecode(WebUtility.HtmlDecode(value));
        string normalized = decoded.Normalize(NormalizationForm.FormC);
        return Whitespace.Replace(normalized, " ")
            .Trim()
            .ToLowerInvariant();
    }

    private string EncodeBytes(string token)
    {
        byte[] bytes = Encoding.UTF8.GetBytes(token);
        StringBuilder builder = new(bytes.Length);
        foreach (byte value in bytes)
        {
            builder.Append(_byteEncoder[value]);
        }
        return builder.ToString();
    }

    private string[] ApplyBpe(string token)
    {
        if (_cache.TryGetValue(token, out string[]? cached))
        {
            return cached;
        }

        if (token.Length == 0)
        {
            return [];
        }

        List<string> word = token
            .Select(character => character.ToString())
            .ToList();
        word[^1] += "</w>";

        while (word.Count > 1)
        {
            int bestRank = int.MaxValue;
            (string Left, string Right)? bestPair = null;
            for (int index = 0; index < word.Count - 1; index++)
            {
                (string Left, string Right) pair = (word[index], word[index + 1]);
                if (_mergeRanks.TryGetValue(pair, out int candidateRank) &&
                    candidateRank < bestRank)
                {
                    bestRank = candidateRank;
                    bestPair = pair;
                }
            }

            if (bestPair is null)
            {
                break;
            }

            List<string> merged = [];
            for (int index = 0; index < word.Count;)
            {
                if (index < word.Count - 1 &&
                    string.Equals(word[index], bestPair.Value.Left, StringComparison.Ordinal) &&
                    string.Equals(word[index + 1], bestPair.Value.Right, StringComparison.Ordinal))
                {
                    merged.Add(word[index] + word[index + 1]);
                    index += 2;
                }
                else
                {
                    merged.Add(word[index]);
                    index++;
                }
            }

            word = merged;
        }

        string[] result = word.ToArray();
        _cache[token] = result;
        return result;
    }

    private static IReadOnlyDictionary<byte, char> BuildByteEncoder()
    {
        List<int> bytes = [];
        bytes.AddRange(Enumerable.Range('!', '~' - '!' + 1));
        bytes.AddRange(Enumerable.Range('¡', '¬' - '¡' + 1));
        bytes.AddRange(Enumerable.Range('®', 'ÿ' - '®' + 1));

        List<int> codePoints = [.. bytes];
        int extra = 0;
        for (int value = 0; value < 256; value++)
        {
            if (bytes.Contains(value))
            {
                continue;
            }

            bytes.Add(value);
            codePoints.Add(256 + extra++);
        }

        return bytes
            .Select((value, index) => (Byte: (byte)value, Character: (char)codePoints[index]))
            .ToDictionary(value => value.Byte, value => value.Character);
    }
}

internal static class ClipImagePreprocessor
{
    private static readonly float[] Mean = [0.48145466f, 0.4578275f, 0.40821073f];
    private static readonly float[] StandardDeviation = [0.26862954f, 0.26130258f, 0.27577711f];

    public static float[] Preprocess(
        ImageFrame image,
        CancellationToken cancellationToken)
    {
        if (image.Size.Width < 1 || image.Size.Height < 1)
        {
            throw new ArgumentException("CLIP input image must be non-empty.", nameof(image));
        }

        using Mat source = ToBgrMat(image);
        double scale = (double)ClipZeroShotTagger.ImageSize /
            Math.Min(source.Width, source.Height);
        int resizedWidth = Math.Max(
            ClipZeroShotTagger.ImageSize,
            (int)Math.Round(source.Width * scale, MidpointRounding.AwayFromZero));
        int resizedHeight = Math.Max(
            ClipZeroShotTagger.ImageSize,
            (int)Math.Round(source.Height * scale, MidpointRounding.AwayFromZero));

        using Mat resized = new();
        Cv2.Resize(
            source,
            resized,
            new Size(resizedWidth, resizedHeight),
            0,
            0,
            InterpolationFlags.Cubic);

        int x = (resizedWidth - ClipZeroShotTagger.ImageSize) / 2;
        int y = (resizedHeight - ClipZeroShotTagger.ImageSize) / 2;
        using Mat cropView = new(
            resized,
            new Rect(
                x,
                y,
                ClipZeroShotTagger.ImageSize,
                ClipZeroShotTagger.ImageSize));
        using Mat crop = cropView.Clone();

        int pixelCount =
            ClipZeroShotTagger.ImageSize * ClipZeroShotTagger.ImageSize;
        byte[] bgr = new byte[pixelCount * 3];
        Marshal.Copy(crop.Data, bgr, 0, bgr.Length);

        float[] tensor = new float[pixelCount * 3];
        for (int pixel = 0; pixel < pixelCount; pixel++)
        {
            if ((pixel & 4095) == 0)
            {
                cancellationToken.ThrowIfCancellationRequested();
            }

            int sourceOffset = pixel * 3;
            float red = bgr[sourceOffset + 2] / 255f;
            float green = bgr[sourceOffset + 1] / 255f;
            float blue = bgr[sourceOffset] / 255f;
            tensor[pixel] = (red - Mean[0]) / StandardDeviation[0];
            tensor[pixelCount + pixel] = (green - Mean[1]) / StandardDeviation[1];
            tensor[(2 * pixelCount) + pixel] = (blue - Mean[2]) / StandardDeviation[2];
        }

        return tensor;
    }

    private static Mat ToBgrMat(ImageFrame image)
    {
        byte[] packed = PackRows(image);
        int channels = ImageFrame.BytesPerPixel(image.Format);
        MatType type = channels switch
        {
            1 => MatType.CV_8UC1,
            3 => MatType.CV_8UC3,
            4 => MatType.CV_8UC4,
            _ => throw new ArgumentOutOfRangeException(nameof(image)),
        };

        using Mat raw = new(image.Size.Height, image.Size.Width, type);
        Marshal.Copy(packed, 0, raw.Data, packed.Length);
        Mat bgr = new();

        switch (image.Format)
        {
            case PixelFormat.Bgr24:
                raw.CopyTo(bgr);
                break;
            case PixelFormat.Rgb24:
                Cv2.CvtColor(raw, bgr, ColorConversionCodes.RGB2BGR);
                break;
            case PixelFormat.Gray8:
                Cv2.CvtColor(raw, bgr, ColorConversionCodes.GRAY2BGR);
                break;
            case PixelFormat.Bgra32:
                Cv2.CvtColor(raw, bgr, ColorConversionCodes.BGRA2BGR);
                break;
            case PixelFormat.Rgba32:
                Cv2.CvtColor(raw, bgr, ColorConversionCodes.RGBA2BGR);
                break;
            default:
                bgr.Dispose();
                throw new ArgumentOutOfRangeException(nameof(image));
        }

        return bgr;
    }

    private static byte[] PackRows(ImageFrame image)
    {
        int packedStride = checked(
            image.Size.Width * ImageFrame.BytesPerPixel(image.Format));
        if (image.Stride == packedStride)
        {
            return image.ToArray();
        }

        byte[] result = new byte[checked(packedStride * image.Size.Height)];
        ReadOnlySpan<byte> source = image.Data;
        for (int row = 0; row < image.Size.Height; row++)
        {
            source.Slice(row * image.Stride, packedStride)
                .CopyTo(result.AsSpan(row * packedStride, packedStride));
        }

        return result;
    }
}
