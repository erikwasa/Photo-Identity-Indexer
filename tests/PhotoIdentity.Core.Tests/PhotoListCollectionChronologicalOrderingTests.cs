using PhotoIdentity.Core.Collections;
using PhotoIdentity.Core.Identifiers;
using Xunit;

namespace PhotoIdentity.Core.Tests;

public sealed class PhotoListCollectionChronologicalOrderingTests
{
    [Fact]
    public void Oldest_first_keeps_equal_and_undated_items_stable()
    {
        AssetRevisionId firstUndated = AssetRevisionId.New();
        AssetRevisionId equalSecond = AssetRevisionId.New();
        AssetRevisionId newest = AssetRevisionId.New();
        AssetRevisionId equalFirst = AssetRevisionId.New();
        AssetRevisionId secondUndated = AssetRevisionId.New();

        AssetRevisionId[] result = PhotoListCollectionChronologicalOrdering.Apply(
            [firstUndated, equalSecond, newest, equalFirst, secondUndated],
            [
                new(equalSecond, new DateTime(2020, 1, 1, 12, 0, 0)),
                new(newest, new DateTime(2024, 6, 1, 8, 0, 0)),
                new(equalFirst, new DateTime(2020, 1, 1, 12, 0, 0)),
            ],
            PhotoListCollectionChronologicalOrder.OldestFirst);

        Assert.Equal(
            [equalSecond, equalFirst, newest, firstUndated, secondUndated],
            result);
    }

    [Fact]
    public void Newest_first_reverses_only_timestamp_order_and_leaves_undated_last()
    {
        AssetRevisionId oldest = AssetRevisionId.New();
        AssetRevisionId firstUndated = AssetRevisionId.New();
        AssetRevisionId newest = AssetRevisionId.New();
        AssetRevisionId middle = AssetRevisionId.New();
        AssetRevisionId secondUndated = AssetRevisionId.New();

        AssetRevisionId[] result = PhotoListCollectionChronologicalOrdering.Apply(
            [oldest, firstUndated, newest, middle, secondUndated],
            [
                new(oldest, new DateTime(2018, 1, 1)),
                new(newest, new DateTime(2024, 1, 1)),
                new(middle, new DateTime(2021, 1, 1)),
            ],
            PhotoListCollectionChronologicalOrder.NewestFirst);

        Assert.Equal(
            [newest, middle, oldest, firstUndated, secondUndated],
            result);
    }

    [Fact]
    public void Missing_evidence_is_treated_as_undated()
    {
        AssetRevisionId dated = AssetRevisionId.New();
        AssetRevisionId missing = AssetRevisionId.New();

        AssetRevisionId[] result = PhotoListCollectionChronologicalOrdering.Apply(
            [missing, dated],
            [new(dated, new DateTime(2022, 4, 3))],
            PhotoListCollectionChronologicalOrder.OldestFirst);

        Assert.Equal([dated, missing], result);
    }
}
