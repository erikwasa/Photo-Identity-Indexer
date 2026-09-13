namespace PhotoIdentity.Core.Clustering;

/// <summary>
/// Deterministic exact cosine-distance DBSCAN used by the initial M25 production policy.
/// Pairwise work is quadratic, matching the WI-0113 evaluation semantics, but memory stays
/// bounded by the face cap and an explicit sparse-neighbour edge budget rather than retaining a
/// dense distance matrix. Inputs are sorted by stable face-occurrence ID before cluster expansion.
/// </summary>
public sealed class ProvisionalFaceDbscanClusterer
{
    public const int DefaultMaximumUndirectedNeighborEdges = 2_000_000;

    private readonly int _maximumUndirectedNeighborEdges;

    public ProvisionalFaceDbscanClusterer(
        int maximumUndirectedNeighborEdges = DefaultMaximumUndirectedNeighborEdges)
    {
        if (maximumUndirectedNeighborEdges < 1)
        {
            throw new ArgumentOutOfRangeException(nameof(maximumUndirectedNeighborEdges));
        }

        _maximumUndirectedNeighborEdges = maximumUndirectedNeighborEdges;
    }

    public async Task<ProvisionalFaceClusterComputation> ComputeAsync(
        IReadOnlyList<ProvisionalFaceClusterInputFace> faces,
        ProvisionalFaceClusterPolicy policy,
        Func<int, CancellationToken, Task>? reportProgress = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(faces);
        ArgumentNullException.ThrowIfNull(policy);
        policy.Validate();

        if (!string.Equals(policy.Algorithm, "dbscan-cosine", StringComparison.Ordinal))
        {
            throw new NotSupportedException(
                $"Production provisional clustering does not support algorithm '{policy.Algorithm}'.");
        }

        double eps = policy.DistanceThreshold
            ?? throw new ArgumentException("DBSCAN requires a cosine-distance threshold.", nameof(policy));
        if (faces.Count > ProvisionalFaceClusterPolicies.MaximumFacesPerRun)
        {
            throw new InvalidOperationException(
                $"Provisional clustering is capped at {ProvisionalFaceClusterPolicies.MaximumFacesPerRun} faces per run.");
        }

        if (faces.Count == 0)
        {
            return new ProvisionalFaceClusterComputation(0, 0, 0, []);
        }

        List<ProvisionalFaceClusterInputFace> ordered = faces
            .OrderBy(face => face.FaceOccurrenceId.Value)
            .ToList();
        int dimensions = ordered[0].Embedding.Dimensions;
        if (ordered.Any(face => face.Embedding.Dimensions != dimensions))
        {
            throw new InvalidOperationException(
                "All embeddings in one provisional clustering run must have the same dimensions.");
        }

        var neighbours = new List<int>[ordered.Count];
        for (int index = 0; index < ordered.Count; index++)
        {
            neighbours[index] = [index];
        }

        int undirectedEdgeCount = 0;
        for (int left = 0; left < ordered.Count; left++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            for (int right = left + 1; right < ordered.Count; right++)
            {
                double similarity = ordered[left].Embedding.CosineSimilarity(ordered[right].Embedding);
                double distance = Math.Clamp(1d - similarity, 0d, 2d);
                if (distance > eps)
                {
                    continue;
                }

                undirectedEdgeCount++;
                if (undirectedEdgeCount > _maximumUndirectedNeighborEdges)
                {
                    throw new InvalidOperationException(
                        $"Provisional clustering exceeded the bounded neighbour budget of {_maximumUndirectedNeighborEdges:N0} exact edges. " +
                        "Do not silently weaken the DBSCAN policy; measure the production workload before changing retrieval/indexing strategy.");
                }

                neighbours[left].Add(right);
                neighbours[right].Add(left);
            }

            if (reportProgress is not null &&
                (left == ordered.Count - 1 || (left + 1) % 64 == 0))
            {
                await reportProgress(left + 1, cancellationToken);
            }
        }

        foreach (List<int> list in neighbours)
        {
            list.Sort();
        }

        bool[] core = neighbours
            .Select(list => list.Count >= policy.MinimumSamples)
            .ToArray();
        bool[] visited = new bool[ordered.Count];
        int[] labels = Enumerable.Repeat(-1, ordered.Count).ToArray();
        int nextCluster = 0;

        for (int start = 0; start < ordered.Count; start++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (visited[start])
            {
                continue;
            }

            visited[start] = true;
            if (!core[start])
            {
                continue;
            }

            int cluster = nextCluster++;
            labels[start] = cluster;
            Queue<int> queue = new();
            HashSet<int> queued = [];
            foreach (int neighbour in neighbours[start])
            {
                if (queued.Add(neighbour))
                {
                    queue.Enqueue(neighbour);
                }
            }

            while (queue.Count > 0)
            {
                int candidate = queue.Dequeue();
                if (!visited[candidate])
                {
                    visited[candidate] = true;
                    if (core[candidate])
                    {
                        foreach (int neighbour in neighbours[candidate])
                        {
                            if (queued.Add(neighbour))
                            {
                                queue.Enqueue(neighbour);
                            }
                        }
                    }
                }

                if (labels[candidate] < 0)
                {
                    labels[candidate] = cluster;
                }
            }
        }

        // DBSCAN's min_samples already implies at least that many density members, but keep the
        // generic policy's minimum-cluster-size safety boundary explicit for future policies.
        foreach (IGrouping<int, int> cluster in labels
            .Select((label, index) => new { label, index })
            .Where(item => item.label >= 0)
            .GroupBy(item => item.label, item => item.index)
            .ToList())
        {
            int[] members = cluster.ToArray();
            if (members.Length >= policy.MinimumClusterSize)
            {
                continue;
            }

            foreach (int member in members)
            {
                labels[member] = -1;
            }
        }

        Dictionary<int, string> keys = labels
            .Select((label, index) => new { label, index })
            .Where(item => item.label >= 0)
            .GroupBy(item => item.label, item => item.index)
            .OrderBy(group => group.Min(index => ordered[index].FaceOccurrenceId.Value))
            .Select((group, ordinal) => new
            {
                Label = group.Key,
                Key = $"cluster-{ordinal + 1:D6}",
            })
            .ToDictionary(item => item.Label, item => item.Key);

        List<ProvisionalFaceClusterComputedMembership> memberships = new(ordered.Count);
        int noiseCount = 0;
        for (int index = 0; index < ordered.Count; index++)
        {
            if (labels[index] < 0)
            {
                noiseCount++;
                memberships.Add(new(
                    ordered[index].FaceOccurrenceId,
                    null,
                    ProvisionalFaceClusterMemberRole.Noise));
                continue;
            }

            memberships.Add(new(
                ordered[index].FaceOccurrenceId,
                keys[labels[index]],
                core[index]
                    ? ProvisionalFaceClusterMemberRole.Core
                    : ProvisionalFaceClusterMemberRole.Border));
        }

        return new ProvisionalFaceClusterComputation(
            ordered.Count,
            keys.Count,
            noiseCount,
            memberships);
    }
}
