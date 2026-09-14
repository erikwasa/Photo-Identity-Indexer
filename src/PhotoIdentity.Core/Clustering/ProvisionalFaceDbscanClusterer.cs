using System.Numerics;

namespace PhotoIdentity.Core.Clustering;

/// <summary>
/// Deterministic exact cosine-distance DBSCAN used by the initial M25 production policy.
/// Pairwise work is quadratic, matching the WI-0113 evaluation semantics, while normalized SIMD
/// dot products and bounded parallel row chunks avoid the much slower scalar reference path.
/// Memory stays bounded by the face cap and an explicit sparse-neighbour edge budget rather than
/// retaining a dense distance matrix. Inputs are sorted by stable face-occurrence ID before
/// cluster expansion.
/// </summary>
public sealed class ProvisionalFaceDbscanClusterer
{
    public const int DefaultMaximumUndirectedNeighborEdges = 2_000_000;
    private const int PairwiseRowChunkSize = 64;

    private readonly int _maximumUndirectedNeighborEdges;
    private readonly int _maximumDegreeOfParallelism;

    public ProvisionalFaceDbscanClusterer(
        int maximumUndirectedNeighborEdges = DefaultMaximumUndirectedNeighborEdges,
        int? maximumDegreeOfParallelism = null)
    {
        if (maximumUndirectedNeighborEdges < 1)
        {
            throw new ArgumentOutOfRangeException(nameof(maximumUndirectedNeighborEdges));
        }

        int degree = maximumDegreeOfParallelism ?? Math.Max(1, Environment.ProcessorCount - 1);
        if (degree < 1)
        {
            throw new ArgumentOutOfRangeException(nameof(maximumDegreeOfParallelism));
        }

        _maximumUndirectedNeighborEdges = maximumUndirectedNeighborEdges;
        _maximumDegreeOfParallelism = degree;
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

        float[][] normalized = ordered
            .Select(face => Normalize(face.Embedding))
            .ToArray();
        var forwardNeighbours = new List<int>[ordered.Count];
        for (int index = 0; index < forwardNeighbours.Length; index++)
        {
            forwardNeighbours[index] = [];
        }

        int undirectedEdgeCount = 0;
        int overflow = 0;
        ParallelOptions parallelOptions = new()
        {
            CancellationToken = cancellationToken,
            MaxDegreeOfParallelism = _maximumDegreeOfParallelism,
        };

        for (int chunkStart = 0; chunkStart < ordered.Count; chunkStart += PairwiseRowChunkSize)
        {
            cancellationToken.ThrowIfCancellationRequested();
            int chunkEnd = Math.Min(ordered.Count, chunkStart + PairwiseRowChunkSize);
            Parallel.For(chunkStart, chunkEnd, parallelOptions, left =>
            {
                float[] leftVector = normalized[left];
                List<int> local = forwardNeighbours[left];
                for (int right = left + 1; right < normalized.Length; right++)
                {
                    if (Volatile.Read(ref overflow) != 0)
                    {
                        break;
                    }

                    float similarity = Dot(leftVector, normalized[right]);
                    double distance = Math.Clamp(1d - similarity, 0d, 2d);
                    if (distance > eps)
                    {
                        continue;
                    }

                    int edgeCount = Interlocked.Increment(ref undirectedEdgeCount);
                    if (edgeCount > _maximumUndirectedNeighborEdges)
                    {
                        Volatile.Write(ref overflow, 1);
                        break;
                    }

                    local.Add(right);
                }
            });

            if (Volatile.Read(ref overflow) != 0)
            {
                throw new InvalidOperationException(
                    $"Provisional clustering exceeded the bounded neighbour budget of {_maximumUndirectedNeighborEdges:N0} exact edges. " +
                    "Do not silently weaken the DBSCAN policy; measure the production workload before changing retrieval/indexing strategy.");
            }

            if (reportProgress is not null)
            {
                await reportProgress(chunkEnd, cancellationToken);
            }
        }

        var neighbours = new List<int>[ordered.Count];
        for (int index = 0; index < ordered.Count; index++)
        {
            neighbours[index] = [index];
        }

        for (int left = 0; left < forwardNeighbours.Length; left++)
        {
            foreach (int right in forwardNeighbours[left])
            {
                neighbours[left].Add(right);
                neighbours[right].Add(left);
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

    private static float[] Normalize(PhotoIdentity.Core.Recognition.EmbeddingVector embedding)
    {
        float[] values = embedding.ToArray();
        float inverseNorm = (float)(1d / embedding.L2Norm);
        for (int index = 0; index < values.Length; index++)
        {
            values[index] *= inverseNorm;
        }

        return values;
    }

    private static float Dot(float[] left, float[] right)
    {
        int vectorWidth = Vector<float>.Count;
        int index = 0;
        float sum = 0;
        for (; index <= left.Length - vectorWidth; index += vectorWidth)
        {
            sum += Vector.Dot(
                new Vector<float>(left, index),
                new Vector<float>(right, index));
        }

        for (; index < left.Length; index++)
        {
            sum += left[index] * right[index];
        }

        return sum;
    }
}
