using System.Diagnostics;

namespace PhotoIdentity.Worker;

public static class ArchiveThroughputMetricNames
{
    public const string Synchronization = "synchronization";
    public const string OneDriveWait = "onedrive-wait";
    public const string ActiveLoopDelay = "active-loop-delay";
    public const string SourceVerification = "source-verification";
    public const string SourceVerificationHash = "source-verification-hash";
    public const string OriginalVerificationHash = "original-verification-hash";
    public const string MetadataInspection = "metadata-inspection";
    public const string AnalysisSessionInitialization = "analysis-session-initialization";
    public const string AnalysisSessionLifetime = "analysis-session-lifetime";
    public const string AnalysisSourceHash = "analysis-source-hash";
    public const string ImageDecode = "image-decode";
    public const string FaceDetection = "face-detection";
    public const string FaceAlignment = "face-alignment";
    public const string FaceEmbedding = "face-embedding";
    public const string FacePersistence = "face-persistence";
    public const string AnalysisResultPersistence = "analysis-result-persistence";
    public const string ReviewProxyGeneration = "review-proxy-generation";
    public const string FaceReviewDerivativeGeneration = "face-review-derivative-generation";
    public const string HydrationRequest = "hydration-request";
    public const string ReleaseRequest = "release-request";

    public const string AdvanceInvocations = "advance-invocations";
    public const string AnalysisAttempts = "analysis-attempts";
    public const string ModelSessionInitializations = "model-session-initializations";
    public const string ModelSessionReuses = "model-session-reuses";
    public const string FacesDetected = "faces-detected";
    public const string MetadataInspections = "metadata-inspections";
    public const string SourceVerificationsCompleted = "source-verifications-completed";
    public const string ReviewProxiesGenerated = "review-proxies-generated";
    public const string FaceReviewDerivativeRevisions = "face-review-derivative-revisions";
    public const string HydrationRequests = "hydration-requests";
    public const string ReleaseRequests = "release-requests";
    public const string ArchiveErrors = "archive-errors";

    public const string SourceVerificationHashKind = "source-verification";
    public const string OriginalStatusHashKind = "original-status";
    public const string OriginalOpenHashKind = "original-open";
    public const string AnalysisHashKind = "analysis";
    public const string SynchronizationHashKind = "synchronization";
}

public sealed record ArchiveThroughputStageSnapshot(
    string Name,
    long Count,
    double TotalMilliseconds,
    double AverageMilliseconds,
    double MaxMilliseconds);

public sealed record ArchiveThroughputCounterSnapshot(
    string Name,
    long Value);

public sealed record ArchiveThroughputHashReadSnapshot(
    string Kind,
    long Count,
    long Bytes,
    int SubjectCount,
    double AverageReadsPerSubject,
    long MaxReadsPerSubject);

public sealed record ArchiveThroughputSnapshot(
    long Generation,
    DateTimeOffset ResetAtUtc,
    DateTimeOffset CapturedAtUtc,
    IReadOnlyList<ArchiveThroughputStageSnapshot> Stages,
    IReadOnlyList<ArchiveThroughputCounterSnapshot> Counters,
    IReadOnlyList<ArchiveThroughputHashReadSnapshot> HashReads);

/// <summary>
/// Process-local, privacy-safe aggregate instrumentation for WI-0076 benchmarks.
/// Opaque asset/revision keys are retained only in memory to calculate aggregate hash-read
/// distributions and are never returned by the snapshot contract.
/// </summary>
public sealed class ArchiveThroughputMetrics
{
    private readonly object _gate = new();
    private readonly TimeProvider _timeProvider;
    private readonly Dictionary<string, StageAccumulator> _stages = new(StringComparer.Ordinal);
    private readonly Dictionary<string, long> _counters = new(StringComparer.Ordinal);
    private readonly Dictionary<string, HashAccumulator> _hashReads = new(StringComparer.Ordinal);
    private long _generation;
    private DateTimeOffset _resetAtUtc;

    public ArchiveThroughputMetrics(TimeProvider? timeProvider = null)
    {
        _timeProvider = timeProvider ?? TimeProvider.System;
        _resetAtUtc = _timeProvider.GetUtcNow();
    }

    public IDisposable Measure(string name)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        return new MeasurementScope(this, name, Stopwatch.GetTimestamp());
    }

    public void RecordCounter(string name, long delta = 1)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        lock (_gate)
        {
            _counters.TryGetValue(name, out long current);
            _counters[name] = checked(current + delta);
        }
    }

    public void RecordHashRead(string kind, string subjectKey, long bytes)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(kind);
        ArgumentException.ThrowIfNullOrWhiteSpace(subjectKey);
        ArgumentOutOfRangeException.ThrowIfNegative(bytes);

        lock (_gate)
        {
            if (!_hashReads.TryGetValue(kind, out HashAccumulator? accumulator))
            {
                accumulator = new HashAccumulator();
                _hashReads.Add(kind, accumulator);
            }

            accumulator.Count++;
            accumulator.Bytes = checked(accumulator.Bytes + bytes);
            accumulator.SubjectReads.TryGetValue(subjectKey, out long subjectReads);
            accumulator.SubjectReads[subjectKey] = subjectReads + 1;
        }
    }

    public void RecordAggregateHashReads(
        string kind,
        long count,
        long bytes,
        int subjectCount,
        long maxReadsPerSubject = 1)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(kind);
        ArgumentOutOfRangeException.ThrowIfNegative(count);
        ArgumentOutOfRangeException.ThrowIfNegative(bytes);
        ArgumentOutOfRangeException.ThrowIfNegative(subjectCount);
        ArgumentOutOfRangeException.ThrowIfNegative(maxReadsPerSubject);

        if (count == 0)
        {
            return;
        }

        lock (_gate)
        {
            if (!_hashReads.TryGetValue(kind, out HashAccumulator? accumulator))
            {
                accumulator = new HashAccumulator();
                _hashReads.Add(kind, accumulator);
            }

            accumulator.Count = checked(accumulator.Count + count);
            accumulator.Bytes = checked(accumulator.Bytes + bytes);
            accumulator.AnonymousSubjectCount = checked(accumulator.AnonymousSubjectCount + subjectCount);
            accumulator.AnonymousMaxReadsPerSubject = Math.Max(
                accumulator.AnonymousMaxReadsPerSubject,
                maxReadsPerSubject);
        }
    }

    public ArchiveThroughputSnapshot GetSnapshot()
    {
        lock (_gate)
        {
            DateTimeOffset capturedAtUtc = _timeProvider.GetUtcNow();
            ArchiveThroughputStageSnapshot[] stages = _stages
                .OrderBy(value => value.Key, StringComparer.Ordinal)
                .Select(value => new ArchiveThroughputStageSnapshot(
                    value.Key,
                    value.Value.Count,
                    value.Value.TotalMilliseconds,
                    value.Value.Count == 0 ? 0d : value.Value.TotalMilliseconds / value.Value.Count,
                    value.Value.MaxMilliseconds))
                .ToArray();
            ArchiveThroughputCounterSnapshot[] counters = _counters
                .OrderBy(value => value.Key, StringComparer.Ordinal)
                .Select(value => new ArchiveThroughputCounterSnapshot(value.Key, value.Value))
                .ToArray();
            ArchiveThroughputHashReadSnapshot[] hashReads = _hashReads
                .OrderBy(value => value.Key, StringComparer.Ordinal)
                .Select(value =>
                {
                    int explicitSubjectCount = value.Value.SubjectReads.Count;
                    int subjectCount = explicitSubjectCount + value.Value.AnonymousSubjectCount;
                    long explicitMaxReads = explicitSubjectCount == 0
                        ? 0
                        : value.Value.SubjectReads.Values.Max();
                    long maxReadsPerSubject = Math.Max(
                        explicitMaxReads,
                        value.Value.AnonymousMaxReadsPerSubject);
                    return new ArchiveThroughputHashReadSnapshot(
                        value.Key,
                        value.Value.Count,
                        value.Value.Bytes,
                        subjectCount,
                        subjectCount == 0 ? 0d : (double)value.Value.Count / subjectCount,
                        maxReadsPerSubject);
                })
                .ToArray();

            return new ArchiveThroughputSnapshot(
                _generation,
                _resetAtUtc,
                capturedAtUtc,
                stages,
                counters,
                hashReads);
        }
    }

    public ArchiveThroughputSnapshot Reset()
    {
        lock (_gate)
        {
            _stages.Clear();
            _counters.Clear();
            _hashReads.Clear();
            _generation++;
            _resetAtUtc = _timeProvider.GetUtcNow();
            return new ArchiveThroughputSnapshot(
                _generation,
                _resetAtUtc,
                _resetAtUtc,
                [],
                [],
                []);
        }
    }

    private void RecordStage(string name, TimeSpan elapsed)
    {
        lock (_gate)
        {
            if (!_stages.TryGetValue(name, out StageAccumulator? accumulator))
            {
                accumulator = new StageAccumulator();
                _stages.Add(name, accumulator);
            }

            double milliseconds = elapsed.TotalMilliseconds;
            accumulator.Count++;
            accumulator.TotalMilliseconds += milliseconds;
            accumulator.MaxMilliseconds = Math.Max(accumulator.MaxMilliseconds, milliseconds);
        }
    }

    private sealed class MeasurementScope : IDisposable
    {
        private readonly ArchiveThroughputMetrics _owner;
        private readonly string _name;
        private readonly long _startedTimestamp;
        private int _disposed;

        public MeasurementScope(
            ArchiveThroughputMetrics owner,
            string name,
            long startedTimestamp)
        {
            _owner = owner;
            _name = name;
            _startedTimestamp = startedTimestamp;
        }

        public void Dispose()
        {
            if (Interlocked.Exchange(ref _disposed, 1) != 0)
            {
                return;
            }

            _owner.RecordStage(_name, Stopwatch.GetElapsedTime(_startedTimestamp));
        }
    }

    private sealed class StageAccumulator
    {
        public long Count { get; set; }
        public double TotalMilliseconds { get; set; }
        public double MaxMilliseconds { get; set; }
    }

    private sealed class HashAccumulator
    {
        public long Count { get; set; }
        public long Bytes { get; set; }
        public int AnonymousSubjectCount { get; set; }
        public long AnonymousMaxReadsPerSubject { get; set; }
        public Dictionary<string, long> SubjectReads { get; } = new(StringComparer.Ordinal);
    }
}
