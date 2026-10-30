using PhotoIdentity.Core.Processing;
using Xunit;

namespace PhotoIdentity_Integration_Tests;

public sealed class VideoProxyPublicationQuotaConfigurationTests
{
    [Fact]
    public void Unconfigured_quota_keeps_existing_default_disabled()
    {
        Assert.Null(VideoProxyPublicationQuotaConfiguration.ParseOptional(null, null));
    }

    [Theory]
    [InlineData("64", "1")]
    [InlineData("1024", "256")]
    [InlineData("16384", "256")]
    public void Both_approved_limits_are_exact_mib_values(string total, string perProxy)
    {
        VideoProxyRetentionLimits limits = Assert.IsType<VideoProxyRetentionLimits>(
            VideoProxyPublicationQuotaConfiguration.ParseOptional(total, perProxy));
        Assert.Equal(long.Parse(total) * 1024 * 1024, limits.MaximumCacheBytes);
        Assert.Equal(long.Parse(perProxy) * 1024 * 1024, limits.MaximumProxyBytes);
    }

    [Theory]
    [InlineData(null, "256")]
    [InlineData("1024", null)]
    [InlineData("", "256")]
    [InlineData("64", "")]
    [InlineData("0", "1")]
    [InlineData("63", "1")]
    [InlineData("16385", "256")]
    [InlineData("1024", "257")]
    [InlineData("1024", "0")]
    [InlineData("-1", "1")]
    [InlineData("1024.5", "1")]
    [InlineData(" 1024", "1")]
    [InlineData("99999999999999999999", "1")]
    public void Partial_or_invalid_configuration_rejects_before_processing(
        string? total, string? perProxy)
    {
        Assert.Throws<ArgumentException>(() =>
            VideoProxyPublicationQuotaConfiguration.ParseOptional(total, perProxy));
    }
}
