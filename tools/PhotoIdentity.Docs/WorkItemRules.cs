namespace PhotoIdentity.Docs;

public static class WorkItemRules
{
    public static bool IsReady(WorkItem item, IReadOnlyDictionary<string, WorkItem> items)
    {
        if (item.Status is not ("proposed" or "ready"))
        {
            return false;
        }

        return item.Blockers.All(id =>
            items.TryGetValue(id, out WorkItem? blocker) &&
            blocker.Status == "completed");
    }

    public static string CalculateMilestoneStatus(
        Milestone milestone,
        IReadOnlyDictionary<string, WorkItem> items)
    {
        List<WorkItem> members = milestone.WorkItems
            .Where(items.ContainsKey)
            .Select(id => items[id])
            .ToList();

        if (members.Count == 0)
        {
            return "proposed";
        }

        if (members.All(item => item.Status == "completed"))
        {
            return "completed";
        }

        if (members.Any(item => item.Status is "in_progress" or "in_review"))
        {
            return "in_progress";
        }

        if (members.Any(item => item.Status == "blocked"))
        {
            return "blocked";
        }

        if (members.Any(item => IsReady(item, items)))
        {
            return "ready";
        }

        return "proposed";
    }
}
