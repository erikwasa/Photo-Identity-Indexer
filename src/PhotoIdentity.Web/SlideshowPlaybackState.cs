namespace PhotoIdentity.Web;

public enum SlideshowAdvanceResult
{
    None,
    Moved,
    CycledSamePhoto,
    StoppedAtEnd,
    ExitRequested,
}

public sealed class SlideshowPlaybackState
{
    private IReadOnlyList<string> _revisionIds = [];
    private bool _resetTimerWhenReady;

    public SlideshowSettings Settings { get; private set; } = SlideshowSettings.Defaults;
    public int CurrentIndex { get; private set; }
    public string? CurrentRevisionId =>
        CurrentIndex >= 0 && CurrentIndex < _revisionIds.Count ? _revisionIds[CurrentIndex] : null;
    public int Count => _revisionIds.Count;
    public bool IsPlaying { get; private set; }
    public bool IsImageReady { get; private set; }
    public bool IsDocumentVisible { get; private set; } = true;
    public bool ExitRequested { get; private set; }
    public TimeSpan Remaining { get; private set; }
    public double ProgressFraction
    {
        get
        {
            double duration = Settings.ImageDurationSeconds;
            if (!IsImageReady || duration <= 0)
            {
                return 0;
            }

            double remaining = Math.Clamp(Remaining.TotalSeconds, 0, duration);
            return Math.Clamp(1d - (remaining / duration), 0d, 1d);
        }
    }

    public void LoadSnapshot(IEnumerable<string> revisionIds, SlideshowSettings settings)
    {
        ArgumentNullException.ThrowIfNull(revisionIds);
        Settings = settings.Normalize();
        _revisionIds = revisionIds
            .Where(value => !string.IsNullOrWhiteSpace(value))
            .Distinct(StringComparer.Ordinal)
            .ToArray();
        CurrentIndex = 0;
        ExitRequested = false;
        Remaining = Duration();
        IsImageReady = false;
        _resetTimerWhenReady = _revisionIds.Count > 0;
        IsPlaying = _revisionIds.Count > 0 && Settings.Autoplay;
    }

    public void ApplySettings(SlideshowSettings settings)
    {
        SlideshowSettings normalized = settings.Normalize();
        int previousDuration = Settings.ImageDurationSeconds;
        double elapsedFraction = previousDuration <= 0
            ? 0
            : Math.Clamp(1d - (Remaining.TotalSeconds / previousDuration), 0d, 1d);

        bool autoplayChanged = normalized.Autoplay != Settings.Autoplay;
        Settings = normalized;

        if (_resetTimerWhenReady || !IsImageReady)
        {
            Remaining = Duration();
        }
        else if (previousDuration != Settings.ImageDurationSeconds)
        {
            Remaining = TimeSpan.FromSeconds(
                Settings.ImageDurationSeconds * (1d - elapsedFraction));
        }

        if (autoplayChanged)
        {
            if (Settings.Autoplay)
            {
                Resume();
            }
            else
            {
                Pause();
            }
        }
    }

    public void MarkCurrentImageReady()
    {
        if (CurrentRevisionId is null)
        {
            return;
        }

        IsImageReady = true;
        if (_resetTimerWhenReady)
        {
            Remaining = Duration();
            _resetTimerWhenReady = false;
        }
    }

    public void MarkCurrentImageUnavailable()
    {
        IsImageReady = false;
        Pause();
    }

    public void Pause() => IsPlaying = false;

    public void Resume()
    {
        if (CurrentRevisionId is null || ExitRequested)
        {
            return;
        }

        if (Remaining <= TimeSpan.Zero)
        {
            Remaining = Duration();
        }

        IsPlaying = true;
    }

    public void TogglePlay()
    {
        if (IsPlaying)
        {
            Pause();
        }
        else
        {
            Resume();
        }
    }

    public void SetDocumentVisible(bool visible)
    {
        IsDocumentVisible = visible;
    }

    public SlideshowAdvanceResult NextManual() => AdvanceFromCurrent();

    public SlideshowAdvanceResult PreviousManual()
    {
        if (_revisionIds.Count == 0 || CurrentIndex <= 0)
        {
            return SlideshowAdvanceResult.None;
        }

        MoveTo(CurrentIndex - 1);
        return SlideshowAdvanceResult.Moved;
    }

    public SlideshowAdvanceResult AdvanceTime(TimeSpan elapsed)
    {
        if (elapsed <= TimeSpan.Zero ||
            !IsPlaying ||
            !IsDocumentVisible ||
            !IsImageReady ||
            CurrentRevisionId is null)
        {
            return SlideshowAdvanceResult.None;
        }

        Remaining -= elapsed;
        if (Remaining > TimeSpan.Zero)
        {
            return SlideshowAdvanceResult.None;
        }

        return AdvanceFromCurrent();
    }

    public IReadOnlyList<string> GetPrefetchRevisionIds(int window = 1)
    {
        if (window < 1 || _revisionIds.Count <= 1)
        {
            return [];
        }

        HashSet<int> indices = [];
        List<string> result = [];
        bool loop = Settings.AfterLastPhoto == SlideshowSettings.Loop;

        for (int distance = 1; distance <= window; distance++)
        {
            int previous = CurrentIndex - distance;
            int next = CurrentIndex + distance;

            if (loop)
            {
                previous = Mod(previous, _revisionIds.Count);
                next = Mod(next, _revisionIds.Count);
            }

            AddIndex(previous);
            AddIndex(next);
        }

        return result;

        void AddIndex(int index)
        {
            if (index < 0 || index >= _revisionIds.Count || index == CurrentIndex || !indices.Add(index))
            {
                return;
            }

            result.Add(_revisionIds[index]);
        }
    }

    private SlideshowAdvanceResult AdvanceFromCurrent()
    {
        if (_revisionIds.Count == 0)
        {
            return SlideshowAdvanceResult.None;
        }

        if (CurrentIndex < _revisionIds.Count - 1)
        {
            MoveTo(CurrentIndex + 1);
            return SlideshowAdvanceResult.Moved;
        }

        return Settings.AfterLastPhoto switch
        {
            SlideshowSettings.Stop => StopAtEnd(),
            SlideshowSettings.Exit => RequestExit(),
            _ => LoopFromEnd(),
        };
    }

    private SlideshowAdvanceResult LoopFromEnd()
    {
        if (_revisionIds.Count == 1)
        {
            Remaining = Duration();
            IsImageReady = true;
            _resetTimerWhenReady = false;
            return SlideshowAdvanceResult.CycledSamePhoto;
        }

        MoveTo(0);
        return SlideshowAdvanceResult.Moved;
    }

    private SlideshowAdvanceResult StopAtEnd()
    {
        Pause();
        Remaining = TimeSpan.Zero;
        return SlideshowAdvanceResult.StoppedAtEnd;
    }

    private SlideshowAdvanceResult RequestExit()
    {
        Pause();
        ExitRequested = true;
        Remaining = TimeSpan.Zero;
        return SlideshowAdvanceResult.ExitRequested;
    }

    private void MoveTo(int index)
    {
        CurrentIndex = index;
        IsImageReady = false;
        Remaining = Duration();
        _resetTimerWhenReady = true;
    }

    private TimeSpan Duration() => TimeSpan.FromSeconds(Settings.ImageDurationSeconds);

    private static int Mod(int value, int modulus)
    {
        int remainder = value % modulus;
        return remainder < 0 ? remainder + modulus : remainder;
    }
}
