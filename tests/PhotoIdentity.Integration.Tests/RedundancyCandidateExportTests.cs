using PhotoIdentity.PhotoQualityEvaluation;
using Xunit;

namespace PhotoIdentity.Integration.Tests;

public sealed class RedundancyCandidateExportTests
{
    [Fact]
    public void Exact_cosine_is_deterministic_for_normalized_synthetic_vectors()
    {
        Assert.Equal(1d, RedundancyCandidateExportCommand.DotNormalized(
            [1f, 0f], [1f, 0f]), 10);
        Assert.Equal(0d, RedundancyCandidateExportCommand.DotNormalized(
            [1f, 0f], [0f, 1f]), 10);
        Assert.Equal(-1d, RedundancyCandidateExportCommand.DotNormalized(
            [1f, 0f], [-1f, 0f]), 10);
        Assert.Throws<InvalidDataException>(() =>
            RedundancyCandidateExportCommand.DotNormalized([1f], [1f, 0f]));
    }

    [Fact]
    public void Nearest_dhash_retains_top_k_with_stable_index_tie_break()
    {
        List<RedundancyCandidateExportCommand.Neighbor> best = [];
        foreach (var hit in new[]
        {
            new RedundancyCandidateExportCommand.Neighbor(9, 12),
            new RedundancyCandidateExportCommand.Neighbor(5, 2),
            new RedundancyCandidateExportCommand.Neighbor(8, 2),
            new RedundancyCandidateExportCommand.Neighbor(3, 2),
            new RedundancyCandidateExportCommand.Neighbor(1, 0),
        })
            RedundancyCandidateExportCommand.Offer(best, hit, 3);

        Assert.Equal([(1, 0), (3, 2), (5, 2)],
            best.Select(x => (x.Index, x.Distance)).ToArray());
    }



    [Theory]
    [InlineData(25, 0.62, true)]
    [InlineData(30, 0.70, true)]
    [InlineData(64, 0.77999, true)]
    [InlineData(24, 0.70, false)]
    [InlineData(25, 0.61999, false)]
    [InlineData(25, 0.78, false)]
    [InlineData(4, 0.70, false)]
    public void Opt_in_borderline_supply_uses_disjoint_near_copy_safe_band(
        int dhashDistance, double clipCosine, bool expected)
    {
        Assert.Equal(expected,
            RedundancyCandidateExportCommand.IsBorderlineCandidate(
                dhashDistance, clipCosine));
    }

    [Fact]
    public void Top_k_optimization_matches_exact_stable_sort_over_dense_candidates()
    {
        RedundancyCandidateExportCommand.Neighbor[] hits =
            Enumerable.Range(0, 512)
                .Select(i => new RedundancyCandidateExportCommand.Neighbor(
                    (i * 37) % 512, (i * 13) % 65))
                .Reverse()
                .ToArray();
        List<RedundancyCandidateExportCommand.Neighbor> selected = [];
        foreach (var hit in hits)
            RedundancyCandidateExportCommand.Offer(selected, hit, 8);

        Assert.Equal(
            hits.OrderBy(x => x.Distance).ThenBy(x => x.Index)
                .Take(8).Select(x => (x.Index, x.Distance)).ToArray(),
            selected.Select(x => (x.Index, x.Distance)).ToArray());
    }

    [Fact]
    public void Full_population_budget_is_strictly_bounded()
    {
        RedundancyCandidateExportCommand.ValidateFullPopulation(27_050, 7_500_000_000L);
        Assert.Throws<InvalidOperationException>(() =>
            RedundancyCandidateExportCommand.ValidateFullPopulation(1, 100));
        Assert.Throws<InvalidOperationException>(() =>
            RedundancyCandidateExportCommand.ValidateFullPopulation(30_001, 100));
        Assert.Throws<InvalidOperationException>(() =>
            RedundancyCandidateExportCommand.ValidateFullPopulation(
                27_050, 17L * 1024 * 1024 * 1024));
        Assert.Throws<InvalidOperationException>(() =>
            RedundancyCandidateExportCommand.ValidateFullPopulation(27_050, -1));
    }

    [Theory]
    [InlineData("../secrets.json")]
    [InlineData("docs/unsafe.json")]
    [InlineData("private/../docs/unsafe.json")]
    [InlineData("private/../../secrets.json")]
    [InlineData("private")]
    public void Candidate_exports_cannot_escape_private_directory(string path)
    {
        Assert.Throws<ArgumentException>(() =>
            RedundancyCandidateExportCommand.PrivatePath(path));
    }

    [Fact]
    public void Candidate_exports_accept_only_relative_private_files()
    {
        string path = RedundancyCandidateExportCommand.PrivatePath(
            "private/wi0194-synthetic-export.json");
        Assert.EndsWith("wi0194-synthetic-export.json", path);
    }
}
