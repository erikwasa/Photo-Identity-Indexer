using PhotoIdentity.Web;
using PhotoIdentity.Web.Contracts;
using Xunit;

namespace PhotoIdentity_Integration_Tests;

public sealed class ReviewSelectionRangeTests
{
    [Fact]
    public void Resolve_selects_only_eligible_faces_in_inclusive_loaded_range()
    {
        DateTimeOffset now = new(2026, 8, 15, 8, 0, 0, TimeSpan.Zero);
        ReviewFaceResponse[] faces = Enumerable.Range(1, 30)
            .Select(index => new ReviewFaceResponse(
                $"face-{index}",
                $"/image/{index}",
                "photo.jpg",
                index,
                Confidence: null,
                State: index is 7 or 21 ? "assigned" : "unreviewed",
                Person: null,
                now.AddSeconds(index)))
            .ToArray();

        IReadOnlyList<string> selected = ReviewSelectionRange.Resolve(
            faces,
            "face-1",
            "face-30");

        Assert.Equal(28, selected.Count);
        Assert.Equal("face-1", selected[0]);
        Assert.Equal("face-30", selected[^1]);
        Assert.DoesNotContain("face-7", selected);
        Assert.DoesNotContain("face-21", selected);
    }

    [Fact]
    public void Resolve_supports_reverse_ranges_and_never_reaches_unloaded_faces()
    {
        DateTimeOffset now = new(2026, 8, 15, 8, 0, 0, TimeSpan.Zero);
        ReviewFaceResponse[] loadedFaces = Enumerable.Range(1, 40)
            .Select(index => new ReviewFaceResponse(
                $"face-{index}",
                $"/image/{index}",
                "photo.jpg",
                index,
                Confidence: null,
                State: "unreviewed",
                Person: null,
                now.AddSeconds(index)))
            .ToArray();

        IReadOnlyList<string> selected = ReviewSelectionRange.Resolve(
            loadedFaces,
            "face-30",
            "face-6");

        Assert.Equal(25, selected.Count);
        Assert.Equal("face-6", selected[0]);
        Assert.Equal("face-30", selected[^1]);
        Assert.DoesNotContain("face-41", selected);
        Assert.Empty(ReviewSelectionRange.Resolve(loadedFaces, "face-30", "face-41"));
    }
}
