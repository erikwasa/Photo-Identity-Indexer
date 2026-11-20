using PhotoIdentity.Web.Contracts;
using Xunit;

namespace PhotoIdentity_Integration_Tests;

/// <summary>
/// Synthetic, no-PostgreSQL contract checks. Does not fetch any original or
/// invoke an inference model. The repository's existing disposable PG tests
/// separately exercise revision locks and human-review persistence.
/// </summary>
public sealed class VideoFacePersistedReviewWebContractTests
{
    private static PersistedVideoFaceObservation Face(string id, string? decision = null,
        string? person = null) =>
        new(id, 0, 1000, 0, 1920, 1080, .2, .2, .05, .1, [],
            decision, person, person is null ? null : "Current Person");

    [Fact]
    public void Presence_first_summary_deduplicates_people_and_hides_only_reviewed_faces()
    {
        PersistedVideoFaceObservation[] observations =
        [
            Face("a", "known", "PERSON-ONE"),
            Face("b", "known", "person-one"),
            Face("c", "unknown"),
            Face("d", "not-face"),
            Face("e", "cannot-tell"),
            Face("f", "clear"),
            Face("g")
        ];
        Assert.Equal(1, VideoFaceReviewPresentation.DistinctConfirmedPeople(observations));
        Assert.Equal(2, VideoFaceReviewPresentation.Visible(observations, false).Count);
        Assert.Equal(7, VideoFaceReviewPresentation.Visible(observations, true).Count);
        Assert.False(VideoFaceReviewPresentation.IsReviewed(Face("x", "clear")));
        Assert.True(VideoFaceReviewPresentation.IsReviewed(Face("x", "cannot-tell")));
        Assert.Empty(VideoFaceReviewPresentation.Visible([], false));
        Assert.Equal(0, VideoFaceReviewPresentation.DistinctConfirmedPeople([]));
        Assert.Equal("0:01.000", VideoFaceReviewPresentation.Moment(1000));
    }

    [Fact]
    public void App_review_is_revision_bound_and_never_automatically_assigns_people()
    {
        string root = Root();
        string page = File.ReadAllText(Path.Combine(root, "src", "PhotoIdentity.Web",
            "Pages", "VideoFacePersistedReview.razor"));
        string details = File.ReadAllText(Path.Combine(root, "src", "PhotoIdentity.Web",
            "Pages", "VideoDetails.razor"));
        string api = File.ReadAllText(Path.Combine(root, "src", "PhotoIdentity.Api",
            "VideoFaceReviewEndpoints.cs"));
        string seek = File.ReadAllText(Path.Combine(root, "src", "PhotoIdentity.Web",
            "wwwroot", "js", "video-face-persisted-review.js"));

        Assert.Contains("@page \"/videos/{RevisionId}/faces\"", page);
        Assert.Contains("/face-review", page);
        Assert.Contains("/decision", page);
        Assert.Contains("/api/review/people/maintenance", page);
        Assert.Contains("VisualConfirmed", page);
        Assert.Contains("FrameReady", page);
        Assert.Contains("ShowReviewed = false", page);
        Assert.Contains("DistinctConfirmedPeople", page);
        Assert.Contains("/play", page);
        Assert.Contains("preload=\"none\"", page);
        Assert.Contains("FaceReviewAvailable", details);
        Assert.Contains("PhotoIdentity:VideoFaceReviewEnabled", api);
        Assert.Contains("NoStore(context)", api);
        Assert.DoesNotContain("PublishAsync", page);
        Assert.DoesNotContain("video_person_actions", page);
        Assert.DoesNotContain("localStorage", seek);
        Assert.DoesNotContain("canvas", seek, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("fetch(", seek, StringComparison.OrdinalIgnoreCase);
    }

    private static string Root()
    {
        foreach (string start in new[] { Directory.GetCurrentDirectory(), AppContext.BaseDirectory })
        {
            DirectoryInfo? directory = new(Path.GetFullPath(start));
            for (int depth = 0; directory is not null && depth < 12;
                 depth++, directory = directory.Parent)
                if (File.Exists(Path.Combine(directory.FullName, "PhotoIdentity.slnx")))
                    return directory.FullName;
        }
        throw new DirectoryNotFoundException("Repository root not found.");
    }
}
