using YamlDotNet.Serialization;

namespace PhotoIdentity.Docs;

public sealed class WorkItemRegistry
{
    public int SchemaVersion { get; set; }
    public List<string> AllowedStatuses { get; set; } = [];
    public List<WorkItem> WorkItems { get; set; } = [];
}

public sealed class WorkItem
{
    public string Id { get; set; } = "";
    public string Title { get; set; } = "";
    public string Milestone { get; set; } = "";
    public string Status { get; set; } = "";
    public string Owner { get; set; } = "";
    public string Document { get; set; } = "";
    public string? StartedAt { get; set; }
    public string? CompletedAt { get; set; }
    public string? VerifiedAt { get; set; }
    public string? VerifiedBy { get; set; }
    public string? LastUpdatedAt { get; set; }
    public string? Branch { get; set; }
    public List<string> Blockers { get; set; } = [];
    public List<string> BlockerNotes { get; set; } = [];
    public List<Evidence> Evidence { get; set; } = [];
}

public sealed class Evidence
{
    public string Type { get; set; } = "";
    public string Value { get; set; } = "";
}

public sealed class MilestoneRegistry
{
    public int SchemaVersion { get; set; }
    public List<Milestone> Milestones { get; set; } = [];
}

public sealed class Milestone
{
    public string Id { get; set; } = "";
    public string Title { get; set; } = "";
    public string Status { get; set; } = "";
    public string Document { get; set; } = "";
    public List<string> WorkItems { get; set; } = [];
}

public sealed record ValidationIssue(string Code, string Message);

public sealed class ValidationResult
{
    public List<ValidationIssue> Issues { get; } = [];
    public bool IsValid => Issues.Count == 0;

    public void Add(string code, string message) => Issues.Add(new ValidationIssue(code, message));
}
