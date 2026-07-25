using PhotoIdentity.Docs;

namespace PhotoIdentity.Docs.Tests;

public sealed class DocumentationValidatorTests : IDisposable
{
    private readonly string _root = Path.Combine(
        Path.GetTempPath(),
        $"PhotoIdentityDocsTests-{Guid.NewGuid():N}");

    public DocumentationValidatorTests()
    {
        Directory.CreateDirectory(Path.Combine(_root, "docs", "delivery", "status"));
        Directory.CreateDirectory(Path.Combine(_root, "docs", "delivery", "work-items"));
        Directory.CreateDirectory(Path.Combine(_root, "docs", "delivery", "milestones"));
    }

    [Fact]
    public void DetectsDuplicateIdsCyclesAndMissingCompletionEvidence()
    {
        WorkItem first = WorkItem("WI-0001", "completed", "WI-0002");
        WorkItem duplicate = WorkItem("WI-0001", "proposed");
        WorkItem second = WorkItem("WI-0002", "proposed", "WI-0001");
        WriteWorkItemDocument(first);
        WriteWorkItemDocument(second);
        WriteMilestoneDocument();

        WorkItemRegistry workItems = new()
        {
            SchemaVersion = 1,
            AllowedStatuses =
            [
                "proposed", "ready", "in_progress", "blocked",
                "in_review", "completed", "cancelled",
            ],
            WorkItems = [first, duplicate, second],
        };
        MilestoneRegistry milestones = new()
        {
            SchemaVersion = 1,
            Milestones =
            [
                new Milestone
                {
                    Id = "M00",
                    Title = "Test",
                    Status = "in_progress",
                    Document = "../milestones/M00.md",
                    WorkItems = ["WI-0001", "WI-0002"],
                },
            ],
        };

        RepositoryPaths paths = Paths();
        ValidationResult result = new DocumentationValidator().Validate(paths, workItems, milestones);

        Assert.Contains(result.Issues, issue => issue.Code == "WORK_ID");
        Assert.Contains(result.Issues, issue => issue.Code == "WORK_CYCLE");
        Assert.Contains(result.Issues, issue => issue.Code == "COMPLETED_WITHOUT_EVIDENCE");
    }

    [Fact]
    public void DetectsBlockedItemWithoutBlockers()
    {
        WorkItem blocked = WorkItem("WI-0001", "blocked");
        WriteWorkItemDocument(blocked);
        WriteMilestoneDocument();

        WorkItemRegistry workItems = Registry(blocked);
        MilestoneRegistry milestones = Milestones(blocked);

        ValidationResult result = new DocumentationValidator().Validate(Paths(), workItems, milestones);

        Assert.Contains(result.Issues, issue => issue.Code == "BLOCKED_WITHOUT_BLOCKER");
    }

    public void Dispose()
    {
        if (Directory.Exists(_root))
        {
            Directory.Delete(_root, recursive: true);
        }
    }

    private RepositoryPaths Paths() =>
        new(
            _root,
            Path.Combine(_root, "docs", "delivery", "status", "work-items.yaml"),
            Path.Combine(_root, "docs", "delivery", "status", "milestones.yaml"),
            Path.Combine(_root, "docs", "delivery", "status", "current.md"),
            Path.Combine(_root, "docs", "delivery", "roadmap.md"));

    private static WorkItemRegistry Registry(params WorkItem[] items) =>
        new()
        {
            SchemaVersion = 1,
            AllowedStatuses =
            [
                "proposed", "ready", "in_progress", "blocked",
                "in_review", "completed", "cancelled",
            ],
            WorkItems = [.. items],
        };

    private static MilestoneRegistry Milestones(params WorkItem[] items) =>
        new()
        {
            SchemaVersion = 1,
            Milestones =
            [
                new Milestone
                {
                    Id = "M00",
                    Title = "Test",
                    Status = items.Any(item => item.Status == "blocked") ? "blocked" : "in_progress",
                    Document = "../milestones/M00.md",
                    WorkItems = items.Select(item => item.Id).Distinct().ToList(),
                },
            ],
        };

    private static WorkItem WorkItem(string id, string status, params string[] blockers) =>
        new()
        {
            Id = id,
            Title = id,
            Milestone = "M00",
            Status = status,
            Owner = "agent",
            Document = $"../work-items/{id}.md",
            StartedAt = status is "in_progress" or "in_review" ? "2026-07-25" : null,
            CompletedAt = status == "completed" ? "2026-07-25" : null,
            VerifiedAt = status == "completed" ? "2026-07-25" : null,
            VerifiedBy = status == "completed" ? "human" : null,
            Blockers = [.. blockers],
        };

    private void WriteWorkItemDocument(WorkItem item)
    {
        File.WriteAllText(
            Path.Combine(_root, "docs", "delivery", "work-items", $"{item.Id}.md"),
            $"---\nid: {item.Id}\ntitle: {item.Title}\nmilestone: {item.Milestone}\n---\n");
    }

    private void WriteMilestoneDocument()
    {
        File.WriteAllText(
            Path.Combine(_root, "docs", "delivery", "milestones", "M00.md"),
            "# Test\n");
    }
}
