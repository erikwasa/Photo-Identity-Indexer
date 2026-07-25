using Microsoft.ML.OnnxRuntime;
using Microsoft.ML.OnnxRuntime.Tensors;

namespace PhotoIdentity.Recognition.Onnx.SFace;

internal interface ISFaceInferenceSession : IDisposable
{
    SFaceTensor Run(
        float[] input,
        long[] shape,
        CancellationToken cancellationToken);
}

internal sealed record SFaceTensor(string Name, long[] Shape, float[] Values)
{
    public SFaceTensor(string name, IReadOnlyList<long> shape, ReadOnlySpan<float> values)
        : this(name, shape.ToArray(), values.ToArray())
    {
    }
}

public sealed class SFaceOutputException : Exception
{
    public SFaceOutputException(string message)
        : base(message)
    {
    }
}

internal sealed class OnnxSFaceInferenceSession : ISFaceInferenceSession
{
    private readonly InferenceSession _session;
    private readonly string _inputName;
    private readonly string _outputName;

    public OnnxSFaceInferenceSession(string modelPath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(modelPath);

        _session = new InferenceSession(modelPath);
        if (_session.InputNames.Count != 1)
        {
            _session.Dispose();
            throw new SFaceOutputException(
                $"SFace must expose exactly one input, but the model exposes {_session.InputNames.Count}.");
        }

        if (_session.OutputNames.Count != 1)
        {
            _session.Dispose();
            throw new SFaceOutputException(
                $"SFace must expose exactly one output, but the model exposes {_session.OutputNames.Count}.");
        }

        _inputName = _session.InputNames.Single();
        _outputName = _session.OutputNames.Single();
    }

    public SFaceTensor Run(
        float[] input,
        long[] shape,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(input);
        ArgumentNullException.ThrowIfNull(shape);
        cancellationToken.ThrowIfCancellationRequested();

        using OrtValue inputValue = OrtValue.CreateTensorValueFromMemory(input, shape);
        using RunOptions runOptions = new();
        string[] inputNames = [_inputName];
        OrtValue[] inputValues = [inputValue];
        string[] outputNames = [_outputName];

        using IDisposableReadOnlyCollection<OrtValue> outputValues = _session.Run(
            runOptions,
            inputNames,
            inputValues,
            outputNames);

        cancellationToken.ThrowIfCancellationRequested();
        OrtValue outputValue = outputValues.Single();
        OrtTensorTypeAndShapeInfo typeAndShape = outputValue.GetTensorTypeAndShape();
        if (typeAndShape.ElementDataType != TensorElementType.Float)
        {
            throw new SFaceOutputException(
                $"SFace output '{_outputName}' must contain float32 values, " +
                $"but the model returned {typeAndShape.ElementDataType}.");
        }

        return new SFaceTensor(
            _outputName,
            typeAndShape.Shape,
            outputValue.GetTensorDataAsSpan<float>());
    }

    public void Dispose() => _session.Dispose();
}
