using Microsoft.Extensions.Configuration;
using PhotoIdentity.Core.Clustering;
using PhotoIdentity.Core.Identifiers;
using PhotoIdentity.Core.Recognition;
using PhotoIdentity.Core.Review;
using PhotoIdentity.Persistence.Postgres;

namespace PhotoIdentity.Api;

public static class ProvisionalFaceClusterEndpoints
{
    public static IEndpointRouteBuilder MapProvisionalFaceClusterEndpoints(this IEndpointRouteBuilder endpoints)
    {
        RouteGroupBuilder group = endpoints.MapGroup("/api/review/provisional-clusters");
        group.MapGet("/models", ListModelsAsync);
        group.MapGet("", GetAsync);
        group.MapPost("", StartAsync);
        group.MapGet("/groups", ListGroupsAsync);
        group.MapGet("/review-groups", ListReviewGroupsAsync);
        group.MapGet("/review-groups/{derivedClusterKey}/members", ListReviewGroupMembersAsync);
        group.MapGet("/review-groups/{derivedClusterKey}/known-person-advisory", GetKnownPersonAdvisoryAsync);
        group.MapPost("/review-groups/{derivedClusterKey}/not-same", RecordNotSameAsync);
        return endpoints;
    }

    private static async Task<IResult> ListModelsAsync(
        IServiceProvider services,
        CancellationToken cancellationToken)
    {
        IProvisionalFaceClusterRepository? repository = ResolveRepository(services);
        IIdentityMatchModelRepository? models = services.GetService<IIdentityMatchModelRepository>();
        if (repository is null || models is null)
        {
            return PostgreSqlRequired();
        }

        IReadOnlyList<ReviewIdentityMatchModelRevision> revisions = await models.ListAsync(cancellationToken);
        return Results.Ok(revisions.Select(model => new
        {
            ModelId = model.ModelId.ToString(),
            ModelHash = model.ModelHash.ToString(),
            model.FaceCount,
        }));
    }

    private static async Task<IResult> GetAsync(
        IServiceProvider services,
        string? modelId,
        string? modelHash,
        bool includeUnknown = false,
        CancellationToken cancellationToken = default)
    {
        IProvisionalFaceClusterRepository? repository = ResolveRepository(services);
        if (repository is null)
        {
            return PostgreSqlRequired();
        }

        if (!TryModelRevision(modelId, modelHash, out ModelId parsedModelId, out Sha256Digest parsedModelHash))
        {
            return BadRequest("An exact embedding model revision is required.");
        }

        ProvisionalFaceClusterPolicy policy = ProvisionalFaceClusterPolicies.InitialDbscan;
        ProvisionalFaceClusterRun? run = await repository.GetLatestAsync(
            parsedModelId,
            parsedModelHash,
            policy.Version,
            includeUnknown,
            cancellationToken);
        if (run is null)
        {
            return Results.Ok(new
            {
                ModelId = parsedModelId.ToString(),
                ModelHash = parsedModelHash.ToString(),
                PolicyVersion = policy.Version,
                policy.Algorithm,
                policy.DistanceThreshold,
                policy.MinimumSamples,
                IncludeUnknown = includeUnknown,
                Status = "not-run",
                IsActive = false,
                IsCurrent = false,
                TargetCount = 0,
                ProcessedTargetCount = 0,
                ClusterCount = 0,
                NoiseCount = 0,
                RequestedAtUtc = (DateTimeOffset?)null,
                StartedAtUtc = (DateTimeOffset?)null,
                CompletedAtUtc = (DateTimeOffset?)null,
                UpdatedAtUtc = (DateTimeOffset?)null,
                Error = (string?)null,
            });
        }

        bool evidenceMatches = await repository.EvidenceStillMatchesAsync(run, cancellationToken);
        bool current = string.Equals(run.Status, ProvisionalFaceClusterRunStatuses.Completed, StringComparison.Ordinal)
            && evidenceMatches;
        return Results.Ok(ToResponse(run, current));
    }

    private static async Task<IResult> StartAsync(
        StartProvisionalFaceClusterRequest request,
        IServiceProvider services,
        TimeProvider timeProvider,
        string? modelId,
        string? modelHash,
        CancellationToken cancellationToken)
    {
        IProvisionalFaceClusterRepository? repository = ResolveRepository(services);
        if (repository is null)
        {
            return PostgreSqlRequired();
        }

        if (!TryModelRevision(modelId, modelHash, out ModelId parsedModelId, out Sha256Digest parsedModelHash))
        {
            return BadRequest("An exact embedding model revision is required.");
        }

        if (request is null || string.IsNullOrWhiteSpace(request.Actor))
        {
            return BadRequest("A clustering actor is required.");
        }

        try
        {
            ProvisionalFaceClusterRun run = await repository.StartAsync(
                parsedModelId,
                parsedModelHash,
                ProvisionalFaceClusterPolicies.InitialDbscan,
                request.IncludeUnknown,
                request.Actor,
                timeProvider.GetUtcNow(),
                cancellationToken);
            return Results.Accepted(value: ToResponse(run, current: false));
        }
        catch (InvalidOperationException exception) when (
            exception.Message.Contains("active", StringComparison.OrdinalIgnoreCase))
        {
            return Results.Conflict(new { error = exception.Message });
        }
        catch (ArgumentException exception)
        {
            return BadRequest(exception.Message);
        }
    }

    private static async Task<IResult> ListGroupsAsync(
        IServiceProvider services,
        string? modelId,
        string? modelHash,
        bool includeUnknown = false,
        int maximumGroups = 500,
        CancellationToken cancellationToken = default)
    {
        IProvisionalFaceClusterRepository? repository = ResolveRepository(services);
        if (repository is null)
        {
            return PostgreSqlRequired();
        }

        if (!TryModelRevision(modelId, modelHash, out ModelId parsedModelId, out Sha256Digest parsedModelHash))
        {
            return BadRequest("An exact embedding model revision is required.");
        }

        try
        {
            IReadOnlyList<ProvisionalFaceClusterGroupSummary> groups = await repository.ListCurrentGroupsAsync(
                parsedModelId,
                parsedModelHash,
                ProvisionalFaceClusterPolicies.InitialDbscan.Version,
                includeUnknown,
                maximumGroups,
                cancellationToken);
            return Results.Ok(groups);
        }
        catch (ArgumentOutOfRangeException exception)
        {
            return BadRequest(exception.Message);
        }
    }

    private static async Task<IResult> ListReviewGroupsAsync(
        IServiceProvider services,
        string? modelId,
        string? modelHash,
        bool includeUnknown = false,
        int offset = 0,
        int limit = 30,
        CancellationToken cancellationToken = default)
    {
        IProvisionalFaceClusterReviewRepository? repository = ResolveReviewRepository(services);
        if (repository is null)
        {
            return PostgreSqlRequired();
        }

        if (!TryModelRevision(modelId, modelHash, out ModelId parsedModelId, out Sha256Digest parsedModelHash))
        {
            return BadRequest("An exact embedding model revision is required.");
        }

        try
        {
            ProvisionalFaceClusterReviewGroupPage page = await repository.ListCurrentGroupsAsync(
                parsedModelId,
                parsedModelHash,
                ProvisionalFaceClusterPolicies.InitialDbscan.Version,
                includeUnknown,
                offset,
                limit,
                cancellationToken);
            return Results.Ok(new
            {
                Items = page.Items.Select(group => new
                {
                    group.RunId,
                    group.DerivedClusterKey,
                    group.MemberCount,
                    group.CoreCount,
                    group.BorderCount,
                    group.CoreShare,
                    Status = "provisional",
                    RepresentativeFaceIds = group.RepresentativeFaceIds.Select(face => face.ToString()).ToArray(),
                    RepresentativeImageUrls = group.RepresentativeFaceIds
                        .Select(face => $"/api/review/faces/{face}/image")
                        .ToArray(),
                }).ToArray(),
                page.Offset,
                page.Limit,
                page.Total,
            });
        }
        catch (ArgumentOutOfRangeException exception)
        {
            return BadRequest(exception.Message);
        }
    }

    private static async Task<IResult> ListReviewGroupMembersAsync(
        string derivedClusterKey,
        IServiceProvider services,
        string? modelId,
        string? modelHash,
        bool includeUnknown = false,
        int offset = 0,
        int limit = 200,
        CancellationToken cancellationToken = default)
    {
        IProvisionalFaceClusterReviewRepository? repository = ResolveReviewRepository(services);
        if (repository is null)
        {
            return PostgreSqlRequired();
        }

        if (!TryModelRevision(modelId, modelHash, out ModelId parsedModelId, out Sha256Digest parsedModelHash))
        {
            return BadRequest("An exact embedding model revision is required.");
        }

        try
        {
            IReadOnlyList<ProvisionalFaceClusterReviewMember> members = await repository.ListCurrentGroupMembersAsync(
                parsedModelId,
                parsedModelHash,
                ProvisionalFaceClusterPolicies.InitialDbscan.Version,
                includeUnknown,
                derivedClusterKey,
                offset,
                limit,
                cancellationToken);
            return Results.Ok(members.Select(member => new
            {
                member.RunId,
                member.DerivedClusterKey,
                FaceId = member.FaceOccurrenceId.ToString(),
                Role = member.Role.ToString().ToLowerInvariant(),
                ImageUrl = $"/api/review/faces/{member.FaceOccurrenceId}/image",
                DetailsUrl = $"/faces/{member.FaceOccurrenceId}",
            }));
        }
        catch (ArgumentException exception)
        {
            return BadRequest(exception.Message);
        }
    }

    private static async Task<IResult> GetKnownPersonAdvisoryAsync(
        string derivedClusterKey,
        IServiceProvider services,
        string? modelId,
        string? modelHash,
        bool includeUnknown = false,
        CancellationToken cancellationToken = default)
    {
        IProvisionalFaceClusterKnownPersonAdvisoryRepository? repository =
            ResolveKnownPersonAdvisoryRepository(services);
        if (repository is null)
        {
            return PostgreSqlRequired();
        }

        if (!TryModelRevision(modelId, modelHash, out ModelId parsedModelId, out Sha256Digest parsedModelHash))
        {
            return BadRequest("An exact embedding model revision is required.");
        }

        try
        {
            ProvisionalFaceClusterKnownPersonAdvisory? advisory = await repository.GetCurrentAsync(
                parsedModelId,
                parsedModelHash,
                ProvisionalFaceClusterPolicies.InitialDbscan.Version,
                includeUnknown,
                derivedClusterKey,
                cancellationToken);
            if (advisory is null)
            {
                return Results.NotFound(new { error = "The requested current provisional cluster was not found." });
            }

            return Results.Ok(new
            {
                advisory.ClusterRunId,
                ModelId = advisory.ModelId.ToString(),
                ModelHash = advisory.ModelHash.ToString(),
                advisory.ClusterPolicyVersion,
                advisory.IncludeUnknown,
                advisory.DerivedClusterKey,
                advisory.AdvisoryPolicyVersion,
                advisory.IdentitySuggestionPolicyVersion,
                advisory.MemberCount,
                advisory.CoreCount,
                advisory.CoreShare,
                advisory.InternalConflictCount,
                advisory.RankedEvidenceCount,
                advisory.RankedEvidenceCoverage,
                advisory.QualifyingEvidenceCount,
                advisory.Status,
                advisory.Explanation,
                advisory.CanonicalAssignmentAllowed,
                Candidate = ToAdvisoryCandidate(advisory.Candidate),
                CompetingCandidate = ToAdvisoryCandidate(advisory.CompetingCandidate),
                advisory.EvaluatedAtUtc,
            });
        }
        catch (ArgumentException exception)
        {
            return BadRequest(exception.Message);
        }
    }

    private static async Task<IResult> RecordNotSameAsync(
        string derivedClusterKey,
        RecordProvisionalFaceNotSameRequest request,
        IServiceProvider services,
        TimeProvider timeProvider,
        string? modelId,
        string? modelHash,
        bool includeUnknown = false,
        CancellationToken cancellationToken = default)
    {
        IProvisionalFaceClusterReviewRepository? reviewRepository = ResolveReviewRepository(services);
        IProvisionalFaceClusterRepository? clusterRepository = ResolveRepository(services);
        if (reviewRepository is null || clusterRepository is null)
        {
            return PostgreSqlRequired();
        }

        if (!TryModelRevision(modelId, modelHash, out ModelId parsedModelId, out Sha256Digest parsedModelHash))
        {
            return BadRequest("An exact embedding model revision is required.");
        }
        if (request is null || string.IsNullOrWhiteSpace(request.Actor))
        {
            return BadRequest("A review actor is required.");
        }
        if (!Guid.TryParse(request.AnchorFaceId, out Guid anchorGuid))
        {
            return BadRequest("A valid anchor face is required.");
        }

        List<FaceOccurrenceId> others = [];
        foreach (string faceId in request.OtherFaceIds ?? [])
        {
            if (!Guid.TryParse(faceId, out Guid parsed))
            {
                return BadRequest($"Face '{faceId}' is not a valid face occurrence identifier.");
            }
            others.Add(FaceOccurrenceId.From(parsed));
        }

        try
        {
            int recorded = await reviewRepository.RecordNotSameAsync(
                parsedModelId,
                parsedModelHash,
                ProvisionalFaceClusterPolicies.InitialDbscan.Version,
                includeUnknown,
                derivedClusterKey,
                FaceOccurrenceId.From(anchorGuid),
                others,
                request.Actor,
                timeProvider.GetUtcNow(),
                cancellationToken);

            bool refreshQueued = await QueueConstraintRefreshAsync(
                clusterRepository,
                parsedModelId,
                parsedModelHash,
                includeUnknown,
                request.Actor,
                timeProvider.GetUtcNow(),
                cancellationToken);

            return Results.Accepted(value: new
            {
                RecordedCount = recorded,
                RefreshQueued = refreshQueued,
                Message = refreshQueued
                    ? "Not-same evidence recorded and a replacement provisional-cluster run was queued."
                    : "Not-same evidence recorded; an equivalent replacement run is already active.",
            });
        }
        catch (ArgumentException exception)
        {
            return BadRequest(exception.Message);
        }
        catch (InvalidOperationException exception)
        {
            return Results.Conflict(new { error = exception.Message });
        }
    }

    private static async Task<bool> QueueConstraintRefreshAsync(
        IProvisionalFaceClusterRepository repository,
        ModelId modelId,
        Sha256Digest modelHash,
        bool includeUnknown,
        string actor,
        DateTimeOffset now,
        CancellationToken cancellationToken)
    {
        ProvisionalFaceClusterPolicy policy = ProvisionalFaceClusterPolicies.InitialDbscan;
        ProvisionalFaceClusterRun? latest = await repository.GetLatestAsync(
            modelId,
            modelHash,
            policy.Version,
            includeUnknown,
            cancellationToken);

        if (latest is { IsActive: true })
        {
            await repository.MarkFailedAsync(
                latest.Id,
                "Superseded by explicit not-same discovery feedback; replacement clustering is required.",
                now,
                cancellationToken);
        }

        try
        {
            _ = await repository.StartAsync(
                modelId,
                modelHash,
                policy,
                includeUnknown,
                actor,
                now,
                cancellationToken);
            return true;
        }
        catch (InvalidOperationException exception) when (
            exception.Message.Contains("active", StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }
    }

    private static IProvisionalFaceClusterRepository? ResolveRepository(IServiceProvider services)
    {
        if (!UsesPostgres(services))
        {
            return null;
        }

        IProvisionalFaceClusterRepository? registered = services.GetService<IProvisionalFaceClusterRepository>();
        if (registered is not null)
        {
            return registered;
        }

        PostgresCatalogueDatabase? database = services.GetService<PostgresCatalogueDatabase>();
        return database is null ? null : new PostgresProvisionalFaceClusterRepository(database);
    }

    private static IProvisionalFaceClusterReviewRepository? ResolveReviewRepository(IServiceProvider services)
    {
        if (!UsesPostgres(services))
        {
            return null;
        }

        IProvisionalFaceClusterReviewRepository? registered = services.GetService<IProvisionalFaceClusterReviewRepository>();
        if (registered is not null)
        {
            return registered;
        }

        PostgresCatalogueDatabase? database = services.GetService<PostgresCatalogueDatabase>();
        return database is null ? null : new PostgresProvisionalFaceClusterReviewRepository(database);
    }

    private static IProvisionalFaceClusterKnownPersonAdvisoryRepository? ResolveKnownPersonAdvisoryRepository(
        IServiceProvider services)
    {
        if (!UsesPostgres(services))
        {
            return null;
        }

        IProvisionalFaceClusterKnownPersonAdvisoryRepository? registered =
            services.GetService<IProvisionalFaceClusterKnownPersonAdvisoryRepository>();
        if (registered is not null)
        {
            return registered;
        }

        PostgresCatalogueDatabase? database = services.GetService<PostgresCatalogueDatabase>();
        if (database is null)
        {
            return null;
        }

        IIdentitySuggestionPolicyRepository policies =
            services.GetService<IIdentitySuggestionPolicyRepository>()
            ?? new PostgresIdentitySuggestionPolicyRepository(database, services.GetService<TimeProvider>());
        return new PostgresProvisionalFaceClusterKnownPersonAdvisoryRepository(
            database,
            policies,
            services.GetService<TimeProvider>());
    }

    private static bool UsesPostgres(IServiceProvider services)
    {
        return services.GetService<PostgresCatalogueDatabase>() is not null;
    }

    private static object? ToAdvisoryCandidate(ProvisionalFaceClusterKnownPersonCandidate? candidate) =>
        candidate is null
            ? null
            : new
            {
                PersonId = candidate.PersonId.ToString(),
                candidate.DisplayName,
                candidate.SupportCount,
                candidate.SupportShare,
                candidate.OrdinaryHighCount,
                candidate.OrdinaryMediumCount,
                candidate.MinimumScore,
                candidate.MedianScore,
                candidate.MaximumScore,
                candidate.MedianMargin,
            };

    private static object ToResponse(ProvisionalFaceClusterRun run, bool current) => new
    {
        RunId = run.Id,
        ModelId = run.ModelId.ToString(),
        ModelHash = run.ModelHash.ToString(),
        PolicyVersion = run.Policy.Version,
        run.Policy.Algorithm,
        run.Policy.DistanceThreshold,
        run.Policy.MinimumSamples,
        run.Policy.MinimumClusterSize,
        run.IncludeUnknown,
        run.Status,
        run.IsActive,
        IsCurrent = current,
        run.TargetCount,
        run.ProcessedTargetCount,
        run.ClusterCount,
        run.NoiseCount,
        run.RequestedAtUtc,
        run.StartedAtUtc,
        run.CompletedAtUtc,
        run.UpdatedAtUtc,
        run.Error,
    };

    private static bool TryModelRevision(
        string? modelId,
        string? modelHash,
        out ModelId parsedModelId,
        out Sha256Digest parsedModelHash)
    {
        parsedModelId = default;
        parsedModelHash = default;
        if (string.IsNullOrWhiteSpace(modelId) || string.IsNullOrWhiteSpace(modelHash))
        {
            return false;
        }

        try
        {
            parsedModelId = new ModelId(modelId);
            parsedModelHash = new Sha256Digest(modelHash);
            return true;
        }
        catch (ArgumentException)
        {
            return false;
        }
    }

    private static IResult PostgreSqlRequired() => Results.Problem(
        statusCode: StatusCodes.Status409Conflict,
        detail: "Provisional clustering is available only when PostgreSQL is the selected catalogue provider.");

    private static IResult BadRequest(string message) => Results.BadRequest(new { error = message });

    public sealed record StartProvisionalFaceClusterRequest(string Actor, bool IncludeUnknown = false);

    public sealed record RecordProvisionalFaceNotSameRequest(
        string AnchorFaceId,
        IReadOnlyList<string> OtherFaceIds,
        string Actor);
}
