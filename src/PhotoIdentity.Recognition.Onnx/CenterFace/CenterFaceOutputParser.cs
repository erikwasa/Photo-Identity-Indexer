using PhotoIdentity.Core.Geometry;
using PhotoIdentity.Core.Recognition;

namespace PhotoIdentity.Recognition.Onnx.CenterFace;

internal sealed class CenterFaceTensor
{
    private readonly float[] _data;
    private readonly long[] _shape;

    public CenterFaceTensor(string name, IReadOnlyList<long> shape, ReadOnlySpan<float> data)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        ArgumentNullException.ThrowIfNull(shape);

        long elementCount = 1;
        foreach (long dimension in shape)
        {
            if (dimension <= 0)
            {
                throw new CenterFaceOutputException(
                    $"CenterFace tensor '{name}' has an invalid dimension {dimension}.");
            }

            elementCount = checked(elementCount * dimension);
        }

        if (elementCount != data.Length)
        {
            throw new CenterFaceOutputException(
                $"CenterFace tensor '{name}' declares {elementCount} elements but contains {data.Length}.");
        }

        Name = name;
        _shape = shape.ToArray();
        _data = data.ToArray();
    }

    public string Name { get; }
    public IReadOnlyList<long> Shape => _shape;
    public ReadOnlySpan<float> Data => _data;
}

public sealed class CenterFaceOutputException : Exception
{
    public CenterFaceOutputException(string message)
        : base(message)
    {
    }
}

internal static class CenterFaceOutputParser
{
    private const int OutputStride = 4;

    public static IReadOnlyList<DetectedFaceCandidate> Parse(
        IReadOnlyDictionary<string, CenterFaceTensor> outputs,
        ImageSize inputSize,
        CenterFaceDetectorOptions options)
    {
        ArgumentNullException.ThrowIfNull(outputs);
        ArgumentNullException.ThrowIfNull(options);
        options.Validate();

        if (inputSize.Width % OutputStride != 0 || inputSize.Height % OutputStride != 0)
        {
            throw new CenterFaceOutputException(
                $"CenterFace input dimensions must be divisible by {OutputStride}, but received " +
                $"{inputSize.Width}x{inputSize.Height}.");
        }

        int columns = inputSize.Width / OutputStride;
        int rows = inputSize.Height / OutputStride;
        CenterFaceTensor heatmap = RequireTensor(outputs, "537", 1, rows, columns);
        CenterFaceTensor scale = RequireTensor(outputs, "538", 2, rows, columns);
        CenterFaceTensor offset = RequireTensor(outputs, "539", 2, rows, columns);
        CenterFaceTensor landmarks = RequireTensor(outputs, "540", 10, rows, columns);

        List<RawCandidate> candidates = [];
        for (int row = 0; row < rows; row++)
        {
            for (int column = 0; column < columns; column++)
            {
                int location = checked((row * columns) + column);
                double score = ClampProbability(heatmap.Data[location], heatmap.Name, location);
                if (score <= options.ConfidenceThreshold)
                {
                    continue;
                }

                double height = Math.Exp(Finite(ChannelValue(scale, 0, row, column, rows, columns))) * OutputStride;
                double width = Math.Exp(Finite(ChannelValue(scale, 1, row, column, rows, columns))) * OutputStride;
                if (!double.IsFinite(width) || !double.IsFinite(height) || width <= 0 || height <= 0)
                {
                    throw new CenterFaceOutputException(
                        $"CenterFace scale output produced an invalid box at row {row}, column {column}.");
                }

                double offsetY = Finite(ChannelValue(offset, 0, row, column, rows, columns));
                double offsetX = Finite(ChannelValue(offset, 1, row, column, rows, columns));
                double left = Math.Clamp(
                    ((column + offsetX + 0.5) * OutputStride) - (width / 2),
                    0,
                    inputSize.Width);
                double top = Math.Clamp(
                    ((row + offsetY + 0.5) * OutputStride) - (height / 2),
                    0,
                    inputSize.Height);
                double right = Math.Min(left + width, inputSize.Width);
                double bottom = Math.Min(top + height, inputSize.Height);
                if (right <= left || bottom <= top)
                {
                    continue;
                }

                double[] points = new double[10];
                for (int point = 0; point < 5; point++)
                {
                    double landmarkY = Finite(ChannelValue(
                        landmarks,
                        point * 2,
                        row,
                        column,
                        rows,
                        columns));
                    double landmarkX = Finite(ChannelValue(
                        landmarks,
                        (point * 2) + 1,
                        row,
                        column,
                        rows,
                        columns));
                    points[point * 2] = (landmarkX * width) + left;
                    points[(point * 2) + 1] = (landmarkY * height) + top;
                }

                candidates.Add(new RawCandidate(left, top, right, bottom, points, score));
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

        return retained
            .Select(candidate => ToDetection(candidate, inputSize))
            .ToArray();
    }

    private static CenterFaceTensor RequireTensor(
        IReadOnlyDictionary<string, CenterFaceTensor> outputs,
        string name,
        int channels,
        int rows,
        int columns)
    {
        if (!outputs.TryGetValue(name, out CenterFaceTensor? tensor))
        {
            throw new CenterFaceOutputException($"CenterFace output '{name}' is missing.");
        }

        long[] expectedShape = [1, channels, rows, columns];
        if (!tensor.Shape.SequenceEqual(expectedShape))
        {
            throw new CenterFaceOutputException(
                $"CenterFace output '{name}' has shape [{string.Join(", ", tensor.Shape)}]; " +
                $"expected [{string.Join(", ", expectedShape)}].");
        }

        return tensor;
    }

    private static float ChannelValue(
        CenterFaceTensor tensor,
        int channel,
        int row,
        int column,
        int rows,
        int columns)
    {
        int offset = checked(((channel * rows) + row) * columns + column);
        return tensor.Data[offset];
    }

    private static double ClampProbability(float value, string tensorName, int offset)
    {
        double finite = Finite(value, tensorName, offset);
        return Math.Clamp(finite, 0, 1);
    }

    private static double Finite(float value) => Finite(value, "output", -1);

    private static double Finite(float value, string tensorName, int offset)
    {
        if (!float.IsFinite(value))
        {
            string location = offset >= 0 ? $" at offset {offset}" : string.Empty;
            throw new CenterFaceOutputException(
                $"CenterFace output '{tensorName}' contains a non-finite value{location}.");
        }

        return value;
    }

    private static DetectedFaceCandidate ToDetection(RawCandidate candidate, ImageSize inputSize)
    {
        NormalizedPoint Point(int index) => new(
            Math.Clamp(candidate.Landmarks[index * 2] / inputSize.Width, 0, 1),
            Math.Clamp(candidate.Landmarks[(index * 2) + 1] / inputSize.Height, 0, 1));

        // CenterFace's five-point order is anatomical right eye, anatomical left eye,
        // nose, anatomical right mouth and anatomical left mouth. This matches the
        // mapping used by the independent DeepFace CenterFace adapter and is frozen
        // here explicitly for sface-five-point-v1 compatibility.
        NormalizedFaceLandmarks landmarks = new(
            LeftEye: Point(1),
            RightEye: Point(0),
            Nose: Point(2),
            MouthLeft: Point(4),
            MouthRight: Point(3));

        return new DetectedFaceCandidate(
            new NormalizedBoundingBox(
                candidate.Left / inputSize.Width,
                candidate.Top / inputSize.Height,
                (candidate.Right - candidate.Left) / inputSize.Width,
                (candidate.Bottom - candidate.Top) / inputSize.Height),
            landmarks,
            candidate.Score);
    }

    private static double IntersectionOverUnion(RawCandidate first, RawCandidate second)
    {
        double firstWidth = first.Right - first.Left + 1;
        double firstHeight = first.Bottom - first.Top + 1;
        double secondWidth = second.Right - second.Left + 1;
        double secondHeight = second.Bottom - second.Top + 1;
        double intersectionWidth = Math.Max(
            0,
            Math.Min(first.Right, second.Right) - Math.Max(first.Left, second.Left) + 1);
        double intersectionHeight = Math.Max(
            0,
            Math.Min(first.Bottom, second.Bottom) - Math.Max(first.Top, second.Top) + 1);
        double intersection = intersectionWidth * intersectionHeight;
        double union = (firstWidth * firstHeight) + (secondWidth * secondHeight) - intersection;
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

            int top = left.Top.CompareTo(right.Top);
            if (top != 0)
            {
                return top;
            }

            return left.Left.CompareTo(right.Left);
        }
    }

    private readonly record struct RawCandidate(
        double Left,
        double Top,
        double Right,
        double Bottom,
        double[] Landmarks,
        double Score);
}
