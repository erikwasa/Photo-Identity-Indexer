using Microsoft.AspNetCore.Hosting;
using Xunit;

namespace PhotoIdentity_Integration_Tests;

public sealed class DetectorComparisonWorkspaceApplicationTests
{
    [Fact]
    [Trait(TestCategories.Category, TestCategories.FlakyDiagnostic)]
    public async Task Published_application_contains_bounded_desktop_and_narrow_comparison_workspace_styles()
    {
        string directory = CreateTemporaryDirectory();
        try
        {
            string databasePath = Path.Combine(directory, "catalogue.db");
            string sessionRoot = Path.Combine(directory, "detector-sessions");
            await using PhotoIdentityApiTestFactory factory = new(
                databasePath,
                builder => builder.UseSetting("PhotoIdentity:DetectorEvaluationRoot", sessionRoot));
            using HttpClient client = factory.CreateClient();

            string shell = await client.GetRequiredStringAsync("/");
            string styles = await client.GetRequiredStringAsync("/PhotoIdentity.Web.styles.css");
            string viewportOverrides = await client.GetRequiredStringAsync("/css/detector-comparison-workspace.css?v=1");

            Assert.Contains("css/detector-comparison-workspace.css?v=1", shell, StringComparison.Ordinal);
            Assert.Contains("comparison-review-workspace", styles, StringComparison.Ordinal);
            Assert.Contains("comparison-workspace-body", styles, StringComparison.Ordinal);
            Assert.Contains("comparison-photo-viewport", styles, StringComparison.Ordinal);
            Assert.Contains("comparison-decision-panel", styles, StringComparison.Ordinal);
            Assert.Contains("42dvh", styles, StringComparison.Ordinal);
            Assert.Contains("position:sticky", styles.Replace(" ", string.Empty), StringComparison.Ordinal);
            Assert.Contains("overflow-y:auto", styles.Replace(" ", string.Empty), StringComparison.Ordinal);
            Assert.Contains("calc(100dvh - 14rem)", viewportOverrides, StringComparison.Ordinal);
            Assert.Contains("min-height: 0", viewportOverrides, StringComparison.Ordinal);
            Assert.Contains("height: auto", viewportOverrides, StringComparison.Ordinal);
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
