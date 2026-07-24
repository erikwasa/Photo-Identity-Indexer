using System.Text.RegularExpressions;

namespace PhotoIdentity.Docs;

public sealed partial class DocumentationValidator
{
    [GeneratedRegex(@"(?<!!)\[[^\]]*\]\((?<target>[^)]+)\)", RegexOptions.Compiled)]
    private static partial Regex MarkdownLinkRegex();

    public ValidationResult Validate(
        RepositoryPaths paths,
        WorkItemRegistry workItems,
        MilestoneRegistry milestones)
    {
        ValidationResult result = new();

        if (workItems.SchemaVersion != 1)
        {
            result.Add("WORK_SCHEMA", $"Unsupported work-item schema version {workItems.SchemaVersion}.");
        }

        if (milestones.SchemaVersion != 1)
        {
            result.Add("MILESTONE_SCHEMA", $"Unsupported milestone schema version {milestones.SchemaVersion}.");
        }

        ValidateUniqueValues(workItems.AllowedStatuses, "STATUS_DUPLICATE", "allowed status", result);

        Dictionary<string, WorkItem> itemMap = BuildUniqueMap(
            workItems.WorkItems,
            item => item.Id,
            "WORK_ID",
            "work-item",
            result);

        Dictionary<string, Milestone> milestoneMap = BuildUniqueMap(
            milestones.Milestones,
            milestone => milestone.Id,
            "MILESTONE_ID",
            "milestone",
            result);

        foreach (WorkItem item in workItems.WorkItems)
        {
            ValidateWorkItem(paths, item, workItems.AllowedStatuses, itemMap, milestoneMap, result);
        }

        foreach (Milestone milestone in milestones.Milestones)
        {
            ValidateMilestone(paths, milestone, itemMap, result);
        }

        ValidateMembership(workItems.WorkItems, milestones.Milestones, result);
        ValidateCycles(itemMap, result);
        ValidateMarkdownLinks(paths.Root, result);

        return result;
    }

    private static void ValidateWorkItem(
        RepositoryPaths paths,
        WorkItem item,
        IReadOnlyCollection<string> allowedStatuses,
        IReadOnlyDictionary<string, WorkItem> itemMap,
        IReadOnlyDictionary<string, Milestone> milestoneMap,
        ValidationResult result)
    {
        if (string.IsNullOrWhiteSpace(item.Id))
        {
            result.Add("WORK_ID_MISSING", "A work item has no ID.");
        }

        if (!allowedStatuses.Contains(item.Status, StringComparer.Ordinal))
        {
            result.Add("WORK_STATUS", $"{item.Id} has unsupported status '{item.Status}'.");
        }

        if (!milestoneMap.ContainsKey(item.Milestone))
        {
            result.Add("WORK_MILESTONE", $"{item.Id} references missing milestone {item.Milestone}.");
        }

        string documentPath = ResolveRegistryPath(paths.StatusDirectory, item.Document);
        if (!File.Exists(documentPath))
        {
            result.Add("WORK_DOCUMENT", $"{item.Id} document does not exist: {item.Document}.");
        }
        else
        {
            ValidateWorkItemFrontMatter(documentPath, item, result);
        }

        foreach (string blocker in item.Blockers)
        {
            if (blocker == item.Id)
            {
                result.Add("WORK_SELF_BLOCK", $"{item.Id} blocks itself.");
            }
            else if (!itemMap.ContainsKey(blocker))
            {
                result.Add("WORK_BLOCKER", $"{item.Id} references missing blocker {blocker}.");
            }
        }

        if (item.Status == "blocked" && item.Blockers.Count == 0)
        {
            result.Add("BLOCKED_WITHOUT_BLOCKER", $"{item.Id} is blocked but has no blockers.");
        }

        if (item.Status == "completed" && item.Evidence.Count == 0)
        {
            result.Add("COMPLETED_WITHOUT_EVIDENCE", $"{item.Id} is completed but has no evidence.");
        }

        if (item.Status is "in_progress" or "in_review")
        {
            if (string.IsNullOrWhiteSpace(item.StartedAt))
            {
                result.Add("ACTIVE_WITHOUT_START", $"{item.Id} is active but has no started_at date.");
            }

            if (string.IsNullOrWhiteSpace(item.Owner) || item.Owner == "unassigned")
            {
                result.Add("ACTIVE_WITHOUT_OWNER", $"{item.Id} is active but has no owner.");
            }
        }

        if (item.Status == "completed")
        {
            if (string.IsNullOrWhiteSpace(item.CompletedAt) ||
                string.IsNullOrWhiteSpace(item.VerifiedAt) ||
                string.IsNullOrWhiteSpace(item.VerifiedBy))
            {
                result.Add(
                    "COMPLETION_METADATA",
                    $"{item.Id} is completed but completion or verification metadata is missing.");
            }
        }

        foreach (Evidence evidence in item.Evidence)
        {
            if (string.IsNullOrWhiteSpace(evidence.Type) || string.IsNullOrWhiteSpace(evidence.Value))
            {
                result.Add("EVIDENCE_INVALID", $"{item.Id} contains incomplete evidence.");
            }
        }

        if (item.Status == "ready" && !WorkItemRules.IsReady(item, itemMap))
        {
            result.Add("READY_WITH_BLOCKERS", $"{item.Id} is marked ready before its blockers are completed.");
        }
    }

    private static void ValidateMilestone(
        RepositoryPaths paths,
        Milestone milestone,
        IReadOnlyDictionary<string, WorkItem> itemMap,
        ValidationResult result)
    {
        if (string.IsNullOrWhiteSpace(milestone.Id))
        {
            result.Add("MILESTONE_ID_MISSING", "A milestone has no ID.");
        }

        string documentPath = ResolveRegistryPath(paths.StatusDirectory, milestone.Document);
        if (!File.Exists(documentPath))
        {
            result.Add(
                "MILESTONE_DOCUMENT",
                $"{milestone.Id} document does not exist: {milestone.Document}.");
        }

        foreach (string workItemId in milestone.WorkItems)
        {
            if (!itemMap.ContainsKey(workItemId))
            {
                result.Add(
                    "MILESTONE_WORK_ITEM",
                    $"{milestone.Id} references missing work item {workItemId}.");
            }
        }

        string calculated = WorkItemRules.CalculateMilestoneStatus(milestone, itemMap);
        if (!string.Equals(milestone.Status, calculated, StringComparison.Ordinal))
        {
            result.Add(
                "MILESTONE_STATUS",
                $"{milestone.Id} status is '{milestone.Status}' but calculates to '{calculated}'.");
        }
    }

    private static void ValidateMembership(
        IEnumerable<WorkItem> workItems,
        IEnumerable<Milestone> milestones,
        ValidationResult result)
    {
        Dictionary<string, List<string>> memberships = new(StringComparer.Ordinal);
        foreach (Milestone milestone in milestones)
        {
            foreach (string workItem in milestone.WorkItems)
            {
                memberships.TryAdd(workItem, []);
                memberships[workItem].Add(milestone.Id);
            }
        }

        foreach (WorkItem item in workItems)
        {
            if (!memberships.TryGetValue(item.Id, out List<string>? memberOf))
            {
                result.Add("WORK_NOT_IN_MILESTONE", $"{item.Id} is not listed in any milestone.");
                continue;
            }

            if (memberOf.Count != 1 || memberOf[0] != item.Milestone)
            {
                result.Add(
                    "WORK_MILESTONE_MISMATCH",
                    $"{item.Id} declares {item.Milestone} but is listed in {string.Join(", ", memberOf)}.");
            }
        }
    }

    private static void ValidateCycles(
        IReadOnlyDictionary<string, WorkItem> itemMap,
        ValidationResult result)
    {
        Dictionary<string, int> states = new(StringComparer.Ordinal);
        Stack<string> path = new();

        foreach (string id in itemMap.Keys)
        {
            Visit(id);
        }

        void Visit(string id)
        {
            if (states.TryGetValue(id, out int state))
            {
                if (state == 1)
                {
                    string cycle = string.Join(
                        " -> ",
                        path.Reverse().SkipWhile(value => value != id).Append(id));
                    result.Add("WORK_CYCLE", $"Dependency cycle detected: {cycle}.");
                }

                return;
            }

            states[id] = 1;
            path.Push(id);

            foreach (string blocker in itemMap[id].Blockers.Where(itemMap.ContainsKey))
            {
                Visit(blocker);
            }

            path.Pop();
            states[id] = 2;
        }
    }

    private static void ValidateMarkdownLinks(string root, ValidationResult result)
    {
        IEnumerable<string> markdownFiles = Directory
            .EnumerateFiles(root, "*.md", SearchOption.AllDirectories)
            .Where(path => !IsIgnoredPath(path));

        foreach (string markdownFile in markdownFiles)
        {
            string content = File.ReadAllText(markdownFile);
            foreach (Match match in MarkdownLinkRegex().Matches(content))
            {
                string target = match.Groups["target"].Value.Trim().Trim('<', '>');
                if (target.Length == 0 ||
                    target.StartsWith('#') ||
                    Uri.TryCreate(target, UriKind.Absolute, out _))
                {
                    continue;
                }

                string withoutFragment = target.Split('#', 2)[0].Split('?', 2)[0];
                if (withoutFragment.Length == 0)
                {
                    continue;
                }

                string decoded = Uri.UnescapeDataString(withoutFragment);
                string resolved = Path.GetFullPath(
                    Path.Combine(Path.GetDirectoryName(markdownFile)!, decoded));

                if (!File.Exists(resolved) && !Directory.Exists(resolved))
                {
                    string relativeSource = Path.GetRelativePath(root, markdownFile);
                    result.Add(
                        "BROKEN_LINK",
                        $"{relativeSource} links to missing path '{target}'.");
                }
            }
        }
    }

    private static bool IsIgnoredPath(string path)
    {
        string normalized = path.Replace('\\', '/');
        return normalized.Contains("/.git/", StringComparison.OrdinalIgnoreCase) ||
               normalized.Contains("/bin/", StringComparison.OrdinalIgnoreCase) ||
               normalized.Contains("/obj/", StringComparison.OrdinalIgnoreCase);
    }

    private static void ValidateWorkItemFrontMatter(
        string documentPath,
        WorkItem item,
        ValidationResult result)
    {
        string[] lines = File.ReadAllLines(documentPath);
        if (lines.Length < 3 || lines[0] != "---")
        {
            result.Add("FRONT_MATTER", $"{item.Id} document has no YAML front matter.");
            return;
        }

        Dictionary<string, string> values = new(StringComparer.Ordinal);
        for (int index = 1; index < lines.Length && lines[index] != "---"; index++)
        {
            int separator = lines[index].IndexOf(':');
            if (separator > 0)
            {
                values[lines[index][..separator].Trim()] = lines[index][(separator + 1)..].Trim();
            }
        }

        Compare("id", item.Id);
        Compare("title", item.Title);
        Compare("milestone", item.Milestone);

        void Compare(string key, string expected)
        {
            if (!values.TryGetValue(key, out string? actual) ||
                !string.Equals(actual, expected, StringComparison.Ordinal))
            {
                result.Add(
                    "FRONT_MATTER_MISMATCH",
                    $"{item.Id} document front matter {key} does not match the registry.");
            }
        }
    }

    private static string ResolveRegistryPath(string statusDirectory, string relativePath) =>
        Path.GetFullPath(Path.Combine(statusDirectory, relativePath));

    private static Dictionary<string, T> BuildUniqueMap<T>(
        IEnumerable<T> values,
        Func<T, string> keySelector,
        string code,
        string description,
        ValidationResult result)
    {
        Dictionary<string, T> map = new(StringComparer.Ordinal);
        foreach (T value in values)
        {
            string key = keySelector(value);
            if (!map.TryAdd(key, value))
            {
                result.Add(code, $"Duplicate {description} ID '{key}'.");
            }
        }

        return map;
    }

    private static void ValidateUniqueValues(
        IEnumerable<string> values,
        string code,
        string description,
        ValidationResult result)
    {
        HashSet<string> seen = new(StringComparer.Ordinal);
        foreach (string value in values)
        {
            if (!seen.Add(value))
            {
                result.Add(code, $"Duplicate {description} '{value}'.");
            }
        }
    }
}
