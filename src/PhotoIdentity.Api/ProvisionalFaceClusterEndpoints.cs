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
            IReadOnlyList<ProvisionalFaceClusterGroupSummary> groups =
                await repository.ListCurrentGroupsAsync(
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

    private static IProvisionalFaceClusterRepository? ResolveRepository(IServiceProvider services)
    {
        IConfiguration? configuration = services.GetService<IConfiguration>();
        if (configuration is null ||
            CataloguePersistenceComposition.ResolveProvider(configuration) != CatalogueProviderKind.Postgres)
        {
            return null;
        }

        IProvisionalFaceClusterRepository? registered =
            services.GetService<IProvisionalFaceClusterRepository>();
        if (registered is not null)
        {
            return registered;
        }

        PostgresCatalogueDatabase? database = services.GetService<PostgresCatalogueDatabase>();
        return database is null ? null : new PostgresProvisionalFaceClusterRepository(database);
    }

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
}
