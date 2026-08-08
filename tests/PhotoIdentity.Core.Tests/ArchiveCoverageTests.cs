using PhotoIdentity.Core.Sources;

namespace PhotoIdentity.Core.Tests;

public sealed class ArchiveCoverageTests
{
    [Fact]
    public void Parent_folder_subsumes_existing_children()
    {
        IReadOnlyList<string> folders = ArchiveCoverage.NormalizeIncludedFolders(
            ["1970/01", "1970\\02", "1970", "em-wedding"]);

        Assert.Equal(["1970", "em-wedding"], folders);
    }

    [Fact]
    public void Source_root_subsumes_every_relative_folder()
    {
        IReadOnlyList<string> folders = ArchiveCoverage.NormalizeIncludedFolders(
            ["2026/07", ".", "em-wedding"]);

        Assert.Equal([string.Empty], folders);
    }

    [Fact]
    public void Coverage_matches_folder_boundaries_only()
    {
        Assert.True(ArchiveCoverage.Covers("1970", "1970/01"));
        Assert.True(ArchiveCoverage.Covers("1970/01", "1970/01"));
        Assert.False(ArchiveCoverage.Covers("1970/01", "1970/010"));
    }

    [Theory]
    [InlineData("../outside")]
    [InlineData("1970/../2026")]
    [InlineData("C:\\Photos")]
    public void Escaping_or_absolute_paths_are_rejected(string folder)
    {
        Assert.Throws<ArgumentException>(() => ArchiveCoverage.NormalizeRelativeFolder(folder));
    }
}
