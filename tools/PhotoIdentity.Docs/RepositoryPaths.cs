namespace PhotoIdentity.Docs;

public sealed record RepositoryPaths(
    string Root,
    string WorkItemsRegistry,
    string MilestonesRegistry,
    string Roadmap)
{
    public string StatusDirectory => Path.GetDirectoryName(WorkItemsRegistry)!;
    public string WorkItemsDirectory => Path.GetFullPath(Path.Combine(StatusDirectory, "../work-items"));
    public string MilestonesDirectory => Path.GetFullPath(Path.Combine(StatusDirectory, "../milestones"));

    public static RepositoryPaths Discover(string? startPath = null)
    {
        string current = Path.GetFullPath(startPath ?? Directory.GetCurrentDirectory());
        if (File.Exists(current))
        {
            current = Path.GetDirectoryName(current)!;
        }

        while (true)
        {
            string workItems = Path.Combine(current, "docs", "delivery", "status", "work-items.yaml");
            string milestones = Path.Combine(current, "docs", "delivery", "status", "milestones.yaml");
            if (File.Exists(workItems) && File.Exists(milestones))
            {
                return new RepositoryPaths(
                    current,
                    workItems,
                    milestones,
                    Path.Combine(current, "docs", "delivery", "roadmap.md"));
            }

            DirectoryInfo? parent = Directory.GetParent(current);
            if (parent is null)
            {
                throw new DirectoryNotFoundException(
                    "Could not find docs/delivery/status/work-items.yaml in this directory or any parent.");
            }

            current = parent.FullName;
        }
    }
}
