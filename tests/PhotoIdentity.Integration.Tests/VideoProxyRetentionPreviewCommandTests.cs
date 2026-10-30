using PhotoIdentity.Cli;
using Xunit;

namespace PhotoIdentity_Integration_Tests;

public sealed class VideoProxyRetentionPreviewCommandTests
{
    private static string[] Valid() =>
    [
        "--postgres-connection-env", "PHOTOIDENTITY_TEST_POSTGRES_ADMIN_CONNECTION_STRING",
        "--database", "photoidentity_private_test",
        "--cache-root", Path.GetFullPath("private-video-cache"),
        "--quota-mib", "1024",
        "--max-proxy-mib", "256",
        "--incoming-mib", "128"
    ];

    [Fact]
    public void Preview_requires_explicit_database_and_private_root()
    {
        var settings = VideoProxyRetentionPreviewOptions.Parse(Valid());
        Assert.Equal(1024, settings.QuotaMiB);
        Assert.Equal(256, settings.MaximumProxyMiB);
        Assert.Equal(128, settings.IncomingMiB);
        Assert.Equal(1024, settings.MaximumRecords);
        Assert.Equal(TimeSpan.FromHours(48), settings.MinimumAge);
    }

    [Theory]
    [InlineData("--apply")]
    [InlineData("--verify-hashes")]
    [InlineData("--quota-mib")]
    public void Preview_rejects_mutating_or_incomplete_arguments(string argument)
    {
        Assert.Throws<ArgumentException>(() =>
            VideoProxyRetentionPreviewOptions.Parse([.. Valid(), argument]));
    }

    [Theory]
    [InlineData("--quota-mib", "63")]
    [InlineData("--quota-mib", "16385")]
    [InlineData("--max-proxy-mib", "257")]
    [InlineData("--incoming-mib", "0")]
    [InlineData("--max-records", "2049")]
    [InlineData("--min-age-hours", "23")]
    public void Preview_rejects_out_of_range_budgets(string name, string value)
    {
        string[] arguments = Valid().Concat([name, value]).ToArray();
        Assert.Throws<ArgumentException>(() =>
            VideoProxyRetentionPreviewOptions.Parse(arguments));
    }

    [Fact]
    public void Preview_cannot_accept_repeated_or_relative_options()
    {
        Assert.Throws<ArgumentException>(() =>
            VideoProxyRetentionPreviewOptions.Parse(
                [.. Valid(), "--quota-mib", "100"]));
        string[] args = Valid();
        args[5] = "relative-cache";
        Assert.Throws<ArgumentException>(() =>
            VideoProxyRetentionPreviewOptions.Parse(args));
    }
}
