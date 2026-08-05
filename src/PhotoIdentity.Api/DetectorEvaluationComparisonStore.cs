using System.Data;
using System.Text.Json;

namespace PhotoIdentity.Api;

internal sealed class DetectorEvaluationComparisonStore
{
    public const int CurrentSchemaVersion = 1;

    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        WriteIndented = true,
    };

    private readonly string _rootPath;
    private readonly TimeProvider _timeProvider;
    private readonly SemaphoreSlim _gate = new(1, 1);

    public DetectorEvaluationComparisonStore(string rootPath, TimeProvider timeProvider)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(rootPath);
        ArgumentNullException.ThrowIfNull(timeProvider);
        _rootPath = Path.GetFullPath(rootPath);
        _timeProvider = timeProvider;
        Directory.CreateDirectory(_rootPath);
    }

    public async Task<IReadOnlyList<StoredDetectorEvaluationComparison>> ListAsync(
        CancellationToken cancellationToken = default)
    {
        await _gate.WaitAsync(cancellationToken);
        try
        {
            List<StoredDetectorEvaluationComparison> comparisons = [];
            foreach (string path in Directory.EnumerateFiles(_rootPath, "*.json", SearchOption.TopDirectoryOnly))
            {
                comparisons.Add(await LoadFileAsync(path, cancellationToken));
            }

            return comparisons
                .OrderByDescending(comparison => comparison.UpdatedAtUtc)
                .ThenBy(comparison => comparison.Name, StringComparer.OrdinalIgnoreCase)
                .ToArray();
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task<StoredDetectorEvaluationComparison?> GetAsync(
        Guid comparisonId,
        CancellationToken cancellationToken = default)
    {
        if (comparisonId == Guid.Empty)
        {
            throw new ArgumentException("Comparison identifier cannot be empty.", nameof(comparisonId));
        }

        await _gate.WaitAsync(cancellationToken);
        try
        {
            string path = ComparisonPath(comparisonId);
            return File.Exists(path)
                ? await LoadFileAsync(path, cancellationToken)
                : null;
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task<StoredDetectorEvaluationComparison> CreateAsync(
        DetectorEvaluationComparisonSeed seed,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(seed);
        ArgumentException.ThrowIfNullOrWhiteSpace(seed.Name);
        if (seed.Photos.Count == 0)
        {
            throw new ArgumentException("A comparison must contain at least one photo.", nameof(seed));
        }

        DateTimeOffset now = _timeProvider.GetUtcNow();
        StoredDetectorEvaluationComparison comparison = new()
        {
            Id = Guid.NewGuid(),
            Name = seed.Name.Trim(),
            BaselineSessionId = seed.BaselineSessionId,
            BaselineName = seed.BaselineName,
            GroundTruthFrozenAtUtc = seed.GroundTruthFrozenAtUtc,
            CandidateProcessingRunId = seed.CandidateProcessingRunId,
            IouThreshold = seed.IouThreshold,
            CreatedAtUtc = now,
            UpdatedAtUtc = now,
            Photos = seed.Photos.ToList(),
        };

        await _gate.WaitAsync(cancellationToken);
        try
        {
            await SaveFileAsync(comparison, cancellationToken);
            return comparison;
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task<StoredDetectorEvaluationComparison?> SavePhotoCorrectionAsync(
        Guid comparisonId,
        string candidateRevisionId,
        DetectorEvaluationComparisonCorrectionUpdate update,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(candidateRevisionId);
        ArgumentNullException.ThrowIfNull(update);

        await _gate.WaitAsync(cancellationToken);
        try
        {
            string path = ComparisonPath(comparisonId);
            if (!File.Exists(path))
            {
                return null;
            }

            StoredDetectorEvaluationComparison comparison = await LoadFileAsync(path, cancellationToken);
            StoredDetectorEvaluationComparisonPhoto? photo = comparison.Photos.FirstOrDefault(value =>
                string.Equals(value.CandidateRevisionId, candidateRevisionId, StringComparison.OrdinalIgnoreCase));
            if (photo is null)
            {
                throw new KeyNotFoundException("The candidate photo does not belong to this comparison.");
            }

            ValidateCorrection(photo, update);
            photo.Correction = new StoredDetectorEvaluationManualCorrection
            {
                Matches = update.Matches.Select(match => new StoredDetectorEvaluationManualMatch
                {
                    GroundTruthFaceId = match.GroundTruthFaceId,
                    CandidateDetectionId = match.CandidateDetectionId,
                }).ToList(),
                FalseCandidateDetectionIds = update.FalseCandidateDetectionIds.ToList(),
                DuplicateCandidateDetectionIds = update.DuplicateCandidateDetectionIds.ToList(),
                MissedGroundTruthFaceIds = update.MissedGroundTruthFaceIds.ToList(),
                Notes = NormalizeOptional(update.Notes),
            };
            comparison.UpdatedAtUtc = _timeProvider.GetUtcNow();
            await SaveFileAsync(comparison, cancellationToken);
            return comparison;
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task<StoredDetectorEvaluationComparison?> SaveGateAssessmentAsync(
        Guid comparisonId,
        bool? materialCategoryFailure,
        string? notes,
        CancellationToken cancellationToken = default)
    {
        await _gate.WaitAsync(cancellationToken);
        try
        {
            string path = ComparisonPath(comparisonId);
            if (!File.Exists(path))
            {
                return null;
            }

            StoredDetectorEvaluationComparison comparison = await LoadFileAsync(path, cancellationToken);
            comparison.MaterialCategoryFailure = materialCategoryFailure;
            comparison.GateNotes = NormalizeOptional(notes);
            comparison.UpdatedAtUtc = _timeProvider.GetUtcNow();
            await SaveFileAsync(comparison, cancellationToken);
            return comparison;
        }
        finally
        {
            _gate.Release();
        }
    }

    private static void ValidateCorrection(
        StoredDetectorEvaluationComparisonPhoto photo,
        DetectorEvaluationComparisonCorrectionUpdate update)
    {
        HashSet<string> exceptionGroundTruthIds = photo.ExceptionComponents
            .SelectMany(component => component.GroundTruthFaceIds)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        HashSet<string> exceptionCandidateIds = photo.ExceptionComponents
            .SelectMany(component => component.CandidateDetectionIds)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        HashSet<string> usedGroundTruthIds = new(StringComparer.OrdinalIgnoreCase);
        HashSet<string> usedCandidateIds = new(StringComparer.OrdinalIgnoreCase);
        foreach (StoredDetectorEvaluationManualMatch match in update.Matches)
        {
            if (!exceptionGroundTruthIds.Contains(match.GroundTruthFaceId) ||
                !exceptionCandidateIds.Contains(match.CandidateDetectionId))
            {
                throw new ArgumentException(
                    "Manual matches may reference only ground-truth faces and candidate detections surfaced as exceptions.",
                    nameof(update));
            }

            if (!usedGroundTruthIds.Add(match.GroundTruthFaceId) ||
                !usedCandidateIds.Add(match.CandidateDetectionId))
            {
                throw new ArgumentException("Manual matches must be one-to-one.", nameof(update));
            }
        }

        AddUnique(update.FalseCandidateDetectionIds, exceptionCandidateIds, usedCandidateIds, "false candidate detection", update);
        AddUnique(update.DuplicateCandidateDetectionIds, exceptionCandidateIds, usedCandidateIds, "duplicate candidate detection", update);
        AddUnique(update.MissedGroundTruthFaceIds, exceptionGroundTruthIds, usedGroundTruthIds, "missed ground-truth face", update);
    }

    private static void AddUnique(
        IReadOnlyList<string> values,
        HashSet<string> allowed,
        HashSet<string> used,
        string label,
        DetectorEvaluationComparisonCorrectionUpdate update)
    {
        foreach (string value in values)
        {
            if (!allowed.Contains(value))
            {
                throw new ArgumentException($"Unknown {label} '{value}'.", nameof(update));
            }

            if (!used.Add(value))
            {
                throw new ArgumentException($"{label} '{value}' was resolved more than once.", nameof(update));
            }
        }
    }

    private string ComparisonPath(Guid comparisonId) => Path.Combine(_rootPath, $"{comparisonId:D}.json");

    private static string? NormalizeOptional(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    private static async Task<StoredDetectorEvaluationComparison> LoadFileAsync(
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
        StoredDetectorEvaluationComparison? comparison = await JsonSerializer.DeserializeAsync<StoredDetectorEvaluationComparison>(
            stream,
            JsonOptions,
            cancellationToken);
        if (comparison is null)
        {
            throw new DataException($"Detector comparison file '{Path.GetFileName(path)}' was empty.");
        }

        if (comparison.SchemaVersion != CurrentSchemaVersion)
        {
            throw new DataException(
                $"Detector comparison schema {comparison.SchemaVersion} is not supported by schema {CurrentSchemaVersion}.");
        }

        return comparison;
    }

    private async Task SaveFileAsync(
        StoredDetectorEvaluationComparison comparison,
        CancellationToken cancellationToken)
    {
        string path = ComparisonPath(comparison.Id);
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
                await JsonSerializer.SerializeAsync(stream, comparison, JsonOptions, cancellationToken);
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
