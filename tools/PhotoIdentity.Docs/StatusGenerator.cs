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

        string roadmap = BuildRoadmap(milestones);

        if (checkOnly)
        {
            return CheckFile(paths.Roadmap, roadmap, output);
        }

        _store.SaveMilestones(paths, milestones);
        RegistryStore.WriteAtomically(paths.Roadmap, roadmap);
        output.WriteLine("Generated milestone statuses and roadmap.md.");
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
        builder.AppendLine("0.x local inference, review, model evaluation and archive-readiness foundations");
        builder.AppendLine("1.0 permanent catalogue ready to begin processing the real full archive");
        builder.AppendLine("post-1.0 complete archive coverage and ongoing synchronisation");
        builder.AppendLine("post-1.0 improve identity automation and operator experience");
        builder.AppendLine("later add capture metadata, location and semantic library intelligence");
        builder.AppendLine("```");

        return builder.ToString().Replace("\r\n", "\n", StringComparison.Ordinal);
    }
}
