using Microsoft.ML.OnnxRuntime;

namespace PhotoIdentity.Recognition.Onnx.YuNet;

internal interface IYuNetInferenceSession : IDisposable
{
    IReadOnlyDictionary<string, YuNetTensor> Run(
        float[] input,
        long[] shape,
        CancellationToken cancellationToken);
}

internal sealed class OnnxYuNetInferenceSession : IYuNetInferenceSession
{
    private readonly InferenceSession _session;
    private readonly string _inputName;
    private readonly string[] _outputNames;

    public OnnxYuNetInferenceSession(string modelPath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(modelPath);

        _session = new InferenceSession(modelPath);
        if (_session.InputNames.Count != 1)
        {
            _session.Dispose();
            throw new YuNetOutputException(
                $"YuNet must expose exactly one input, but the model exposes {_session.InputNames.Count}.");
        }

        _inputName = _session.InputNames.Single();
        _outputNames = _session.OutputNames.ToArray();
    }

    public IReadOnlyDictionary<string, YuNetTensor> Run(
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

        using IDisposableReadOnlyCollection<OrtValue> outputValues = _session.Run(
            runOptions,
            inputNames,
            inputValues,
            _outputNames);

        Dictionary<string, YuNetTensor> outputs = new(StringComparer.Ordinal);
        for (int index = 0; index < _outputNames.Length; index++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            OrtValue outputValue = outputValues[index];
            OrtTensorTypeAndShapeInfo typeAndShape = outputValue.GetTensorTypeAndShape();
            if (typeAndShape.ElementDataType != TensorElementType.Float)
            {
                throw new YuNetOutputException(
                    $"YuNet output '{_outputNames[index]}' must contain float32 values, " +
                    $"but the model returned {typeAndShape.ElementDataType}.");
            }

            outputs.Add(
                _outputNames[index],
                new YuNetTensor(
                    _outputNames[index],
                    typeAndShape.Shape,
                    outputValue.GetTensorDataAsSpan<float>()));
        }

        return outputs;
    }

    public void Dispose() => _session.Dispose();
}
