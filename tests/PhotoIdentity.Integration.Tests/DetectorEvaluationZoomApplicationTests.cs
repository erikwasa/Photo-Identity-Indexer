using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Xunit;

namespace PhotoIdentity_Integration_Tests;

public sealed class DetectorEvaluationZoomApplicationTests
{
    [Fact]
    public async Task Published_application_serves_versioned_detector_zoom_helper()
    {
        string directory = CreateTemporaryDirectory();
        try
        {
            string databasePath = Path.Combine(directory, "catalogue.db");
            string sessionRoot = Path.Combine(directory, "private-sessions");

            await using DetectorEvaluationZoomApiFactory factory = new(databasePath, sessionRoot);
            using HttpClient client = factory.CreateClient();

            string shell = await client.GetStringAsync("/");
            Assert.Contains("detector-evaluation.js?v=2", shell, StringComparison.Ordinal);

            string script = await client.GetStringAsync("/detector-evaluation.js?v=2");
            Assert.Contains("applyZoom", script, StringComparison.Ordinal);
            Assert.Contains("naturalWidth", script, StringComparison.Ordinal);
            Assert.Contains("scrollLeft", script, StringComparison.Ordinal);
            Assert.Contains("getNormalizedPoint", script, StringComparison.Ordinal);
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

    private sealed class DetectorEvaluationZoomApiFactory : WebApplicationFactory<PhotoIdentity.Api.Program>
    {
        private readonly string _databasePath;
        private readonly string _sessionRoot;

        public DetectorEvaluationZoomApiFactory(string databasePath, string sessionRoot)
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
