using PhotoIdentity.Docs;

namespace PhotoIdentity.Docs.Tests;

public sealed class RegistryStoreTests
{
    [Fact]
    public void LoadWorkItemsCombinesActiveItemsWithTerminalArchiveHistory()
    {
        RepositoryPaths paths = CreatePaths();
        WriteActiveAndArchive(paths);

        WorkItemRegistry combined = new RegistryStore().LoadWorkItems(paths);
        Dictionary<string, WorkItem> map = combined.WorkItems
            .ToDictionary(item => item.Id, StringComparer.Ordinal);

        Assert.Equal(2, map.Count);
        Assert.Equal("completed", map["WI-0001"].Status);
        Assert.Equal("proposed", map["WI-0002"].Status);
        Assert.True(WorkItemRules.IsReady(map["WI-0002"], map));
    }

    [Fact]
    public void SaveWorkItemsWritesOnlyTheActiveRegistryEntries()
    {
        RepositoryPaths paths = CreatePaths();
        WriteActiveAndArchive(paths);
        RegistryStore store = new();
        WorkItemRegistry combined = store.LoadWorkItems(paths);

        combined.WorkItems.Single(item => item.Id == "WI-0002").Status = "ready";
        store.SaveWorkItems(paths, combined);

        WorkItemRegistry active = store.LoadActiveWorkItems(paths);
        Assert.Single(active.WorkItems);
        Assert.Equal("WI-0002", active.WorkItems[0].Id);
        Assert.Equal("ready", active.WorkItems[0].Status);

        WorkItemRegistry reloadedCombined = store.LoadWorkItems(paths);
        Assert.Equal(
            "completed",
            reloadedCombined.WorkItems.Single(item => item.Id == "WI-0001").Status);
    }

    [Fact]
    public void SaveWorkItemsRejectsChangesToArchivedEntries()
    {
        RepositoryPaths paths = CreatePaths();
        WriteActiveAndArchive(paths);
        RegistryStore store = new();
        WorkItemRegistry combined = store.LoadWorkItems(paths);

        combined.WorkItems.Single(item => item.Id == "WI-0001").Owner = "different-owner";

        InvalidOperationException exception = Assert.Throws<InvalidOperationException>(
            () => store.SaveWorkItems(paths, combined));
        Assert.Contains("read-only", exception.Message, StringComparison.Ordinal);
    }

    private static RepositoryPaths CreatePaths()
    {
        string root = Path.Combine(Path.GetTempPath(), $"PhotoIdentityDocsRegistryTests-{Guid.NewGuid():N}");
        string status = Path.Combine(root, "docs", "delivery", "status");
        Directory.CreateDirectory(Path.Combine(status, "archive"));
        return new RepositoryPaths(
            root,
            Path.Combine(status, "work-items.yaml"),
            Path.Combine(status, "milestones.yaml"),
            Path.Combine(root, "docs", "delivery", "roadmap.md"));
    }

    private static void WriteActiveAndArchive(RepositoryPaths paths)
    {
        RegistryStore.WriteAtomically(
            paths.WorkItemsRegistry,
            Registry(
                """
                - id: WI-0002
                  title: Active
                  milestone: M00
                  status: proposed
                  owner: unassigned
                  document: ../work-items/WI-0002.md
                  blockers:
                  - WI-0001
                  blocker_notes: []
                  evidence: []
                """));

        RegistryStore.WriteAtomically(
            Path.Combine(paths.WorkItemsArchiveDirectory, "work-items-legacy.yaml"),
            Registry(
                """
                - id: WI-0001
                  title: Archived
                  milestone: M00
                  status: completed
                  owner: ai-agent
                  document: ../work-items/WI-0001.md
                  blockers: []
                  blocker_notes: []
                  evidence:
                  - type: verification
                    value: passed
                - id: WI-0002
                  title: Stale active snapshot
                  milestone: M00
                  status: proposed
                  owner: unassigned
                  document: ../work-items/WI-0002.md
                  blockers:
                  - WI-0001
                  blocker_notes: []
                  evidence: []
                """));
    }

    private static string Registry(string workItems) =>
        """
        schema_version: 1
        allowed_statuses:
        - proposed
        - ready
        - in_progress
        - blocked
        - in_review
        - completed
        - cancelled
        work_items:
        """ + "\n" + workItems + "\n";
}
