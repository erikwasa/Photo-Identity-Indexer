using Xunit;

namespace PhotoIdentity_Integration_Tests;

public sealed class ManualCollectionChronologicalSortUiTests
{
    [Fact]
    public void Manual_collection_editor_exposes_compact_server_side_sort_actions_and_keeps_manual_moves()
    {
        string markup = ReadRepositoryFile(
            "src",
            "PhotoIdentity.Web",
            "Pages",
            "ManualCollection.razor");
        string styles = ReadRepositoryFile(
            "src",
            "PhotoIdentity.Web",
            "Pages",
            "ManualCollection.razor.css");

        Assert.Contains("Oldest first", markup, StringComparison.Ordinal);
        Assert.Contains("Newest first", markup, StringComparison.Ordinal);
        Assert.Contains("PhotoListCollectionSortRequest(direction)", markup, StringComparison.Ordinal);
        Assert.Contains("/sort", markup, StringComparison.Ordinal);
        Assert.Contains("MoveAsync(itemIndex, -1)", markup, StringComparison.Ordinal);
        Assert.Contains("MoveAsync(itemIndex, 1)", markup, StringComparison.Ordinal);
        Assert.Contains("manual-collection-sort-actions", styles, StringComparison.Ordinal);
        Assert.Contains("grid-template-columns: repeat(2, minmax(0, 1fr));", styles, StringComparison.Ordinal);
    }

    private static string ReadRepositoryFile(params string[] segments) =>
        File.ReadAllText(Path.Combine(ResolveRepositoryRoot(), Path.Combine(segments)));

    private static string ResolveRepositoryRoot()
    {
        foreach (string candidate in new[] { Directory.GetCurrentDirectory(), AppContext.BaseDirectory })
        {
            DirectoryInfo? directory = new(Path.GetFullPath(candidate));
            for (int depth = 0; directory is not null && depth < 12; depth++, directory = directory.Parent)
            {
                if (File.Exists(Path.Combine(directory.FullName, "PhotoIdentity.slnx")))
                {
                    return directory.FullName;
                }
            }
        }

        throw new DirectoryNotFoundException(
            "Could not resolve the Photo Identity repository root for manual collection UI tests.");
    }
}
