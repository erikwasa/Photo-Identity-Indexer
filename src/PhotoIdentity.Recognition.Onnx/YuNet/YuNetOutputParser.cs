using PhotoIdentity.Core.Geometry;
using PhotoIdentity.Core.Recognition;

namespace PhotoIdentity.Recognition.Onnx.YuNet;

internal sealed class YuNetTensor
{
    private readonly float[] _data;
    private readonly long[] _shape;

    public YuNetTensor(string name, IReadOnlyList<long> shape, ReadOnlySpan<float> data)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        ArgumentNullException.ThrowIfNull(shape);

        long elementCount = 1;
        foreach (long dimension in shape)
        {
            if (dimension <= 0)
            {
                throw new YuNetOutputException(
                    $"YuNet tensor '{name}' has an invalid dimension {dimension}.");
            }

            elementCount = checked(elementCount * dimension);
        }

        if (elementCount != data.Length)
        {
            throw new YuNetOutputException(
                $"YuNet tensor '{name}' declares {elementCount} elements but contains {data.Length}.");
        }

        Name = name;
        _shape = shape.ToArray();
        _data = data.ToArray();
    }

    public string Name { get; }
    public IReadOnlyList<long> Shape => _shape;
    public ReadOnlySpan<float> Data => _data;
}

public sealed class YuNetOutputException : Exception
{
    public YuNetOutputException(string message)
        : base(message)
    {
    }
}

internal static class YuNetOutputParser
{
    private static readonly int[] Strides = [8, 16, 32];

    public static IReadOnlyList<DetectedFaceCandidate> Parse(
        IReadOnlyDictionary<string, YuNetTensor> outputs,
        ImageSize inputSize,
        YuNetDetectorOptions options)
    {
        ArgumentNullException.ThrowIfNull(outputs);
        ArgumentNullException.ThrowIfNull(options);
        options.Validate();

        if (inputSize.Width % 32 != 0 || inputSize.Height % 32 != 0)
        {
            throw new YuNetOutputException(
                $"YuNet input dimensions must be divisible by 32, but received " +
                $"{inputSize.Width}x{inputSize.Height}.");
        }

        List<RawCandidate> candidates = [];
        foreach (int stride in Strides)
        {
            int columns = inputSize.Width / stride;
            int rows = inputSize.Height / stride;
            int locations = checked(columns * rows);

            YuNetTensor classification = RequireTensor(outputs, $"cls_{stride}", locations, 1);
            YuNetTensor objectness = RequireTensor(outputs, $"obj_{stride}", locations, 1);
            YuNetTensor boxes = RequireTensor(outputs, $"bbox_{stride}", locations, 4);
            YuNetTensor landmarks = RequireTensor(outputs, $"kps_{stride}", locations, 10);

            for (int row = 0; row < rows; row++)
            {
                for (int column = 0; column < columns; column++)
                {
                    int location = checked((row * columns) + column);
                    double classScore = ClampProbability(
                        classification.Data[location],
                        classification.Name,
                        location);
                    double objectScore = ClampProbability(
                        objectness.Data[location],
                        objectness.Name,
                        location);
                    double score = Math.Sqrt(classScore * objectScore);
                    if (score < options.ConfidenceThreshold)
                    {
                        continue;
                    }

                    int boxOffset = checked(location * 4);
                    double centreX = (column + Finite(boxes.Data[boxOffset], boxes.Name, boxOffset)) * stride;
                    double centreY = (row + Finite(boxes.Data[boxOffset + 1], boxes.Name, boxOffset + 1)) * stride;
                    double width = Math.Exp(Finite(boxes.Data[boxOffset + 2], boxes.Name, boxOffset + 2)) * stride;
                    double height = Math.Exp(Finite(boxes.Data[boxOffset + 3], boxes.Name, boxOffset + 3)) * stride;

                    if (!double.IsFinite(width) || !double.IsFinite(height) || width <= 0 || height <= 0)
                    {
                        throw new YuNetOutputException(
                            $"YuNet output '{boxes.Name}' produced an invalid box at location {location}.");
                    }

                    double[] points = new double[10];
                    int landmarkOffset = checked(location * 10);
                    for (int point = 0; point < 5; point++)
                    {
                        int xOffset = landmarkOffset + (point * 2);
                        points[point * 2] =
                            (Finite(landmarks.Data[xOffset], landmarks.Name, xOffset) + column) * stride;
                        points[(point * 2) + 1] =
                            (Finite(landmarks.Data[xOffset + 1], landmarks.Name, xOffset + 1) + row) * stride;
                    }

                    candidates.Add(new RawCandidate(
                        centreX - (width / 2),
                        centreY - (height / 2),
                        width,
                        height,
                        points,
                        score));
                }
            }
        }

        candidates.Sort(RawCandidateComparer.Instance);
        if (candidates.Count > options.TopK)
        {
            candidates.RemoveRange(options.TopK, candidates.Count - options.TopK);
        }

        List<RawCandidate> retained = [];
        foreach (RawCandidate candidate in candidates)
        {
            if (retained.Any(existing => IntersectionOverUnion(candidate, existing) >= options.NmsThreshold))
            {
                continue;
            }

            retained.Add(candidate);
        }

        List<DetectedFaceCandidate> detections = [];
        foreach (RawCandidate candidate in retained)
        {
            DetectedFaceCandidate? detection = ToDetection(candidate, inputSize);
            if (detection is not null)
            {
                detections.Add(detection);
            }
        }

        return detections;
    }

    private static YuNetTensor RequireTensor(
        IReadOnlyDictionary<string, YuNetTensor> outputs,
        string name,
        int locations,
        int valuesPerLocation)
    {
        if (!outputs.TryGetValue(name, out YuNetTensor? tensor))
        {
            throw new YuNetOutputException($"YuNet output '{name}' is missing.");
        }

        long[] expectedShape = [1, locations, valuesPerLocation];
        if (!tensor.Shape.SequenceEqual(expectedShape))
        {
            throw new YuNetOutputException(
                $"YuNet output '{name}' has shape [{string.Join(", ", tensor.Shape)}]; " +
                $"expected [{string.Join(", ", expectedShape)}].");
        }

        return tensor;
    }

    private static double ClampProbability(float value, string tensorName, int offset)
    {
        double finite = Finite(value, tensorName, offset);
        return Math.Clamp(finite, 0, 1);
    }

    private static double Finite(float value, string tensorName, int offset)
    {
        if (!float.IsFinite(value))
        {
            throw new YuNetOutputException(
                $"YuNet output '{tensorName}' contains a non-finite value at offset {offset}.");
        }

        return value;
    }

    private static DetectedFaceCandidate? ToDetection(RawCandidate candidate, ImageSize size)
    {
        double left = Math.Clamp(candidate.X, 0, size.Width);
        double top = Math.Clamp(candidate.Y, 0, size.Height);
        double right = Math.Clamp(candidate.X + candidate.Width, 0, size.Width);
        double bottom = Math.Clamp(candidate.Y + candidate.Height, 0, size.Height);
        double width = right - left;
        double height = bottom - top;
        if (width <= 0 || height <= 0)
        {
            return null;
        }

        NormalizedPoint Point(int index) => new(
            Math.Clamp(candidate.Landmarks[index * 2] / size.Width, 0, 1),
            Math.Clamp(candidate.Landmarks[(index * 2) + 1] / size.Height, 0, 1));

        // OpenCV YuNet emits right eye, left eye, nose, right mouth and left mouth.
        NormalizedFaceLandmarks landmarks = new(
            LeftEye: Point(1),
            RightEye: Point(0),
            Nose: Point(2),
            MouthLeft: Point(4),
            MouthRight: Point(3));

        return new DetectedFaceCandidate(
            new NormalizedBoundingBox(
                left / size.Width,
                top / size.Height,
                width / size.Width,
                height / size.Height),
            landmarks,
            candidate.Score);
    }

    private static double IntersectionOverUnion(RawCandidate first, RawCandidate second)
    {
        double intersectionWidth = Math.Max(
            0,
            Math.Min(first.X + first.Width, second.X + second.Width) - Math.Max(first.X, second.X));
        double intersectionHeight = Math.Max(
            0,
            Math.Min(first.Y + first.Height, second.Y + second.Height) - Math.Max(first.Y, second.Y));
        double intersection = intersectionWidth * intersectionHeight;
        double union = (first.Width * first.Height) + (second.Width * second.Height) - intersection;
        return union <= 0 ? 0 : intersection / union;
    }

    private sealed class RawCandidateComparer : IComparer<RawCandidate>
    {
        public static RawCandidateComparer Instance { get; } = new();

        public int Compare(RawCandidate left, RawCandidate right)
        {
            int score = right.Score.CompareTo(left.Score);
            if (score != 0)
            {
                return score;
            }

            int x = left.X.CompareTo(right.X);
            return x != 0 ? x : left.Y.CompareTo(right.Y);
        }
    }

    private readonly record struct RawCandidate(
        double X,
        double Y,
        double Width,
        double Height,
        double[] Landmarks,
        double Score);
}
