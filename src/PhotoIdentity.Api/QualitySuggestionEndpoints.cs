using System.Security.Cryptography;
using PhotoIdentity.Core.Identifiers;
using PhotoIdentity.Core.Imaging;
using PhotoIdentity.Imaging.OpenCv;
using PhotoIdentity.Persistence.Postgres;

namespace PhotoIdentity.Api;

/// <summary>
/// Explicit operator-triggered scan of bounded EXISTING current review proxies.
/// All routes are suggestion/review-only. There is deliberately no purge,
/// hide, removal, scheduling, source hydration or collection mutation route.
/// </summary>
public static class QualitySuggestionEndpoints
{
    private const string ProfileId = "jpeg-1600-q78";

    public static IEndpointRouteBuilder MapQualitySuggestionEndpoints(
        this IEndpointRouteBuilder endpoints)
    {
        endpoints.MapGet("/api/quality-suggestions", ListAsync);
        endpoints.MapPost("/api/quality-suggestions/scan", ScanAsync);
        endpoints.MapPut("/api/quality-suggestions/context/{scope}", SetContextAsync);
        endpoints.MapPut("/api/quality-suggestions/review/{revisionId}", ReviewAsync);
        return endpoints;
    }

    private static async Task<IResult> ListAsync(
        QualitySuggestionWorkspace store, string? scope, string? reason,
        string? filter, int? offset, int? limit, CancellationToken cancellationToken)
    {
        try
        {
            QualitySuggestionSnapshot state = await store.ReadAsync(cancellationToken);
            return Results.Ok(QualitySuggestionWorkspace.Page(
                state, scope ?? "archive-cleanup", reason ?? "all", filter ?? "all",
                offset ?? 0, limit ?? 30));
        }
        catch (ArgumentException)
        {
            return Results.BadRequest(new { error = "Invalid suggestion filters." });
        }
    }

    private static async Task<IResult> ReviewAsync(
        string revisionId, QualitySuggestionDecisionRequest request,
        QualitySuggestionWorkspace store,
        CollectionReviewProxyFileResolver resolver,
        CancellationToken cancellationToken)
    {
        if (!Guid.TryParse(revisionId, out Guid guid) || guid == Guid.Empty ||
            !QualitySuggestionWorkspace.Scopes.Contains(request.Scope) ||
            !QualitySuggestionWorkspace.Decisions.Contains(request.Decision))
            return Results.BadRequest(new { error = "Invalid manual review action." });

        // Avoid adding a decision to a photo whose source copy was excluded,
        // whose revision became stale, or whose durable proxy disappeared.
        ResolvedCollectionReviewProxy? current = await resolver.ResolveWithMetadataAsync(
            AssetRevisionId.From(guid), cancellationToken);
        if (current is null || current.Metadata.ProfileId != ProfileId)
            return Results.Conflict(new { error = "Photo is no longer eligible for review." });
        try
        {
            await store.DecideAsync(request.Scope, revisionId.ToLowerInvariant(),
                request.Decision, cancellationToken);
            return Results.Ok(new { recorded = true, excluded = false });
        }
        catch (InvalidOperationException)
        {
            return Results.Conflict(new { error = "Suggestion is unavailable or the context is off." });
        }
        catch (ArgumentException)
        {
            return Results.BadRequest(new { error = "Invalid manual review action." });
        }
    }

    private static async Task<IResult> SetContextAsync(
        string scope, QualitySuggestionContextRequest request,
        QualitySuggestionWorkspace store, CancellationToken cancellationToken)
    {
        try
        {
            QualitySuggestionSnapshot state = await store.SetScopeAsync(
                scope, request.Enabled, cancellationToken);
            return Results.Ok(new
            {
                enabledScopes = state.EnabledScopes,
                productionFilteringChanged = false
            });
        }
        catch (ArgumentException)
        {
            return Results.BadRequest(new { error = "Unsupported review context." });
        }
    }

    private static async Task<IResult> ScanAsync(
        int? limit,
        PostgresPhotoQualityEvaluationRepository repository,
        CollectionReviewProxyFileResolver resolver,
        ReviewProxyServingConfiguration configuration,
        QualitySuggestionWorkspace store,
        CancellationToken cancellationToken)
    {
        int count = limit ?? 1000;
        if (count is < 1 or > 5000)
            return Results.BadRequest(new { error = "The existing-proxy scan limit must be 1 to 5,000." });

        if (!configuration.IsConfigured || configuration.ProfileId != ProfileId)
            return Results.Conflict(new { error = "Calibrated jpeg-1600-q78 review proxies must be configured." });

        IReadOnlyList<PhotoQualityEvaluationCandidate> population =
            await repository.ReadCurrentProxySampleAsync(ProfileId, count, cancellationToken);

        IReadOnlyDictionary<AssetRevisionId, ResolvedCollectionReviewProxy> eligible =
            await resolver.ResolveManyWithMetadataAsync(
                population.Select(x => x.AssetRevisionId).ToArray(), cancellationToken);

        OpenCvTechnicalPhotoQualityAnalyzer analyzer = new();
        ReviewProxyProfile profile = new(ProfileId, 1600, 78);
        List<QualitySuggestionCandidate> candidates = [];
        int unreadable = 0;

        foreach (PhotoQualityEvaluationCandidate row in population)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (!eligible.TryGetValue(row.AssetRevisionId, out ResolvedCollectionReviewProxy? proxy) ||
                proxy.Metadata.ProfileId != ProfileId)
            {
                unreadable++;
                continue;
            }
            try
            {
                FileInfo info = new(proxy.File.Path);
                if (!info.Exists || info.Length < 1 ||
                    info.Length > 16 * 1024 * 1024 ||
                    info.Length != proxy.Metadata.EncodedByteLength)
                {
                    unreadable++;
                    continue;
                }
                byte[] encoded = await File.ReadAllBytesAsync(proxy.File.Path, cancellationToken);
                if (!string.Equals(
                    Convert.ToHexString(SHA256.HashData(encoded)),
                    proxy.Metadata.ContentHash.ToString(), StringComparison.OrdinalIgnoreCase))
                {
                    unreadable++;
                    continue;
                }
                TechnicalPhotoQualityEvidence evidence =
                    analyzer.Analyze(encoded, profile, cancellationToken);
                string[] known = evidence.Reasons
                    .Where(reason => QualitySuggestionWorkspace.Reasons.ContainsKey(reason))
                    .Distinct(StringComparer.Ordinal).ToArray();
                if (known.Length == 0)
                    continue;
                int priority = known.Max(reason =>
                    QualitySuggestionWorkspace.Reasons[reason].Weight) * 10 + known.Length;
                candidates.Add(new QualitySuggestionCandidate(
                    row.AssetRevisionId.ToString().ToLowerInvariant(), known, priority));
            }
            catch (Exception exception) when (
                exception is IOException or UnauthorizedAccessException or InvalidDataException)
            {
                unreadable++;
            }
        }

        QualitySuggestionCandidate[] ranked = candidates
            .OrderByDescending(c => c.Priority)
            .ThenBy(c => c.RevisionId, StringComparer.Ordinal).Take(1500).ToArray();
        QualitySuggestionSnapshot state =
            await store.ReplaceCandidatesAsync(population.Count, ranked, cancellationToken);
        return Results.Ok(new
        {
            protocol = QualitySuggestionWorkspace.Protocol,
            scannedCurrentProxyCandidates = population.Count,
            suggested = state.Candidates.Count,
            unreadableOrExcluded = unreadable,
            humanDecisionsPreserved = state.Decisions.Count,
            noInferredModelWeights = true,
            noOriginalsHydrated = true,
            exclusionsPerformed = 0,
            productionCollectionChanges = false
        });
    }
}

public sealed record QualitySuggestionDecisionRequest(string Scope, string Decision);
public sealed record QualitySuggestionContextRequest(bool Enabled);
