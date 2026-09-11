namespace PhotoIdentity.Docs;

public sealed record RepositoryPaths(
    string Root,
    string WorkItemsRegistry,
    string MilestonesRegistry,
    string Roadmap)
{
    public string StatusDirectory => Path.GetDirectoryName(WorkItemsRegistry)!;
    public string WorkItemsArchiveDirectory => Path.Combine(StatusDirectory, "archive");
    public string WorkItemShardDirectory => Path.Combine(StatusDirectory, "work-items");
    public string WorkItemShardRegistry => Path.Combine(WorkItemShardDirectory, "registry.yaml");
    public string ActiveWorkItemShardDirectory => Path.Combine(WorkItemShardDirectory, "active");
    public string ArchivedWorkItemShardDirectory => Path.Combine(WorkItemShardDirectory, "archive");
    public string WorkItemsIndex => Path.Combine(StatusDirectory, "work-items-index.md");
    public string WorkItemsDirectory => Path.GetFullPath(Path.Combine(StatusDirectory, "../work-items"));
    public string MilestonesDirectory => Path.GetFullPath(Path.Combine(StatusDirectory, "../milestones"));

    public bool HasShardedWorkItemStore => File.Exists(WorkItemShardRegistry);

    public IReadOnlyList<string> ActiveWorkItemShards =>
        EnumerateWorkItemShards(ActiveWorkItemShardDirectory);

    public IReadOnlyList<string> ArchivedWorkItemShards =>
        EnumerateWorkItemShards(ArchivedWorkItemShardDirectory);

    public IReadOnlyList<string> ArchivedWorkItemRegistries =>
        Directory.Exists(WorkItemsArchiveDirectory)
            ? Directory.EnumerateFiles(
                    WorkItemsArchiveDirectory,
                    "work-items-*.yaml",
                    SearchOption.TopDirectoryOnly)
                .OrderBy(path => path, StringComparer.Ordinal)
                .ToList()
            : [];

    public string ActiveWorkItemShard(string id) =>
        Path.Combine(ActiveWorkItemShardDirectory, $"{id}.yaml");

    public string ArchivedWorkItemShard(string id) =>
        Path.Combine(ArchivedWorkItemShardDirectory, $"{id}.yaml");

    public static RepositoryPaths Discover(string? startPath = null)
    {
        string current = Path.GetFullPath(startPath ?? Directory.GetCurrentDirectory());
        if (File.Exists(current))
        {
            current = Path.GetDirectoryName(current)!;
        }

        while (true)
        {
            string status = Path.Combine(current, "docs", "delivery", "status");
            string workItems = Path.Combine(status, "work-items.yaml");
            string shardRegistry = Path.Combine(status, "work-items", "registry.yaml");
            string milestones = Path.Combine(status, "milestones.yaml");
            if ((File.Exists(workItems) || File.Exists(shardRegistry)) && File.Exists(milestones))
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
                    "Could not find work-item status storage in this directory or any parent.");
            }

            current = parent.FullName;
        }
    }

    private static IReadOnlyList<string> EnumerateWorkItemShards(string directory) =>
        Directory.Exists(directory)
            ? Directory.EnumerateFiles(directory, "WI-*.yaml", SearchOption.TopDirectoryOnly)
                .OrderBy(path => path, StringComparer.Ordinal)
                .ToList()
            : [];
}
