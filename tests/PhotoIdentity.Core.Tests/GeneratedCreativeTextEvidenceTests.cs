using PhotoIdentity.Core.Collections;
using PhotoIdentity.Core.Identifiers;
using Xunit;

namespace PhotoIdentity.Core.Tests;

public sealed class GeneratedCreativeTextEvidenceTests
{
    [Fact]
    public void Evidence_is_versioned_derived_text_with_guard_state()
    {
        AssetRevisionId revision = AssetRevisionId.From(
            Guid.Parse("11111111-1111-1111-1111-111111111111"));

        GeneratedCreativeTextEvidence evidence = new(
            revision,
            "qwen2.5vl:3b",
            new string('a', 64),
            CreativeCollectionGeneratedTextPolicies.VisibleCaptionV1,
            "Two people sit at a table.",
            []);

        Assert.Equal(revision, evidence.RevisionId);
        Assert.Equal("qwen2.5vl:3b", evidence.ModelId);
        Assert.True(evidence.PassesGuard);
        Assert.Empty(evidence.RiskFlags);
    }

    [Fact]
    public void Guard_flags_relationship_event_date_age_and_possible_proper_name_claims()
    {
        IReadOnlyList<string> flags = GeneratedCreativeTextGuard.Evaluate(
            "A mother and daughter celebrate a birthday in Paris in 2024 with a 10-year-old child.");

        Assert.Contains(GeneratedCreativeTextRiskCodes.RelationshipClaim, flags);
        Assert.Contains(GeneratedCreativeTextRiskCodes.EventIdentityClaim, flags);
        Assert.Contains(GeneratedCreativeTextRiskCodes.DateOrYearClaim, flags);
        Assert.Contains(GeneratedCreativeTextRiskCodes.AgeClaim, flags);
        Assert.Contains(GeneratedCreativeTextRiskCodes.PossibleProperNameOrLocation, flags);
    }

    [Theory]
    [InlineData("En person klädd i en svart och vit mönstrad klänning står i en inomhusmiljö. Personens ansikte är inte tydligt synligt. Hållningen är osäker.")]
    [InlineData("En man i mörk kostym håller i en glas med dryck. Han sitter på en mörk fåtölj. I bakgrunden syns några människor och en julgran.")]
    [InlineData("En man står på en brygga med havet i bakgrunden. Han är klädd i en svart vattenskjorta och håller en kamera i handen. Han har en livboj på bryggan.")]
    public void Guard_ignores_capitalization_at_sentence_boundaries(string caption)
    {
        IReadOnlyList<string> flags = GeneratedCreativeTextGuard.Evaluate(caption);

        Assert.DoesNotContain(
            GeneratedCreativeTextRiskCodes.PossibleProperNameOrLocation,
            flags);
    }

    [Fact]
    public void Guard_still_flags_proper_name_or_location_inside_later_sentence()
    {
        IReadOnlyList<string> flags = GeneratedCreativeTextGuard.Evaluate(
            "En person står vid havet. Han tittar mot Stockholm.");

        Assert.Contains(
            GeneratedCreativeTextRiskCodes.PossibleProperNameOrLocation,
            flags);
    }

    [Fact]
    public void Guard_accepts_neutral_visible_content()
    {
        Assert.Empty(GeneratedCreativeTextGuard.Evaluate(
            "Several people sit around a table with plates and glasses."));
    }

    [Fact]
    public void Deterministic_caption_uses_only_selection_provenance_date_and_people_count()
    {
        AssetRevisionId revision = AssetRevisionId.From(
            Guid.Parse("11111111-1111-1111-1111-111111111111"));
        CreativeCollectionCandidate candidate = new(
            revision,
            new DateTime(2026, 9, 20, 10, 30, 0),
            CreativeCollectionCandidateKinds.DirectAnchor,
            []);

        string caption = CreativeCollectionDeterministicCaption.Build(
            candidate,
            ["person-a", "person-b"]);

        Assert.Equal(
            "Direct collection match · captured 2026-09-20 · 2 identified people recorded.",
            caption);
        Assert.DoesNotContain("person-a", caption);
        Assert.DoesNotContain("person-b", caption);
    }
}
