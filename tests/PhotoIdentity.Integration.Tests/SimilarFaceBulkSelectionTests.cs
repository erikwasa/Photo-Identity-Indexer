using PhotoIdentity.Web;
using PhotoIdentity.Web.Contracts;
using Xunit;

namespace PhotoIdentity_Integration_Tests;

public sealed class SimilarFaceBulkSelectionTests
{
    [Fact]
    public void Assign_includes_unreviewed_source_face()
    {
        ReviewFaceResponse source = CreateSource("unreviewed");

        string[] ids = SimilarFaceBulkSelection.BuildFaceIds(
            ["candidate-b", "candidate-a"],
            SimilarFaceBulkSelection.AssignAction,
            source);

        Assert.Equal(["candidate-a", "candidate-b", "source-face"], ids);
        Assert.True(SimilarFaceBulkSelection.IncludesSource(
            SimilarFaceBulkSelection.AssignAction,
            source));
    }

    [Theory]
    [InlineData("unknown")]
    [InlineData("reject")]
    public void Non_assignment_actions_never_include_source_face(string action)
    {
        ReviewFaceResponse source = CreateSource("unreviewed");

        string[] ids = SimilarFaceBulkSelection.BuildFaceIds(
            ["candidate-face"],
            action,
            source);

        Assert.Equal(["candidate-face"], ids);
        Assert.False(SimilarFaceBulkSelection.IncludesSource(action, source));
    }

    [Theory]
    [InlineData("assigned")]
    [InlineData("unknown")]
    [InlineData("rejected")]
    public void Assign_does_not_rewrite_already_reviewed_source_face(string state)
    {
        ReviewFaceResponse source = CreateSource(state);

        string[] ids = SimilarFaceBulkSelection.BuildFaceIds(
            ["candidate-face"],
            SimilarFaceBulkSelection.AssignAction,
            source);

        Assert.Equal(["candidate-face"], ids);
        Assert.False(SimilarFaceBulkSelection.IncludesSource(
            SimilarFaceBulkSelection.AssignAction,
            source));
    }

    private static ReviewFaceResponse CreateSource(string state) => new(
        Id: "source-face",
        ImageUrl: "/api/review/faces/source-face/image",
        PhotoName: "source.jpg",
        FaceOrdinal: 0,
        Confidence: 0.98,
        State: state,
        Person: null,
        CreatedAtUtc: new DateTimeOffset(2026, 9, 13, 13, 0, 0, TimeSpan.Zero));
}
