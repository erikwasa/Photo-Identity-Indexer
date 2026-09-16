using System.Net.Http.Json;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using PhotoIdentity.Core.Geometry;
using PhotoIdentity.Core.Identifiers;
using PhotoIdentity.Core.Imaging;
using PhotoIdentity.Web.Contracts;
using Xunit;

namespace PhotoIdentity_Integration_Tests;

public sealed class SlideshowFaceGeometryApplicationTests
{
    [Fact]
    public async Task Endpoint_returns_only_normalized_geometry_for_the_requested_revision()
    {
        string directory = Path.Combine(
            Path.GetTempPath(),
            "PhotoIdentity.Integration.Tests",
            Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);

        try
        {
            string databasePath = Path.Combine(directory, "catalogue.db");
            AssetRevisionId revisionId = AssetRevisionId.From(Guid.NewGuid());
            FaceReviewGeometry[] faces =
            [
                new(
                    FaceOccurrenceId.From(Guid.NewGuid()),
                    new NormalizedBoundingBox(0.20d, 0.25d, 0.18d, 0.22d)),
                new(
                    FaceOccurrenceId.From(Guid.NewGuid()),
                    new NormalizedBoundingBox(0.55d, 0.24d, 0.16d, 0.20d)),
            ];
            FakeFaceReviewDerivativeRepository repository = new(revisionId, faces);

            await using PhotoIdentityApiTestFactory factory = new(
                databasePath,
                builder => builder.ConfigureServices(services =>
                {
                    services.RemoveAll<IFaceReviewDerivativeRepository>();
                    services.AddSingleton<IFaceReviewDerivativeRepository>(repository);
                }));
            using HttpClient client = factory.CreateClient();

            SlideshowFaceGeometryResponse response =
                await client.GetFromJsonAsync<SlideshowFaceGeometryResponse>(
                    $"/api/collections/photos/{revisionId}/slideshow-face-geometry")
                ?? throw new InvalidOperationException("Slideshow geometry response was empty.");

            Assert.Equal(revisionId, repository.RequestedRevisionId);
            Assert.Collection(
                response.Faces,
                face => Assert.Equal(
                    new SlideshowFaceBoxResponse(0.20d, 0.25d, 0.18d, 0.22d),
                    face),
                face => Assert.Equal(
                    new SlideshowFaceBoxResponse(0.55d, 0.24d, 0.16d, 0.20d),
                    face));
        }
        finally
        {
            if (Directory.Exists(directory))
            {
                Directory.Delete(directory, recursive: true);
            }
        }
    }

    private sealed class FakeFaceReviewDerivativeRepository(
        AssetRevisionId expectedRevisionId,
        IReadOnlyList<FaceReviewGeometry> faces) : IFaceReviewDerivativeRepository
    {
        public AssetRevisionId? RequestedRevisionId { get; private set; }

        public Task<IReadOnlyList<FaceReviewGeometry>> GetFacesAsync(
            AssetRevisionId revisionId,
            CancellationToken cancellationToken = default)
        {
            Assert.Equal(expectedRevisionId, revisionId);
            RequestedRevisionId = revisionId;
            return Task.FromResult(faces);
        }

        public Task<FaceReviewDerivativeRecord?> GetAsync(
            FaceOccurrenceId faceOccurrenceId,
            string profileId,
            CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public Task<bool> IsRevisionCompleteAsync(
            AssetRevisionId revisionId,
            string profileId,
            CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public Task RecordRevisionCompletionAsync(
            AssetRevisionId revisionId,
            string profileId,
            IReadOnlyList<FaceReviewDerivativeRecord> derivatives,
            DateTimeOffset completedAtUtc,
            CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();
    }
}
