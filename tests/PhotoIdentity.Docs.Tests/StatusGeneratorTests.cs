using PhotoIdentity.Docs;

namespace PhotoIdentity.Docs.Tests;

public sealed class StatusGeneratorTests
{
    [Fact]
    public void GenerateShardedViewsKeepsCompatibilityRegistryBoundedToCurrentWork()
    {
        RepositoryPaths paths = CreatePaths();
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

        WorkItemRegistry workItems = new()
        {
            SchemaVersion = 1,
            AllowedStatuses =
            [
                "proposed",
                "ready",
                "in_progress",
                "blocked",
                "in_review",
                "completed",
                "cancelled",
            ],
            WorkItems =
            [
                new WorkItem
                {
                    Id = "WI-0109",
                    Title = "Current item",
                    Milestone = "M00",
                    Status = "in_progress",
                    Owner = "ai-agent",
                    Document = "../work-items/WI-0109.md",
                    StartedAt = "2026-09-11",
                    Blockers = [],
                    BlockerNotes = [],
                    Evidence = [],
                },
                new WorkItem
                {
                    Id = "WI-0057",
                    Title = "Archived item",
                    Milestone = "M00",
                    Status = "completed",
                    Owner = "ai-agent",
                    Document = "../work-items/WI-0057.md",
                    CompletedAt = "2026-08-14",
                    VerifiedAt = "2026-08-14",
                    VerifiedBy = "automated",
                    Blockers = [],
                    BlockerNotes = [],
                    Evidence = [new Evidence { Type = "workflow", Value = "passed" }],
                },
            ],
        };
        MilestoneRegistry milestones = new()
        {
            SchemaVersion = 1,
            Milestones =
            [
                new Milestone
                {
                    Id = "M00",
                    Title = "Repository and architecture",
                    Status = "in_progress",
                    Document = "../milestones/M00.md",
                    WorkItems = ["WI-0057", "WI-0109"],
                },
            ],
        };

        StatusGenerator generator = new(new RegistryStore());
        Assert.True(generator.Generate(paths, workItems, milestones, checkOnly: false, TextWriter.Null));

        string compatibility = File.ReadAllText(paths.WorkItemsRegistry);
        Assert.Contains("Generated from canonical work-item shards", compatibility, StringComparison.Ordinal);
        Assert.Contains("WI-0109", compatibility, StringComparison.Ordinal);
        Assert.DoesNotContain("WI-0057", compatibility, StringComparison.Ordinal);

        string index = File.ReadAllText(paths.WorkItemsIndex);
        Assert.Contains("Current work items: **1**", index, StringComparison.Ordinal);
        Assert.Contains("Archived terminal items: **1**", index, StringComparison.Ordinal);
        Assert.Contains("work-items/active/WI-0109.yaml", index, StringComparison.Ordinal);
        Assert.Contains("show WI-0057", index, StringComparison.Ordinal);

        Assert.True(generator.Generate(paths, workItems, milestones, checkOnly: true, TextWriter.Null));
    }

    private static RepositoryPaths CreatePaths()
    {
        string root = Path.Combine(Path.GetTempPath(), $"PhotoIdentityDocsGeneratorTests-{Guid.NewGuid():N}");
        string status = Path.Combine(root, "docs", "delivery", "status");
        Directory.CreateDirectory(status);
        return new RepositoryPaths(
            root,
            Path.Combine(status, "work-items.yaml"),
            Path.Combine(status, "milestones.yaml"),
            Path.Combine(root, "docs", "delivery", "roadmap.md"));
    }
}
