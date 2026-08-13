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
        Load<WorkItemRegistry>(paths.WorkItemsRegistry);

    public WorkItemRegistry LoadWorkItems(RepositoryPaths paths)
    {
        WorkItemRegistry active = LoadActiveWorkItems(paths);
        WorkItemRegistry combined = new()
        {
            SchemaVersion = active.SchemaVersion,
            AllowedStatuses = [.. active.AllowedStatuses],
        };

        combined.WorkItems.AddRange(LoadArchivedTerminalWorkItems(paths, active));
        combined.WorkItems.AddRange(active.WorkItems);
        return combined;
    }

    public MilestoneRegistry LoadMilestones(RepositoryPaths paths) =>
        Load<MilestoneRegistry>(paths.MilestonesRegistry);

    public void SaveWorkItems(RepositoryPaths paths, WorkItemRegistry registry)
    {
        WorkItemRegistry activeBeforeWrite = LoadActiveWorkItems(paths);
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

    public void SaveMilestones(RepositoryPaths paths, MilestoneRegistry registry) =>
        Save(paths.MilestonesRegistry, registry);

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
                archive.WorkItems.Where(item => item.Status is "completed" or "cancelled"));
        }

        return archivedItems;
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

    private void Save<T>(string path, T value)
    {
        string content = _serializer.Serialize(value).Replace("\r\n", "\n", StringComparison.Ordinal);
        WriteAtomically(path, content);
    }

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
