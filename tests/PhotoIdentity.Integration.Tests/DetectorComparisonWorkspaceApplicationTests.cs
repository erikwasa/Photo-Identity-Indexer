using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Xunit;

namespace PhotoIdentity_Integration_Tests;

public sealed class DetectorComparisonWorkspaceApplicationTests
{
    [Fact]
    public async Task Published_application_contains_bounded_desktop_and_narrow_comparison_workspace_styles()
    {
        string directory = CreateTemporaryDirectory();
        try
        {
            await using DetectorComparisonWorkspaceApiFactory factory = new(
                Path.Combine(directory, "catalogue.db"),
                Path.Combine(directory, "private-sessions"));
            using HttpClient client = factory.CreateClient();

            string styles = await client.GetStringAsync("/PhotoIdentity.Web.styles.css");

            Assert.Contains("comparison-review-workspace", styles, StringComparison.Ordinal);
            Assert.Contains("comparison-workspace-body", styles, StringComparison.Ordinal);
            Assert.Contains("comparison-photo-viewport", styles, StringComparison.Ordinal);
            Assert.Contains("comparison-decision-panel", styles, StringComparison.Ordinal);
            Assert.Contains("100dvh", styles, StringComparison.Ordinal);
            Assert.Contains("42dvh", styles, StringComparison.Ordinal);
            Assert.Contains("position:sticky", styles.Replace(" ", string.Empty), StringComparison.Ordinal);
            Assert.Contains("overflow-y:auto", styles.Replace(" ", string.Empty), StringComparison.Ordinal);
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

    private sealed class DetectorComparisonWorkspaceApiFactory : WebApplicationFactory<PhotoIdentity.Api.Program>
    {
        private readonly string _databasePath;
        private readonly string _sessionRoot;

        public DetectorComparisonWorkspaceApiFactory(string databasePath, string sessionRoot)
        {
            _databasePath = databasePath;
            _sessionRoot = sessionRoot;
        }

        protected override void ConfigureWebHost(IWebHostBuilder builder)
        {
            builder.UseSetting("PhotoIdentity:DatabasePath", _databasePath);
            builder.UseSetting("PhotoIdentity:DetectorEvaluationRoot", _sessionRoot);
        }
    }
}
