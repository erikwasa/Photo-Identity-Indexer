using PhotoIdentity.Core.Identifiers;
using PhotoIdentity.Core.People;
using Xunit;

namespace PhotoIdentity.Core.Tests;

public sealed class PersonFamilyMetadataTests
{
    [Fact]
    public void Partial_birth_dates_preserve_precision_without_inventing_components()
    {
        PersonBirthDate year = new(1984, null, null, "year");
        Assert.Equal(new DateOnly(1984, 1, 1), year.Earliest);
        Assert.Equal(new DateOnly(1984, 12, 31), year.Latest);

        PersonBirthDate month = new(1984, 7, null, "month");
        Assert.Equal(new DateOnly(1984, 7, 1), month.Earliest);
        Assert.Equal(new DateOnly(1984, 7, 31), month.Latest);

        PersonBirthDate day = new(1984, 7, 12, "day");
        Assert.Equal(day.Earliest, day.Latest);
        Assert.Throws<ArgumentOutOfRangeException>(() => new PersonBirthDate(1984, null, 12, "day"));
    }

    [Fact]
    public void Age_range_is_deterministic_for_uncertain_birth_and_photo_dates()
    {
        PersonBirthDate birth = new(2010, null, null, "year");

        PersonAgeRange? during2015 = PersonAgeCalculator.Calculate(
            birth,
            new DateOnly(2015, 1, 1),
            new DateOnly(2015, 12, 31));

        Assert.NotNull(during2015);
        Assert.Equal(4, during2015!.MinimumYears);
        Assert.Equal(5, during2015.MaximumYears);
        Assert.True(during2015.Overlaps(5, 5));
        Assert.False(during2015.Overlaps(6, 8));
    }

    [Fact]
    public void Relationship_kinds_have_deterministic_inverse_semantics()
    {
        Assert.Equal("child", PersonRelationshipKinds.Inverse("parent"));
        Assert.Equal("parent", PersonRelationshipKinds.Inverse("child"));
        Assert.Equal("grandchild", PersonRelationshipKinds.Inverse("grandparent"));
        Assert.Equal("grandparent", PersonRelationshipKinds.Inverse("grandchild"));
        Assert.Equal("spouse", PersonRelationshipKinds.Inverse("spouse"));
        Assert.Equal("sibling", PersonRelationshipKinds.Inverse("sibling"));
    }

    [Fact]
    public void Smart_collection_family_criteria_validate_requested_ranges_and_kinds()
    {
        PersonId person = PersonId.New();
        _ = new PhotoIdentity.Core.Collections.SmartCollectionAgeCriterion(person, 2, 6);
        _ = new PhotoIdentity.Core.Collections.SmartCollectionRelationshipCriterion(
            person,
            ["child", "spouse"]);

        Assert.Throws<ArgumentException>(() =>
            new PhotoIdentity.Core.Collections.SmartCollectionAgeCriterion(person, 7, 6));
        Assert.Throws<ArgumentException>(() =>
            new PhotoIdentity.Core.Collections.SmartCollectionRelationshipCriterion(person, []));
    }
}
