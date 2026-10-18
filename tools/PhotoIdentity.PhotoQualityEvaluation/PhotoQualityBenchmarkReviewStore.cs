namespace PhotoIdentity.PhotoQualityEvaluation;

internal sealed class PhotoQualityBenchmarkReviewStore
{
    private static readonly HashSet<string> AllowedLevels =
    [
        "very-low",
        "low",
        "normal",
        "high",
        "very-high",
        "unsure",
    ];

    private readonly string _manifestPath;
    private readonly string _proxyRoot;
    private readonly SemaphoreSlim _gate = new(1, 1);
    private readonly string[] _reviewOrder;
    private PhotoQualityBenchmarkExport _manifest;

    private PhotoQualityBenchmarkReviewStore(
        string manifestPath,
        string proxyRoot,
        PhotoQualityBenchmarkExport manifest)
    {
        _manifestPath = Path.GetFullPath(manifestPath);
        _proxyRoot = Path.GetFullPath(proxyRoot);
        _manifest = manifest;
        _reviewOrder = manifest.Photos
            .OrderBy(
                photo => PhotoQualityBenchmarkFile.StableBlindOrderKey(
                    manifest.BenchmarkId,
                    photo.RevisionId),
                StringComparer.Ordinal)
            .Select(photo => photo.RevisionId)
            .ToArray();
    }

    public static async Task<PhotoQualityBenchmarkReviewStore> OpenAsync(
        string manifestPath,
        string proxyRoot)
    {
        if (!Directory.Exists(proxyRoot))
        {
            throw new DirectoryNotFoundException(
                $"Review-proxy root does not exist: {Path.GetFullPath(proxyRoot)}");
        }

        PhotoQualityBenchmarkExport manifest =
            await PhotoQualityBenchmarkFile.LoadAsync(manifestPath);
        return new PhotoQualityBenchmarkReviewStore(manifestPath, proxyRoot, manifest);
    }

    public PhotoQualityBlindReviewState GetState(int requestedIndex)
    {
        int index = Math.Clamp(requestedIndex, 0, _reviewOrder.Length - 1);
        PhotoQualityBenchmarkPhoto photo = Find(_reviewOrder[index]);
        int reviewed = _manifest.Photos.Count(
            candidate => !string.IsNullOrWhiteSpace(candidate.Review.QualityLevel));

        return new PhotoQualityBlindReviewState(
            _manifest.BenchmarkId,
            index,
            _reviewOrder.Length,
            reviewed,
            _reviewOrder.Length - reviewed,
            FindNextUnreviewed(index),
            photo.RevisionId,
            photo.Review.QualityLevel,
            photo.Review.PersonallyImportant,
            photo.Review.Notes);
    }

    public async Task<PhotoQualityBlindReviewState> SaveReviewAsync(
        string revisionId,
        PhotoQualityBlindReviewUpdate update)
    {
        ArgumentNullException.ThrowIfNull(update);
        string level = NormalizeLevel(update.QualityLevel);

        await _gate.WaitAsync();
        try
        {
            PhotoQualityBenchmarkPhoto[] photos = _manifest.Photos.ToArray();
            int photoIndex = Array.FindIndex(
                photos,
                photo => string.Equals(
                    photo.RevisionId,
                    revisionId,
                    StringComparison.OrdinalIgnoreCase));
            if (photoIndex < 0)
            {
                throw new KeyNotFoundException($"Benchmark revision '{revisionId}' was not found.");
            }

            PhotoQualityBenchmarkPhoto current = photos[photoIndex];
            photos[photoIndex] = current with
            {
                Review = new PhotoQualityBenchmarkHumanReview(
                    level,
                    update.PersonallyImportant,
                    NormalizeNotes(update.Notes)),
            };
            _manifest = _manifest with { Photos = photos };
            await PhotoQualityBenchmarkFile.SaveAtomicAsync(
                _manifestPath,
                _manifest,
                backupSuffix: ".before-review.bak");
        }
        finally
        {
            _gate.Release();
        }

        return GetState(IndexOf(revisionId));
    }

    public async Task<PhotoQualityBlindReviewState> ClearReviewAsync(string revisionId)
    {
        await _gate.WaitAsync();
        try
        {
            PhotoQualityBenchmarkPhoto[] photos = _manifest.Photos.ToArray();
            int index = Array.FindIndex(
                photos,
                photo => string.Equals(
                    photo.RevisionId,
                    revisionId,
                    StringComparison.OrdinalIgnoreCase));
            if (index < 0)
            {
                throw new KeyNotFoundException($"Benchmark revision '{revisionId}' was not found.");
            }

            photos[index] = photos[index] with
            {
                Review = new PhotoQualityBenchmarkHumanReview(null, null, null),
            };
            _manifest = _manifest with { Photos = photos };
            await PhotoQualityBenchmarkFile.SaveAtomicAsync(
                _manifestPath,
                _manifest,
                backupSuffix: ".before-review.bak");
        }
        finally
        {
            _gate.Release();
        }

        return GetState(IndexOf(revisionId));
    }

    public async Task<byte[]> ReadProxyAsync(string revisionId)
    {
        PhotoQualityBenchmarkPhoto photo = Find(revisionId);
        string path = PhotoQualityBenchmarkFile.ResolveProxyPath(
            _proxyRoot,
            photo.ProxyRelativePath);
        byte[] content = await File.ReadAllBytesAsync(path);
        PhotoQualityBenchmarkFile.VerifyProxySha256(photo, content);
        return content;
    }

    private int? FindNextUnreviewed(int currentIndex)
    {
        for (int offset = 1; offset <= _reviewOrder.Length; offset++)
        {
            int candidateIndex = (currentIndex + offset) % _reviewOrder.Length;
            PhotoQualityBenchmarkPhoto photo = Find(_reviewOrder[candidateIndex]);
            if (string.IsNullOrWhiteSpace(photo.Review.QualityLevel))
            {
                return candidateIndex;
            }
        }

        return null;
    }

    private int IndexOf(string revisionId)
    {
        int index = Array.FindIndex(
            _reviewOrder,
            id => string.Equals(id, revisionId, StringComparison.OrdinalIgnoreCase));
        if (index < 0)
        {
            throw new KeyNotFoundException($"Benchmark revision '{revisionId}' was not found.");
        }

        return index;
    }

    private PhotoQualityBenchmarkPhoto Find(string revisionId) =>
        _manifest.Photos.FirstOrDefault(
            photo => string.Equals(
                photo.RevisionId,
                revisionId,
                StringComparison.OrdinalIgnoreCase))
        ?? throw new KeyNotFoundException($"Benchmark revision '{revisionId}' was not found.");

    private static string NormalizeLevel(string? value)
    {
        string level = (value ?? string.Empty).Trim().ToLowerInvariant();
        if (!AllowedLevels.Contains(level))
        {
            throw new ArgumentException(
                "qualityLevel must be one of very-low, low, normal, high, very-high, unsure.");
        }

        return level;
    }

    private static string? NormalizeNotes(string? value)
    {
        string? notes = string.IsNullOrWhiteSpace(value)
            ? null
            : value.Trim();
        if (notes is not null && notes.Length > 2000)
        {
            throw new ArgumentException("Review notes must be 2000 characters or fewer.");
        }

        return notes;
    }
}

internal sealed record PhotoQualityBlindReviewUpdate(
    string? QualityLevel,
    bool PersonallyImportant,
    string? Notes);

internal sealed record PhotoQualityBlindReviewState(
    string BenchmarkId,
    int Index,
    int Total,
    int Reviewed,
    int Remaining,
    int? NextUnreviewedIndex,
    string RevisionId,
    string? QualityLevel,
    bool? PersonallyImportant,
    string? Notes);
