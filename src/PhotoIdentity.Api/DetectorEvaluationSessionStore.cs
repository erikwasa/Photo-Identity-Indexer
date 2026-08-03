using System.Data;
using System.Text.Json;
using PhotoIdentity.Core.Geometry;

namespace PhotoIdentity.Api;

internal static class DetectorEvaluationDispositions
{
    public const string Correct = "correct";
    public const string BackgroundUnknown = "background-unknown";
    public const string FalseDetection = "false";
    public const string Duplicate = "duplicate";

    public static bool IsValid(string? value) => value is
        Correct or BackgroundUnknown or FalseDetection or Duplicate;

    public static bool CountsAsCorrect(string? value) => value is Correct or BackgroundUnknown;
}

internal sealed record DetectorEvaluationSessionSeed(
    string Name,
    string ProcessingRunId,
    IReadOnlyList<DetectorEvaluationPhotoSeed> Photos);

internal sealed record DetectorEvaluationPhotoSeed(
    string RevisionId,
    string RevisionSha256,
    string PhotoName,
    string SampleId,
    string SampleGroup,
    string SourceGroup,
    string PrimaryCategory,
    int CountableFaces,
    IReadOnlyList<string> DetectionIds);

internal sealed record DetectorEvaluationPhotoReviewUpdate(
    IReadOnlyList<DetectorEvaluationDetectionJudgementUpdate> DetectionJudgements,
    IReadOnlyList<DetectorEvaluationMissedFaceUpdate> MissedFaces,
    string? MissReason,
    string? Notes);

internal sealed record DetectorEvaluationDetectionJudgementUpdate(
    string DetectionId,
    string? Disposition);

internal sealed record DetectorEvaluationMissedFaceUpdate(
    string Id,
    NormalizedBoundingBox BoundingBox);

internal sealed class StoredDetectorEvaluationSession
{
    public int SchemaVersion { get; init; } = DetectorEvaluationSessionStore.CurrentSchemaVersion;
    public Guid Id { get; init; }
    public string Name { get; init; } = string.Empty;
    public string ProcessingRunId { get; init; } = string.Empty;
    public DateTimeOffset CreatedAtUtc { get; init; }
    public DateTimeOffset UpdatedAtUtc { get; set; }
    public List<StoredDetectorEvaluationPhoto> Photos { get; init; } = [];
}

internal sealed class StoredDetectorEvaluationPhoto
{
    public string RevisionId { get; init; } = string.Empty;
    public string RevisionSha256 { get; init; } = string.Empty;
    public string PhotoName { get; init; } = string.Empty;
    public string SampleId { get; init; } = string.Empty;
    public string SampleGroup { get; init; } = string.Empty;
    public string SourceGroup { get; init; } = string.Empty;
    public string PrimaryCategory { get; init; } = string.Empty;
    public int CountableFaces { get; init; }
    public List<StoredDetectorEvaluationDetection> Detections { get; init; } = [];
    public List<StoredDetectorEvaluationMissedFace> MissedFaces { get; set; } = [];
    public string? MissReason { get; set; }
    public string? Notes { get; set; }
}

internal sealed class StoredDetectorEvaluationDetection
{
    public string Id { get; init; } = string.Empty;
    public string? Disposition { get; set; }
}

internal sealed class StoredDetectorEvaluationMissedFace
{
    public string Id { get; init; } = string.Empty;
    public double X { get; init; }
    public double Y { get; init; }
    public double Width { get; init; }
    public double Height { get; init; }
}

/// <summary>
/// Persists private detector-evaluation ground truth outside the canonical identity catalogue.
/// </summary>
internal sealed class DetectorEvaluationSessionStore
{
    public const int CurrentSchemaVersion = 1;

    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        WriteIndented = true,
    };

    private readonly string _rootPath;
    private readonly TimeProvider _timeProvider;
    private readonly SemaphoreSlim _gate = new(1, 1);

    public DetectorEvaluationSessionStore(string rootPath, TimeProvider timeProvider)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(rootPath);
        ArgumentNullException.ThrowIfNull(timeProvider);

        _rootPath = Path.GetFullPath(rootPath);
        _timeProvider = timeProvider;
        Directory.CreateDirectory(_rootPath);
    }

    public async Task<IReadOnlyList<StoredDetectorEvaluationSession>> ListAsync(
        string? processingRunId = null,
        CancellationToken cancellationToken = default)
    {
        await _gate.WaitAsync(cancellationToken);
        try
        {
            List<StoredDetectorEvaluationSession> sessions = [];
            foreach (string path in Directory.EnumerateFiles(_rootPath, "*.json", SearchOption.TopDirectoryOnly))
            {
                StoredDetectorEvaluationSession session = await LoadFileAsync(path, cancellationToken);
                if (string.IsNullOrWhiteSpace(processingRunId) ||
                    string.Equals(session.ProcessingRunId, processingRunId, StringComparison.OrdinalIgnoreCase))
                {
                    sessions.Add(session);
                }
            }

            return sessions
                .OrderByDescending(session => session.UpdatedAtUtc)
                .ThenBy(session => session.Name, StringComparer.OrdinalIgnoreCase)
                .ToArray();
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task<StoredDetectorEvaluationSession?> GetAsync(
        Guid sessionId,
        CancellationToken cancellationToken = default)
    {
        if (sessionId == Guid.Empty)
        {
            throw new ArgumentException("Session identifier cannot be empty.", nameof(sessionId));
        }

        await _gate.WaitAsync(cancellationToken);
        try
        {
            string path = SessionPath(sessionId);
            return File.Exists(path)
                ? await LoadFileAsync(path, cancellationToken)
                : null;
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task<StoredDetectorEvaluationSession> CreateAsync(
        DetectorEvaluationSessionSeed seed,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(seed);
        if (string.IsNullOrWhiteSpace(seed.Name))
        {
            throw new ArgumentException("The evaluation session name is required.", nameof(seed));
        }

        if (seed.Photos.Count == 0)
        {
            throw new ArgumentException("The evaluation session must contain at least one photo.", nameof(seed));
        }

        DateTimeOffset now = _timeProvider.GetUtcNow();
        StoredDetectorEvaluationSession session = new()
        {
            Id = Guid.NewGuid(),
            Name = seed.Name.Trim(),
            ProcessingRunId = seed.ProcessingRunId,
            CreatedAtUtc = now,
            UpdatedAtUtc = now,
            Photos = seed.Photos.Select(photo => new StoredDetectorEvaluationPhoto
            {
                RevisionId = photo.RevisionId,
                RevisionSha256 = photo.RevisionSha256,
                PhotoName = photo.PhotoName,
                SampleId = photo.SampleId,
                SampleGroup = photo.SampleGroup,
                SourceGroup = photo.SourceGroup,
                PrimaryCategory = photo.PrimaryCategory,
                CountableFaces = photo.CountableFaces,
                Detections = photo.DetectionIds.Select(id => new StoredDetectorEvaluationDetection
                {
                    Id = id,
                }).ToList(),
            }).ToList(),
        };

        await _gate.WaitAsync(cancellationToken);
        try
        {
            await SaveFileAsync(session, cancellationToken);
            return session;
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task<StoredDetectorEvaluationSession?> SavePhotoAsync(
        Guid sessionId,
        string revisionId,
        DetectorEvaluationPhotoReviewUpdate update,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(revisionId);
        ArgumentNullException.ThrowIfNull(update);

        await _gate.WaitAsync(cancellationToken);
        try
        {
            string path = SessionPath(sessionId);
            if (!File.Exists(path))
            {
                return null;
            }

            StoredDetectorEvaluationSession session = await LoadFileAsync(path, cancellationToken);
            StoredDetectorEvaluationPhoto? photo = session.Photos.FirstOrDefault(value =>
                string.Equals(value.RevisionId, revisionId, StringComparison.OrdinalIgnoreCase));
            if (photo is null)
            {
                throw new KeyNotFoundException("The evaluation photo does not belong to this session.");
            }

            Dictionary<string, string?> judgements = new(StringComparer.OrdinalIgnoreCase);
            foreach (DetectorEvaluationDetectionJudgementUpdate judgement in update.DetectionJudgements)
            {
                if (!judgements.TryAdd(judgement.DetectionId, judgement.Disposition))
                {
                    throw new ArgumentException("Each detection may be classified only once.", nameof(update));
                }

                if (judgement.Disposition is not null &&
                    !DetectorEvaluationDispositions.IsValid(judgement.Disposition))
                {
                    throw new ArgumentException(
                        $"Unknown detector-evaluation disposition '{judgement.Disposition}'.",
                        nameof(update));
                }
            }

            HashSet<string> expectedDetectionIds = photo.Detections
                .Select(detection => detection.Id)
                .ToHashSet(StringComparer.OrdinalIgnoreCase);
            if (judgements.Count != expectedDetectionIds.Count ||
                judgements.Keys.Any(id => !expectedDetectionIds.Contains(id)))
            {
                throw new ArgumentException(
                    "The photo review must include every persisted detection exactly once.",
                    nameof(update));
            }

            foreach (StoredDetectorEvaluationDetection detection in photo.Detections)
            {
                detection.Disposition = judgements[detection.Id];
            }

            HashSet<string> missedIds = new(StringComparer.OrdinalIgnoreCase);
            photo.MissedFaces = update.MissedFaces.Select(missed =>
            {
                if (!Guid.TryParse(missed.Id, out Guid parsedId) || parsedId == Guid.Empty)
                {
                    throw new ArgumentException("Every missed face must have a valid identifier.", nameof(update));
                }

                if (!missedIds.Add(missed.Id))
                {
                    throw new ArgumentException("Missed-face identifiers must be unique.", nameof(update));
                }

                return new StoredDetectorEvaluationMissedFace
                {
                    Id = parsedId.ToString("D"),
                    X = missed.BoundingBox.X,
                    Y = missed.BoundingBox.Y,
                    Width = missed.BoundingBox.Width,
                    Height = missed.BoundingBox.Height,
                };
            }).ToList();
            photo.MissReason = NormalizeOptional(update.MissReason);
            photo.Notes = NormalizeOptional(update.Notes);
            session.UpdatedAtUtc = _timeProvider.GetUtcNow();

            await SaveFileAsync(session, cancellationToken);
            return session;
        }
        finally
        {
            _gate.Release();
        }
    }

    private string SessionPath(Guid sessionId) => Path.Combine(_rootPath, $"{sessionId:D}.json");

    private static string? NormalizeOptional(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    private static async Task<StoredDetectorEvaluationSession> LoadFileAsync(
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
        StoredDetectorEvaluationSession? session = await JsonSerializer.DeserializeAsync<StoredDetectorEvaluationSession>(
            stream,
            JsonOptions,
            cancellationToken);
        if (session is null)
        {
            throw new DataException($"Detector-evaluation session file '{Path.GetFileName(path)}' was empty.");
        }

        if (session.SchemaVersion != CurrentSchemaVersion)
        {
            throw new DataException(
                $"Detector-evaluation session schema {session.SchemaVersion} is not supported by schema {CurrentSchemaVersion}.");
        }

        return session;
    }

    private async Task SaveFileAsync(
        StoredDetectorEvaluationSession session,
        CancellationToken cancellationToken)
    {
        string path = SessionPath(session.Id);
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
                await JsonSerializer.SerializeAsync(stream, session, JsonOptions, cancellationToken);
                await stream.FlushAsync(cancellationToken);
            }

            File.Move(temporaryPath, path, overwrite: true);
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
