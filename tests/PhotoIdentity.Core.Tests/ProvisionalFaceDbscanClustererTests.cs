using PhotoIdentity.Core.Clustering;
using PhotoIdentity.Core.Identifiers;
using PhotoIdentity.Core.Recognition;
using Xunit;

namespace PhotoIdentity.Core.Tests;

public sealed class ProvisionalFaceDbscanClustererTests
{
    [Fact]
    public async Task Selected_policy_is_deterministic_and_keeps_sparse_faces_as_noise()
    {
        ProvisionalFaceClusterInputFace first = Face([1f, 0f]);
        ProvisionalFaceClusterInputFace second = Face([0.98f, 0.05f]);
        ProvisionalFaceClusterInputFace third = Face([0.97f, -0.08f]);
        ProvisionalFaceClusterInputFace noise = Face([0f, 1f]);
        ProvisionalFaceDbscanClusterer clusterer = new();

        ProvisionalFaceClusterComputation forward = await clusterer.ComputeAsync(
            [first, second, third, noise],
            ProvisionalFaceClusterPolicies.InitialDbscan);
        ProvisionalFaceClusterComputation reversed = await clusterer.ComputeAsync(
            [noise, third, second, first],
            ProvisionalFaceClusterPolicies.InitialDbscan);

        Assert.Equal(1, forward.ClusterCount);
        Assert.Equal(1, forward.NoiseCount);
        Assert.Equal(4, forward.EvaluatedFaceCount);
        Assert.Equal(
            forward.Memberships.ToDictionary(member => member.FaceOccurrenceId),
            reversed.Memberships.ToDictionary(member => member.FaceOccurrenceId));
        Assert.All(
            forward.Memberships.Where(member => member.FaceOccurrenceId != noise.FaceOccurrenceId),
            member =>
            {
                Assert.NotNull(member.DerivedClusterKey);
                Assert.Equal(ProvisionalFaceClusterMemberRole.Core, member.Role);
            });
        ProvisionalFaceClusterComputedMembership noiseMember = Assert.Single(
            forward.Memberships,
            member => member.FaceOccurrenceId == noise.FaceOccurrenceId);
        Assert.Null(noiseMember.DerivedClusterKey);
        Assert.Equal(ProvisionalFaceClusterMemberRole.Noise, noiseMember.Role);
    }

    [Fact]
    public async Task Previously_noise_faces_form_cluster_when_new_dense_evidence_arrives()
    {
        ProvisionalFaceClusterInputFace first = Face([1f, 0f]);
        ProvisionalFaceClusterInputFace second = Face([0.99f, 0.04f]);
        ProvisionalFaceClusterInputFace third = Face([0.98f, -0.04f]);
        ProvisionalFaceDbscanClusterer clusterer = new();

        ProvisionalFaceClusterComputation before = await clusterer.ComputeAsync(
            [first, second],
            ProvisionalFaceClusterPolicies.InitialDbscan);
        ProvisionalFaceClusterComputation after = await clusterer.ComputeAsync(
            [first, second, third],
            ProvisionalFaceClusterPolicies.InitialDbscan);

        Assert.Equal(0, before.ClusterCount);
        Assert.Equal(2, before.NoiseCount);
        Assert.Equal(1, after.ClusterCount);
        Assert.Equal(0, after.NoiseCount);
    }

    [Fact]
    public async Task Durable_not_same_evidence_partitions_false_merge_without_changing_policy()
    {
        ProvisionalFaceClusterInputFace[] faces = Enumerable.Range(1, 6)
            .Select(index => Face(
                [1f, 0f],
                Guid.Parse($"00000000-0000-0000-0000-{index:D12}")))
            .ToArray();
        ProvisionalFaceNotSameConstraint[] constraints =
        [
            ProvisionalFaceNotSameConstraint.Create(faces[0].FaceOccurrenceId, faces[3].FaceOccurrenceId),
            ProvisionalFaceNotSameConstraint.Create(faces[0].FaceOccurrenceId, faces[4].FaceOccurrenceId),
            ProvisionalFaceNotSameConstraint.Create(faces[0].FaceOccurrenceId, faces[5].FaceOccurrenceId),
        ];
        ProvisionalFaceDbscanClusterer clusterer = new();

        ProvisionalFaceClusterComputation result = await clusterer.ComputeAsync(
            faces,
            ProvisionalFaceClusterPolicies.InitialDbscan,
            constraints);

        Assert.Equal(2, result.ClusterCount);
        Assert.Equal(0, result.NoiseCount);
        string firstKey = Assert.Single(
            result.Memberships,
            member => member.FaceOccurrenceId == faces[0].FaceOccurrenceId).DerivedClusterKey!;
        Assert.All(
            result.Memberships.Where(member =>
                member.FaceOccurrenceId == faces[3].FaceOccurrenceId ||
                member.FaceOccurrenceId == faces[4].FaceOccurrenceId ||
                member.FaceOccurrenceId == faces[5].FaceOccurrenceId),
            member => Assert.NotEqual(firstKey, member.DerivedClusterKey));
    }

    [Fact]
    public async Task Neighbour_edge_budget_fails_closed_instead_of_weakening_policy()
    {
        ProvisionalFaceDbscanClusterer clusterer = new(maximumUndirectedNeighborEdges: 1);
        ProvisionalFaceClusterInputFace first = Face([1f, 0f]);
        ProvisionalFaceClusterInputFace second = Face([1f, 0f]);
        ProvisionalFaceClusterInputFace third = Face([1f, 0f]);

        InvalidOperationException error = await Assert.ThrowsAsync<InvalidOperationException>(
            () => clusterer.ComputeAsync(
                [first, second, third],
                ProvisionalFaceClusterPolicies.InitialDbscan));

        Assert.Contains("bounded neighbour budget", error.Message, StringComparison.OrdinalIgnoreCase);
    }

    private static ProvisionalFaceClusterInputFace Face(float[] embedding, Guid? faceId = null) =>
        new(
            faceId is Guid id ? FaceOccurrenceId.From(id) : FaceOccurrenceId.New(),
            AssetRevisionId.New(),
            "unreviewed",
            new EmbeddingVector(embedding));
}
