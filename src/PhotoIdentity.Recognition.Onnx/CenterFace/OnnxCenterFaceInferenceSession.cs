using Microsoft.ML.OnnxRuntime;
using Microsoft.ML.OnnxRuntime.Tensors;

namespace PhotoIdentity.Recognition.Onnx.CenterFace;

internal interface ICenterFaceInferenceSession : IDisposable
{
    IReadOnlyDictionary<string, CenterFaceTensor> Run(
        float[] input,
        long[] shape,
        CancellationToken cancellationToken);
}

internal sealed class OnnxCenterFaceInferenceSession : ICenterFaceInferenceSession
{
    private static readonly string[] RequiredOutputNames = ["537", "538", "539", "540"];

    private readonly InferenceSession _session;
    private readonly string _inputName;

    public OnnxCenterFaceInferenceSession(string modelPath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(modelPath);

        _session = new InferenceSession(modelPath);
        if (_session.InputNames.Count != 1)
        {
            _session.Dispose();
            throw new CenterFaceOutputException(
                $"CenterFace must expose exactly one input, but the model exposes {_session.InputNames.Count}.");
        }

        string[] missingOutputs = RequiredOutputNames
            .Where(name => !_session.OutputNames.Contains(name, StringComparer.Ordinal))
            .ToArray();
        if (missingOutputs.Length > 0)
        {
            _session.Dispose();
            throw new CenterFaceOutputException(
                $"CenterFace is missing required ONNX outputs: {string.Join(", ", missingOutputs)}.");
        }

        _inputName = _session.InputNames.Single();
    }

    public IReadOnlyDictionary<string, CenterFaceTensor> Run(
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
            RequiredOutputNames);

        Dictionary<string, CenterFaceTensor> outputs = new(StringComparer.Ordinal);
        for (int index = 0; index < RequiredOutputNames.Length; index++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            OrtValue outputValue = outputValues[index];
            OrtTensorTypeAndShapeInfo typeAndShape = outputValue.GetTensorTypeAndShape();
            if (typeAndShape.ElementDataType != TensorElementType.Float)
            {
                throw new CenterFaceOutputException(
                    $"CenterFace output '{RequiredOutputNames[index]}' must contain float32 values, " +
                    $"but the model returned {typeAndShape.ElementDataType}.");
            }

            outputs.Add(
                RequiredOutputNames[index],
                new CenterFaceTensor(
                    RequiredOutputNames[index],
                    typeAndShape.Shape,
                    outputValue.GetTensorDataAsSpan<float>()));
        }

        return outputs;
    }

    public void Dispose() => _session.Dispose();
}
