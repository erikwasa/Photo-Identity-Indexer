namespace PhotoIdentity.Web.Components;

internal static class NullableDoubleFormattingExtensions
{
    public static string ToString(this double? value, IFormatProvider provider) =>
        value?.ToString(provider) ?? string.Empty;
}
