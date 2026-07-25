using System.Text;

namespace PhotoIdentity.Docs;

public sealed class StatusGenerator
{
    private readonly RegistryStore _store;

    public StatusGenerator(RegistryStore store)
    {
        _store = store;
    }

    public bool Generate(
        RepositoryPaths paths,
        WorkItemRegistry workItems,
        MilestoneRegistry milestones,
        bool checkOnly,
        TextWriter output)
    {
        Dictionary<string, WorkItem> itemMap = workItems.WorkItems
            .ToDictionary(item => item.Id, StringComparer.Ordinal);

        foreach (Milestone milestone in milestones.Milestones)
        {
            milestone.Status = WorkItemRules.CalculateMilestoneStatus(milestone, itemMap);
        }

        string current = BuildCurrent(workItems, milestones, itemMap);
        string roadmap = BuildRoadmap(milestones);

        if (checkOnly)
        {
            bool matches = CheckFile(paths.CurrentStatus, current, output) &
                           CheckFile(paths.Roadmap, roadmap, output);
            return matches;
        }

        _store.SaveMilestones(paths, milestones);
        RegistryStore.WriteAtomically(paths.CurrentStatus, current);
        RegistryStore.WriteAtomically(paths.Roadmap, roadmap);
        output.WriteLine("Generated milestone statuses, current.md and roadmap.md.");
        return true;
    }

    private static bool CheckFile(string path, string expected, TextWriter output)
    {
        string actual = File.Exists(path)
            ? File.ReadAllText(path).Replace("\r\n", "\n", StringComparison.Ordinal)
            : "";

        if (actual == expected)
        {
            return true;
        }

        output.WriteLine($"Generated file is stale: {path}");
        return false;
    }

    private static string BuildCurrent(
        WorkItemRegistry registry,
        MilestoneRegistry milestones,
        IReadOnlyDictionary<string, WorkItem> itemMap)
    {
        List<WorkItem> active = registry.WorkItems
            .Where(item => item.Status is "in_progress" or "in_review" or "blocked")
            .OrderBy(item => item.Id, StringComparer.Ordinal)
            .ToList();

        List<WorkItem> completed = registry.WorkItems
            .Where(item => item.Status == "completed")
            .OrderByDescending(item => item.CompletedAt, StringComparer.Ordinal)
            .ThenByDescending(item => item.Id, StringComparer.Ordinal)
            .Take(3)
            .ToList();

        List<WorkItem> ready = registry.WorkItems
            .Where(item => WorkItemRules.IsReady(item, itemMap))
            .OrderBy(item => item.Id, StringComparer.Ordinal)
            .Take(5)
            .ToList();

        StringBuilder builder = new();
        builder.AppendLine("# Current delivery status");
        builder.AppendLine();
        builder.AppendLine("Generated from `work-items.yaml` and `milestones.yaml`. Do not edit manually.");
        builder.AppendLine();
        builder.AppendLine("## Milestones");
        builder.AppendLine();
        foreach (Milestone milestone in milestones.Milestones.Where(value => value.Status != "proposed"))
        {
            builder.AppendLine($"- **{milestone.Id} — {milestone.Title}**: `{milestone.Status}`");
        }

        AppendWorkItems("Active work", active);
        AppendWorkItems("Recently completed", completed);
        AppendWorkItems("Next ready work", ready);

        return builder.ToString().Replace("\r\n", "\n", StringComparison.Ordinal);

        void AppendWorkItems(string heading, IReadOnlyCollection<WorkItem> items)
        {
            builder.AppendLine();
            builder.AppendLine($"## {heading}");
            builder.AppendLine();

            if (items.Count == 0)
            {
                builder.AppendLine("None.");
                return;
            }

            foreach (WorkItem item in items)
            {
                string document = Path.GetFileName(item.Document);
                builder.AppendLine(
                    $"- [**{item.Id} — {item.Title}**](../work-items/{document}) — `{item.Status}`; owner: `{item.Owner}`");
            }
        }
    }

    private static string BuildRoadmap(MilestoneRegistry milestones)
    {
        StringBuilder builder = new();
        builder.AppendLine("# Roadmap");
        builder.AppendLine();
        builder.AppendLine("Generated from [`status/milestones.yaml`](status/milestones.yaml). Do not edit the table manually.");
        builder.AppendLine();
        builder.AppendLine("| Milestone | Outcome | Current status |");
        builder.AppendLine("|---|---|---|");
        foreach (Milestone milestone in milestones.Milestones)
        {
            builder.AppendLine($"| {milestone.Id} | {milestone.Title} | {milestone.Status} |");
        }

        builder.AppendLine();
        builder.AppendLine("Expected evolution:");
        builder.AppendLine();
        builder.AppendLine("```text");
        builder.AppendLine("0.1 local inference and review");
        builder.AppendLine("0.2 OneDrive hydration and staging");
        builder.AppendLine("0.3 multi-model evaluation");
        builder.AppendLine("0.4 portable Azure execution without identities");
        builder.AppendLine("0.5 budget-controlled archive processing");
        builder.AppendLine("0.6 ongoing synchronisation");
        builder.AppendLine("1.0 stable people index and collection API");
        builder.AppendLine("```");

        return builder.ToString().Replace("\r\n", "\n", StringComparison.Ordinal);
    }
}
