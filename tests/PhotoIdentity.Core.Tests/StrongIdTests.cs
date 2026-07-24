using PhotoIdentity.Core.Identifiers;

namespace PhotoIdentity.Core.Tests;

public sealed class StrongIdTests
{
    [Fact]
    public void DifferentIdentifierTypesRemainDistinct()
    {
        Guid value = Guid.NewGuid();
        AssetId assetId = AssetId.From(value);
        PersonId personId = PersonId.From(value);

        Assert.Equal(value, assetId.Value);
        Assert.Equal(value, personId.Value);
        Assert.NotEqual((object)assetId, (object)personId);
    }

    [Fact]
    public void EmptyIdentifiersAreRejected()
    {
        Assert.Throws<ArgumentException>(() => AssetId.From(Guid.Empty));
        Assert.True(default(AssetId).IsEmpty);
    }

    [Fact]
    public void StringIdentifiersAreTrimmedAndValidated()
    {
        ModelId modelId = new("  sface-v1  ");

        Assert.Equal("sface-v1", modelId.Value);
        Assert.Throws<ArgumentException>(() => new ModelId(" "));
    }
}
