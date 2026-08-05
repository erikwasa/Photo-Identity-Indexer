namespace PhotoIdentity.Api;

internal static class DetectorEvaluationMatching
{
    public static StoredDetectorEvaluationComparisonPhoto BuildPhoto(
        StoredDetectorGroundTruthPhoto groundTruthPhoto,
        string candidateRevisionId,
        IReadOnlyList<StoredDetectorEvaluationCandidateDetection> candidateDetections,
        double iouThreshold)
    {
        ArgumentNullException.ThrowIfNull(groundTruthPhoto);
        ArgumentException.ThrowIfNullOrWhiteSpace(candidateRevisionId);
        ArgumentNullException.ThrowIfNull(candidateDetections);
        if (iouThreshold is <= 0 or > 1)
        {
            throw new ArgumentOutOfRangeException(nameof(iouThreshold), "IoU threshold must be greater than zero and at most one.");
        }

        StoredDetectorGroundTruthFace[] groundTruthFaces = groundTruthPhoto.Faces
            .OrderBy(face => face.Id, StringComparer.Ordinal)
            .ToArray();
        StoredDetectorEvaluationCandidateDetection[] candidates = candidateDetections
            .OrderBy(candidate => candidate.FaceNumber)
            .ThenBy(candidate => candidate.Id, StringComparer.Ordinal)
            .ToArray();

        List<MatchEdge> edges = [];
        for (int groundTruthIndex = 0; groundTruthIndex < groundTruthFaces.Length; groundTruthIndex++)
        {
            for (int candidateIndex = 0; candidateIndex < candidates.Length; candidateIndex++)
            {
                double iou = CalculateIou(groundTruthFaces[groundTruthIndex], candidates[candidateIndex]);
                if (iou + 1e-12 >= iouThreshold)
                {
                    edges.Add(new MatchEdge(groundTruthIndex, candidateIndex, iou));
                }
            }
        }

        List<StoredDetectorEvaluationAutomaticMatch> automaticMatches = [];
        List<ComponentBuilder> components = BuildConnectedComponents(
            groundTruthFaces.Length,
            candidates.Length,
            edges);
        List<StoredDetectorEvaluationExceptionComponent> exceptions = [];
        int exceptionNumber = 0;

        foreach (ComponentBuilder component in components
                     .OrderBy(value => ComponentSortKey(value, groundTruthFaces, candidates), StringComparer.Ordinal))
        {
            if (component.GroundTruthIndices.Count == 1 && component.CandidateIndices.Count == 1)
            {
                int groundTruthIndex = component.GroundTruthIndices[0];
                int candidateIndex = component.CandidateIndices[0];
                MatchEdge edge = component.Edges.Single();
                automaticMatches.Add(new StoredDetectorEvaluationAutomaticMatch
                {
                    GroundTruthFaceId = groundTruthFaces[groundTruthIndex].Id,
                    CandidateDetectionId = candidates[candidateIndex].Id,
                    Iou = edge.Iou,
                });
                continue;
            }

            exceptionNumber++;
            string kind = component.Edges.Count == 0
                ? DetectorEvaluationComparisonKinds.Unmatched
                : component.GroundTruthIndices.Count == 1 && component.CandidateIndices.Count > 1
                    ? DetectorEvaluationComparisonKinds.Duplicate
                    : DetectorEvaluationComparisonKinds.Ambiguous;
            exceptions.Add(new StoredDetectorEvaluationExceptionComponent
            {
                Id = $"exception-{exceptionNumber:D3}",
                Kind = kind,
                GroundTruthFaceIds = component.GroundTruthIndices
                    .Select(index => groundTruthFaces[index].Id)
                    .OrderBy(value => value, StringComparer.Ordinal)
                    .ToList(),
                CandidateDetectionIds = component.CandidateIndices
                    .Select(index => candidates[index].Id)
                    .OrderBy(value => candidates.First(candidate => candidate.Id == value).FaceNumber)
                    .ThenBy(value => value, StringComparer.Ordinal)
                    .ToList(),
            });
        }

        return new StoredDetectorEvaluationComparisonPhoto
        {
            CandidateRevisionId = candidateRevisionId,
            RevisionSha256 = groundTruthPhoto.RevisionSha256,
            PhotoName = groundTruthPhoto.PhotoName,
            SampleId = groundTruthPhoto.SampleId,
            SampleGroup = groundTruthPhoto.SampleGroup,
            SourceGroup = groundTruthPhoto.SourceGroup,
            PrimaryCategory = groundTruthPhoto.PrimaryCategory,
            CountableFaces = groundTruthPhoto.CountableFaces,
            GroundTruthFaces = groundTruthFaces.ToList(),
            CandidateDetections = candidates.ToList(),
            AutomaticMatches = automaticMatches
                .OrderBy(match => match.GroundTruthFaceId, StringComparer.Ordinal)
                .ThenBy(match => match.CandidateDetectionId, StringComparer.Ordinal)
                .ToList(),
            ExceptionComponents = exceptions,
        };
    }

    public static double CalculateIou(
        StoredDetectorGroundTruthFace groundTruth,
        StoredDetectorEvaluationCandidateDetection candidate)
    {
        double left = Math.Max(groundTruth.X, candidate.X);
        double top = Math.Max(groundTruth.Y, candidate.Y);
        double right = Math.Min(groundTruth.X + groundTruth.Width, candidate.X + candidate.Width);
        double bottom = Math.Min(groundTruth.Y + groundTruth.Height, candidate.Y + candidate.Height);
        double intersectionWidth = Math.Max(0, right - left);
        double intersectionHeight = Math.Max(0, bottom - top);
        double intersection = intersectionWidth * intersectionHeight;
        double union = (groundTruth.Width * groundTruth.Height) +
                       (candidate.Width * candidate.Height) -
                       intersection;
        return union <= 0 ? 0 : intersection / union;
    }

    private static List<ComponentBuilder> BuildConnectedComponents(
        int groundTruthCount,
        int candidateCount,
        IReadOnlyList<MatchEdge> edges)
    {
        List<MatchEdge>[] groundTruthEdges = Enumerable.Range(0, groundTruthCount)
            .Select(_ => new List<MatchEdge>())
            .ToArray();
        List<MatchEdge>[] candidateEdges = Enumerable.Range(0, candidateCount)
            .Select(_ => new List<MatchEdge>())
            .ToArray();
        foreach (MatchEdge edge in edges)
        {
            groundTruthEdges[edge.GroundTruthIndex].Add(edge);
            candidateEdges[edge.CandidateIndex].Add(edge);
        }

        bool[] visitedGroundTruth = new bool[groundTruthCount];
        bool[] visitedCandidates = new bool[candidateCount];
        List<ComponentBuilder> components = [];

        for (int groundTruthIndex = 0; groundTruthIndex < groundTruthCount; groundTruthIndex++)
        {
            if (visitedGroundTruth[groundTruthIndex])
            {
                continue;
            }

            if (groundTruthEdges[groundTruthIndex].Count == 0)
            {
                visitedGroundTruth[groundTruthIndex] = true;
                components.Add(new ComponentBuilder([groundTruthIndex], [], []));
                continue;
            }

            components.Add(WalkComponent(
                groundTruthIndex,
                startsWithGroundTruth: true,
                groundTruthEdges,
                candidateEdges,
                visitedGroundTruth,
                visitedCandidates));
        }

        for (int candidateIndex = 0; candidateIndex < candidateCount; candidateIndex++)
        {
            if (visitedCandidates[candidateIndex])
            {
                continue;
            }

            if (candidateEdges[candidateIndex].Count == 0)
            {
                visitedCandidates[candidateIndex] = true;
                components.Add(new ComponentBuilder([], [candidateIndex], []));
                continue;
            }

            components.Add(WalkComponent(
                candidateIndex,
                startsWithGroundTruth: false,
                groundTruthEdges,
                candidateEdges,
                visitedGroundTruth,
                visitedCandidates));
        }

        return components;
    }

    private static ComponentBuilder WalkComponent(
        int startIndex,
        bool startsWithGroundTruth,
        IReadOnlyList<MatchEdge>[] groundTruthEdges,
        IReadOnlyList<MatchEdge>[] candidateEdges,
        bool[] visitedGroundTruth,
        bool[] visitedCandidates)
    {
        Queue<(bool GroundTruth, int Index)> queue = new();
        queue.Enqueue((startsWithGroundTruth, startIndex));
        HashSet<int> groundTruthIndices = [];
        HashSet<int> candidateIndices = [];
        HashSet<MatchEdge> componentEdges = [];

        while (queue.Count > 0)
        {
            (bool groundTruth, int index) = queue.Dequeue();
            if (groundTruth)
            {
                if (visitedGroundTruth[index])
                {
                    continue;
                }

                visitedGroundTruth[index] = true;
                groundTruthIndices.Add(index);
                foreach (MatchEdge edge in groundTruthEdges[index])
                {
                    componentEdges.Add(edge);
                    if (!visitedCandidates[edge.CandidateIndex])
                    {
                        queue.Enqueue((false, edge.CandidateIndex));
                    }
                }
            }
            else
            {
                if (visitedCandidates[index])
                {
                    continue;
                }

                visitedCandidates[index] = true;
                candidateIndices.Add(index);
                foreach (MatchEdge edge in candidateEdges[index])
                {
                    componentEdges.Add(edge);
                    if (!visitedGroundTruth[edge.GroundTruthIndex])
                    {
                        queue.Enqueue((true, edge.GroundTruthIndex));
                    }
                }
            }
        }

        return new ComponentBuilder(
            groundTruthIndices.OrderBy(value => value).ToList(),
            candidateIndices.OrderBy(value => value).ToList(),
            componentEdges
                .OrderByDescending(edge => edge.Iou)
                .ThenBy(edge => edge.GroundTruthIndex)
                .ThenBy(edge => edge.CandidateIndex)
                .ToList());
    }

    private static string ComponentSortKey(
        ComponentBuilder component,
        IReadOnlyList<StoredDetectorGroundTruthFace> groundTruthFaces,
        IReadOnlyList<StoredDetectorEvaluationCandidateDetection> candidates)
    {
        string groundTruthKey = component.GroundTruthIndices.Count == 0
            ? "~"
            : component.GroundTruthIndices.Select(index => groundTruthFaces[index].Id).OrderBy(value => value, StringComparer.Ordinal).First();
        string candidateKey = component.CandidateIndices.Count == 0
            ? "9999999999"
            : component.CandidateIndices.Select(index => candidates[index].FaceNumber).Min().ToString("D10");
        return $"{groundTruthKey}|{candidateKey}";
    }

    private sealed record MatchEdge(int GroundTruthIndex, int CandidateIndex, double Iou);

    private sealed record ComponentBuilder(
        IReadOnlyList<int> GroundTruthIndices,
        IReadOnlyList<int> CandidateIndices,
        IReadOnlyList<MatchEdge> Edges);
}
