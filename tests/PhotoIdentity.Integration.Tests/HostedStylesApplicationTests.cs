using Xunit;

namespace PhotoIdentity_Integration_Tests;

public sealed class HostedStylesApplicationTests
{
    [Fact]
    [Trait(TestCategories.Category, TestCategories.FlakyDiagnostic)]
    public async Task Hosted_client_links_and_serves_the_Blazor_isolated_styles_bundle()
    {
        string directory = CreateTemporaryDirectory();
        try
        {
            string databasePath = Path.Combine(directory, "catalogue.db");
            await using PhotoIdentityApiTestFactory factory = new(databasePath);
            using HttpClient client = factory.CreateClient();

            string index = await client.GetRequiredStringAsync("/");
            Assert.Contains(
                "href=\"PhotoIdentity.Web.styles.css\"",
                index,
                StringComparison.Ordinal);

            string styles = await client.GetRequiredStringAsync("/PhotoIdentity.Web.styles.css");
            Assert.Contains(".collection-photo", styles, StringComparison.Ordinal);
            Assert.Contains(".collection-person-option", styles, StringComparison.Ordinal);
        }
        finally
        {
            DeleteTemporaryDirectory(directory);
        }
    }

    private static string CreateTemporaryDirectory()
    {
        string directory = Path.Combine(
            Path.GetTempPath(),
            "PhotoIdentity.Integration.Tests",
            Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        return directory;
    }

    private static void DeleteTemporaryDirectory(string directory)
    {
        if (Directory.Exists(directory))
        {
            Directory.Delete(directory, recursive: true);
        }
    }
}
