using OpenCvSharp;
using PhotoIdentity.Recognition.Onnx.CenterFace;
using Xunit;

namespace PhotoIdentity.Recognition.Tests;

public sealed class CenterFaceOpenCvDnnTensorTests
{
    [Fact]
    public void Output_reader_copies_four_dimensional_float_tensor()
    {
        float[] expected = Enumerable.Range(0, 24)
            .Select(value => value + 0.25f)
            .ToArray();
        using Mat output = Mat.FromPixelData(
            [1, 2, 3, 4],
            MatType.CV_32FC1,
            expected);

        CenterFaceTensor tensor = OpenCvDnnCenterFaceInferenceSession.ReadOutputTensor(
            "537",
            output);

        Assert.Equal([1L, 2L, 3L, 4L], tensor.Shape);
        Assert.Equal(expected, tensor.Data.ToArray());
    }

    [Fact]
    public void Output_reader_rejects_non_float_tensor()
    {
        byte[] values = new byte[24];
        using Mat output = Mat.FromPixelData(
            [1, 2, 3, 4],
            MatType.CV_8UC1,
            values);

        CenterFaceOutputException exception = Assert.Throws<CenterFaceOutputException>(() =>
            OpenCvDnnCenterFaceInferenceSession.ReadOutputTensor("537", output));

        Assert.Contains("float32", exception.Message, StringComparison.Ordinal);
    }
}
