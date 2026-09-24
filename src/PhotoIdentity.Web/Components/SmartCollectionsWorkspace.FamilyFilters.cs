using Microsoft.AspNetCore.Components;
using PhotoIdentity.Web.Contracts;

namespace PhotoIdentity.Web.Components;

public partial class SmartCollectionsWorkspace
{
    private static readonly FamilyRelationshipOption[] FamilyRelationshipOptions =
    [
        new("parent", "Parent of"),
        new("child", "Child of"),
        new("spouse", "Spouse of"),
        new("sibling", "Sibling of"),
        new("grandparent", "Grandparent of"),
        new("grandchild", "Grandchild of"),
    ];

    private bool UseAgeFilter { get; set; }
    private string AgePersonId { get; set; } = string.Empty;
    private int AgeMinimumYears { get; set; }
    private int AgeMaximumYears { get; set; } = 130;
    private bool UseRelationshipFilter { get; set; }
    private string RelationshipPersonId { get; set; } = string.Empty;
    private HashSet<string> SelectedRelationshipKinds { get; } = new(StringComparer.Ordinal);

    private void ResetFamilyFilters()
    {
        UseAgeFilter = false;
        AgePersonId = string.Empty;
        AgeMinimumYears = 0;
        AgeMaximumYears = 130;
        UseRelationshipFilter = false;
        RelationshipPersonId = string.Empty;
        SelectedRelationshipKinds.Clear();
    }

    private void ApplyFamilyFilters(
        SmartCollectionAgeRequest? age,
        SmartCollectionRelationshipRequest? relationship)
    {
        UseAgeFilter = age is not null;
        AgePersonId = age?.PersonId ?? string.Empty;
        AgeMinimumYears = age?.MinimumYears ?? 0;
        AgeMaximumYears = age?.MaximumYears ?? 130;

        UseRelationshipFilter = relationship is not null;
        RelationshipPersonId = relationship?.PersonId ?? string.Empty;
        SelectedRelationshipKinds.Clear();
        foreach (string kind in relationship?.Kinds ?? [])
        {
            if (FamilyRelationshipOptions.Any(option =>
                    string.Equals(option.Value, kind, StringComparison.OrdinalIgnoreCase)))
            {
                SelectedRelationshipKinds.Add(kind.Trim().ToLowerInvariant());
            }
        }
    }

    private bool TryBuildFamilyFilters(
        out SmartCollectionAgeRequest? age,
        out SmartCollectionRelationshipRequest? relationship)
    {
        age = null;
        relationship = null;

        if (UseAgeFilter)
        {
            if (string.IsNullOrWhiteSpace(AgePersonId))
            {
                Error = "Choose a person for the age-at-photo filter.";
                return false;
            }

            if (AgeMinimumYears is < 0 or > 130 || AgeMaximumYears is < 0 or > 130)
            {
                Error = "Age-at-photo values must be between 0 and 130 years.";
                return false;
            }

            if (AgeMinimumYears > AgeMaximumYears)
            {
                Error = "Minimum age cannot be greater than maximum age.";
                return false;
            }

            age = new SmartCollectionAgeRequest(
                AgePersonId,
                AgeMinimumYears,
                AgeMaximumYears);
        }

        if (UseRelationshipFilter)
        {
            if (string.IsNullOrWhiteSpace(RelationshipPersonId))
            {
                Error = "Choose a person for the family-relationship filter.";
                return false;
            }

            string[] kinds = SelectedRelationshipKinds
                .OrderBy(value => value, StringComparer.Ordinal)
                .ToArray();
            if (kinds.Length == 0)
            {
                Error = "Choose at least one family relationship kind.";
                return false;
            }

            relationship = new SmartCollectionRelationshipRequest(
                RelationshipPersonId,
                kinds);
        }

        return true;
    }

    private SmartCollectionAgeRequest? CurrentAgeRequest => UseAgeFilter
        ? new SmartCollectionAgeRequest(AgePersonId, AgeMinimumYears, AgeMaximumYears)
        : null;

    private SmartCollectionRelationshipRequest? CurrentRelationshipRequest => UseRelationshipFilter
        ? new SmartCollectionRelationshipRequest(
            RelationshipPersonId,
            SelectedRelationshipKinds.OrderBy(value => value, StringComparer.Ordinal).ToArray())
        : null;

    private void ToggleRelationshipKind(string kind, ChangeEventArgs args)
    {
        if (IsChecked(args))
        {
            SelectedRelationshipKinds.Add(kind);
        }
        else
        {
            SelectedRelationshipKinds.Remove(kind);
        }
    }

    private string AgeSummary(SmartCollectionFilterResponse filter)
    {
        if (filter.Age is null)
        {
            return "Any age";
        }

        return $"Age: {PersonLabel(filter.Age.PersonId)} · {filter.Age.MinimumYears}–{filter.Age.MaximumYears} years";
    }

    private string RelationshipSummary(SmartCollectionFilterResponse filter)
    {
        if (filter.Relationship is null)
        {
            return "Any family relationship";
        }

        string kinds = string.Join(
            ", ",
            filter.Relationship.Kinds.Select(RelationshipKindLabel));
        return $"Relationships from {PersonLabel(filter.Relationship.PersonId)}: {kinds}";
    }

    private string PersonLabel(string id) =>
        People.FirstOrDefault(person => string.Equals(person.Id, id, StringComparison.Ordinal))?.DisplayName ?? id;

    private static string RelationshipKindLabel(string kind) => kind switch
    {
        "parent" => "parent of",
        "child" => "child of",
        "spouse" => "spouse of",
        "sibling" => "sibling of",
        "grandparent" => "grandparent of",
        "grandchild" => "grandchild of",
        _ => kind,
    };

    private sealed record FamilyRelationshipOption(string Value, string Label);
}
