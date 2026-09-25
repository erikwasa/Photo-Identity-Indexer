using PhotoIdentity.Core.Identifiers;

namespace PhotoIdentity.Core.People;

public static class PersonBirthDatePrecisions
{
    public const string Year = "year";
    public const string Month = "month";
    public const string Day = "day";

    public static string Normalize(string value) => value?.Trim().ToLowerInvariant() switch
    {
        Year => Year,
        Month => Month,
        Day => Day,
        _ => throw new ArgumentException("Birth-date precision must be 'year', 'month', or 'day'.", nameof(value)),
    };
}

public sealed record PersonBirthDate
{
    public PersonBirthDate(int year, int? month, int? day, string precision)
    {
        Precision = PersonBirthDatePrecisions.Normalize(precision);
        if (year is < 1 or > 9999)
        {
            throw new ArgumentOutOfRangeException(nameof(year), "Birth year must be between 1 and 9999.");
        }

        if (Precision == PersonBirthDatePrecisions.Year)
        {
            if (month is not null || day is not null)
            {
                throw new ArgumentException("Year-precision birth metadata cannot contain a month or day.");
            }
        }
        else
        {
            if (month is null || month is < 1 or > 12)
            {
                throw new ArgumentOutOfRangeException(nameof(month), "Month- or day-precision birth metadata requires a month between 1 and 12.");
            }

            if (Precision == PersonBirthDatePrecisions.Month)
            {
                if (day is not null)
                {
                    throw new ArgumentException("Month-precision birth metadata cannot contain a day.");
                }
            }
            else
            {
                if (day is null)
                {
                    throw new ArgumentException("Day-precision birth metadata requires a day.");
                }

                _ = new DateOnly(year, month.Value, day.Value);
            }
        }

        Year = year;
        Month = month;
        Day = day;
    }

    public int Year { get; }
    public int? Month { get; }
    public int? Day { get; }
    public string Precision { get; }

    public DateOnly Earliest => Precision switch
    {
        PersonBirthDatePrecisions.Year => new DateOnly(Year, 1, 1),
        PersonBirthDatePrecisions.Month => new DateOnly(Year, Month!.Value, 1),
        _ => new DateOnly(Year, Month!.Value, Day!.Value),
    };

    public DateOnly Latest => Precision switch
    {
        PersonBirthDatePrecisions.Year => new DateOnly(Year, 12, 31),
        PersonBirthDatePrecisions.Month => new DateOnly(Year, Month!.Value, DateTime.DaysInMonth(Year, Month.Value)),
        _ => Earliest,
    };
}

public sealed record PersonAgeRange(int MinimumYears, int MaximumYears)
{
    public bool Overlaps(int minimumYears, int maximumYears) =>
        MaximumYears >= minimumYears && MinimumYears <= maximumYears;
}

public static class PersonAgeCalculator
{
    public static PersonAgeRange? Calculate(
        PersonBirthDate? birthDate,
        DateOnly? photoFrom,
        DateOnly? photoTo)
    {
        if (birthDate is null || photoFrom is null || photoTo is null || photoFrom > photoTo)
        {
            return null;
        }

        if (photoTo.Value < birthDate.Earliest)
        {
            return null;
        }

        int maximum = WholeYears(birthDate.Earliest, photoTo.Value);
        if (maximum < 0)
        {
            return null;
        }

        int minimum = photoFrom.Value < birthDate.Latest
            ? 0
            : Math.Max(0, WholeYears(birthDate.Latest, photoFrom.Value));
        return new PersonAgeRange(minimum, maximum);
    }

    private static int WholeYears(DateOnly birthDate, DateOnly date)
    {
        int years = date.Year - birthDate.Year;
        if (date.Month < birthDate.Month ||
            (date.Month == birthDate.Month && date.Day < birthDate.Day))
        {
            years--;
        }

        return years;
    }
}

public static class PersonRelationshipKinds
{
    public const string Parent = "parent";
    public const string Child = "child";
    public const string Spouse = "spouse";
    public const string Sibling = "sibling";
    public const string Grandparent = "grandparent";
    public const string Grandchild = "grandchild";
    public const string Cousin = "cousin";

    public static IReadOnlyList<string> All { get; } =
    [Parent, Child, Spouse, Sibling, Grandparent, Grandchild, Cousin];

    public static string Normalize(string value) => value?.Trim().ToLowerInvariant() switch
    {
        Parent => Parent,
        Child => Child,
        Spouse => Spouse,
        Sibling => Sibling,
        Grandparent => Grandparent,
        Grandchild => Grandchild,
        Cousin => Cousin,
        _ => throw new ArgumentException(
            "Relationship kind must be parent, child, spouse, sibling, grandparent, grandchild, or cousin.",
            nameof(value)),
    };

    public static string Inverse(string value) => Normalize(value) switch
    {
        Parent => Child,
        Child => Parent,
        Grandparent => Grandchild,
        Grandchild => Grandparent,
        Spouse => Spouse,
        Sibling => Sibling,
        Cousin => Cousin,
        _ => throw new InvalidOperationException("Unsupported relationship kind."),
    };

    public static bool IsSymmetric(string value)
    {
        string kind = Normalize(value);
        return kind is Spouse or Sibling or Cousin;
    }
}

public sealed record PersonFamilyRelationship(
    long Id,
    PersonId PersonId,
    PersonId RelatedPersonId,
    string RelatedPersonDisplayName,
    string Kind,
    string Actor,
    DateTimeOffset CreatedAtUtc);

public sealed record PersonFamilyMetadata(
    PersonId PersonId,
    PersonBirthDate? BirthDate,
    IReadOnlyList<PersonFamilyRelationship> Relationships);

public interface IPersonFamilyMetadataRepository
{
    Task<PersonFamilyMetadata> GetAsync(
        PersonId personId,
        CancellationToken cancellationToken = default);

    Task SetBirthDateAsync(
        PersonId personId,
        PersonBirthDate? birthDate,
        string actor,
        DateTimeOffset changedAtUtc,
        CancellationToken cancellationToken = default);

    Task<PersonFamilyRelationship> AddRelationshipAsync(
        PersonId personId,
        PersonId relatedPersonId,
        string kind,
        string actor,
        DateTimeOffset createdAtUtc,
        CancellationToken cancellationToken = default);

    Task<bool> DeleteRelationshipAsync(
        PersonId personId,
        long relationshipId,
        CancellationToken cancellationToken = default);
}
