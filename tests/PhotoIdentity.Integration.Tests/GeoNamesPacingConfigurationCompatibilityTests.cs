using System.Net.Http.Json;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Data.Sqlite;
using PhotoIdentity.Api;
using Xunit;

namespace PhotoIdentity_Integration_Tests;

public sealed class GeoNamesPacingConfigurationCompatibilityTests
{
    [Theory]
    [InlineData(null, null, 11_000, 30_000)]
    [InlineData(5_000, null, 5_000, 5_000)]
    [InlineData(45_000, null, 11_000, 45_000)]
    [InlineData(5_000, 11_000, 11_000, 11_000)]
    [InlineData(45_000, 5_000, 5_000, 45_000)]
    public async Task Status_preserves_raw_default_while_honoring_automatic_overrides(
        int? automaticInterval,
        int? rawInterval,
        int expectedRawInterval,
        int expectedEffectiveAutomaticInterval)
    {
        string directory = Path.Combine(
            Path.GetTempPath(),
            "PhotoIdentity.Integration.Tests",
            Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);

        try
        {
            await using PhotoIdentityApiTestFactory factory = new(
                Path.Combine(directory, "catalogue.db"),
                builder =>
                {
                    builder.UseSetting("PhotoIdentity:GeoNames:Username", "pacing-compatibility-test");
                    builder.UseSetting("PhotoIdentity:GeoNames:AutomaticEnrichmentEnabled", "false");
                    if (automaticInterval is int automatic)
                    {
                        builder.UseSetting(
                            "PhotoIdentity:GeoNames:AutomaticMinimumRequestIntervalMilliseconds",
                            automatic.ToString(System.Globalization.CultureInfo.InvariantCulture));
                    }

                    if (rawInterval is int raw)
                    {
                        builder.UseSetting(
                            "PhotoIdentity:GeoNames:MinimumRequestIntervalMilliseconds",
                            raw.ToString(System.Globalization.CultureInfo.InvariantCulture));
                    }
                });
            using HttpClient client = factory.CreateClient();

            PhotoPlaceEnrichmentStatusResponse status =
                await client.GetFromJsonAsync<PhotoPlaceEnrichmentStatusResponse>("/api/place-enrichment/status")
                ?? throw new InvalidOperationException("The GeoNames status response was empty.");

            Assert.Equal(expectedRawInterval, status.MinimumRequestIntervalMilliseconds);
            Assert.Equal(
                expectedEffectiveAutomaticInterval,
                status.AutomaticMinimumRequestIntervalMilliseconds);
        }
        finally
        {
            SqliteConnection.ClearAllPools();
            if (Directory.Exists(directory))
            {
                Directory.Delete(directory, recursive: true);
            }
        }
    }
}
