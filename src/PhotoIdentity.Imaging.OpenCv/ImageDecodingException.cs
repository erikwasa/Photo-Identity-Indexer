namespace PhotoIdentity.Imaging.OpenCv;

public enum ImageDecodingFailure
{
    UnsupportedFormat,
    CorruptMedia,
}

public sealed class ImageDecodingException : Exception
{
    public ImageDecodingException(
        ImageDecodingFailure failure,
        string message,
        Exception? innerException = null)
        : base(message, innerException)
    {
        Failure = failure;
    }

    public ImageDecodingFailure Failure { get; }
}
