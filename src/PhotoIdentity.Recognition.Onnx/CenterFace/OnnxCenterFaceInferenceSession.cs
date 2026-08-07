using System.Runtime.InteropServices;
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

    private readonly string _modelPath;

    public OpenCvDnnCenterFaceInferenceSession(string modelPath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(modelPath);
        _modelPath = modelPath;
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

        // CenterFace/OpenCV smoke evidence showed a valid first inference followed by
        // corrupted later calls when one native Net was reused across source images.
        // An independent CenterFace adapter documents the same second-call failure
        // mode. Keep each image inference isolated by loading and disposing a fresh
        // network while preserving all model/preprocessing/decoder parameters.
        using Net net = LoadNetwork();
        net.SetInput(inputBlob);

        Mat[] outputBlobs = RequiredOutputNames.Select(_ => new Mat()).ToArray();
        try
        {
            net.Forward(outputBlobs, RequiredOutputNames);

            Dictionary<string, CenterFaceTensor> outputs = new(StringComparer.Ordinal);
            for (int index = 0; index < RequiredOutputNames.Length; index++)
            {
                cancellationToken.ThrowIfCancellationRequested();
                string outputName = RequiredOutputNames[index];
                outputs.Add(outputName, ReadOutputTensor(outputName, outputBlobs[index]));
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

    private Net LoadNetwork()
    {
        Net net = Net.ReadNetFromONNX(_modelPath)
            ?? throw new CenterFaceOutputException("OpenCV DNN could not load the CenterFace ONNX graph.");
        if (net.Empty())
        {
            net.Dispose();
            throw new CenterFaceOutputException("OpenCV DNN loaded an empty CenterFace network.");
        }

        return net;
    }

    internal static CenterFaceTensor ReadOutputTensor(string name, Mat output)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        ArgumentNullException.ThrowIfNull(output);

        if (output.Empty())
        {
            throw new CenterFaceOutputException($"CenterFace output '{name}' was empty.");
        }

        if (output.Type() != MatType.CV_32FC1)
        {
            throw new CenterFaceOutputException(
                $"CenterFace output '{name}' must contain float32 values, but OpenCV returned {output.Type()}.");
        }

        long[] outputShape = Enumerable.Range(0, output.Dims)
            .Select(dimension => (long)output.Size(dimension))
            .ToArray();

        long elementCount = 1;
        foreach (long dimension in outputShape)
        {
            if (dimension <= 0)
            {
                throw new CenterFaceOutputException(
                    $"CenterFace output '{name}' has invalid shape [{string.Join(", ", outputShape)}].");
            }

            elementCount = checked(elementCount * dimension);
        }

        if (elementCount > int.MaxValue)
        {
            throw new CenterFaceOutputException(
                $"CenterFace output '{name}' contains too many elements to copy safely: {elementCount}.");
        }

        float[] data = new float[(int)elementCount];
        Mat source = output;
        Mat? contiguousCopy = null;
        try
        {
            if (!output.IsContinuous())
            {
                contiguousCopy = output.Clone();
                source = contiguousCopy;
            }

            Marshal.Copy(source.Data, data, 0, data.Length);
        }
        finally
        {
            contiguousCopy?.Dispose();
        }

        return new CenterFaceTensor(name, outputShape, data);
    }

    public void Dispose()
    {
        // Networks are intentionally created and disposed per Run call.
    }
}
