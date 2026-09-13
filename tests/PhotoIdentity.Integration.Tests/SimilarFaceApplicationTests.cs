using System.Net;
using System.Net.Http.Json;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.DependencyInjection;
using PhotoIdentity.Core.Identifiers;
using PhotoIdentity.Core.Recognition;
using PhotoIdentity.Core.Review;
using PhotoIdentity.Web.Contracts;
using Xunit;

namespace PhotoIdentity_Integration_Tests;

public sealed class SimilarFaceApplicationTests
{
    [Fact]
    public async Task Similar_face_endpoint_preserves_scope_and_selected_subset_flows_into_existing_bulk_review()
    {
        string directory = Path.Combine(Path.GetTempPath(), $"photoidentity-similar-api-{Guid.NewGuid():N}");
        Directory.CreateDirectory(directory);
        try
        {
            string databasePath = Path.Combine(directory, "catalogue.db");
            FaceOccurrenceId sourceFaceId = FaceOccurrenceId.New();
            FaceOccurrenceId firstFaceId = FaceOccurrenceId.New();
            FaceOccurrenceId unknownFaceId = FaceOccurrenceId.New();
            ModelId modelId = new("sface-test");
            Sha256Digest modelHash = new(new string('a', 64));
            FakeSimilarFaceRepository fake = new(
                new CatalogueSimilarFaceQueryResult(
                    sourceFaceId,
                    modelId,
                    modelHash,
                    IncludeUnknown: true,
                    ScannedFaceCount: 17,
                    ElapsedMilliseconds: 12,
                    Items:
                    [
                        new CatalogueSimilarFace(CreateFace(firstFaceId, CatalogueReviewStates.Unreviewed), 0.9876),
                        new CatalogueSimilarFace(CreateFace(unknownFaceId, CatalogueReviewStates.Unknown), 0.8765),
                    ]));
            FakeBulkReviewRepository bulk = new();

            await using PhotoIdentityApiTestFactory factory = new(
                databasePath,
                builder => builder.ConfigureServices(services =>
                {
                    services.AddSingleton<ISimilarFaceRepository>(fake);
                    services.AddSingleton<IBulkReviewRepository>(bulk);
                }));
            using HttpClient client = factory.CreateClient();

            string endpoint =
                $"/api/review/faces/{sourceFaceId}/similar" +
                $"?modelId={modelId}&modelHash={modelHash}&includeUnknown=true&limit=20";
            SimilarFacePageResponse response = Assert.IsType<SimilarFacePageResponse>(
                await client.GetFromJsonAsync<SimilarFacePageResponse>(endpoint));

            Assert.Equal(sourceFaceId.ToString(), response.SourceFaceId);
            Assert.Equal(modelId.ToString(), response.ModelId);
            Assert.Equal(modelHash.ToString(), response.ModelHash);
            Assert.True(response.IncludeUnknown);
            Assert.Equal(17, response.ScannedFaceCount);
            Assert.Equal(12, response.QueryElapsedMilliseconds);
            Assert.Equal([firstFaceId.ToString(), unknownFaceId.ToString()], response.Items.Select(item => item.Face.Id).ToArray());
            Assert.Equal([0.9876, 0.8765], response.Items.Select(item => item.Similarity).ToArray());
            Assert.Equal(CatalogueReviewStates.Unknown, response.Items[1].Face.State);
            Assert.Equal(sourceFaceId, fake.SourceFaceId);
            Assert.Equal(modelId, fake.ModelId);
            Assert.Equal(modelHash, fake.ModelHash);
            Assert.True(fake.IncludeUnknown);
            Assert.Equal(20, fake.Limit);

            PersonId personId = PersonId.New();
            string[] selectedSubset = [response.Items[0].Face.Id];
            using HttpResponseMessage previewMessage = await client.PostAsJsonAsync(
                "/api/review/bulk/preview",
                new BulkReviewPreviewRequest(selectedSubset, BulkReviewActionKinds.Assign, personId.ToString()));
            await previewMessage.EnsureSuccessWithDiagnosticBodyAsync("bulk preview from similar-face result");
            BulkReviewPreviewResponse preview = Assert.IsType<BulkReviewPreviewResponse>(
                await previewMessage.Content.ReadFromJsonAsync<BulkReviewPreviewResponse>());

            using HttpResponseMessage commitMessage = await client.PostAsJsonAsync(
                "/api/review/bulk/commit",
                new BulkReviewCommitRequest(
                    selectedSubset,
                    BulkReviewActionKinds.Assign,
                    personId.ToString(),
                    preview.AffectedCount,
                    preview.PreviewToken,
                    Confirm: true,
                    Actor: "test"));
            await commitMessage.EnsureSuccessWithDiagnosticBodyAsync("bulk commit from similar-face result");
            BulkReviewCommitResponse committed = Assert.IsType<BulkReviewCommitResponse>(
                await commitMessage.Content.ReadFromJsonAsync<BulkReviewCommitResponse>());
            Assert.Equal(1, committed.AffectedCount);
            Assert.Equal([firstFaceId], bulk.CommittedFaceIds);
            Assert.DoesNotContain(unknownFaceId, bulk.CommittedFaceIds);

            using HttpResponseMessage invalid = await client.GetAsync(
                $"/api/review/faces/{sourceFaceId}/similar?modelId={modelId}&modelHash=bad");
            Assert.Equal(HttpStatusCode.BadRequest, invalid.StatusCode);
        }
        finally
        {
            try
            {
                Directory.Delete(directory, recursive: true);
            }
            catch (IOException)
            {
                // SQLite can briefly retain a file handle after the test host stops on Windows.
            }
            catch (UnauthorizedAccessException)
            {
                // Best-effort cleanup only.
            }
        }
    }

    private static CatalogueReviewFace CreateFace(FaceOccurrenceId id, string state) => new(
        id,
        Ordinal: 0,
        CreatedAtUtc: new DateTimeOffset(2026, 9, 13, 12, 0, 0, TimeSpan.Zero),
        PhotoName: "photo.jpg",
        MediaType: "image/jpeg",
        PhotoWidth: 1920,
        PhotoHeight: 1080,
        RevisionHash: new Sha256Digest(new string('b', 64)),
        CropStoragePath: "crops/face.jpg",
        Confidence: 0.95,
        State: state,
        Person: null,
        ActiveActionId: state == CatalogueReviewStates.Unknown ? 1 : null,
        RevisionId: AssetRevisionId.New(),
        BoundingBoxJson: "[0.1,0.1,0.5,0.5]");

    private sealed class FakeSimilarFaceRepository : ISimilarFaceRepository
    {
        private readonly CatalogueSimilarFaceQueryResult _result;

        public FakeSimilarFaceRepository(CatalogueSimilarFaceQueryResult result)
        {
            _result = result;
        }

        public FaceOccurrenceId SourceFaceId { get; private set; }
        public ModelId ModelId { get; private set; }
        public Sha256Digest ModelHash { get; private set; }
        public bool IncludeUnknown { get; private set; }
        public int Limit { get; private set; }

        public Task<CatalogueSimilarFaceQueryResult?> FindSimilarAsync(
            FaceOccurrenceId sourceFaceId,
            ModelId modelId,
            Sha256Digest modelHash,
            bool includeUnknown = false,
            int limit = 100,
            CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            SourceFaceId = sourceFaceId;
            ModelId = modelId;
            ModelHash = modelHash;
            IncludeUnknown = includeUnknown;
            Limit = limit;
            return Task.FromResult<CatalogueSimilarFaceQueryResult?>(_result);
        }
    }

    private sealed class FakeBulkReviewRepository : IBulkReviewRepository
    {
        public IReadOnlyList<FaceOccurrenceId> CommittedFaceIds { get; private set; } = [];

        public Task<BulkReviewPreview> PreviewAsync(
            IReadOnlyCollection<FaceOccurrenceId> faceOccurrenceIds,
            string action,
            PersonId? personId,
            CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            ReviewPerson? person = personId is PersonId id
                ? new ReviewPerson(id, "Candidate person")
                : null;
            return Task.FromResult(new BulkReviewPreview(
                action,
                faceOccurrenceIds.Count,
                faceOccurrenceIds.Count,
                SkippedCount: 0,
                PreviewToken: "similar-face-preview",
                person));
        }

        public Task<BulkReviewResult> CommitAsync(
            IReadOnlyCollection<FaceOccurrenceId> faceOccurrenceIds,
            string action,
            PersonId? personId,
            int expectedAffectedCount,
            string previewToken,
            string actor,
            DateTimeOffset createdAtUtc,
            string? note = null,
            CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            Assert.Equal("similar-face-preview", previewToken);
            Assert.Equal(expectedAffectedCount, faceOccurrenceIds.Count);
            CommittedFaceIds = faceOccurrenceIds.OrderBy(id => id.ToString(), StringComparer.Ordinal).ToArray();
            ReviewPerson? person = personId is PersonId id
                ? new ReviewPerson(id, "Candidate person")
                : null;
            return Task.FromResult(new BulkReviewResult(
                action,
                faceOccurrenceIds.Count,
                faceOccurrenceIds.Count,
                person,
                createdAtUtc));
        }
    }
}
