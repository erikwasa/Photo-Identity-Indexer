using System.Net;
using System.Net.Http.Json;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using PhotoIdentity.Persistence.Sqlite;
using PhotoIdentity.Web.Contracts;
using Xunit;

namespace PhotoIdentity_Integration_Tests;

public sealed class IdentitySuggestionPolicyApplicationTests
{
    private const string ModelA = "sface-a";
    private const string ModelB = "sface-b";
    private static readonly string ModelHashA = new('a', 64);
    private static readonly string ModelHashB = new('b', 64);

    [Fact]
    public async Task Policy_api_requires_exact_model_and_keeps_revisions_isolated()
    {
        string directory = CreateTemporaryDirectory();
        try
        {
            string databasePath = Path.Combine(directory, "catalogue.db");
            await using ReviewApiFactory factory = new(databasePath);
            using HttpClient client = factory.CreateClient();

            using HttpResponseMessage unscoped = await client.GetAsync("/api/review/suggestion-policy");
            Assert.Equal(HttpStatusCode.BadRequest, unscoped.StatusCode);

            IdentitySuggestionPolicyResponse defaultA = Assert.IsType<IdentitySuggestionPolicyResponse>(
                await client.GetFromJsonAsync<IdentitySuggestionPolicyResponse>(PolicyUrl(ModelA, ModelHashA)));
            Assert.Equal(ModelA, defaultA.ModelId);
            Assert.Equal(ModelHashA, defaultA.ModelHash);
            Assert.Equal(1, defaultA.Version);
            Assert.False(defaultA.AutoAssignEnabled);

            using HttpResponseMessage updatedResponse = await client.PutAsJsonAsync(
                PolicyUrl(ModelA, ModelHashA),
                new UpdateIdentitySuggestionPolicyRequest(
                    AutoAssignEnabled: true,
                    HighScoreThreshold: 0.88,
                    HighMarginThreshold: 0.16,
                    MediumScoreThreshold: 0.62,
                    Actor: "policy-api:test"));
            updatedResponse.EnsureSuccessStatusCode();
            IdentitySuggestionPolicyResponse updatedA = Assert.IsType<IdentitySuggestionPolicyResponse>(
                await updatedResponse.Content.ReadFromJsonAsync<IdentitySuggestionPolicyResponse>());
            Assert.Equal(ModelA, updatedA.ModelId);
            Assert.Equal(ModelHashA, updatedA.ModelHash);
            Assert.Equal(2, updatedA.Version);
            Assert.True(updatedA.AutoAssignEnabled);
            Assert.Equal(0.88, updatedA.HighScoreThreshold, 10);
            Assert.Equal(0.16, updatedA.HighMarginThreshold, 10);
            Assert.Equal(0.62, updatedA.MediumScoreThreshold, 10);

            IdentitySuggestionPolicyResponse defaultB = Assert.IsType<IdentitySuggestionPolicyResponse>(
                await client.GetFromJsonAsync<IdentitySuggestionPolicyResponse>(PolicyUrl(ModelB, ModelHashB)));
            Assert.Equal(ModelB, defaultB.ModelId);
            Assert.Equal(ModelHashB, defaultB.ModelHash);
            Assert.Equal(1, defaultB.Version);
            Assert.False(defaultB.AutoAssignEnabled);
            Assert.Equal(IdentitySuggestionPolicy.DefaultHighScoreThreshold, defaultB.HighScoreThreshold);
            Assert.Equal(IdentitySuggestionPolicy.DefaultHighMarginThreshold, defaultB.HighMarginThreshold);
            Assert.Equal(IdentitySuggestionPolicy.DefaultMediumScoreThreshold, defaultB.MediumScoreThreshold);

            IdentitySuggestionPolicyResponse persistedA = Assert.IsType<IdentitySuggestionPolicyResponse>(
                await client.GetFromJsonAsync<IdentitySuggestionPolicyResponse>(PolicyUrl(ModelA, ModelHashA)));
            Assert.Equal(updatedA, persistedA);
        }
        finally
        {
            DeleteTemporaryDirectory(directory);
        }
    }

    private static string PolicyUrl(string modelId, string modelHash) =>
        $"/api/review/suggestion-policy?modelId={Uri.EscapeDataString(modelId)}" +
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

        protected override void ConfigureWebHost(IWebHostBuilder builder)
        {
            builder.UseSetting("PhotoIdentity:DatabasePath", _databasePath);
        }
    }
}
