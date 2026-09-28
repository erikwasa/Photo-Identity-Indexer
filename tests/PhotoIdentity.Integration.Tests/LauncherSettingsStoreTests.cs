using System.Globalization;
using System.Text.Json.Nodes;
using PhotoIdentity.Api;
using Xunit;

namespace PhotoIdentity_Integration_Tests;

public sealed class LauncherSettingsStoreTests
{
    [Fact]
    public async Task Save_archive_hydration_policy_preserves_other_launcher_settings_and_writes_gb_as_bytes()
    {
        string directory = Path.Combine(
            Path.GetTempPath(),
            "PhotoIdentity.Integration.Tests",
            Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        string path = Path.Combine(directory, "launcher.json");

        try
        {
            await File.WriteAllTextAsync(
                path,
                """
                {
                  "publishPath": "app",
                  "settings": {
                    "PhotoIdentity__ReviewProxyProfileId": "jpeg-test"
                  }
                }
                """);

            LauncherSettingsStore store = new(path);
            await store.SaveArchiveHydrationPolicyAsync(20m, 30.5m, 3);

            JsonObject root = JsonNode.Parse(await File.ReadAllTextAsync(path))!.AsObject();
            JsonObject settings = root["settings"]!.AsObject();

            Assert.Equal("app", root["publishPath"]!.GetValue<string>());
            Assert.Equal("jpeg-test", settings["PhotoIdentity__ReviewProxyProfileId"]!.GetValue<string>());
            Assert.Equal(
                LauncherSettingsStore.GigabytesToBytes(20m, allowZero: true, "reserve")
                    .ToString(CultureInfo.InvariantCulture),
                settings["PhotoIdentity__ArchiveHydration__MinimumFreeSpaceReserveBytes"]!.GetValue<string>());
            Assert.Equal(
                LauncherSettingsStore.GigabytesToBytes(30.5m, allowZero: false, "maximum")
                    .ToString(CultureInfo.InvariantCulture),
                settings["PhotoIdentity__ArchiveHydration__MaximumManagedHydrationBytes"]!.GetValue<string>());
            Assert.Equal(
                "3",
                settings["PhotoIdentity__ArchiveHydration__MaximumConcurrentOperations"]!.GetValue<string>());
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [Theory]
    [InlineData("0", 0L)]
    [InlineData("1", 1073741824L)]
    [InlineData("0.5", 536870912L)]
    public void Gigabytes_to_bytes_is_deterministic(string gigabytes, long expectedBytes)
    {
        decimal value = decimal.Parse(gigabytes, CultureInfo.InvariantCulture);
        Assert.Equal(
            expectedBytes,
            LauncherSettingsStore.GigabytesToBytes(value, allowZero: true, "value"));
    }

    [Fact]
    public async Task Save_archive_hydration_policy_rejects_invalid_limits_without_changing_file()
    {
        string directory = Path.Combine(
            Path.GetTempPath(),
            "PhotoIdentity.Integration.Tests",
            Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        string path = Path.Combine(directory, "launcher.json");
        const string original = "{\"settings\":{}}";

        try
        {
            await File.WriteAllTextAsync(path, original);
            LauncherSettingsStore store = new(path);

            await Assert.ThrowsAsync<ArgumentOutOfRangeException>(() =>
                store.SaveArchiveHydrationPolicyAsync(0m, 0m, 2));
            Assert.Equal(original, await File.ReadAllTextAsync(path));
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }
}
