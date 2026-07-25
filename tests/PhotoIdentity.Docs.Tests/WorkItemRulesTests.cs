using PhotoIdentity.Docs;

namespace PhotoIdentity.Docs.Tests;

public sealed class WorkItemRulesTests
{
    [Fact]
    public void ProposedItemIsReadyWhenAllDependenciesAreCompleted()
    {
        WorkItem completed = Item("WI-0001", "completed");
        WorkItem candidate = Item("WI-0002", "proposed", "WI-0001");
        Dictionary<string, WorkItem> items = new()
        {
            [completed.Id] = completed,
            [candidate.Id] = candidate,
        };

        Assert.True(WorkItemRules.IsReady(candidate, items));
    }

    [Fact]
    public void MilestoneCompletesOnlyWhenAllMembersComplete()
    {
        WorkItem first = Item("WI-0001", "completed");
        WorkItem second = Item("WI-0002", "completed");
        Dictionary<string, WorkItem> items = new()
        {
            [first.Id] = first,
            [second.Id] = second,
        };
        Milestone milestone = new()
        {
            Id = "M00",
            WorkItems = [first.Id, second.Id],
        };

        Assert.Equal("completed", WorkItemRules.CalculateMilestoneStatus(milestone, items));
    }

    private static WorkItem Item(string id, string status, params string[] blockers) =>
        new()
        {
            Id = id,
            Title = id,
            Milestone = "M00",
            Status = status,
            Owner = "test",
            Document = $"{id}.md",
            Blockers = [.. blockers],
            Evidence = status == "completed"
                ? [new Evidence { Type = "test", Value = "passed" }]
                : [],
        };
}
