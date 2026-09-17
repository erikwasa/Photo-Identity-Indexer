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
    private SlideshowPresentationEvidence _currentEvidence = SlideshowPresentationEvidence.Unavailable;

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
    public SlideshowTimingDecision CurrentTiming { get; private set; } =
        SlideshowTimingDecision.Fallback(SlideshowSettings.Defaults.ImageDurationSeconds);
    public double ProgressFraction
    {
        get
        {
            double duration = CurrentTiming.EffectiveDuration.TotalSeconds;
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
        _currentEvidence = SlideshowPresentationEvidence.Unavailable;
        CurrentTiming = SlideshowTimingDecision.Fallback(Settings.ImageDurationSeconds);
        Remaining = CurrentTiming.EffectiveDuration;
        IsImageReady = false;
        _resetTimerWhenReady = _revisionIds.Count > 0;
        IsPlaying = _revisionIds.Count > 0 && Settings.Autoplay;
    }

    public void ApplySettings(SlideshowSettings settings)
    {
        SlideshowSettings normalized = settings.Normalize();
        TimeSpan previousEffectiveDuration = CurrentTiming.EffectiveDuration;
        double elapsedFraction = previousEffectiveDuration <= TimeSpan.Zero
            ? 0
            : Math.Clamp(
                1d - (Remaining.TotalSeconds / previousEffectiveDuration.TotalSeconds),
                0d,
                1d);

        bool autoplayChanged = normalized.Autoplay != Settings.Autoplay;
        bool configuredDurationChanged = normalized.ImageDurationSeconds != Settings.ImageDurationSeconds;
        Settings = normalized;

        if (_resetTimerWhenReady || !IsImageReady)
        {
            _currentEvidence = SlideshowPresentationEvidence.Unavailable;
            CurrentTiming = SlideshowTimingDecision.Fallback(Settings.ImageDurationSeconds);
            Remaining = CurrentTiming.EffectiveDuration;
        }
        else if (configuredDurationChanged)
        {
            CurrentTiming = SlideshowTimingPolicy.Create(
                Settings.ImageDurationSeconds,
                _currentEvidence);
            Remaining = TimeSpan.FromTicks((long)Math.Round(
                CurrentTiming.EffectiveDuration.Ticks * (1d - elapsedFraction),
                MidpointRounding.AwayFromZero));
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

    public void MarkCurrentImageReady() =>
        MarkCurrentImageReady(SlideshowPresentationEvidence.Unavailable);

    public void MarkCurrentImageReady(SlideshowPresentationEvidence evidence)
    {
        if (CurrentRevisionId is null)
        {
            return;
        }

        _currentEvidence = evidence ?? SlideshowPresentationEvidence.Unavailable;
        CurrentTiming = SlideshowTimingPolicy.Create(
            Settings.ImageDurationSeconds,
            _currentEvidence);
        IsImageReady = true;
        if (_resetTimerWhenReady)
        {
            Remaining = CurrentTiming.EffectiveDuration;
            _resetTimerWhenReady = false;
        }
    }

    public void MarkCurrentImageUnavailable()
    {
        IsImageReady = false;
        _currentEvidence = SlideshowPresentationEvidence.Unavailable;
        CurrentTiming = SlideshowTimingDecision.Fallback(Settings.ImageDurationSeconds);
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
            Remaining = CurrentTiming.EffectiveDuration;
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

    public SlideshowAdvanceResult NextManual() =>
        Settings.ManualNavigation ? AdvanceFromCurrent() : SlideshowAdvanceResult.None;

    public SlideshowAdvanceResult PreviousManual()
    {
        if (!Settings.ManualNavigation || _revisionIds.Count == 0 || CurrentIndex <= 0)
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
            Remaining = CurrentTiming.EffectiveDuration;
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
        _currentEvidence = SlideshowPresentationEvidence.Unavailable;
        CurrentTiming = SlideshowTimingDecision.Fallback(Settings.ImageDurationSeconds);
        Remaining = CurrentTiming.EffectiveDuration;
        _resetTimerWhenReady = true;
    }

    private static int Mod(int value, int modulus)
    {
        int remainder = value % modulus;
        return remainder < 0 ? remainder + modulus : remainder;
    }
}
