using PhotoIdentity.Web;
using PhotoIdentity.Web.Contracts;
using Xunit;

namespace PhotoIdentity_Integration_Tests;

public sealed class SlideshowLibraryCatalogueTests
{
    [Fact]
    public void Combine_places_smart_manual_and_creative_items_in_one_deterministic_name_order()
    {
        SlideshowLibraryCollectionResponse[] smart =
        [
            new("smart-b", "Bravo"),
            new("smart-a", "Alpha"),
        ];
        PhotoListCollectionResponse[] manual =
        [
            new(
                "manual-c",
                "Charlie",
                ["revision-1", "revision-2"],
                DateTimeOffset.UtcNow,
                DateTimeOffset.UtcNow),
        ];
        CreativeCollectionRecipeResponse[] creative =
        [
            Creative("creative-d", "Delta", "smart-a", "Alpha", 50),
        ];

        IReadOnlyList<SlideshowLibraryItem> items =
            SlideshowLibraryCatalogue.Combine(smart, manual, creative);

        Assert.Equal(["Alpha", "Bravo", "Charlie", "Delta"], items.Select(item => item.Name));
        Assert.Equal(
            [
                SlideshowLibraryItemKind.Smart,
                SlideshowLibraryItemKind.Smart,
                SlideshowLibraryItemKind.Manual,
                SlideshowLibraryItemKind.Creative,
            ],
            items.Select(item => item.Kind));
    }

    [Fact]
    public void Creative_item_keeps_internal_launch_cover_and_truthful_quantity_metadata()
    {
        CreativeCollectionRecipeResponse creative =
            Creative("creative-1", "Summer sampler", "anchor-1", "Summer", 40);

        SlideshowLibraryItem item = Assert.Single(
            SlideshowLibraryCatalogue.Combine([], [], [creative]));

        Assert.True(item.Creative);
        Assert.False(item.Manual);
        Assert.False(item.SupportsPreparation);
        Assert.Equal("creative-1", item.Id);
        Assert.Equal("anchor-1", item.CoverCollectionId);
        Assert.Equal("Summer", item.SourceCollectionName);
        Assert.Equal(40, item.CreativeTargetCount);
        Assert.Equal("Up to 40 photos", SlideshowLibraryPresentation.CreativeTargetLabel(item.CreativeTargetCount!.Value));
    }

    [Fact]
    public void Manual_item_keeps_exact_membership_and_preparation_routing_metadata()
    {
        PhotoListCollectionResponse manual = new(
            "manual-1",
            "Hand picked",
            ["revision-a", "revision-b", "revision-c"],
            DateTimeOffset.UtcNow,
            DateTimeOffset.UtcNow);

        SlideshowLibraryItem item = Assert.Single(
            SlideshowLibraryCatalogue.Combine([], [manual], []));

        Assert.True(item.Manual);
        Assert.False(item.Creative);
        Assert.True(item.SupportsPreparation);
        Assert.Equal("revision-a", item.CoverRevisionId);
        Assert.Equal(3, item.PhotoCount);
        Assert.Equal(manual.RevisionIds, item.RevisionIds);
    }

    [Fact]
    public void Equal_names_are_ordered_by_id_independent_of_creation_kind()
    {
        IReadOnlyList<SlideshowLibraryItem> items = SlideshowLibraryCatalogue.Combine(
            [new("b", "Same")],
            [new("a", "Same", [], DateTimeOffset.UtcNow, DateTimeOffset.UtcNow)],
            []);

        Assert.Equal(["a", "b"], items.Select(item => item.Id));
    }

    private static CreativeCollectionRecipeResponse Creative(
        string id,
        string name,
        string anchorId,
        string anchorName,
        int targetCount) =>
        new(
            id,
            name,
            anchorId,
            anchorName,
            targetCount,
            MomentGapMinutes: 30,
            MomentPolicyVersion: "moment-v1",
            ContextStrength: "balanced",
            ContextPolicyVersion: "context-v1",
            SelectionPolicyVersion: "selection-v1",
            OrderingPolicyVersion: "ordering-v1",
            NoveltyEnabled: false,
            NoveltyPolicyVersion: "novelty-v1",
            CreatedAtUtc: DateTimeOffset.UtcNow,
            UpdatedAtUtc: DateTimeOffset.UtcNow);
}
