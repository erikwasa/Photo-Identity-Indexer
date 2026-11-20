namespace PhotoIdentity.Web.Contracts;

/// <summary>
/// Ephemeral client-side visual readiness for one explicitly inspected face.
/// A late image/video callback is never evidence for a different observation,
/// including when the reviewer reopens the *same* face in a new session.
/// This is a UX guard, not a server claim that the human recognized anyone:
/// the server continues to require source-bound decisions and a separate,
/// explicit presence-promotion approval.
/// </summary>
public sealed class VideoFaceReviewVisualGate
{
    private long _generation;

    public long Generation => _generation;
    public string? ObservationId { get; private set; }
    public bool CropReady { get; private set; }
    public bool CropUnavailable { get; private set; }
    public bool ProxyReady { get; private set; }
    public bool ProxyUnavailable { get; private set; }
    public bool VisualConfirmed { get; set; }
    public bool PresenceApproved { get; set; }

    public bool HasVisual => CropReady || (ProxyReady && !ProxyUnavailable);

    public long Begin(string observationId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(observationId);
        Reset();
        ObservationId = observationId;
        return _generation;
    }

    public void Reset()
    {
        // A new generation invalidates all callbacks still in flight,
        // even if Begin subsequently receives the same observation ID.
        _generation = checked(_generation + 1);
        ObservationId = null;
        CropReady = false;
        CropUnavailable = false;
        ProxyReady = false;
        ProxyUnavailable = false;
        VisualConfirmed = false;
        PresenceApproved = false;
    }

    public bool CropLoaded(long generation, string observationId)
    {
        if (!Matches(generation, observationId)) return false;
        CropReady = true;
        CropUnavailable = false;
        RevokeAcknowledgements();
        return true;
    }

    public bool CropFailed(long generation, string observationId)
    {
        if (!Matches(generation, observationId)) return false;
        CropReady = false;
        CropUnavailable = true;
        RevokeAcknowledgements();
        return true;
    }

    public bool ProxyFrameReady(long generation, string observationId)
    {
        if (!Matches(generation, observationId)) return false;
        ProxyReady = true;
        ProxyUnavailable = false;
        RevokeAcknowledgements();
        return true;
    }

    public bool ProxyFailed(long generation, string observationId)
    {
        if (!Matches(generation, observationId)) return false;
        ProxyReady = false;
        ProxyUnavailable = true;
        RevokeAcknowledgements();
        return true;
    }

    public bool Matches(long generation, string observationId) =>
        generation == _generation && ObservationId is not null &&
        string.Equals(ObservationId, observationId, StringComparison.Ordinal);

    public bool CanReview(string observationId) =>
        ObservationId is not null &&
        string.Equals(ObservationId, observationId, StringComparison.Ordinal) &&
        HasVisual && VisualConfirmed;

    private void RevokeAcknowledgements()
    {
        // If the visual context changes after a confirmation checkbox was
        // ticked, an operator must explicitly re-check both acknowledgements.
        VisualConfirmed = false;
        PresenceApproved = false;
    }
}
