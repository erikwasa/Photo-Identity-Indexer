namespace PhotoIdentity.Docs;

public static class Program
{
    public static int Main(string[] args)
    {
        try
        {
            return Run(args, Console.Out, Console.Error);
        }
        catch (Exception exception)
        {
            Console.Error.WriteLine($"error: {exception.Message}");
            return 1;
        }
    }

    internal static int Run(string[] args, TextWriter output, TextWriter error)
    {
        if (args.Length == 0 || args[0] is "-h" or "--help" or "help")
        {
            PrintUsage(output);
            return args.Length == 0 ? 1 : 0;
        }

        string command = args[0];
        Dictionary<string, List<string>> options = ParseOptions(args.Skip(1).ToArray(), out List<string> positionals);
        string? root = SingleOption(options, "root");

        RepositoryPaths paths = RepositoryPaths.Discover(root);
        RegistryStore store = new();
        WorkItemRegistry workItems = store.LoadWorkItems(paths);
        MilestoneRegistry milestones = store.LoadMilestones(paths);
        DocumentationValidator validator = new();
        StatusGenerator generator = new(store);

        if (command == "validate")
        {
            return PrintValidation(validator.Validate(paths, workItems, milestones), output);
        }

        if (command == "generate")
        {
            bool check = options.ContainsKey("check");
            bool success = generator.Generate(paths, workItems, milestones, check, output);
            return success ? 0 : 1;
        }

        if (command == "next")
        {
            Dictionary<string, WorkItem> itemMap = workItems.WorkItems
                .ToDictionary(value => value.Id, StringComparer.Ordinal);
            List<WorkItem> ready = workItems.WorkItems
                .Where(value => WorkItemRules.IsReady(value, itemMap))
                .OrderBy(value => value.Id, StringComparer.Ordinal)
                .ToList();

            if (ready.Count == 0)
            {
                output.WriteLine("No work items are ready.");
                return 0;
            }

            foreach (WorkItem readyItem in ready)
            {
                output.WriteLine($"{readyItem.Id}\t{readyItem.Milestone}\t{readyItem.Title}");
            }

            return 0;
        }

        ValidationResult before = validator.Validate(paths, workItems, milestones);
        if (!before.IsValid)
        {
            error.WriteLine("Registry validation failed before the requested mutation.");
            PrintValidation(before, error);
            return 1;
        }

        if (positionals.Count == 0)
        {
            throw new ArgumentException($"{command} requires a work-item ID.");
        }

        string id = positionals[0];
        Dictionary<string, WorkItem> map = workItems.WorkItems
            .ToDictionary(value => value.Id, StringComparer.Ordinal);
        if (!map.TryGetValue(id, out WorkItem? selectedItem))
        {
            throw new KeyNotFoundException($"Unknown work item '{id}'.");
        }

        WorkItemService service = new();
        DateOnly today = DateOnly.FromDateTime(DateTime.UtcNow);

        switch (command)
        {
            case "start":
                service.Start(
                    selectedItem,
                    map,
                    SingleOption(options, "owner") ?? "ai-agent",
                    SingleOption(options, "branch"),
                    today);
                break;

            case "block":
                List<string> blockerIds = MultiOption(options, "on");
                foreach (string blockerId in blockerIds)
                {
                    if (!map.ContainsKey(blockerId))
                    {
                        throw new KeyNotFoundException($"Unknown blocker '{blockerId}'.");
                    }
                }

                service.Block(selectedItem, blockerIds, SingleOption(options, "note"), today);
                break;

            case "review":
                service.Review(selectedItem, today);
                break;

            case "complete":
                service.Complete(
                    selectedItem,
                    new Evidence
                    {
                        Type = RequiredOption(options, "evidence-type"),
                        Value = RequiredOption(options, "evidence-value"),
                    },
                    RequiredOption(options, "verified-by"),
                    today);
                break;

            default:
                throw new ArgumentException($"Unknown command '{command}'.");
        }

        store.SaveWorkItems(paths, workItems);
        generator.Generate(paths, workItems, milestones, checkOnly: false, output);

        WorkItemRegistry reloadedWorkItems = store.LoadWorkItems(paths);
        MilestoneRegistry reloadedMilestones = store.LoadMilestones(paths);
        ValidationResult after = validator.Validate(paths, reloadedWorkItems, reloadedMilestones);
        if (!after.IsValid)
        {
            error.WriteLine("Mutation wrote invalid documentation state:");
            PrintValidation(after, error);
            return 1;
        }

        output.WriteLine($"{id} is now {selectedItem.Status}.");
        return 0;
    }

    private static int PrintValidation(ValidationResult result, TextWriter output)
    {
        if (result.IsValid)
        {
            output.WriteLine("Documentation registries and links are valid.");
            return 0;
        }

        foreach (ValidationIssue issue in result.Issues)
        {
            output.WriteLine($"{issue.Code}: {issue.Message}");
        }

        output.WriteLine($"{result.Issues.Count} validation issue(s).");
        return 1;
    }

    private static Dictionary<string, List<string>> ParseOptions(
        string[] args,
        out List<string> positionals)
    {
        Dictionary<string, List<string>> options = new(StringComparer.Ordinal);
        positionals = [];

        for (int index = 0; index < args.Length; index++)
        {
            string value = args[index];
            if (!value.StartsWith("--", StringComparison.Ordinal))
            {
                positionals.Add(value);
                continue;
            }

            string name = value[2..];
            if (name == "check")
            {
                options.TryAdd(name, []);
                continue;
            }

            if (index + 1 >= args.Length || args[index + 1].StartsWith("--", StringComparison.Ordinal))
            {
                throw new ArgumentException($"Option --{name} requires a value.");
            }

            options.TryAdd(name, []);
            options[name].Add(args[++index]);
        }

        return options;
    }

    private static string RequiredOption(
        IReadOnlyDictionary<string, List<string>> options,
        string name) =>
        SingleOption(options, name)
        ?? throw new ArgumentException($"Option --{name} is required.");

    private static string? SingleOption(
        IReadOnlyDictionary<string, List<string>> options,
        string name)
    {
        if (!options.TryGetValue(name, out List<string>? values))
        {
            return null;
        }

        if (values.Count != 1)
        {
            throw new ArgumentException($"Option --{name} must be supplied exactly once.");
        }

        return values[0];
    }

    private static List<string> MultiOption(
        IReadOnlyDictionary<string, List<string>> options,
        string name) =>
        options.TryGetValue(name, out List<string>? values) ? values : [];

    private static void PrintUsage(TextWriter output)
    {
        output.WriteLine("""
            PhotoIdentity.Docs

              validate [--root PATH]
              generate [--check] [--root PATH]
              next [--root PATH]
              start ID [--owner NAME] [--branch NAME] [--root PATH]
              block ID --on BLOCKER [--on BLOCKER] [--note TEXT] [--root PATH]
              review ID [--root PATH]
              complete ID --evidence-type TYPE --evidence-value VALUE --verified-by NAME [--root PATH]
            """);
    }
}
