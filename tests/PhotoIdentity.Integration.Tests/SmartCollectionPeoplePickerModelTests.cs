using PhotoIdentity.Web.Components;
using PhotoIdentity.Web.Contracts;
using Xunit;

namespace PhotoIdentity_Integration_Tests;

public sealed class SmartCollectionPeoplePickerModelTests
{
    [Fact]
    public void Search_is_case_insensitive_and_does_not_change_existing_selection()
    {
        ReviewPersonResponse[] people =
        [
            new("ada", "Ada Lovelace"),
            new("grace", "Grace Hopper", IsFavorite: true),
            new("greta", "Greta Example"),
            new("hidden", "Grace Hidden", HiddenFromSmartCollections: true),
        ];
        HashSet<string> selected = new(StringComparer.Ordinal) { "ada" };

        IReadOnlyList<ReviewPersonResponse> candidates =
            SmartCollectionPeoplePickerModel.SearchCandidates(people, selected, "GR");

        Assert.Equal(["Grace Hopper", "Greta Example"], candidates.Select(person => person.DisplayName));
        Assert.Equal(["ada"], selected);
    }

    [Fact]
    public void Selected_hidden_people_are_retained_but_never_return_to_discovery()
    {
        ReviewPersonResponse[] people =
        [
            new("visible", "Visible Person"),
            new("hidden", "Hidden Person", HiddenFromSmartCollections: true),
        ];
        HashSet<string> selected = new(StringComparer.Ordinal) { "hidden" };

        ReviewPersonResponse retained = Assert.Single(
            SmartCollectionPeoplePickerModel.SelectedPeople(people, selected));
        Assert.Equal("hidden", retained.Id);
        Assert.True(retained.HiddenFromSmartCollections);
        Assert.Single(SmartCollectionPeoplePickerModel.SearchCandidates(people, selected, ""));

        selected.Remove("hidden");

        IReadOnlyList<ReviewPersonResponse> afterRemoval =
            SmartCollectionPeoplePickerModel.SearchCandidates(people, selected, "hidden");
        Assert.Empty(afterRemoval);
    }

    [Fact]
    public void Candidate_order_is_deterministic_and_portrait_fallback_is_stable()
    {
        ReviewPersonResponse[] people =
        [
            new("z", "Zoe", RepresentativeFaceId: "face-z", RepresentativeImageUrl: "/api/review/faces/face-z/image?size=360"),
            new("a2", "alice"),
            new("a1", "Alice", IsFavorite: true),
        ];
        HashSet<string> selected = new(StringComparer.Ordinal);

        IReadOnlyList<ReviewPersonResponse> candidates =
            SmartCollectionPeoplePickerModel.SearchCandidates(people, selected, "");

        Assert.Equal(["a1", "a2", "z"], candidates.Select(person => person.Id));
        Assert.Equal("A", SmartCollectionPeoplePickerModel.FallbackInitial(candidates[1]));
        Assert.Equal("/api/review/faces/face-z/image?size=360", candidates[2].RepresentativeImageUrl);
    }
}
