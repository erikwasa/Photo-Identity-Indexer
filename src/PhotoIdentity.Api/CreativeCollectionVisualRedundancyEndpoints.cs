using System.Collections.Concurrent;
using PhotoIdentity.Core.Collections;
using PhotoIdentity.Core.Identifiers;
using PhotoIdentity.Imaging.OpenCv;

namespace PhotoIdentity.Api;

public sealed record CreativeVisualRedundancyMemberResponse(
    string RevisionId,
    DateTime TakenAtLocal,
    string Kind,
    string ThumbnailUrl,
    int DistanceFromRepresentative);

public sealed record CreativeVisualRedundancyGroupResponse(
    string Id,
    string MomentId,
    int MemberCount,
    int MaximumPairDistance,
    DateTime StartedAtLocal,
    DateTime EndedAtLocal,
    CreativeVisualRedundancyMemberResponse[] Members);

public sealed record CreativeVisualRedundancyPolicyResponse(
    string PolicyVersion,
    string AlgorithmVersion,
    int MaximumHammingDistance,
    double MaximumCaptureSpanSeconds,
    int RedundantGroupCount,
    int GroupedPhotoCount,
    int SuppressiblePhotoCount,
    int LargestGroupSize,
    int BaselineRepeatedSelectedFrames,
    int RedundancyAwareRepeatedSelectedFrames,
    CreativeVisualRedundancyGroupResponse[] Groups);

public sealed record CreativeVisualRedundancyPreviewResponse(
    string CollectionId,
    string CollectionName,
    int DirectAnchorCount,
    int AddedContextCount,
    int TotalCandidateCount,
    int RequestedTargetCount,
    int BaselineSelectedCount,
    int CandidateSampleCount,
    bool CandidateSampleTruncated,
    int FingerprintedCandidateCount,
    int MissingProxyCount,
    int UnreadableProxyCount,
    string EvidenceSemantics,
    CreativeVisualRedundancyPolicyResponse[] Policies);

public static class CreativeCollectionVisualRedundancyEndpoints
{
    private const int DefaultMaximumCandidates = 200;
    private const int MaximumCandidates = 500;
    private const int HashConcurrency = 4;

    public static IEndpointRouteBuilder MapCreativeCollectionVisualRedundancyEndpoints(
        this IEndpointRouteBuilder endpoints)
    {
        endpoints.MapGet(
            "/api/smart-collections/{id:guid}/creative-redundancy-preview",
            PreviewAsync);
        return endpoints;
    }

    private static async Task<IResult> PreviewAsync(
        Guid id,
        ISmartCollectionRepository definitions,
        ISmartCollectionQueryRepository query,
        CollectionReviewProxyFileResolver proxyResolver,
        CancellationToken cancellationToken,
        int targetCount = 50,
        int momentGapMinutes = 30,
        int maxCandidates = DefaultMaximumCandidates)
    {
        if (id == Guid.Empty)
        {
            return Results.BadRequest(new { error = "Smart collection identifier cannot be empty." });
        }

        try
        {
            CreativeCollectionSelectionPolicy.ValidateTargetCount(targetCount);
            _ = PhotoMomentGapPolicy.CreateTimeGapEvaluation(momentGapMinutes);
        }
        catch (ArgumentException exception)
        {
            return Results.BadRequest(new { error = exception.Message });
        }

        if (maxCandidates is < 1 or > MaximumCandidates)
        {
            return Results.BadRequest(new
            {
                error = $"Visual redundancy candidate sample must be between 1 and {MaximumCandidates}.",
            });
        }

        SmartCollectionId collectionId = SmartCollectionId.From(id);
        SmartCollectionDefinition? definition =
            await definitions.GetAsync(collectionId, cancellationToken);
        if (definition is null)
        {
            return Results.NotFound();
        }

        SmartCollectionSlideshowSnapshot? anchorSnapshot =
            await query.CreateSlideshowSnapshotAsync(collectionId, cancellationToken);
        if (anchorSnapshot is null)
        {
            return Results.NotFound();
        }

        PhotoMomentGapPolicy momentPolicy =
            PhotoMomentGapPolicy.CreateTimeGapEvaluation(momentGapMinutes);
        if (anchorSnapshot.RevisionIds.Count == 0)
        {
            return Results.Ok(new CreativeVisualRedundancyPreviewResponse(
                definition.Id.ToString(),
                definition.Name,
                0,
                0,
                0,
                targetCount,
                0,
                0,
                false,
                0,
                0,
                0,
                "Derived from local review proxies only; no source identity, Smart Collection membership or moment truth is changed.",
                EmptyPolicyResponses()));
        }

        IReadOnlyList<SmartCollectionPhoto> cataloguePhotos = await query.QueryAllAsync(
            new SmartCollectionFilter(),
            cancellationToken);
        PhotoMomentCandidate[] momentCandidates = cataloguePhotos
            .Select(photo => new PhotoMomentCandidate(
                photo.RevisionId,
                photo.TakenAtLocal,
                PeopleKeys: photo.PeopleKeys,
                Latitude: photo.Latitude,
                Longitude: photo.Longitude))
            .ToArray();
        PhotoMomentClusteringResult moments = PhotoMomentClusterer.Cluster(
            momentCandidates,
            momentPolicy);
        CreativeCollectionCandidateSet generated = CreativeCollectionCandidateGenerator.Generate(
            momentCandidates,
            anchorSnapshot.RevisionIds,
            moments,
            CreativeCollectionContextPolicy.BalancedV1);
        CreativeCollectionSelectionResult baseline = CreativeCollectionSelector.Select(
            generated,
            momentCandidates,
            moments,
            targetCount,
            CreativeCollectionSelectionPolicy.BalancedV1);

        CreativeCollectionCandidate[] sample = generated.Candidates
            .OrderBy(candidate => candidate.TakenAtLocal.HasValue ? 0 : 1)
            .ThenBy(candidate => candidate.TakenAtLocal ?? DateTime.MaxValue)
            .ThenBy(candidate => candidate.RevisionId.ToString(), StringComparer.Ordinal)
            .Take(maxCandidates)
            .ToArray();

        FingerprintSample fingerprints = await FingerprintAsync(
            sample,
            proxyResolver,
            cancellationToken);

        Dictionary<AssetRevisionId, string> kindByRevision = generated.Candidates
            .ToDictionary(candidate => candidate.RevisionId, candidate => candidate.Kind);

        CreativeVisualRedundancyPolicyResponse[] policies = PhotoVisualRedundancyPolicy.EvaluationPolicies
            .Select(policy => EvaluatePolicy(
                policy,
                fingerprints.Fingerprints,
                moments,
                generated,
                momentCandidates,
                baseline,
                kindByRevision,
                targetCount))
            .ToArray();

        return Results.Ok(new CreativeVisualRedundancyPreviewResponse(
            definition.Id.ToString(),
            definition.Name,
            generated.DirectAnchorCount,
            generated.AddedContextCount,
            generated.TotalCandidateCount,
            targetCount,
            baseline.SelectedCount,
            sample.Length,
            generated.TotalCandidateCount > sample.Length,
            fingerprints.Fingerprints.Count,
            fingerprints.MissingProxyCount,
            fingerprints.UnreadableProxyCount,
            "Derived from local review proxies only; no source identity, Smart Collection membership or moment truth is changed.",
            policies));
    }

    private static CreativeVisualRedundancyPolicyResponse EvaluatePolicy(
        PhotoVisualRedundancyPolicy policy,
        IReadOnlyList<PhotoVisualFingerprint> fingerprints,
        PhotoMomentClusteringResult moments,
        CreativeCollectionCandidateSet generated,
        IReadOnlyList<PhotoMomentCandidate> catalogue,
        CreativeCollectionSelectionResult baseline,
        IReadOnlyDictionary<AssetRevisionId, string> kindByRevision,
        int targetCount)
    {
        PhotoVisualRedundancyResult redundancy = PhotoVisualRedundancyGrouper.Group(
            fingerprints,
            moments,
            policy);
        CreativeCollectionSelectionResult selected = CreativeCollectionSelector.Select(
            generated,
            catalogue,
            moments,
            redundancy,
            targetCount,
            CreativeCollectionSelectionPolicy.BalancedV1);

        CreativeVisualRedundancyGroupResponse[] groups = redundancy.Groups
            .Select(group => new CreativeVisualRedundancyGroupResponse(
                group.Id,
                group.MomentId,
                group.Members.Count,
                group.MaximumPairDistance,
                group.StartedAtLocal,
                group.EndedAtLocal,
                group.Members
                    .Select(member => new CreativeVisualRedundancyMemberResponse(
                        member.RevisionId.ToString(),
                        member.TakenAtLocal,
                        kindByRevision.GetValueOrDefault(member.RevisionId) ?? "unknown",
                        $"/api/collections/photos/{member.RevisionId}/thumbnail",
                        member.DistanceFromRepresentative))
                    .ToArray()))
            .ToArray();

        return new CreativeVisualRedundancyPolicyResponse(
            policy.Version,
            PhotoVisualRedundancyPolicy.AlgorithmVersion,
            policy.MaximumHammingDistance,
            policy.MaximumCaptureSpan.TotalSeconds,
            redundancy.Groups.Count,
            redundancy.GroupedPhotoCount,
            redundancy.SuppressiblePhotoCount,
            redundancy.Groups.Count == 0 ? 0 : redundancy.Groups.Max(group => group.Members.Count),
            CountRepeatedSelectedFrames(baseline, redundancy),
            CountRepeatedSelectedFrames(selected, redundancy),
            groups);
    }

    private static int CountRepeatedSelectedFrames(
        CreativeCollectionSelectionResult selection,
        PhotoVisualRedundancyResult redundancy)
    {
        HashSet<AssetRevisionId> selected = selection.Selected
            .Select(item => item.Candidate.RevisionId)
            .ToHashSet();
        return redundancy.Groups.Sum(group =>
        {
            int selectedFromGroup = group.Members.Count(member => selected.Contains(member.RevisionId));
            return Math.Max(0, selectedFromGroup - 1);
        });
    }

    private static async Task<FingerprintSample> FingerprintAsync(
        IReadOnlyList<CreativeCollectionCandidate> candidates,
        CollectionReviewProxyFileResolver proxyResolver,
        CancellationToken cancellationToken)
    {
        ConcurrentBag<PhotoVisualFingerprint> fingerprints = [];
        int missing = 0;
        int unreadable = 0;
        using SemaphoreSlim gate = new(HashConcurrency);
        OpenCvPerceptualHashCalculator calculator = new();

        Task[] work = candidates.Select(async candidate =>
        {
            await gate.WaitAsync(cancellationToken);
            try
            {
                CollectionPhotoFile? proxy = await proxyResolver.ResolveAsync(
                    candidate.RevisionId,
                    cancellationToken);
                if (proxy is null)
                {
                    Interlocked.Increment(ref missing);
                    return;
                }

                try
                {
                    PhotoPerceptualHash64 hash = await calculator.ComputeAsync(
                        proxy.Path,
                        cancellationToken);
                    fingerprints.Add(new PhotoVisualFingerprint(
                        candidate.RevisionId,
                        candidate.TakenAtLocal,
                        hash));
                }
                catch (Exception exception) when (
                    exception is InvalidDataException or
                    IOException or
                    UnauthorizedAccessException)
                {
                    Interlocked.Increment(ref unreadable);
                }
            }
            finally
            {
                gate.Release();
            }
        }).ToArray();

        await Task.WhenAll(work);
        PhotoVisualFingerprint[] ordered = fingerprints
            .OrderBy(item => item.TakenAtLocal.HasValue ? 0 : 1)
            .ThenBy(item => item.TakenAtLocal ?? DateTime.MaxValue)
            .ThenBy(item => item.RevisionId.ToString(), StringComparer.Ordinal)
            .ToArray();
        return new FingerprintSample(ordered, missing, unreadable);
    }

    private static CreativeVisualRedundancyPolicyResponse[] EmptyPolicyResponses() =>
        PhotoVisualRedundancyPolicy.EvaluationPolicies
            .Select(policy => new CreativeVisualRedundancyPolicyResponse(
                policy.Version,
                PhotoVisualRedundancyPolicy.AlgorithmVersion,
                policy.MaximumHammingDistance,
                policy.MaximumCaptureSpan.TotalSeconds,
                0,
                0,
                0,
                0,
                0,
                0,
                []))
            .ToArray();

    private sealed record FingerprintSample(
        IReadOnlyList<PhotoVisualFingerprint> Fingerprints,
        int MissingProxyCount,
        int UnreadableProxyCount);
}
