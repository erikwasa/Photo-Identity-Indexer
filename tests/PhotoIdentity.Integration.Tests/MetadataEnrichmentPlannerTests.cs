using PhotoIdentity.Cli;
using PhotoIdentity.Core.Identifiers;
using PhotoIdentity.Core.Sources;

namespace PhotoIdentity.Integration.Tests;

public sealed class MetadataEnrichmentPlannerTests
{
    [Fact]
    public void WhatsApp_filename_proposes_exact_day()
    {
        MetadataEnrichmentPlanItem item = PlanOne("2025/02/IMG-20250209-WA0004.jpg");

        Assert.Equal("2025-02-09", item.ProposedDate?.ToString());
        Assert.Equal("filename", item.DateReason);
        Assert.Null(item.Ambiguity);
    }

    [Fact]
    public void Generic_file_in_year_month_directory_proposes_month_precision()
    {
        MetadataEnrichmentPlanItem item = PlanOne("2025/08/file_000000003ee061f7bfca76a9993fa1e9.png");

        Assert.Equal("2025-08", item.ProposedDate?.ToString());
        Assert.Equal("directory", item.DateReason);
        Assert.Null(item.Ambiguity);
    }

    [Fact]
    public void Miscellaneous_1970_directory_is_not_used_as_a_capture_date()
    {
        MetadataEnrichmentPlanItem item = PlanOne("1970/cup.jpg");

        Assert.Null(item.ProposedDate);
        Assert.Null(item.DateReason);
        Assert.Null(item.Ambiguity);
    }

    [Fact]
    public void Unix_millisecond_filename_can_recover_date_inside_1970_directory()
    {
        MetadataEnrichmentPlanItem item = PlanOne("1970/1443639480450.jpg");

        Assert.Equal("2015-09-30", item.ProposedDate?.ToString());
        Assert.Equal("filename", item.DateReason);
        Assert.Null(item.Ambiguity);
    }

    [Fact]
    public void Filename_and_directory_month_conflict_is_reported_not_guessed()
    {
        MetadataEnrichmentPlanItem item = PlanOne("2025/02/IMG-20250301-WA0000.jpg");

        Assert.Null(item.ProposedDate);
        Assert.Contains("conflicts", item.Ambiguity, StringComparison.Ordinal);
    }

    [Fact]
    public void Partial_precision_date_must_be_fully_contained_by_place_rule()
    {
        MetadataEnrichmentCandidate candidate = new(
            AssetRevisionId.From(Guid.NewGuid()),
            "2024/07/photo.jpg",
            new PhotoCaptureDateValue(2024, 7).InclusiveRange,
            "2024-07",
            HasLocation: false);
        MetadataEnrichmentRuleSet rules = RulesWithPlace(
            "summer-trip",
            "2024-07-06",
            "2024-07-20",
            "Spain/Canary Islands/Tenerife");

        MetadataEnrichmentPlanItem item = MetadataEnrichmentPlanner.Build([candidate], rules).Items.Single();

        Assert.Null(item.ProposedPlace);
        Assert.Contains("not fully contained", item.Ambiguity, StringComparison.Ordinal);
    }

    [Fact]
    public void Exact_date_inside_place_rule_proposes_place()
    {
        MetadataEnrichmentCandidate candidate = new(
            AssetRevisionId.From(Guid.NewGuid()),
            "2024/07/photo.jpg",
            new PhotoCaptureDateValue(2024, 7, 11).InclusiveRange,
            "2024-07-11",
            HasLocation: false);
        MetadataEnrichmentRuleSet rules = RulesWithPlace(
            "summer-trip",
            "2024-07-06",
            "2024-07-20",
            "Spain/Canary Islands/Tenerife");

        MetadataEnrichmentPlanItem item = MetadataEnrichmentPlanner.Build([candidate], rules).Items.Single();

        Assert.Equal("Spain/Canary Islands/Tenerife", item.ProposedPlace);
        Assert.Equal("summer-trip", item.PlaceRule);
        Assert.Null(item.Ambiguity);
    }

    [Fact]
    public void Existing_location_and_date_are_never_replaced()
    {
        MetadataEnrichmentCandidate candidate = new(
            AssetRevisionId.From(Guid.NewGuid()),
            "2024/07/IMG-20240711-WA0000.jpg",
            new PhotoCaptureDateValue(2024, 7, 10).InclusiveRange,
            "2024-07-10",
            HasLocation: true);
        MetadataEnrichmentRuleSet rules = RulesWithPlace(
            "summer-trip",
            "2024-07-06",
            "2024-07-20",
            "Spain/Canary Islands/Tenerife");

        MetadataEnrichmentPlanItem item = MetadataEnrichmentPlanner.Build([candidate], rules).Items.Single();

        Assert.Null(item.ProposedDate);
        Assert.Null(item.ProposedPlace);
        Assert.Null(item.Ambiguity);
    }

    [Fact]
    public void Different_places_from_overlapping_matching_rules_are_ambiguous()
    {
        MetadataEnrichmentCandidate candidate = new(
            AssetRevisionId.From(Guid.NewGuid()),
            "2024/07/photo.jpg",
            new PhotoCaptureDateValue(2024, 7, 11).InclusiveRange,
            "2024-07-11",
            HasLocation: false);
        MetadataEnrichmentRuleSet rules = new()
        {
            PlaceRules =
            [
                new MetadataPlaceRuleDefinition
                {
                    Name = "trip-a",
                    From = "2024-07-01",
                    To = "2024-07-20",
                    Place = "Spain/Canary Islands/Tenerife",
                },
                new MetadataPlaceRuleDefinition
                {
                    Name = "trip-b",
                    From = "2024-07-10",
                    To = "2024-07-12",
                    Place = "Portugal/Madeira/Funchal",
                },
            ],
        };

        MetadataEnrichmentPlanItem item = MetadataEnrichmentPlanner.Build([candidate], rules).Items.Single();

        Assert.Null(item.ProposedPlace);
        Assert.Contains("different Places", item.Ambiguity, StringComparison.Ordinal);
    }

    [Fact]
    public void Command_is_dry_run_unless_apply_is_explicit()
    {
        MetadataEnrichmentCommandOptions dryRun = MetadataEnrichmentCommandOptions.Parse(
            ["--postgres-connection-env", "PHOTOIDENTITY_TEST", "--rules", "rules.json"]);
        MetadataEnrichmentCommandOptions apply = MetadataEnrichmentCommandOptions.Parse(
            ["--postgres-connection-env", "PHOTOIDENTITY_TEST", "--rules", "rules.json", "--apply"]);

        Assert.False(dryRun.Apply);
        Assert.True(apply.Apply);
    }

    private static MetadataEnrichmentPlanItem PlanOne(string sourceKey)
    {
        MetadataEnrichmentCandidate candidate = new(
            AssetRevisionId.From(Guid.NewGuid()),
            sourceKey,
            ExistingDateRange: null,
            ExistingDateDisplay: null,
            HasLocation: true);
        return MetadataEnrichmentPlanner.Build([candidate], new MetadataEnrichmentRuleSet()).Items.Single();
    }

    private static MetadataEnrichmentRuleSet RulesWithPlace(
        string name,
        string from,
        string to,
        string place) =>
        new()
        {
            PlaceRules =
            [
                new MetadataPlaceRuleDefinition
                {
                    Name = name,
                    From = from,
                    To = to,
                    Place = place,
                },
            ],
        };
}
