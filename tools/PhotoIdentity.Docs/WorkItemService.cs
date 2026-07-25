using System.Globalization;

namespace PhotoIdentity.Docs;

public sealed class WorkItemService
{
    public void Start(
        WorkItem item,
        IReadOnlyDictionary<string, WorkItem> items,
        string owner,
        string? branch,
        DateOnly date)
    {
        if (item.Status is not ("proposed" or "ready"))
        {
            throw new InvalidOperationException(
                $"{item.Id} cannot start from status '{item.Status}'.");
        }

        List<string> incomplete = item.Blockers
            .Where(id => !items.TryGetValue(id, out WorkItem? blocker) || blocker.Status != "completed")
            .ToList();

        if (incomplete.Count > 0)
        {
            throw new InvalidOperationException(
                $"{item.Id} cannot start until these blockers are completed: {string.Join(", ", incomplete)}.");
        }

        if (string.IsNullOrWhiteSpace(owner) || owner == "unassigned")
        {
            throw new ArgumentException("An active work item must have an owner.", nameof(owner));
        }

        item.Status = "in_progress";
        item.Owner = owner;
        item.StartedAt ??= Format(date);
        item.LastUpdatedAt = Format(date);
        item.Branch = string.IsNullOrWhiteSpace(branch) ? item.Branch : branch;
    }

    public void Block(
        WorkItem item,
        IReadOnlyCollection<string> blockerIds,
        string? note,
        DateOnly date)
    {
        if (item.Status is "completed" or "cancelled")
        {
            throw new InvalidOperationException(
                $"{item.Id} cannot be blocked from status '{item.Status}'.");
        }

        if (blockerIds.Count == 0)
        {
            throw new ArgumentException("At least one blocker ID is required.", nameof(blockerIds));
        }

        foreach (string blocker in blockerIds)
        {
            if (!item.Blockers.Contains(blocker, StringComparer.Ordinal))
            {
                item.Blockers.Add(blocker);
            }
        }

        if (!string.IsNullOrWhiteSpace(note))
        {
            item.BlockerNotes.Add(note);
        }

        item.Status = "blocked";
        item.LastUpdatedAt = Format(date);
    }

    public void Review(WorkItem item, DateOnly date)
    {
        if (item.Status != "in_progress")
        {
            throw new InvalidOperationException(
                $"{item.Id} can enter review only from in_progress.");
        }

        item.Status = "in_review";
        item.LastUpdatedAt = Format(date);
    }

    public void Complete(
        WorkItem item,
        Evidence evidence,
        string verifiedBy,
        DateOnly date)
    {
        if (item.Status != "in_review")
        {
            throw new InvalidOperationException(
                $"{item.Id} can complete only from in_review.");
        }

        if (string.IsNullOrWhiteSpace(evidence.Type) || string.IsNullOrWhiteSpace(evidence.Value))
        {
            throw new ArgumentException("Completion evidence must include a type and value.", nameof(evidence));
        }

        if (string.IsNullOrWhiteSpace(verifiedBy))
        {
            throw new ArgumentException("A verifier is required.", nameof(verifiedBy));
        }

        item.Evidence.Add(evidence);
        item.Status = "completed";
        item.CompletedAt = Format(date);
        item.VerifiedAt = Format(date);
        item.VerifiedBy = verifiedBy;
        item.LastUpdatedAt = Format(date);
    }

    private static string Format(DateOnly date) =>
        date.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
}
