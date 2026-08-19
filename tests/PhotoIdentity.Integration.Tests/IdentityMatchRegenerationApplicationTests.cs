using System.Net;
using System.Net.Http.Json;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Xunit;

namespace PhotoIdentity_Integration_Tests;

public sealed class IdentityMatchRegenerationApplicationTests
{
    private const string ModelId = "sface-web-test";
    private static readonly string ModelHash = new('d', 64);

    [Fact]
    public async Task Regeneration_api_requires_exact_model_starts_background_work_and_reports_state()
    {
        string directory = CreateTemporaryDirectory();
        try
        {
            string databasePath = Path.Combine(directory, "catalogue.db");
            await using ReviewApiFactory factory = new(databasePath);
            using HttpClient client = factory.CreateClient();

            using HttpResponseMessage unscoped = await client.GetAsync("/api/review/match-regeneration");
            Assert.Equal(HttpStatusCode.BadRequest, unscoped.StatusCode);

            string url = RegenerationUrl(ModelId, ModelHash);
            using HttpResponseMessage start = await client.PostAsJsonAsync(url, new { actor = "application-test" });
            Assert.Equal(HttpStatusCode.Accepted, start.StatusCode);
            MatchState accepted = Assert.IsType<MatchState>(
                await start.Content.ReadFromJsonAsync<MatchState>());
            Assert.Equal(ModelId, accepted.ModelId);
            Assert.Equal(ModelHash, accepted.ModelHash);
            Assert.Equal(1, accepted.PolicyVersion);
            Assert.Equal(0, accepted.TargetCount);
            Assert.True(accepted.IsActive);

            MatchState latest = Assert.IsType<MatchState>(await client.GetFromJsonAsync<MatchState>(url));
            Assert.Equal(accepted.RunId, latest.RunId);
            Assert.Contains(latest.Status, new[] { "pending", "running", "completed" });
            Assert.False(latest.IsStale);
        }
        finally
        {
            DeleteTemporaryDirectory(directory);
        }
    }

    private static string RegenerationUrl(string modelId, string modelHash) =>
        $"/api/review/match-regeneration?modelId={Uri.EscapeDataString(modelId)}" +
        $"&modelHash={Uri.EscapeDataString(modelHash)}";

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

    private sealed class ReviewApiFactory : WebApplicationFactory<PhotoIdentity.Api.Program>
    {
        private readonly string _databasePath;

        public ReviewApiFactory(string databasePath)
        {
            _databasePath = databasePath;
        }

        protected override bool DisableBackgroundWorkers => false;

        protected override void ConfigureWebHost(IWebHostBuilder builder)
        {
            builder.UseSetting("PhotoIdentity:DatabasePath", _databasePath);
        }
    }

    private sealed record MatchState(
        Guid? RunId,
        string ModelId,
        string ModelHash,
        int PolicyVersion,
        string Status,
        bool IsActive,
        bool IsStale,
        int TargetCount,
        int ProcessedTargetCount,
        int SuggestedTargetCount,
        int SuggestionCount,
        int AutomaticallyAssignedCount,
        int ErrorCount,
        DateTimeOffset? RequestedAtUtc,
        DateTimeOffset? StartedAtUtc,
        DateTimeOffset? CompletedAtUtc,
        DateTimeOffset? UpdatedAtUtc,
        string? Error);
}
