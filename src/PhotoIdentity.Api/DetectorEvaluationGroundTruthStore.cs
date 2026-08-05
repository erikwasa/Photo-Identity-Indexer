using System.Data;
using System.Text.Json;

namespace PhotoIdentity.Api;

internal sealed class StoredDetectorGroundTruth
{
    public int SchemaVersion { get; init; } = DetectorEvaluationGroundTruthStore.CurrentSchemaVersion;
    public Guid BaselineSessionId { get; init; }
    public string Name { get; init; } = string.Empty;
    public DateTimeOffset FrozenAtUtc { get; init; }
    public List<StoredDetectorGroundTruthPhoto> Photos { get; init; } = [];
}

internal sealed class StoredDetectorGroundTruthPhoto
{
    public string BaselineRevisionId { get; init; } = string.Empty;
    public string RevisionSha256 { get; init; } = string.Empty;
    public string PhotoName { get; init; } = string.Empty;
    public string SampleId { get; init; } = string.Empty;
    public string SampleGroup { get; init; } = string.Empty;
    public string SourceGroup { get; init; } = string.Empty;
    public string PrimaryCategory { get; init; } = string.Empty;
    public int CountableFaces { get; init; }
    public List<StoredDetectorGroundTruthFace> Faces { get; init; } = [];
}

internal sealed class StoredDetectorGroundTruthFace
{
    public string Id { get; init; } = string.Empty;
    public double X { get; init; }
    public double Y { get; init; }
    public double Width { get; init; }
    public double Height { get; init; }
    public bool IsBackgroundUnknown { get; init; }
    public string Origin { get; init; } = string.Empty;
}

internal sealed class DetectorEvaluationGroundTruthStore
{
    public const int CurrentSchemaVersion = 1;

    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        WriteIndented = true,
    };

    private readonly string _rootPath;
    private readonly TimeProvider _timeProvider;
    private readonly SemaphoreSlim _gate = new(1, 1);

    public DetectorEvaluationGroundTruthStore(string rootPath, TimeProvider timeProvider)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(rootPath);
        ArgumentNullException.ThrowIfNull(timeProvider);
        _rootPath = Path.GetFullPath(rootPath);
        _timeProvider = timeProvider;
        Directory.CreateDirectory(_rootPath);
    }

    public async Task<IReadOnlyList<StoredDetectorGroundTruth>> ListAsync(
        CancellationToken cancellationToken = default)
    {
        await _gate.WaitAsync(cancellationToken);
        try
        {
            List<StoredDetectorGroundTruth> snapshots = [];
            foreach (string path in Directory.EnumerateFiles(_rootPath, "*.json", SearchOption.TopDirectoryOnly))
            {
                snapshots.Add(await LoadFileAsync(path, cancellationToken));
            }

            return snapshots
                .OrderByDescending(snapshot => snapshot.FrozenAtUtc)
                .ThenBy(snapshot => snapshot.Name, StringComparer.OrdinalIgnoreCase)
                .ToArray();
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task<StoredDetectorGroundTruth?> GetAsync(
        Guid baselineSessionId,
        CancellationToken cancellationToken = default)
    {
        if (baselineSessionId == Guid.Empty)
        {
            throw new ArgumentException("Baseline session identifier cannot be empty.", nameof(baselineSessionId));
        }

        await _gate.WaitAsync(cancellationToken);
        try
        {
            string path = SnapshotPath(baselineSessionId);
            return File.Exists(path)
                ? await LoadFileAsync(path, cancellationToken)
                : null;
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task<StoredDetectorGroundTruth> CreateAsync(
        Guid baselineSessionId,
        string name,
        IReadOnlyList<StoredDetectorGroundTruthPhoto> photos,
        CancellationToken cancellationToken = default)
    {
        if (baselineSessionId == Guid.Empty)
        {
            throw new ArgumentException("Baseline session identifier cannot be empty.", nameof(baselineSessionId));
        }

        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        ArgumentNullException.ThrowIfNull(photos);
        if (photos.Count == 0)
        {
            throw new ArgumentException("Ground truth must contain at least one photo.", nameof(photos));
        }

        foreach (StoredDetectorGroundTruthPhoto photo in photos)
        {
            if (photo.CountableFaces != photo.Faces.Count)
            {
                throw new ArgumentException(
                    $"Ground truth for '{photo.PhotoName}' contains {photo.Faces.Count} faces but the manifest declares {photo.CountableFaces}.",
                    nameof(photos));
            }
        }

        await _gate.WaitAsync(cancellationToken);
        try
        {
            string path = SnapshotPath(baselineSessionId);
            if (File.Exists(path))
            {
                return await LoadFileAsync(path, cancellationToken);
            }

            StoredDetectorGroundTruth snapshot = new()
            {
                BaselineSessionId = baselineSessionId,
                Name = name.Trim(),
                FrozenAtUtc = _timeProvider.GetUtcNow(),
                Photos = photos.ToList(),
            };
            await SaveFileAsync(path, snapshot, cancellationToken);
            return snapshot;
        }
        finally
        {
            _gate.Release();
        }
    }

    private string SnapshotPath(Guid baselineSessionId) =>
        Path.Combine(_rootPath, $"{baselineSessionId:D}.json");

    private static async Task<StoredDetectorGroundTruth> LoadFileAsync(
        string path,
        CancellationToken cancellationToken)
    {
        await using FileStream stream = new(
            path,
            FileMode.Open,
            FileAccess.Read,
            FileShare.Read,
            64 * 1024,
            FileOptions.Asynchronous | FileOptions.SequentialScan);
        StoredDetectorGroundTruth? snapshot = await JsonSerializer.DeserializeAsync<StoredDetectorGroundTruth>(
            stream,
            JsonOptions,
            cancellationToken);
        if (snapshot is null)
        {
            throw new DataException($"Detector ground-truth file '{Path.GetFileName(path)}' was empty.");
        }

        if (snapshot.SchemaVersion != CurrentSchemaVersion)
        {
            throw new DataException(
                $"Detector ground-truth schema {snapshot.SchemaVersion} is not supported by schema {CurrentSchemaVersion}.");
        }

        return snapshot;
    }

    private static async Task SaveFileAsync(
        string path,
        StoredDetectorGroundTruth snapshot,
        CancellationToken cancellationToken)
    {
        string temporaryPath = $"{path}.{Guid.NewGuid():N}.tmp";
        try
        {
            await using (FileStream stream = new(
                temporaryPath,
                FileMode.CreateNew,
                FileAccess.Write,
                FileShare.None,
                64 * 1024,
                FileOptions.Asynchronous | FileOptions.WriteThrough))
            {
                await JsonSerializer.SerializeAsync(stream, snapshot, JsonOptions, cancellationToken);
                await stream.FlushAsync(cancellationToken);
            }

            File.Move(temporaryPath, path, overwrite: false);
        }
        finally
        {
            if (File.Exists(temporaryPath))
            {
                File.Delete(temporaryPath);
            }
        }
    }
}

