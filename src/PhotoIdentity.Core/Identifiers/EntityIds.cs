namespace PhotoIdentity.Core.Identifiers;

internal static class StrongIdGuard
{
    public static Guid NotEmpty(Guid value, string parameterName)
    {
        if (value == Guid.Empty)
        {
            throw new ArgumentException("Identifier cannot be empty.", parameterName);
        }

        return value;
    }

    public static string NotBlank(string value, string parameterName)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(value, parameterName);
        return value.Trim();
    }
}

public readonly record struct SourceId
{
    private SourceId(Guid value) => Value = StrongIdGuard.NotEmpty(value, nameof(value));
    public Guid Value { get; }
    public bool IsEmpty => Value == Guid.Empty;
    public static SourceId New() => new(Guid.NewGuid());
    public static SourceId From(Guid value) => new(value);
    public override string ToString() => Value.ToString("D");
}

public readonly record struct AssetId
{
    private AssetId(Guid value) => Value = StrongIdGuard.NotEmpty(value, nameof(value));
    public Guid Value { get; }
    public bool IsEmpty => Value == Guid.Empty;
    public static AssetId New() => new(Guid.NewGuid());
    public static AssetId From(Guid value) => new(value);
    public override string ToString() => Value.ToString("D");
}

public readonly record struct AssetRevisionId
{
    private AssetRevisionId(Guid value) => Value = StrongIdGuard.NotEmpty(value, nameof(value));
    public Guid Value { get; }
    public bool IsEmpty => Value == Guid.Empty;
    public static AssetRevisionId New() => new(Guid.NewGuid());
    public static AssetRevisionId From(Guid value) => new(value);
    public override string ToString() => Value.ToString("D");
}

public readonly record struct FaceOccurrenceId
{
    private FaceOccurrenceId(Guid value) => Value = StrongIdGuard.NotEmpty(value, nameof(value));
    public Guid Value { get; }
    public bool IsEmpty => Value == Guid.Empty;
    public static FaceOccurrenceId New() => new(Guid.NewGuid());
    public static FaceOccurrenceId From(Guid value) => new(value);
    public override string ToString() => Value.ToString("D");
}

public readonly record struct FaceCropId
{
    private FaceCropId(Guid value) => Value = StrongIdGuard.NotEmpty(value, nameof(value));
    public Guid Value { get; }
    public bool IsEmpty => Value == Guid.Empty;
    public static FaceCropId New() => new(Guid.NewGuid());
    public static FaceCropId From(Guid value) => new(value);
    public override string ToString() => Value.ToString("D");
}

public readonly record struct PersonId
{
    private PersonId(Guid value) => Value = StrongIdGuard.NotEmpty(value, nameof(value));
    public Guid Value { get; }
    public bool IsEmpty => Value == Guid.Empty;
    public static PersonId New() => new(Guid.NewGuid());
    public static PersonId From(Guid value) => new(value);
    public override string ToString() => Value.ToString("D");
}

public readonly record struct ProcessingRunId
{
    private ProcessingRunId(Guid value) => Value = StrongIdGuard.NotEmpty(value, nameof(value));
    public Guid Value { get; }
    public bool IsEmpty => Value == Guid.Empty;
    public static ProcessingRunId New() => new(Guid.NewGuid());
    public static ProcessingRunId From(Guid value) => new(value);
    public override string ToString() => Value.ToString("D");
}

public readonly record struct ProcessingJobId
{
    private ProcessingJobId(Guid value) => Value = StrongIdGuard.NotEmpty(value, nameof(value));
    public Guid Value { get; }
    public bool IsEmpty => Value == Guid.Empty;
    public static ProcessingJobId New() => new(Guid.NewGuid());
    public static ProcessingJobId From(Guid value) => new(value);
    public override string ToString() => Value.ToString("D");
}

public readonly record struct ModelId
{
    public ModelId(string value) => Value = StrongIdGuard.NotBlank(value, nameof(value));
    public string Value { get; }
    public override string ToString() => Value;
}

public readonly record struct AlignmentProtocolId
{
    public AlignmentProtocolId(string value) => Value = StrongIdGuard.NotBlank(value, nameof(value));
    public string Value { get; }
    public override string ToString() => Value;
}
