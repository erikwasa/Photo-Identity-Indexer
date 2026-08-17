namespace PhotoIdentity.Api;

public static class BrowserImageContentTypes
{
    public static bool CanRender(string? contentType) =>
        contentType?.Trim().ToLowerInvariant() switch
        {
            "image/jpeg" => true,
            "image/jpg" => true,
            "image/png" => true,
            "image/gif" => true,
            "image/webp" => true,
            "image/bmp" => true,
            "image/avif" => true,
            _ => false,
        };
}
