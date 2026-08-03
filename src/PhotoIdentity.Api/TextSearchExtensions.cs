namespace PhotoIdentity.Api;

internal static class TextSearchExtensions
{
    public static bool ContainsAny(this string value, char[] characters)
    {
        ArgumentNullException.ThrowIfNull(value);
        ArgumentNullException.ThrowIfNull(characters);
        return value.IndexOfAny(characters) >= 0;
    }
}
