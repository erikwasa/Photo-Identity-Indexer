using OpenCvSharp;
using OpenCvSharp.Dnn;

namespace PhotoIdentity.Recognition.Onnx.CenterFace;

internal interface ICenterFaceInferenceSession : IDisposable
{
    IReadOnlyDictionary<string, CenterFaceTensor> Run(
        float[] input,
        long[] shape,
        CancellationToken cancellationToken);
}

/// <summary>
/// Executes the pinned CenterFace ONNX graph through OpenCV DNN.
/// The upstream CenterFace reference implementation uses OpenCV DNN because the
/// committed ONNX graph carries stale static input metadata even though the
/// network is fully convolutional and is evaluated at source-dependent dimensions.
/// </summary>
internal sealed class OpenCvDnnCenterFaceInferenceSession : ICenterFaceInferenceSession
{
    private static readonly string[] RequiredOutputNames = ["537", "538", "539", "540"];

    private readonly Net _net;

    public OpenCvDnnCenterFaceInferenceSession(string modelPath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(modelPath);

        _net = Net.ReadNetFromONNX(modelPath)
            ?? throw new CenterFaceOutputException("OpenCV DNN could not load the CenterFace ONNX graph.");
        if (_net.Empty())
        {
            _net.Dispose();
            throw new CenterFaceOutputException("OpenCV DNN loaded an empty CenterFace network.");
        }
    }

    public IReadOnlyDictionary<string, CenterFaceTensor> Run(
        float[] input,
        long[] shape,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(input);
        ArgumentNullException.ThrowIfNull(shape);
        cancellationToken.ThrowIfCancellationRequested();

        if (shape.Length != 4 || shape[0] != 1 || shape[1] != 3)
        {
            throw new CenterFaceOutputException(
                $"CenterFace input must be NCHW [1, 3, H, W], but received [{string.Join(", ", shape)}].");
        }

        int[] matShape = shape
            .Select(dimension => checked((int)dimension))
            .ToArray();
        long expectedElements = 1;
        foreach (long dimension in shape)
        {
            expectedElements = checked(expectedElements * dimension);
        }

        if (expectedElements != input.LongLength)
        {
            throw new CenterFaceOutputException(
                $"CenterFace input shape declares {expectedElements} values but received {input.LongLength}.");
        }

        using Mat inputBlob = Mat.FromPixelData(matShape, MatType.CV_32FC1, input);
        _net.SetInput(inputBlob);

        Mat[] outputBlobs = RequiredOutputNames.Select(_ => new Mat()).ToArray();
        try
        {
            _net.Forward(outputBlobs, RequiredOutputNames);

            Dictionary<string, CenterFaceTensor> outputs = new(StringComparer.Ordinal);
            for (int index = 0; index < RequiredOutputNames.Length; index++)
            {
                cancellationToken.ThrowIfCancellationRequested();
                Mat output = outputBlobs[index];
                if (output.Empty())
                {
                    throw new CenterFaceOutputException(
                        $"CenterFace output '{RequiredOutputNames[index]}' was empty.");
                }

                if (!output.GetArray(out float[] data))
                {
                    throw new CenterFaceOutputException(
                        $"CenterFace output '{RequiredOutputNames[index]}' could not be read as float32 data.");
                }

                long[] outputShape = Enumerable.Range(0, output.Dims)
                    .Select(dimension => (long)output.Size(dimension))
                    .ToArray();
                outputs.Add(
                    RequiredOutputNames[index],
                    new CenterFaceTensor(RequiredOutputNames[index], outputShape, data));
            }

            return outputs;
        }
        finally
        {
            foreach (Mat output in outputBlobs)
            {
                output.Dispose();
            }
        }
    }

    public void Dispose() => _net.Dispose();
}
