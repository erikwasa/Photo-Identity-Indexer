using PhotoIdentity.Web.Contracts;
using Xunit;

namespace PhotoIdentity_Integration_Tests;

/// <summary>
/// Fast, source-only safety contracts. No live server, database, video,
/// model execution, FFmpeg, original pixels or private identity changes.
/// </summary>
public sealed class VideoFacePresenceReviewContractTests
{
    [Fact]
    public void Presence_is_never_automatically_enabled_in_browser_contract()
    {
        var face = new PersistedVideoFaceObservation(
            Guid.NewGuid().ToString("D"), 0, 4000, 0, 1600, 900,
            .2, .1, .05, .1, [], "known", Guid.NewGuid().ToString("D"), "Test person");
        var batch = new PersistedVideoFaceBatch(
            Guid.NewGuid().ToString("D"), Guid.NewGuid().ToString("D"),
            "test", "test", "test", "test", [face]);
        Assert.False(batch.PresencePromotionEnabled);
        Assert.Null(face.Presence);
        Assert.Equal(1, VideoFaceReviewPresentation.DistinctConfirmedPeople(batch.Observations));
    }

    [Fact]
    public void Human_presence_is_separately_opt_in_and_keeps_photo_identity_untouched()
    {
        string root = Root();
        string api = File.ReadAllText(Path.Combine(root, "src", "PhotoIdentity.Api",
            "VideoFaceReviewEndpoints.cs"));
        string repo = File.ReadAllText(Path.Combine(root,
            "src", "PhotoIdentity.Persistence.Postgres",
            "PostgresVideoFacePresenceRepository.cs"));
        string migration = File.ReadAllText(Path.Combine(root,
            "src", "PhotoIdentity.Persistence.Postgres",
            "PostgresCatalogueDatabase.VideoFacePresenceSchema.cs"));
        string reviewRepo = File.ReadAllText(Path.Combine(root,
            "src", "PhotoIdentity.Persistence.Postgres",
            "PostgresVideoFaceReviewRepository.cs"));
        string ui = File.ReadAllText(Path.Combine(root, "src",
            "PhotoIdentity.Web", "Pages", "VideoFacePersistedReview.razor"));

        Assert.Contains("PhotoIdentity:VideoFacePresencePromotionEnabled", api);
        Assert.Contains("PresenceEnabled(settings)", api);
        Assert.Contains("request.ExplicitlyApproved", api);
        Assert.Contains("NoStore(context)", api);
        Assert.Contains("MapPost", api);
        Assert.Contains("MapDelete", api);
        Assert.Contains("PromotePresenceAsync", api);
        Assert.Contains("UndoPresenceAsync", api);
        Assert.Contains("job.status='succeeded'", repo);
        Assert.Contains("job.derivative_kind=@kind", repo);
        Assert.Contains("source_copy_exclusions", repo);
        Assert.Contains("FOR UPDATE OF source", repo);
        Assert.Contains("FOR UPDATE OF asset", repo);
        Assert.Contains("FOR UPDATE OF f", repo);
        Assert.Contains("action_kind='add'", repo);
        Assert.Contains("already-present", repo);
        Assert.Contains("already-linked", repo);
        Assert.Contains("video_face_presence_actions", migration);
        Assert.Contains("ON DELETE CASCADE", migration);
        Assert.Contains("Undo the linked video-person presence", reviewRepo);
        Assert.Contains("I explicitly approve adding", ui);
        Assert.Contains("Undo this face-based presence", ui);
        Assert.Contains("Show already reviewed faces", ui);
        Assert.DoesNotContain("photo_person_actions", repo);
        Assert.DoesNotContain("FaceEmbedder", repo);
        Assert.DoesNotContain("YuNetFaceDetector", repo);
        Assert.DoesNotContain("OneDriveVideoLocalStager", repo);
    }

    private static string Root()
    {
        foreach (string start in new[] { Directory.GetCurrentDirectory(), AppContext.BaseDirectory })
        {
            DirectoryInfo? cursor = new(Path.GetFullPath(start));
            for (int depth = 0; cursor is not null && depth < 12; depth++, cursor = cursor.Parent)
                if (File.Exists(Path.Combine(cursor.FullName, "PhotoIdentity.slnx")))
                    return cursor.FullName;
        }
        throw new DirectoryNotFoundException("Repository root not found.");
    }
}
