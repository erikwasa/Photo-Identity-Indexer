namespace PhotoIdentity.Core.Catalogue;

public enum CaptionInferenceMode
{
    Local,
    Remote,
}

/// <summary>Operator-selected transport only; never part of caption evidence identity.</summary>
public static class CaptionInferenceEndpoint
{
    public static CaptionInferenceMode ParseMode(string? value) => value?.Trim().ToLowerInvariant() switch
    {
        null or "local" => CaptionInferenceMode.Local,
        "remote" => CaptionInferenceMode.Remote,
        _ => throw new ArgumentException("Caption inference mode must be Local or Remote."),
    };

    public static Uri Parse(string value, CaptionInferenceMode mode)
    {
        if (!Uri.TryCreate(value, UriKind.Absolute, out Uri? uri))
        {
            throw new ArgumentException("Caption endpoint must be an absolute HTTP(S) URL.");
        }
        return Validate(uri, mode);
    }

    public static Uri Validate(Uri uri, CaptionInferenceMode mode)
    {
        ArgumentNullException.ThrowIfNull(uri);
        if (!uri.IsAbsoluteUri ||
            (uri.Scheme != Uri.UriSchemeHttp && uri.Scheme != Uri.UriSchemeHttps) ||
            string.IsNullOrEmpty(uri.Host))
        {
            throw new ArgumentException("Caption endpoint must be an absolute HTTP(S) URL.");
        }
        if (!string.IsNullOrEmpty(uri.UserInfo) || !string.IsNullOrEmpty(uri.Query) ||
            !string.IsNullOrEmpty(uri.Fragment))
        {
            throw new ArgumentException("Caption endpoint cannot contain credentials, a query string or a fragment.");
        }
        switch (mode)
        {
            case CaptionInferenceMode.Local when !uri.IsLoopback:
                throw new ArgumentException("Local caption inference requires an absolute loopback HTTP(S) URL.");
            case CaptionInferenceMode.Remote when uri.Scheme != Uri.UriSchemeHttps:
                throw new ArgumentException("Remote caption inference requires an absolute HTTPS URL.");
            case CaptionInferenceMode.Local:
            case CaptionInferenceMode.Remote:
                break;
            default:
                throw new ArgumentException("Caption inference mode must be Local or Remote.");
        }
        return uri.AbsolutePath.EndsWith("/", StringComparison.Ordinal)
            ? uri
            : new Uri(uri.AbsoluteUri + "/", UriKind.Absolute);
    }
}
