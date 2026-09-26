using System.Net;
using System.Net.Http.Json;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.DependencyInjection;
using PhotoIdentity.Core.Identifiers;
using PhotoIdentity.Core.Sources;
using PhotoIdentity.Web;
using Xunit;

namespace PhotoIdentity_Integration_Tests;

public sealed class ArchiveLifecycleApplicationTests
{
    [Fact]
    public async Task Bulk_exclusion_is_atomic_and_immediately_withdraws_the_selected_duplicate_copy()
    {
        string directory = CreateTemporaryDirectory();
        try
        {
            string archiveRoot = Path.Combine(directory, "Kamerabilder");
            string reviewFolder = Path.Combine(archiveRoot, "Review");
            Directory.CreateDirectory(reviewFolder);
            byte[] duplicateBytes = [1, 2, 3, 4, 5, 6];
            await File.WriteAllBytesAsync(Path.Combine(reviewFolder, "copy-a.jpg"), duplicateBytes);
            await File.WriteAllBytesAsync(Path.Combine(reviewFolder, "copy-b.jpg"), duplicateBytes);

            string databasePath = Path.Combine(directory, "catalogue.db");
            await using PhotoIdentityApiTestFactory factory = new(
                databasePath,
                builder =>
                {
                    builder.UseSetting("PhotoIdentity:RepositoryRoot", FindRepositoryRoot());
                    builder.UseSetting(
                        "PhotoIdentity:ArchiveAnalysisOutputRoot",
                        Path.Combine(directory, "analysis-output"));
                });
            using HttpClient client = factory.CreateClient();

            using HttpResponseMessage include = await client.PostAsJsonAsync(
                "/api/archive/include",
                new ArchiveIncludeRequest(archiveRoot, "Review"));
            await include.EnsureSuccessWithDiagnosticBodyAsync();
            using HttpResponseMessage sync = await client.PostAsync("/api/archive/sync", null);
            await sync.EnsureSuccessWithDiagnosticBodyAsync();

            ArchiveExactDuplicateGroupResponse[] initialGroups =
                await client.GetFromJsonAsync<ArchiveExactDuplicateGroupResponse[]>(
                    "/api/archive/exact-duplicates") ?? [];
            ArchiveExactDuplicateGroupResponse initialGroup = Assert.Single(initialGroups);
            Assert.Equal(2, initialGroup.Copies.Count);
            Assert.NotEqual(initialGroup.Copies[0].AssetId, initialGroup.Copies[1].AssetId);

            string selectedRevision = initialGroup.Copies[0].RevisionId;

            using HttpResponseMessage invalidBulk = await client.PostAsJsonAsync(
                "/api/archive/exclusions/revisions",
                new ArchiveExcludeRevisionsRequest([selectedRevision, "not-a-guid"]));
            Assert.Equal(HttpStatusCode.BadRequest, invalidBulk.StatusCode);
            ArchiveSourceCopyExclusionResponse[] afterInvalid =
                await client.GetFromJsonAsync<ArchiveSourceCopyExclusionResponse[]>(
                    "/api/archive/exclusions") ?? [];
            Assert.Empty(afterInvalid);

            using HttpResponseMessage validBulk = await client.PostAsJsonAsync(
                "/api/archive/exclusions/revisions",
                new ArchiveExcludeRevisionsRequest([selectedRevision]));
            await validBulk.EnsureSuccessWithDiagnosticBodyAsync();
            ArchiveBulkExclusionResponse result = Assert.IsType<ArchiveBulkExclusionResponse>(
                await validBulk.Content.ReadFromJsonAsync<ArchiveBulkExclusionResponse>());
            Assert.Equal(1, result.Requested);
            Assert.Equal(1, result.Excluded);
            ArchiveSourceCopyExclusionResponse pending = Assert.Single(result.Exclusions);
            Assert.Equal(SourceCopyPurgeStates.Pending, pending.PurgeState);

            ArchiveSourceCopyExclusionResponse[] exclusions =
                await client.GetFromJsonAsync<ArchiveSourceCopyExclusionResponse[]>(
                    "/api/archive/exclusions") ?? [];
            Assert.Single(exclusions);

            ArchiveExactDuplicateGroupResponse[] afterExclusion =
                await client.GetFromJsonAsync<ArchiveExactDuplicateGroupResponse[]>(
                    "/api/archive/exact-duplicates") ?? [];
            Assert.Empty(afterExclusion);

            ISourceCopyExclusionRepository exclusionRepository =
                factory.Services.GetRequiredService<ISourceCopyExclusionRepository>();
            SourceId sourceId = SourceId.From(Guid.Parse(pending.SourceId));
            await exclusionRepository.SetPurgeStateAsync(
                sourceId,
                pending.SourceKey,
                SourceCopyPurgeStates.Failed,
                "derivative-delete-failed",
                DateTimeOffset.UtcNow);

            using HttpResponseMessage retry = await client.PostAsJsonAsync(
                "/api/archive/exclusions/retry",
                new ArchiveRetrySourceCopyPurgeRequest(pending.SourceId, pending.SourceKey));
            await retry.EnsureSuccessWithDiagnosticBodyAsync();
            ArchiveSourceCopyExclusionResponse retried = Assert.IsType<ArchiveSourceCopyExclusionResponse>(
                await retry.Content.ReadFromJsonAsync<ArchiveSourceCopyExclusionResponse>());
            Assert.Equal(SourceCopyPurgeStates.Pending, retried.PurgeState);
            Assert.Null(retried.PurgeErrorCode);

            using HttpResponseMessage prematureRestore = await client.PostAsJsonAsync(
                "/api/archive/exclusions/restore",
                new ArchiveRestoreSourceCopyRequest(pending.SourceId, pending.SourceKey));
            Assert.Equal(HttpStatusCode.Conflict, prematureRestore.StatusCode);

            await exclusionRepository.SetPurgeStateAsync(
                sourceId,
                pending.SourceKey,
                SourceCopyPurgeStates.Completed,
                null,
                DateTimeOffset.UtcNow);

            using HttpResponseMessage restore = await client.PostAsJsonAsync(
                "/api/archive/exclusions/restore",
                new ArchiveRestoreSourceCopyRequest(pending.SourceId, pending.SourceKey));
            Assert.Equal(HttpStatusCode.NoContent, restore.StatusCode);

            ArchiveSourceCopyExclusionResponse[] afterRestore =
                await client.GetFromJsonAsync<ArchiveSourceCopyExclusionResponse[]>(
                    "/api/archive/exclusions") ?? [];
            Assert.Empty(afterRestore);
        }
        finally
        {
            DeleteTemporaryDirectory(directory);
        }
    }

    [Fact]
    public async Task Removed_source_filter_keeps_the_verified_revision_until_exclusion_then_moves_it_out_of_removed_review()
    {
        string directory = CreateTemporaryDirectory();
        try
        {
            string archiveRoot = Path.Combine(directory, "Kamerabilder");
            string reviewFolder = Path.Combine(archiveRoot, "Review");
            Directory.CreateDirectory(reviewFolder);
            string removedPath = Path.Combine(reviewFolder, "removed.jpg");
            await File.WriteAllBytesAsync(removedPath, [9, 8, 7, 6]);
            await File.WriteAllBytesAsync(Path.Combine(reviewFolder, "retained.jpg"), [4, 3, 2, 1]);

            string databasePath = Path.Combine(directory, "catalogue.db");
            await using PhotoIdentityApiTestFactory factory = new(
                databasePath,
                builder =>
                {
                    builder.UseSetting("PhotoIdentity:RepositoryRoot", FindRepositoryRoot());
                    builder.UseSetting(
                        "PhotoIdentity:ArchiveAnalysisOutputRoot",
                        Path.Combine(directory, "analysis-output"));
                });
            using HttpClient client = factory.CreateClient();

            await (await client.PostAsJsonAsync(
                    "/api/archive/include",
                    new ArchiveIncludeRequest(archiveRoot, "Review")))
                .EnsureSuccessWithDiagnosticBodyAsync();
            await (await client.PostAsync("/api/archive/sync", null))
                .EnsureSuccessWithDiagnosticBodyAsync();

            File.Delete(removedPath);
            await (await client.PostAsync("/api/archive/sync", null))
                .EnsureSuccessWithDiagnosticBodyAsync();

            const string removedQuery =
                "/api/archive/items/filter?availability=all&verification=all&analysis=missing&folder=&offset=0&limit=50";
            ArchiveItemPageResponse removed = Assert.IsType<ArchiveItemPageResponse>(
                await client.GetFromJsonAsync<ArchiveItemPageResponse>(removedQuery));
            ArchiveItemStatusResponse removedItem = Assert.Single(removed.Items);
            Assert.Equal("missing", removedItem.AnalysisState);
            Assert.False(string.IsNullOrWhiteSpace(removedItem.RevisionId));

            using HttpResponseMessage exclude = await client.PostAsJsonAsync(
                "/api/archive/exclusions/revisions",
                new ArchiveExcludeRevisionsRequest([removedItem.RevisionId!]));
            await exclude.EnsureSuccessWithDiagnosticBodyAsync();
            ArchiveBulkExclusionResponse result = Assert.IsType<ArchiveBulkExclusionResponse>(
                await exclude.Content.ReadFromJsonAsync<ArchiveBulkExclusionResponse>());
            Assert.Equal(1, result.Excluded);

            ArchiveItemPageResponse afterExclusion = Assert.IsType<ArchiveItemPageResponse>(
                await client.GetFromJsonAsync<ArchiveItemPageResponse>(removedQuery));
            Assert.Equal(0, afterExclusion.Total);
            Assert.Empty(afterExclusion.Items);
        }
        finally
        {
            DeleteTemporaryDirectory(directory);
        }
    }

    private static string FindRepositoryRoot()
    {
        DirectoryInfo? current = new(AppContext.BaseDirectory);
        while (current is not null)
        {
            if (File.Exists(Path.Combine(current.FullName, "PhotoIdentity.slnx")))
            {
                return current.FullName;
            }

            current = current.Parent;
        }

        throw new DirectoryNotFoundException("Repository root could not be located from the test output directory.");
    }

    private static string CreateTemporaryDirectory()
    {
        string path = Path.Combine(Path.GetTempPath(), $"photoidentity-wi0091-{Guid.NewGuid():N}");
        Directory.CreateDirectory(path);
        return path;
    }

    private static void DeleteTemporaryDirectory(string directory)
    {
        if (Directory.Exists(directory))
        {
            Directory.Delete(directory, recursive: true);
        }
    }
}
