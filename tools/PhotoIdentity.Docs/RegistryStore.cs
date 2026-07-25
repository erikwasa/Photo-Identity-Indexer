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

    public WorkItemRegistry LoadWorkItems(RepositoryPaths paths) =>
        Load<WorkItemRegistry>(paths.WorkItemsRegistry);

    public MilestoneRegistry LoadMilestones(RepositoryPaths paths) =>
        Load<MilestoneRegistry>(paths.MilestonesRegistry);

    public void SaveWorkItems(RepositoryPaths paths, WorkItemRegistry registry) =>
        Save(paths.WorkItemsRegistry, registry);

    public void SaveMilestones(RepositoryPaths paths, MilestoneRegistry registry) =>
        Save(paths.MilestonesRegistry, registry);

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

    public static void WriteAtomically(string path, string content)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        string temporaryPath = $"{path}.{Guid.NewGuid():N}.tmp";
        File.WriteAllText(temporaryPath, content, new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
        File.Move(temporaryPath, path, overwrite: true);
    }
}
