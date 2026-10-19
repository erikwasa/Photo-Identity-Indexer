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
