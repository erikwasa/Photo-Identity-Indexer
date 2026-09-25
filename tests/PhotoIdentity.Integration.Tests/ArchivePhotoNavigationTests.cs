using PhotoIdentity.Web;
using Xunit;

namespace PhotoIdentity_Integration_Tests;

public sealed class ArchivePhotoNavigationTests
{
    [Fact]
    public void Workspace_context_round_trip_preserves_archive_filters_and_offset()
    {
        string returnUrl = ArchiveNavigation.BuildWorkspaceUrl(
            "1970/01",
            "online-only",
            "needs-source-verification",
            "pending",
            100);

        Assert.True(ArchivePhotoNavigation.TryParseWorkspaceContext(
            returnUrl,
            out ArchivePhotoWorkspaceContext? context));
        Assert.NotNull(context);
        Assert.Equal("1970/01", context.Folder);
        Assert.Equal("online-only", context.Availability);
        Assert.Equal("needs-source-verification", context.Verification);
        Assert.Equal("pending", context.Analysis);
        Assert.Equal(100, context.Offset);
    }

    [Fact]
    public void Items_query_preserves_the_active_archive_scope()
    {
        ArchivePhotoWorkspaceContext context = new(
            "1970/01",
            "local",
            "verified",
            "analysed",
            50);

        string url = ArchivePhotoNavigation.BuildItemsUrl(context, 49, 3);

        Assert.Equal(
            "api/archive/items/filter?availability=local&verification=verified&analysis=analysed&folder=1970%2F01&offset=49&limit=3",
            url);
    }

    [Fact]
    public void Navigation_finds_current_photo_and_skips_non_viewable_rows()
    {
        ArchiveItemPageResponse page = new(
            Offset: 50,
            Limit: 5,
            Total: 80,
            Items:
            [
                Item("before"),
                Item(null),
                Item("current"),
                Item(null),
                Item("after"),
            ]);

        Assert.True(ArchivePhotoNavigation.TryFindCurrent(
            page,
            "current",
            out int localIndex,
            out int globalIndex));
        Assert.Equal(2, localIndex);
        Assert.Equal(52, globalIndex);

        ArchivePhotoNeighbor? previous = ArchivePhotoNavigation.FindPreviousNeighbor(page, localIndex);
        ArchivePhotoNeighbor? next = ArchivePhotoNavigation.FindNextNeighbor(page, localIndex);

        Assert.NotNull(previous);
        Assert.Equal(50, previous.Index);
        Assert.Equal("before", previous.RevisionId);
        Assert.NotNull(next);
        Assert.Equal(54, next.Index);
        Assert.Equal("after", next.RevisionId);
    }

    [Theory]
    [InlineData(0, 0)]
    [InlineData(49, 0)]
    [InlineData(50, 50)]
    [InlineData(99, 50)]
    [InlineData(100, 100)]
    public void Result_index_maps_to_archive_page(int index, int expectedOffset)
    {
        Assert.Equal(expectedOffset, ArchivePhotoNavigation.PageOffsetForIndex(index));
    }

    [Theory]
    [InlineData("/archive")]
    [InlineData("/archive?folder=1970&offset=0")]
    [InlineData("/collections?offset=0")]
    public void Incomplete_or_non_archive_context_is_rejected(string returnUrl)
    {
        Assert.False(ArchivePhotoNavigation.TryParseWorkspaceContext(returnUrl, out _));
    }

    private static ArchiveItemStatusResponse Item(string? revisionId) => new(
        RelativePath: revisionId is null ? "placeholder.jpg" : $"{revisionId}.jpg",
        RevisionId: revisionId,
        Availability: "local",
        SourceVerificationState: "verified",
        AnalysisState: "analysed",
        LastError: null);
}
