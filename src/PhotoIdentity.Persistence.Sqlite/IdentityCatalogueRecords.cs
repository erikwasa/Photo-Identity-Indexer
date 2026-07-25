using PhotoIdentity.Core.Identifiers;
using PhotoIdentity.Core.Recognition;

namespace PhotoIdentity.Persistence.Sqlite;

/// <summary>
/// Persisted human identity in the local catalogue.
/// </summary>
public sealed record CataloguePerson
{
    public CataloguePerson(
        PersonId id,
        string? displayName,
        DateTimeOffset createdAtUtc,
        PersonId? mergedIntoPersonId = null)
    {
        if (mergedIntoPersonId is PersonId mergeTarget && mergeTarget == id)
        {
            throw new ArgumentException("A person cannot be merged into itself.", nameof(mergedIntoPersonId));
        }

        Id = id;
        DisplayName = Optional(displayName);
        CreatedAtUtc = createdAtUtc.ToUniversalTime();
        MergedIntoPersonId = mergedIntoPersonId;
    }

    public PersonId Id { get; }
    public string? DisplayName { get; }
    public DateTimeOffset CreatedAtUtc { get; }
    public PersonId? MergedIntoPersonId { get; }

    private static string? Optional(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : value.Trim();
}

/// <summary>
/// Human-authored assignment before its database identity is known.
/// </summary>
public sealed record HumanLabelAssignment
{
    public HumanLabelAssignment(
        PersonId personId,
        FaceOccurrenceId faceOccurrenceId,
        string labelKind,
        string assignedBy,
        DateTimeOffset assignedAtUtc,
        string? note = null)
    {
        PersonId = personId;
        FaceOccurrenceId = faceOccurrenceId;
        LabelKind = Required(labelKind, nameof(labelKind));
        AssignedBy = Required(assignedBy, nameof(assignedBy));
        AssignedAtUtc = assignedAtUtc.ToUniversalTime();
        Note = Optional(note);
    }

    public PersonId PersonId { get; }
    public FaceOccurrenceId FaceOccurrenceId { get; }
    public string LabelKind { get; }
    public string AssignedBy { get; }
    public DateTimeOffset AssignedAtUtc { get; }
    public string? Note { get; }

    private static string Required(string value, string parameterName)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(value, parameterName);
        return value.Trim();
    }

    private static string? Optional(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : value.Trim();
}

/// <summary>
/// Persisted human label. Human labels are independent from observations,
/// crops, embeddings and model suggestions.
/// </summary>
public sealed record CatalogueHumanLabel
{
    public CatalogueHumanLabel(
        long id,
        PersonId personId,
        FaceOccurrenceId faceOccurrenceId,
        string labelKind,
        string assignedBy,
        DateTimeOffset assignedAtUtc,
        string? note = null)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(id);
        ArgumentException.ThrowIfNullOrWhiteSpace(labelKind);
        ArgumentException.ThrowIfNullOrWhiteSpace(assignedBy);

        Id = id;
        PersonId = personId;
        FaceOccurrenceId = faceOccurrenceId;
        LabelKind = labelKind.Trim();
        AssignedBy = assignedBy.Trim();
        AssignedAtUtc = assignedAtUtc.ToUniversalTime();
        Note = string.IsNullOrWhiteSpace(note) ? null : note.Trim();
    }

    public long Id { get; }
    public PersonId PersonId { get; }
    public FaceOccurrenceId FaceOccurrenceId { get; }
    public string LabelKind { get; }
    public string AssignedBy { get; }
    public DateTimeOffset AssignedAtUtc { get; }
    public string? Note { get; }
}

/// <summary>
/// Model-generated identity candidate before its database identity is known.
/// </summary>
public sealed record IdentitySuggestionDraft
{
    public IdentitySuggestionDraft(
        FaceOccurrenceId faceOccurrenceId,
        PersonId suggestedPersonId,
        ModelId modelId,
        Sha256Digest modelHash,
        double score,
        string initialStatus,
        DateTimeOffset createdAtUtc)
    {
        if (!double.IsFinite(score))
        {
            throw new ArgumentOutOfRangeException(nameof(score), "Suggestion score must be finite.");
        }

        ArgumentException.ThrowIfNullOrWhiteSpace(initialStatus);

        FaceOccurrenceId = faceOccurrenceId;
        SuggestedPersonId = suggestedPersonId;
        ModelId = modelId;
        ModelHash = modelHash;
        Score = score;
        InitialStatus = initialStatus.Trim();
        CreatedAtUtc = createdAtUtc.ToUniversalTime();
    }

    public FaceOccurrenceId FaceOccurrenceId { get; }
    public PersonId SuggestedPersonId { get; }
    public ModelId ModelId { get; }
    public Sha256Digest ModelHash { get; }
    public double Score { get; }
    public string InitialStatus { get; }
    public DateTimeOffset CreatedAtUtc { get; }
}

/// <summary>
/// Persisted versioned model suggestion and its review status.
/// </summary>
public sealed record CatalogueIdentitySuggestion
{
    public CatalogueIdentitySuggestion(
        long id,
        FaceOccurrenceId faceOccurrenceId,
        PersonId suggestedPersonId,
        ModelId modelId,
        Sha256Digest modelHash,
        double score,
        string status,
        DateTimeOffset createdAtUtc)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(id);
        if (!double.IsFinite(score))
        {
            throw new ArgumentOutOfRangeException(nameof(score), "Suggestion score must be finite.");
        }

        ArgumentException.ThrowIfNullOrWhiteSpace(status);

        Id = id;
        FaceOccurrenceId = faceOccurrenceId;
        SuggestedPersonId = suggestedPersonId;
        ModelId = modelId;
        ModelHash = modelHash;
        Score = score;
        Status = status.Trim();
        CreatedAtUtc = createdAtUtc.ToUniversalTime();
    }

    public long Id { get; }
    public FaceOccurrenceId FaceOccurrenceId { get; }
    public PersonId SuggestedPersonId { get; }
    public ModelId ModelId { get; }
    public Sha256Digest ModelHash { get; }
    public double Score { get; }
    public string Status { get; }
    public DateTimeOffset CreatedAtUtc { get; }
}
