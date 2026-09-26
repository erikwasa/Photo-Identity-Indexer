using PhotoIdentity.Cli;
using PhotoIdentity.Core.Identifiers;
using PhotoIdentity.Core.Sources;

namespace PhotoIdentity.Integration.Tests;

public sealed class MetadataEnrichmentSourcePrefixTests
{
    [Fact]
    public void Source_prefix_scopes_place_rule_to_matching_folder()
    {
        MetadataEnrichmentCandidate fideli = Candidate("fideli/IMG_1001.jpeg", 2024, 3, 23);
        MetadataEnrichmentCandidate other = Candidate("2024/03/IMG_1001.jpeg", 2024, 3, 23);
        MetadataEnrichmentRuleSet rules = Rules(
            new MetadataPlaceRuleDefinition
            {
                Name = "fideli-sodersjukhuset",
                From = "2024-03-23",
                To = "2024-03-23",
                Place = "Sverige/Stockholms län/Stockholms stad/Södermalm/Södersjukhuset",
                SourcePrefix = "fideli/",
            });

        MetadataEnrichmentPlan plan = MetadataEnrichmentPlanner.Build([fideli, other], rules);

        Assert.Equal(
            "Sverige/Stockholms län/Stockholms stad/Södermalm/Södersjukhuset",
            plan.Items[0].ProposedPlace);
        Assert.Equal("fideli-sodersjukhuset", plan.Items[0].PlaceRule);
        Assert.Null(plan.Items[1].ProposedPlace);
        Assert.Null(plan.Items[1].Ambiguity);
    }

    [Fact]
    public void Source_prefix_does_not_match_similarly_named_sibling_folder()
    {
        MetadataEnrichmentCandidate candidate = Candidate("fideli-backup/IMG_1001.jpeg", 2024, 3, 23);
        MetadataEnrichmentRuleSet rules = Rules(
            new MetadataPlaceRuleDefinition
            {
                Name = "fideli-only",
                From = "2024-03-23",
                To = "2024-03-23",
                Place = "Sverige/Stockholms län/Stockholms stad/Södermalm/Södersjukhuset",
                SourcePrefix = "fideli",
            });

        MetadataEnrichmentPlanItem item = MetadataEnrichmentPlanner.Build([candidate], rules).Items.Single();

        Assert.Null(item.ProposedPlace);
        Assert.Null(item.Ambiguity);
    }

    [Fact]
    public void Different_source_prefixes_can_assign_different_places_on_same_date()
    {
        MetadataEnrichmentCandidate fideli = Candidate("fideli/a.jpeg", 2024, 3, 23);
        MetadataEnrichmentCandidate moa = Candidate("Moa/b.jpeg", 2024, 3, 23);
        MetadataEnrichmentRuleSet rules = Rules(
            new MetadataPlaceRuleDefinition
            {
                Name = "fideli-sodersjukhuset",
                From = "2024-03-23",
                To = "2024-03-23",
                Place = "Sverige/Stockholms län/Stockholms stad/Södermalm/Södersjukhuset",
                SourcePrefix = "fideli/",
            },
            new MetadataPlaceRuleDefinition
            {
                Name = "moa-huddinge",
                From = "2024-03-23",
                To = "2024-03-23",
                Place = "Sverige/Stockholms län/Huddinge Kommun/Flemingsberg/Huddinge Sjukhus",
                SourcePrefix = "Moa/",
            });

        MetadataEnrichmentPlan plan = MetadataEnrichmentPlanner.Build([fideli, moa], rules);

        Assert.Equal(
            "Sverige/Stockholms län/Stockholms stad/Södermalm/Södersjukhuset",
            plan.Items[0].ProposedPlace);
        Assert.Equal(
            "Sverige/Stockholms län/Huddinge Kommun/Flemingsberg/Huddinge Sjukhus",
            plan.Items[1].ProposedPlace);
        Assert.All(plan.Items, item => Assert.Null(item.Ambiguity));
    }

    [Fact]
    public void Source_prefix_normalizes_windows_separators_and_case()
    {
        MetadataEnrichmentCandidate candidate = Candidate("fideli/subfolder/photo.jpeg", 2024, 3, 23);
        MetadataEnrichmentRuleSet rules = Rules(
            new MetadataPlaceRuleDefinition
            {
                Name = "fideli-normalized",
                From = "2024-03-23",
                To = "2024-03-23",
                Place = "Sverige/Stockholms län/Stockholms stad/Södermalm/Södersjukhuset",
                SourcePrefix = @"FIDELI\subfolder",
            });

        MetadataEnrichmentPlanItem item = MetadataEnrichmentPlanner.Build([candidate], rules).Items.Single();

        Assert.Equal(
            "Sverige/Stockholms län/Stockholms stad/Södermalm/Södersjukhuset",
            item.ProposedPlace);
    }

    [Fact]
    public void Source_prefix_rejects_parent_directory_segments()
    {
        MetadataEnrichmentCandidate candidate = Candidate("fideli/photo.jpeg", 2024, 3, 23);
        MetadataEnrichmentRuleSet rules = Rules(
            new MetadataPlaceRuleDefinition
            {
                Name = "unsafe-prefix",
                From = "2024-03-23",
                To = "2024-03-23",
                Place = "Sverige/Stockholms län/Stockholms stad/Södermalm/Södersjukhuset",
                SourcePrefix = "fideli/../Moa",
            });

        ArgumentException exception = Assert.Throws<ArgumentException>(
            () => MetadataEnrichmentPlanner.Build([candidate], rules));

        Assert.Contains("sourcePrefix", exception.Message, StringComparison.Ordinal);
    }

    private static MetadataEnrichmentCandidate Candidate(
        string sourceKey,
        int year,
        int month,
        int day) =>
        new(
            AssetRevisionId.From(Guid.NewGuid()),
            sourceKey,
            new PhotoCaptureDateValue(year, month, day).InclusiveRange,
            $"{year:0000}-{month:00}-{day:00}",
            HasLocation: false);

    private static MetadataEnrichmentRuleSet Rules(params MetadataPlaceRuleDefinition[] placeRules) =>
        new()
        {
            InferDateFromFilename = false,
            InferDateFromDirectory = false,
            PlaceRules = placeRules,
        };
}
