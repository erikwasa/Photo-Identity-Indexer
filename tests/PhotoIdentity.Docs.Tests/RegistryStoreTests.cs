using PhotoIdentity.Docs;

namespace PhotoIdentity.Docs.Tests;

public sealed class RegistryStoreTests
{
    [Fact]
    public void LoadWorkItemsCombinesLegacyActiveItemsWithTerminalArchiveHistory()
    {
        RepositoryPaths paths = CreatePaths();
        WriteLegacyActiveAndArchive(paths);

        WorkItemRegistry combined = new RegistryStore().LoadWorkItems(paths);
        Dictionary<string, WorkItem> map = combined.WorkItems
            .ToDictionary(item => item.Id, StringComparer.Ordinal);

        Assert.Equal(2, map.Count);
        Assert.Equal("completed", map["WI-0001"].Status);
        Assert.Equal("proposed", map["WI-0002"].Status);
        Assert.True(WorkItemRules.IsReady(map["WI-0002"], map));
    }

    [Fact]
    public void LoadWorkItemsCombinesActiveAndArchivedShards()
    {
        RepositoryPaths paths = CreatePaths();
        WriteShardedStore(paths);

        WorkItemRegistry combined = new RegistryStore().LoadWorkItems(paths);
        Dictionary<string, WorkItem> map = combined.WorkItems
            .ToDictionary(item => item.Id, StringComparer.Ordinal);

        Assert.Equal(3, map.Count);
        Assert.Equal("completed", map["WI-0001"].Status);
        Assert.Equal("proposed", map["WI-0002"].Status);
        Assert.Equal("ready", map["WI-0003"].Status);
        Assert.True(WorkItemRules.IsReady(map["WI-0002"], map));
    }

    [Fact]
    public void LoadWorkItemsRejectsDuplicateIdsAcrossShardAreas()
    {
        RepositoryPaths paths = CreatePaths();
        WriteShardRegistry(paths);
        WriteShard(paths.ActiveWorkItemShard("WI-0001"), ActiveItem("WI-0001"));
        WriteShard(paths.ArchivedWorkItemShard("WI-0001"), ArchivedItem("WI-0001"));

        InvalidDataException exception = Assert.Throws<InvalidDataException>(
            () => new RegistryStore().LoadWorkItems(paths));

        Assert.Contains("Duplicate work-item ID 'WI-0001'", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void LoadWorkItemsRejectsTerminalItemInActiveShardArea()
    {
        RepositoryPaths paths = CreatePaths();
        WriteShardRegistry(paths);
        WriteShard(paths.ActiveWorkItemShard("WI-0001"), ArchivedItem("WI-0001"));

        InvalidDataException exception = Assert.Throws<InvalidDataException>(
            () => new RegistryStore().LoadWorkItems(paths));

        Assert.Contains("active shard area", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void SaveWorkItemsUpdatesOnlyTheChangedActiveShard()
    {
        RepositoryPaths paths = CreatePaths();
        WriteShardedStore(paths);
        RegistryStore store = new();
        WorkItemRegistry combined = store.LoadWorkItems(paths);
        string untouchedPath = paths.ActiveWorkItemShard("WI-0003");
        string untouchedBefore = File.ReadAllText(untouchedPath);
        string archivePath = paths.ArchivedWorkItemShard("WI-0001");
        string archiveBefore = File.ReadAllText(archivePath);

        combined.WorkItems.Single(item => item.Id == "WI-0002").Status = "ready";
        store.SaveWorkItems(paths, combined);

        WorkItemRegistry active = store.LoadActiveWorkItems(paths);
        Assert.Equal("ready", active.WorkItems.Single(item => item.Id == "WI-0002").Status);
        Assert.Equal(untouchedBefore, File.ReadAllText(untouchedPath));
        Assert.Equal(archiveBefore, File.ReadAllText(archivePath));
    }

    [Fact]
    public void SaveWorkItemsMovesTerminalItemToArchive()
    {
        RepositoryPaths paths = CreatePaths();
        WriteShardedStore(paths);
        RegistryStore store = new();
        WorkItemRegistry combined = store.LoadWorkItems(paths);
        WorkItem item = combined.WorkItems.Single(value => value.Id == "WI-0002");
        item.Status = "completed";
        item.CompletedAt = "2026-09-11";
        item.VerifiedAt = "2026-09-11";
        item.VerifiedBy = "automated";
        item.Evidence.Add(new Evidence { Type = "verification", Value = "passed" });

        store.SaveWorkItems(paths, combined);

        Assert.False(File.Exists(paths.ActiveWorkItemShard("WI-0002")));
        Assert.True(File.Exists(paths.ArchivedWorkItemShard("WI-0002")));
        WorkItem reloaded = store.LoadWorkItems(paths).WorkItems.Single(value => value.Id == "WI-0002");
        Assert.Equal("completed", reloaded.Status);
    }

    [Fact]
    public void SaveWorkItemsRejectsChangesToArchivedShard()
    {
        RepositoryPaths paths = CreatePaths();
        WriteShardedStore(paths);
        RegistryStore store = new();
        WorkItemRegistry combined = store.LoadWorkItems(paths);

        combined.WorkItems.Single(item => item.Id == "WI-0001").Owner = "different-owner";

        InvalidOperationException exception = Assert.Throws<InvalidOperationException>(
            () => store.SaveWorkItems(paths, combined));
        Assert.Contains("read-only", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void MigrateLegacyWorkItemsToShardsIsLosslessAndIdempotent()
    {
        RepositoryPaths paths = CreatePaths();
        WriteLegacyActiveAndArchive(paths, includeCompletedActiveItem: true);
        string legacyBefore = File.ReadAllText(paths.WorkItemsRegistry);
        RegistryStore store = new();
        WorkItemRegistry before = store.LoadWorkItems(paths);

        WorkItemRegistry migrated = store.MigrateLegacyWorkItemsToShards(paths);
        store.MigrateLegacyWorkItemsToShards(paths);

        Assert.Equal(legacyBefore, File.ReadAllText(paths.WorkItemsRegistry));
        Assert.True(File.Exists(paths.WorkItemShardRegistry));
        Assert.True(File.Exists(paths.ActiveWorkItemShard("WI-0002")));
        Assert.True(File.Exists(paths.ArchivedWorkItemShard("WI-0001")));
        Assert.True(File.Exists(paths.ArchivedWorkItemShard("WI-0003")));
        Assert.Equal(before.WorkItems.Count, migrated.WorkItems.Count);

        WorkItemRegistry after = store.LoadWorkItems(paths);
        Assert.Equal(
            before.WorkItems.OrderBy(item => item.Id).Select(Describe),
            after.WorkItems.OrderBy(item => item.Id).Select(Describe));
    }

    [Fact]
    public void MigrateLegacyWorkItemsToShardsRejectsConflictingPartialShard()
    {
        RepositoryPaths paths = CreatePaths();
        WriteLegacyActiveAndArchive(paths);
        WriteShard(
            paths.ActiveWorkItemShard("WI-0002"),
            ActiveItem("WI-0002").Replace("title: Active", "title: Conflicting", StringComparison.Ordinal));

        InvalidDataException exception = Assert.Throws<InvalidDataException>(
            () => new RegistryStore().MigrateLegacyWorkItemsToShards(paths));

        Assert.Contains("conflicts with the legacy registry", exception.Message, StringComparison.Ordinal);
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

    private static void WriteLegacyActiveAndArchive(
        RepositoryPaths paths,
        bool includeCompletedActiveItem = false)
    {
        string completedActive = includeCompletedActiveItem
            ? """
              - id: WI-0003
                title: Completed after original archive split
                milestone: M00
                status: completed
                owner: ai-agent
                document: ../work-items/WI-0003.md
                completed_at: 2026-09-11
                verified_at: 2026-09-11
                verified_by: automated
                blockers: []
                blocker_notes: []
                evidence:
                - type: verification
                  value: passed
              """
            : "";

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
                """ + (string.IsNullOrEmpty(completedActive) ? "" : "\n" + completedActive)));

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
                  completed_at: 2026-08-14
                  verified_at: 2026-08-14
                  verified_by: automated
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

    private static void WriteShardedStore(RepositoryPaths paths)
    {
        WriteShardRegistry(paths);
        WriteShard(paths.ArchivedWorkItemShard("WI-0001"), ArchivedItem("WI-0001"));
        WriteShard(paths.ActiveWorkItemShard("WI-0002"), ActiveItem("WI-0002"));
        WriteShard(
            paths.ActiveWorkItemShard("WI-0003"),
            ActiveItem("WI-0003")
                .Replace("title: Active", "title: Other active", StringComparison.Ordinal)
                .Replace("status: proposed", "status: ready", StringComparison.Ordinal)
                .Replace("- WI-0001", "[]", StringComparison.Ordinal));
    }

    private static void WriteShardRegistry(RepositoryPaths paths) =>
        RegistryStore.WriteAtomically(
            paths.WorkItemShardRegistry,
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
            """ + "\n");

    private static void WriteShard(string path, string content) =>
        RegistryStore.WriteAtomically(path, content + "\n");

    private static string ActiveItem(string id) =>
        $$"""
        id: {{id}}
        title: Active
        milestone: M00
        status: proposed
        owner: unassigned
        document: ../work-items/{{id}}.md
        blockers:
        - WI-0001
        blocker_notes: []
        evidence: []
        """;

    private static string ArchivedItem(string id) =>
        $$"""
        id: {{id}}
        title: Archived
        milestone: M00
        status: completed
        owner: ai-agent
        document: ../work-items/{{id}}.md
        completed_at: 2026-08-14
        verified_at: 2026-08-14
        verified_by: automated
        blockers: []
        blocker_notes: []
        evidence:
        - type: verification
          value: passed
        """;

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

    private static string Describe(WorkItem item) =>
        string.Join(
            "|",
            item.Id,
            item.Title,
            item.Milestone,
            item.Status,
            item.Owner,
            item.Document,
            item.StartedAt,
            item.CompletedAt,
            item.VerifiedAt,
            item.VerifiedBy,
            item.LastUpdatedAt,
            item.Branch,
            string.Join(",", item.Blockers),
            string.Join(";", item.BlockerNotes),
            string.Join(";", item.Evidence.Select(evidence => $"{evidence.Type}:{evidence.Value}")));
}
