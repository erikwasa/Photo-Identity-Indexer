using System.Text;
using YamlDotNet.Serialization;
using YamlDotNet.Serialization.NamingConventions;

namespace PhotoIdentity.Docs;

public sealed class RegistryStore
{
    private readonly IDeserializer _deserializer;
    private readonly ISerializer _serializer;

    public RegistryStore()
    {
        _deserializer = new DeserializerBuilder()
            .WithNamingConvention(UnderscoredNamingConvention.Instance)
            .IgnoreUnmatchedProperties()
            .Build();

        _serializer = new SerializerBuilder()
            .WithNamingConvention(UnderscoredNamingConvention.Instance)
            .ConfigureDefaultValuesHandling(DefaultValuesHandling.OmitNull)
            .Build();
    }

    public WorkItemRegistry LoadActiveWorkItems(RepositoryPaths paths) =>
        paths.HasShardedWorkItemStore
            ? LoadShardedWorkItems(paths, includeArchived: false)
            : Load<WorkItemRegistry>(paths.WorkItemsRegistry);

    public WorkItemRegistry LoadWorkItems(RepositoryPaths paths) =>
        paths.HasShardedWorkItemStore
            ? LoadShardedWorkItems(paths, includeArchived: true)
            : LoadLegacyWorkItems(paths);

    public MilestoneRegistry LoadMilestones(RepositoryPaths paths) =>
        Load<MilestoneRegistry>(paths.MilestonesRegistry);

    public void SaveWorkItems(RepositoryPaths paths, WorkItemRegistry registry)
    {
        if (paths.HasShardedWorkItemStore)
        {
            SaveShardedWorkItems(paths, registry);
            return;
        }

        SaveLegacyWorkItems(paths, registry);
    }

    public WorkItemRegistry MigrateLegacyWorkItemsToShards(RepositoryPaths paths)
    {
        if (paths.HasShardedWorkItemStore)
        {
            return LoadShardedWorkItems(paths, includeArchived: true);
        }

        if (!File.Exists(paths.WorkItemsRegistry))
        {
            throw new FileNotFoundException(
                "Legacy work-items.yaml is required for shard migration.",
                paths.WorkItemsRegistry);
        }

        WorkItemRegistry legacy = LoadLegacyWorkItems(paths);
        EnsureUniqueIds(legacy.WorkItems, "legacy work-item registries");

        WorkItemShardRegistry metadata = new()
        {
            SchemaVersion = legacy.SchemaVersion,
            AllowedStatuses = [.. legacy.AllowedStatuses],
        };

        HashSet<string> expectedIds = legacy.WorkItems
            .Select(item => item.Id)
            .ToHashSet(StringComparer.Ordinal);
        EnsureNoUnexpectedShards(paths, expectedIds);

        foreach (WorkItem item in legacy.WorkItems.OrderBy(item => item.Id, StringComparer.Ordinal))
        {
            bool terminal = IsTerminal(item.Status);
            string targetPath = terminal
                ? paths.ArchivedWorkItemShard(item.Id)
                : paths.ActiveWorkItemShard(item.Id);
            string otherPath = terminal
                ? paths.ActiveWorkItemShard(item.Id)
                : paths.ArchivedWorkItemShard(item.Id);

            if (File.Exists(otherPath))
            {
                throw new InvalidDataException(
                    $"Work item '{item.Id}' exists in the wrong shard area: {otherPath}.");
            }

            if (File.Exists(targetPath))
            {
                WorkItem existing = LoadShard(targetPath, terminal);
                if (!Equivalent(existing, item))
                {
                    throw new InvalidDataException(
                        $"Existing shard for '{item.Id}' conflicts with the legacy registry: {targetPath}.");
                }

                continue;
            }

            Save(targetPath, item);
        }

        VerifyMigratedShardEquivalence(paths, legacy);
        Save(paths.WorkItemShardRegistry, metadata);
        return legacy;
    }

    public void SaveMilestones(RepositoryPaths paths, MilestoneRegistry registry) =>
        Save(paths.MilestonesRegistry, registry);

    public string SerializeWorkItemRegistry(WorkItemRegistry registry) =>
        Serialize(registry);

    private WorkItemRegistry LoadLegacyWorkItems(RepositoryPaths paths)
    {
        WorkItemRegistry active = Load<WorkItemRegistry>(paths.WorkItemsRegistry);
        WorkItemRegistry combined = new()
        {
            SchemaVersion = active.SchemaVersion,
            AllowedStatuses = [.. active.AllowedStatuses],
        };

        combined.WorkItems.AddRange(LoadArchivedTerminalWorkItems(paths, active));
        combined.WorkItems.AddRange(active.WorkItems);
        return combined;
    }

    private WorkItemRegistry LoadShardedWorkItems(RepositoryPaths paths, bool includeArchived)
    {
        WorkItemShardRegistry metadata = Load<WorkItemShardRegistry>(paths.WorkItemShardRegistry);
        WorkItemRegistry combined = new()
        {
            SchemaVersion = metadata.SchemaVersion,
            AllowedStatuses = [.. metadata.AllowedStatuses],
        };

        Dictionary<string, string> sources = new(StringComparer.Ordinal);
        foreach (string path in paths.ActiveWorkItemShards)
        {
            AddShard(combined.WorkItems, sources, path, expectedTerminal: false);
        }

        if (includeArchived)
        {
            foreach (string path in paths.ArchivedWorkItemShards)
            {
                AddShard(combined.WorkItems, sources, path, expectedTerminal: true);
            }
        }

        return combined;
    }

    private void SaveLegacyWorkItems(RepositoryPaths paths, WorkItemRegistry registry)
    {
        WorkItemRegistry activeBeforeWrite = Load<WorkItemRegistry>(paths.WorkItemsRegistry);
        HashSet<string> activeIds = activeBeforeWrite.WorkItems
            .Select(item => item.Id)
            .ToHashSet(StringComparer.Ordinal);

        foreach (WorkItem archived in LoadArchivedTerminalWorkItems(paths, activeBeforeWrite))
        {
            WorkItem? candidate = registry.WorkItems.SingleOrDefault(item => item.Id == archived.Id);
            if (candidate is null || !Equivalent(candidate, archived))
            {
                throw new InvalidOperationException(
                    $"Archived work item '{archived.Id}' is read-only and cannot be changed by a lifecycle command.");
            }
        }

        WorkItemRegistry activeAfterWrite = new()
        {
            SchemaVersion = registry.SchemaVersion,
            AllowedStatuses = [.. registry.AllowedStatuses],
            WorkItems = registry.WorkItems
                .Where(item => activeIds.Contains(item.Id))
                .ToList(),
        };

        Save(paths.WorkItemsRegistry, activeAfterWrite);
    }

    private void SaveShardedWorkItems(RepositoryPaths paths, WorkItemRegistry requested)
    {
        WorkItemRegistry current = LoadShardedWorkItems(paths, includeArchived: true);
        Dictionary<string, WorkItem> currentMap = current.WorkItems
            .ToDictionary(item => item.Id, StringComparer.Ordinal);
        Dictionary<string, WorkItem> requestedMap = requested.WorkItems
            .ToDictionary(item => item.Id, StringComparer.Ordinal);

        if (!currentMap.Keys.OrderBy(id => id, StringComparer.Ordinal)
            .SequenceEqual(requestedMap.Keys.OrderBy(id => id, StringComparer.Ordinal), StringComparer.Ordinal))
        {
            throw new InvalidOperationException(
                "Lifecycle commands cannot add or remove work items from the sharded registry.");
        }

        List<WorkItem> changed = requested.WorkItems
            .Where(item => !Equivalent(item, currentMap[item.Id]))
            .ToList();

        if (changed.Count == 0)
        {
            return;
        }

        if (changed.Count != 1)
        {
            throw new InvalidOperationException(
                $"A lifecycle command must update exactly one work-item shard; found {changed.Count} changed items.");
        }

        WorkItem candidate = changed[0];
        WorkItem original = currentMap[candidate.Id];
        if (IsTerminal(original.Status))
        {
            throw new InvalidOperationException(
                $"Archived work item '{candidate.Id}' is read-only and cannot be changed by a lifecycle command.");
        }

        string activePath = paths.ActiveWorkItemShard(candidate.Id);
        if (!File.Exists(activePath))
        {
            throw new InvalidDataException(
                $"Active shard for '{candidate.Id}' does not exist at its deterministic path: {activePath}.");
        }

        if (IsTerminal(candidate.Status))
        {
            string archivePath = paths.ArchivedWorkItemShard(candidate.Id);
            if (File.Exists(archivePath))
            {
                throw new InvalidDataException(
                    $"Cannot archive '{candidate.Id}' because an archive shard already exists: {archivePath}.");
            }

            Save(archivePath, candidate);
            File.Delete(activePath);
            return;
        }

        Save(activePath, candidate);
    }

    private List<WorkItem> LoadArchivedTerminalWorkItems(
        RepositoryPaths paths,
        WorkItemRegistry active)
    {
        List<WorkItem> archivedItems = [];
        foreach (string archivePath in paths.ArchivedWorkItemRegistries)
        {
            WorkItemRegistry archive = Load<WorkItemRegistry>(archivePath);
            EnsureCompatible(active, archive, archivePath);
            archivedItems.AddRange(
                archive.WorkItems.Where(item => IsTerminal(item.Status)));
        }

        return archivedItems;
    }

    private void VerifyMigratedShardEquivalence(RepositoryPaths paths, WorkItemRegistry legacy)
    {
        List<WorkItem> migrated = [];
        Dictionary<string, string> sources = new(StringComparer.Ordinal);
        foreach (string path in paths.ActiveWorkItemShards)
        {
            AddShard(migrated, sources, path, expectedTerminal: false);
        }

        foreach (string path in paths.ArchivedWorkItemShards)
        {
            AddShard(migrated, sources, path, expectedTerminal: true);
        }

        if (migrated.Count != legacy.WorkItems.Count)
        {
            throw new InvalidDataException(
                $"Migrated shard count {migrated.Count} does not match legacy logical registry count {legacy.WorkItems.Count}.");
        }

        Dictionary<string, WorkItem> migratedMap = migrated
            .ToDictionary(item => item.Id, StringComparer.Ordinal);
        foreach (WorkItem legacyItem in legacy.WorkItems)
        {
            if (!migratedMap.TryGetValue(legacyItem.Id, out WorkItem? migratedItem) ||
                !Equivalent(legacyItem, migratedItem))
            {
                throw new InvalidDataException(
                    $"Migrated shard for '{legacyItem.Id}' is not equivalent to the legacy logical registry.");
            }
        }
    }

    private void AddShard(
        ICollection<WorkItem> items,
        IDictionary<string, string> sources,
        string path,
        bool expectedTerminal)
    {
        WorkItem item = LoadShard(path, expectedTerminal);
        if (sources.TryGetValue(item.Id, out string? existingPath))
        {
            throw new InvalidDataException(
                $"Duplicate work-item ID '{item.Id}' appears in both {existingPath} and {path}.");
        }

        sources.Add(item.Id, path);
        items.Add(item);
    }

    private WorkItem LoadShard(string path, bool expectedTerminal)
    {
        WorkItem item = Load<WorkItem>(path);
        string expectedFileName = $"{item.Id}.yaml";
        if (!string.Equals(Path.GetFileName(path), expectedFileName, StringComparison.Ordinal))
        {
            throw new InvalidDataException(
                $"Work-item shard {path} contains '{item.Id}' but must be named {expectedFileName}.");
        }

        bool terminal = IsTerminal(item.Status);
        if (terminal != expectedTerminal)
        {
            string expectedArea = expectedTerminal ? "archive" : "active";
            throw new InvalidDataException(
                $"Work item '{item.Id}' has status '{item.Status}' but is stored in the {expectedArea} shard area.");
        }

        return item;
    }

    private void EnsureNoUnexpectedShards(RepositoryPaths paths, IReadOnlySet<string> expectedIds)
    {
        foreach (string path in paths.ActiveWorkItemShards.Concat(paths.ArchivedWorkItemShards))
        {
            string fileId = Path.GetFileNameWithoutExtension(path);
            if (!expectedIds.Contains(fileId))
            {
                throw new InvalidDataException(
                    $"Existing work-item shard is not present in the legacy logical registry: {path}.");
            }
        }
    }

    private static void EnsureUniqueIds(IEnumerable<WorkItem> items, string source)
    {
        string[] duplicates = items
            .GroupBy(item => item.Id, StringComparer.Ordinal)
            .Where(group => group.Count() > 1)
            .Select(group => group.Key)
            .OrderBy(id => id, StringComparer.Ordinal)
            .ToArray();
        if (duplicates.Length > 0)
        {
            throw new InvalidDataException(
                $"Duplicate work-item IDs in {source}: {string.Join(", ", duplicates)}.");
        }
    }

    private bool Equivalent(WorkItem left, WorkItem right) =>
        string.Equals(
            _serializer.Serialize(left),
            _serializer.Serialize(right),
            StringComparison.Ordinal);

    private T Load<T>(string path)
    {
        using StreamReader reader = File.OpenText(path);
        return _deserializer.Deserialize<T>(reader)
            ?? throw new InvalidDataException($"Could not deserialize {path}.");
    }

    private void Save<T>(string path, T value) =>
        WriteAtomically(path, Serialize(value));

    private string Serialize<T>(T value) =>
        _serializer.Serialize(value).Replace("\r\n", "\n", StringComparison.Ordinal);

    private static bool IsTerminal(string status) =>
        status is "completed" or "cancelled";

    private static void EnsureCompatible(
        WorkItemRegistry active,
        WorkItemRegistry archive,
        string archivePath)
    {
        if (archive.SchemaVersion != active.SchemaVersion)
        {
            throw new InvalidDataException(
                $"Archived work-item registry {archivePath} uses schema version {archive.SchemaVersion}; expected {active.SchemaVersion}.");
        }

        if (!archive.AllowedStatuses.SequenceEqual(active.AllowedStatuses, StringComparer.Ordinal))
        {
            throw new InvalidDataException(
                $"Archived work-item registry {archivePath} does not use the active allowed-status set.");
        }
    }

    public static void WriteAtomically(string path, string content)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        string temporaryPath = $"{path}.{Guid.NewGuid():N}.tmp";
        File.WriteAllText(temporaryPath, content, new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
        File.Move(temporaryPath, path, overwrite: true);
    }
}
