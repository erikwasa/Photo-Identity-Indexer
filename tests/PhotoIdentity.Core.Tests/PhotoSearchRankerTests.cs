using PhotoIdentity.Core.Collections;
using PhotoIdentity.Core.Identifiers;

namespace PhotoIdentity.Core.Tests;

public sealed class PhotoSearchRankerTests
{
    [Fact]
    public void Combined_search_keeps_semantic_and_caption_provenance()
    {
        AssetRevisionId both = Revision("00000000-0000-0000-0000-000000000001");
        AssetRevisionId semanticOnly = Revision("00000000-0000-0000-0000-000000000002");
        AssetRevisionId captionOnly = Revision("00000000-0000-0000-0000-000000000003");

        IReadOnlyList<PhotoSearchRankedHit> result = PhotoSearchRanker.Fuse(
            [
                new PhotoSearchSemanticHit(both, 0.91),
                new PhotoSearchSemanticHit(semanticOnly, 0.84),
            ],
            [
                new PhotoSearchCaptionHit(both, "sv", "Barn leker utomhus.", 1.2),
                new PhotoSearchCaptionHit(captionOnly, "sv", "Barn springer på en gräsmatta.", 0.8),
            ],
            PhotoSearchModes.Combined,
            limit: 10);

        PhotoSearchRankedHit first = Assert.Single(result, item => item.RevisionId == both);
        Assert.Equal(new[] { "semantic", "caption" }, first.Sources);
        Assert.Equal(0.91, first.SemanticScore);
        Assert.Equal(1.2, first.CaptionScore);
        Assert.Equal("sv", first.CaptionLanguage);
        Assert.Equal("Barn leker utomhus.", first.Caption);
        Assert.Equal(both, result[0].RevisionId);
    }

    [Fact]
    public void Semantic_only_photo_remains_searchable_without_caption()
    {
        AssetRevisionId revision = Revision("00000000-0000-0000-0000-000000000010");

        PhotoSearchRankedHit hit = Assert.Single(PhotoSearchRanker.Fuse(
            [new PhotoSearchSemanticHit(revision, 0.77)],
            [],
            PhotoSearchModes.Combined,
            limit: 10));

        Assert.Equal(new[] { "semantic" }, hit.Sources);
        Assert.Equal(0.77, hit.SemanticScore);
        Assert.Null(hit.CaptionScore);
        Assert.Null(hit.Caption);
    }

    [Fact]
    public void Caption_mode_does_not_leak_semantic_only_matches()
    {
        AssetRevisionId semantic = Revision("00000000-0000-0000-0000-000000000020");
        AssetRevisionId caption = Revision("00000000-0000-0000-0000-000000000021");

        PhotoSearchRankedHit hit = Assert.Single(PhotoSearchRanker.Fuse(
            [new PhotoSearchSemanticHit(semantic, 0.99)],
            [new PhotoSearchCaptionHit(caption, "en", "A snowy landscape.", 0.7)],
            PhotoSearchModes.Caption,
            limit: 10));

        Assert.Equal(caption, hit.RevisionId);
        Assert.Equal(new[] { "caption" }, hit.Sources);
        Assert.Null(hit.SemanticScore);
    }

    [Fact]
    public void Reciprocal_rank_fusion_is_deterministic_for_equal_scores()
    {
        AssetRevisionId first = Revision("00000000-0000-0000-0000-000000000031");
        AssetRevisionId second = Revision("00000000-0000-0000-0000-000000000032");
        PhotoSearchSemanticHit[] semantic =
        [
            new(second, 0.5),
            new(first, 0.5),
        ];

        IReadOnlyList<PhotoSearchRankedHit> one = PhotoSearchRanker.Fuse(
            semantic,
            [],
            PhotoSearchModes.Semantic,
            limit: 10);
        IReadOnlyList<PhotoSearchRankedHit> two = PhotoSearchRanker.Fuse(
            semantic.Reverse().ToArray(),
            [],
            PhotoSearchModes.Semantic,
            limit: 10);

        Assert.Equal(one.Select(item => item.RevisionId), two.Select(item => item.RevisionId));
        Assert.Equal(first, one[0].RevisionId);
    }

    private static AssetRevisionId Revision(string value) =>
        AssetRevisionId.From(Guid.Parse(value));
}
