using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Xunit;

namespace PhotoIdentity_Integration_Tests;

public sealed class HostedStylesApplicationTests
{
    [Fact]
    public async Task Hosted_client_links_and_serves_the_Blazor_isolated_styles_bundle()
    {
        string directory = CreateTemporaryDirectory();
        try
        {
            string databasePath = Path.Combine(directory, "catalogue.db");
            await using HostedStylesApiFactory factory = new(databasePath);
            using HttpClient client = factory.CreateClient();

            string index = await client.GetStringAsync("/");
            Assert.Contains(
                "href=\"PhotoIdentity.Web.styles.css\"",
                index,
                StringComparison.Ordinal);

            using HttpResponseMessage stylesResponse = await client.GetAsync(
                "/PhotoIdentity.Web.styles.css");
            stylesResponse.EnsureSuccessStatusCode();
            string styles = await stylesResponse.Content.ReadAsStringAsync();
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

    private sealed class HostedStylesApiFactory : WebApplicationFactory<PhotoIdentity.Api.Program>
    {
        private readonly string _databasePath;

        public HostedStylesApiFactory(string databasePath)
        {
            _databasePath = databasePath;
        }

        protected override void ConfigureWebHost(IWebHostBuilder builder)
        {
            builder.UseSetting("PhotoIdentity:DatabasePath", _databasePath);
        }
    }
}
