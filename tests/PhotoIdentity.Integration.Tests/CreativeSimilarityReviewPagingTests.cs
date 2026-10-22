using PhotoIdentity.Api;
using Xunit;

namespace PhotoIdentity.Integration.Tests;

public sealed class CreativeSimilarityReviewPagingTests
{
    [Fact]
    public void Can_access_every_group_beyond_initial_eighty_without_duplicates()
    {
        int[] groups = Enumerable.Range(1, 157).ToArray();
        List<int> visited = [];
        for (int offset = 0; offset < groups.Length; offset += CreativeSimilarityReviewPaging.DefaultPageSize)
        {
            int[] page = CreativeSimilarityReviewPaging.Page(
                groups, offset, CreativeSimilarityReviewPaging.DefaultPageSize);
            Assert.InRange(page.Length, 1, CreativeSimilarityReviewPaging.DefaultPageSize);
            visited.AddRange(page);
        }
        Assert.Equal(groups, visited);
        Assert.Equal(5, CreativeSimilarityReviewPaging.Page(groups, 152, 12).Length);
        Assert.Empty(CreativeSimilarityReviewPaging.Page(groups, 157, 12));
    }

    [Theory]
    [InlineData(-1, 12)]
    [InlineData(0, 0)]
    [InlineData(0, -1)]
    [InlineData(0, 81)]
    public void Invalid_paging_arguments_are_rejected(int offset, int pageSize)
    {
        Assert.False(CreativeSimilarityReviewPaging.IsValid(offset, pageSize));
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            CreativeSimilarityReviewPaging.Page(new[] { 1, 2 }, offset, pageSize));
    }

    [Fact]
    public void Eighty_is_maximum_page_size()
    {
        Assert.True(CreativeSimilarityReviewPaging.IsValid(0, 80));
        Assert.Equal(80, CreativeSimilarityReviewPaging.Page(
            Enumerable.Range(1, 157).ToArray(), 0, 80).Length);
    }
}
