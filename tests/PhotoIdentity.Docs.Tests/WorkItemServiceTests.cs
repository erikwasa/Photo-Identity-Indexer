using PhotoIdentity.Docs;

namespace PhotoIdentity.Docs.Tests;

public sealed class WorkItemServiceTests
{
    private static readonly DateOnly Date = new(2026, 7, 25);

    [Fact]
    public void StartRejectsIncompleteDependencies()
    {
        WorkItem blocker = Item("WI-0001", "in_progress");
        WorkItem candidate = Item("WI-0002", "proposed", blocker.Id);
        Dictionary<string, WorkItem> items = new()
        {
            [blocker.Id] = blocker,
            [candidate.Id] = candidate,
        };

        InvalidOperationException exception = Assert.Throws<InvalidOperationException>(
            () => new WorkItemService().Start(candidate, items, "agent", "branch", Date));

        Assert.Contains(blocker.Id, exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void CompleteRequiresReviewAndRecordsEvidence()
    {
        WorkItem item = Item("WI-0001", "in_review");

        new WorkItemService().Complete(
            item,
            new Evidence { Type = "workflow", Value = "https://example.test/run" },
            "human",
            Date);

        Assert.Equal("completed", item.Status);
        Assert.Equal("2026-07-25", item.CompletedAt);
        Assert.Equal("human", item.VerifiedBy);
        Assert.Single(item.Evidence);
    }

    [Fact]
    public void BlockRequiresAtLeastOneBlocker()
    {
        WorkItem item = Item("WI-0001", "in_progress");

        Assert.Throws<ArgumentException>(
            () => new WorkItemService().Block(item, [], null, Date));
    }

    private static WorkItem Item(string id, string status, params string[] blockers) =>
        new()
        {
            Id = id,
            Title = id,
            Milestone = "M00",
            Status = status,
            Owner = "agent",
            Document = $"{id}.md",
            StartedAt = status is "in_progress" or "in_review" ? "2026-07-25" : null,
            Blockers = [.. blockers],
        };
}
