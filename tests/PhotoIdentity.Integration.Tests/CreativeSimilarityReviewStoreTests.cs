using PhotoIdentity.Api;
using Xunit;

namespace PhotoIdentity.Integration.Tests;

public sealed class CreativeSimilarityReviewStoreTests
{
    private const string RecipeA = "00000000-0000-0000-0000-000000000091";
    private const string RecipeB = "00000000-0000-0000-0000-000000000092";
    private const string PhotoA = "00000000-0000-0000-0000-000000000001";
    private const string PhotoB = "00000000-0000-0000-0000-000000000002";
    private const string PhotoC = "00000000-0000-0000-0000-000000000003";

    [Fact]
    public async Task Manual_choices_persist_across_restarts_and_cannot_affect_other_recipes()
    {
        string path = TempDirectory();
        try
        {
            string group = CreativeSimilarityReviewStore.GroupKey([PhotoA, PhotoB]);
            CreativeSimilarityReviewStore store = new(path);
            Assert.Null(await store.GetAsync(RecipeA, group));
            await store.SaveAsync(RecipeA, group, CreativeSimilarityReviewStore.PreferOne, PhotoB);
            CreativeSimilarityReviewStore reopened = new(path);
            CreativeSimilarityHumanChoice? saved = await reopened.GetAsync(RecipeA, group);
            Assert.NotNull(saved);
            Assert.Equal(CreativeSimilarityReviewStore.PreferOne, saved.Decision);
            Assert.Equal(PhotoB, saved.PreferredRevisionId);
            Assert.Null(await reopened.GetAsync(RecipeB, group));

            await reopened.SaveAsync(RecipeA, group, CreativeSimilarityReviewStore.KeepAll, null);
            saved = await store.GetAsync(RecipeA, group);
            Assert.NotNull(saved);
            Assert.Equal(CreativeSimilarityReviewStore.KeepAll, saved.Decision);
            Assert.Null(saved.PreferredRevisionId);
        }
        finally { Directory.Delete(path, recursive: true); }
    }

    [Fact]
    public void Group_key_is_order_independent_and_bound_to_exact_revisions()
    {
        string ab = CreativeSimilarityReviewStore.GroupKey([PhotoA, PhotoB]);
        Assert.Equal(ab, CreativeSimilarityReviewStore.GroupKey([PhotoB, PhotoA]));
        Assert.NotEqual(ab, CreativeSimilarityReviewStore.GroupKey([PhotoA, PhotoC]));
        Assert.Equal(64, ab.Length);
        Assert.Throws<ArgumentException>(
            () => CreativeSimilarityReviewStore.GroupKey([PhotoA, PhotoA]));
        Assert.Throws<ArgumentException>(
            () => CreativeSimilarityReviewStore.GroupKey([PhotoA]));
        Assert.Throws<ArgumentException>(
            () => CreativeSimilarityReviewStore.GroupKey([PhotoA, "not-a-guid"]));
    }

    [Fact]
    public async Task Rejects_destructive_or_unrecognized_decisions()
    {
        string path = TempDirectory();
        try
        {
            CreativeSimilarityReviewStore store = new(path);
            string group = CreativeSimilarityReviewStore.GroupKey([PhotoA, PhotoB]);
            await Assert.ThrowsAsync<ArgumentException>(
                () => store.SaveAsync(RecipeA, group, "exclude-photo-identity", PhotoA));
            await Assert.ThrowsAsync<ArgumentException>(
                () => store.SaveAsync(RecipeA, group, "auto-hide", PhotoA));
            await Assert.ThrowsAsync<ArgumentException>(
                () => store.SaveAsync(RecipeA, group, CreativeSimilarityReviewStore.PreferOne, null));
            await Assert.ThrowsAsync<ArgumentException>(
                () => store.SaveAsync(RecipeA, group, CreativeSimilarityReviewStore.KeepAll, PhotoA));
            await Assert.ThrowsAsync<ArgumentException>(
                () => store.SaveAsync("not-a-recipe", group, CreativeSimilarityReviewStore.KeepAll, null));
            Assert.Null(await store.GetAsync(RecipeA, group));
        }
        finally { Directory.Delete(path, recursive: true); }
    }

    private static string TempDirectory()
    {
        string path = Path.Combine(Path.GetTempPath(), "photoidentity-similarity-review-" +
            Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(path);
        return path;
    }
}
