using PhotoIdentity.Core.Collections;
using PhotoIdentity.Core.Identifiers;
using PhotoIdentity.Core.Sources;
using PhotoIdentity.Persistence.Postgres;

namespace PhotoIdentity.Api;

public sealed record PhotoListCollectionRequest(
    string Name,
    string[]? RevisionIds = null);

public sealed record PhotoListCollectionSortRequest(string Direction);

public sealed record PhotoListCollectionResponse(
    string Id,
    string Name,
    string[] RevisionIds,
    DateTimeOffset CreatedAtUtc,
    DateTimeOffset UpdatedAtUtc);

public static class PhotoListCollectionEndpoints
{
    private const string OldestFirst = "oldest-first";
    private const string NewestFirst = "newest-first";

    public static IEndpointRouteBuilder MapPhotoListCollectionEndpoints(
        this IEndpointRouteBuilder endpoints)
    {
        endpoints.MapPost("/api/photo-list-collections", CreateAsync);
        endpoints.MapGet("/api/photo-list-collections", ListAsync);
        endpoints.MapGet("/api/photo-list-collections/{id:guid}", GetAsync);
        endpoints.MapPut("/api/photo-list-collections/{id:guid}", UpdateAsync);
        endpoints.MapPost("/api/photo-list-collections/{id:guid}/sort", SortAsync);
        endpoints.MapDelete("/api/photo-list-collections/{id:guid}", DeleteAsync);
        endpoints.MapPost(
            "/api/photo-list-collections/{id:guid}/slideshow-snapshot",
            CreateSlideshowSnapshotAsync);
        return endpoints;
    }

    private static async Task<IResult> CreateAsync(
        PhotoListCollectionRequest request,
        IPhotoListCollectionRepository repository,
        ISourceCopyExclusionRepository exclusions,
        CancellationToken cancellationToken)
    {
        try
        {
            AssetRevisionId[] revisionIds = ParseRevisionIds(request.RevisionIds);
            if (await ContainsExcludedAsync(revisionIds, exclusions, cancellationToken))
            {
                return Results.BadRequest(new { error = "One or more revisions are unavailable." });
            }

            PhotoListCollectionDefinition definition = await repository.CreateAsync(
                request.Name,
                revisionIds,
                cancellationToken);
            return Results.Created(
                $"/api/photo-list-collections/{definition.Id}",
                await ToResponseAsync(definition, exclusions, cancellationToken));
        }
        catch (PhotoListCollectionNameConflictException exception)
        {
            return Results.Conflict(new { error = exception.Message });
        }
        catch (PhotoListCollectionRevisionUnavailableException exception)
        {
            return Results.BadRequest(new
            {
                error = exception.Message,
                revisionIds = exception.RevisionIds.Select(item => item.ToString()).ToArray(),
            });
        }
        catch (ArgumentException exception)
        {
            return Results.BadRequest(new { error = exception.Message });
        }
    }

    private static async Task<IResult> ListAsync(
        IPhotoListCollectionRepository repository,
        ISourceCopyExclusionRepository exclusions,
        CancellationToken cancellationToken)
    {
        IReadOnlyList<PhotoListCollectionDefinition> definitions =
            await repository.ListAsync(cancellationToken);
        IReadOnlySet<AssetRevisionId> excluded = await exclusions.GetExcludedRevisionIdsAsync(
            definitions.SelectMany(definition => definition.RevisionIds).Distinct().ToArray(),
            cancellationToken);
        return Results.Ok(definitions.Select(definition => ToResponse(definition, excluded)).ToArray());
    }

    private static async Task<IResult> GetAsync(
        Guid id,
        IPhotoListCollectionRepository repository,
        ISourceCopyExclusionRepository exclusions,
        CancellationToken cancellationToken)
    {
        if (!TryGetId(id, out PhotoListCollectionId collectionId, out IResult? error))
        {
            return error!;
        }

        PhotoListCollectionDefinition? definition =
            await repository.GetAsync(collectionId, cancellationToken);
        return definition is null
            ? Results.NotFound()
            : Results.Ok(await ToResponseAsync(definition, exclusions, cancellationToken));
    }

    private static async Task<IResult> UpdateAsync(
        Guid id,
        PhotoListCollectionRequest request,
        IPhotoListCollectionRepository repository,
        ISourceCopyExclusionRepository exclusions,
        CancellationToken cancellationToken)
    {
        if (!TryGetId(id, out PhotoListCollectionId collectionId, out IResult? error))
        {
            return error!;
        }

        try
        {
            AssetRevisionId[] revisionIds = ParseRevisionIds(request.RevisionIds);
            if (await ContainsExcludedAsync(revisionIds, exclusions, cancellationToken))
            {
                return Results.BadRequest(new { error = "One or more revisions are unavailable." });
            }

            PhotoListCollectionDefinition? definition = await repository.UpdateAsync(
                collectionId,
                request.Name,
                revisionIds,
                cancellationToken);
            return definition is null
                ? Results.NotFound()
                : Results.Ok(await ToResponseAsync(definition, exclusions, cancellationToken));
        }
        catch (PhotoListCollectionNameConflictException exception)
        {
            return Results.Conflict(new { error = exception.Message });
        }
        catch (PhotoListCollectionRevisionUnavailableException exception)
        {
            return Results.BadRequest(new
            {
                error = exception.Message,
                revisionIds = exception.RevisionIds.Select(item => item.ToString()).ToArray(),
            });
        }
        catch (ArgumentException exception)
        {
            return Results.BadRequest(new { error = exception.Message });
        }
    }

    private static async Task<IResult> SortAsync(
        Guid id,
        PhotoListCollectionSortRequest request,
        IPhotoListCollectionRepository repository,
        ISourceCopyExclusionRepository exclusions,
        IServiceProvider services,
        CancellationToken cancellationToken)
    {
        if (!TryGetId(id, out PhotoListCollectionId collectionId, out IResult? error))
        {
            return error!;
        }

        PhotoListCollectionChronologicalOrder order;
        switch (request.Direction?.Trim().ToLowerInvariant())
        {
            case OldestFirst:
                order = PhotoListCollectionChronologicalOrder.OldestFirst;
                break;
            case NewestFirst:
                order = PhotoListCollectionChronologicalOrder.NewestFirst;
                break;
            default:
                return Results.BadRequest(new
                {
                    error = $"Sort direction must be '{OldestFirst}' or '{NewestFirst}'.",
                });
        }

        PhotoListCollectionDefinition? definition =
            await repository.GetAsync(collectionId, cancellationToken);
        if (definition is null)
        {
            return Results.NotFound();
        }

        try
        {
            IPhotoListCollectionCaptureTimeRepository captureTimes =
                ResolveCaptureTimeRepository(services);
            IReadOnlyList<PhotoListCollectionCaptureTime> evidence =
                await captureTimes.GetCaptureTimesAsync(
                    definition.RevisionIds,
                    cancellationToken);
            AssetRevisionId[] sorted = PhotoListCollectionChronologicalOrdering.Apply(
                definition.RevisionIds,
                evidence,
                order);

            PhotoListCollectionDefinition? updated = await repository.UpdateAsync(
                definition.Id,
                definition.Name,
                sorted,
                cancellationToken);
            return updated is null
                ? Results.NotFound()
                : Results.Ok(await ToResponseAsync(updated, exclusions, cancellationToken));
        }
        catch (PhotoListCollectionRevisionUnavailableException exception)
        {
            return Results.BadRequest(new
            {
                error = exception.Message,
                revisionIds = exception.RevisionIds.Select(item => item.ToString()).ToArray(),
            });
        }
        catch (ArgumentException exception)
        {
            return Results.BadRequest(new { error = exception.Message });
        }
    }

    private static async Task<IResult> DeleteAsync(
        Guid id,
        IPhotoListCollectionRepository repository,
        CancellationToken cancellationToken)
    {
        if (!TryGetId(id, out PhotoListCollectionId collectionId, out IResult? error))
        {
            return error!;
        }

        return await repository.DeleteAsync(collectionId, cancellationToken)
            ? Results.NoContent()
            : Results.NotFound();
    }

    private static async Task<IResult> CreateSlideshowSnapshotAsync(
        Guid id,
        IPhotoListCollectionRepository repository,
        ISourceCopyExclusionRepository exclusions,
        CancellationToken cancellationToken)
    {
        if (!TryGetId(id, out PhotoListCollectionId collectionId, out IResult? error))
        {
            return error!;
        }

        PhotoListCollectionSlideshowSnapshot? snapshot =
            await repository.CreateSlideshowSnapshotAsync(
                collectionId,
                cancellationToken);
        if (snapshot is null)
        {
            return Results.NotFound();
        }

        IReadOnlySet<AssetRevisionId> excluded = await exclusions.GetExcludedRevisionIdsAsync(
            snapshot.RevisionIds.ToArray(), cancellationToken);
        SmartCollectionSlideshowSnapshotItemResponse[] items = snapshot.RevisionIds
            .Where(id => !excluded.Contains(id))
            .Select(id => new SmartCollectionSlideshowSnapshotItemResponse(id.ToString()))
            .ToArray();

        return Results.Ok(new SmartCollectionSlideshowSnapshotResponse(
            snapshot.CollectionId.ToString(),
            snapshot.CollectionName,
            snapshot.CreatedAtUtc,
            items,
            items.Length));
    }

    private static IPhotoListCollectionCaptureTimeRepository ResolveCaptureTimeRepository(
        IServiceProvider services)
    {
        IPhotoListCollectionCaptureTimeRepository? configured =
            services.GetService<IPhotoListCollectionCaptureTimeRepository>();
        if (configured is not null)
        {
            return configured;
        }

        PostgresCatalogueDatabase database =
            services.GetRequiredService<PostgresCatalogueDatabase>();
        return new PostgresPhotoListCollectionCaptureTimeRepository(database);
    }

    private static async Task<PhotoListCollectionResponse> ToResponseAsync(
        PhotoListCollectionDefinition definition,
        ISourceCopyExclusionRepository exclusions,
        CancellationToken cancellationToken)
    {
        IReadOnlySet<AssetRevisionId> excluded = await exclusions.GetExcludedRevisionIdsAsync(
            definition.RevisionIds.ToArray(), cancellationToken);
        return ToResponse(definition, excluded);
    }

    private static PhotoListCollectionResponse ToResponse(
        PhotoListCollectionDefinition definition,
        IReadOnlySet<AssetRevisionId> excluded) => new(
            definition.Id.ToString(),
            definition.Name,
            definition.RevisionIds.Where(id => !excluded.Contains(id)).Select(id => id.ToString()).ToArray(),
            definition.CreatedAtUtc,
            definition.UpdatedAtUtc);

    private static async Task<bool> ContainsExcludedAsync(
        IReadOnlyList<AssetRevisionId> revisionIds,
        ISourceCopyExclusionRepository exclusions,
        CancellationToken cancellationToken)
    {
        IReadOnlySet<AssetRevisionId> excluded = await exclusions.GetExcludedRevisionIdsAsync(
            revisionIds.ToArray(), cancellationToken);
        return excluded.Count > 0;
    }

    private static AssetRevisionId[] ParseRevisionIds(string[]? values)
    {
        List<AssetRevisionId> revisionIds = [];
        foreach (string value in values ?? [])
        {
            if (!Guid.TryParse(value, out Guid parsed) || parsed == Guid.Empty)
            {
                throw new ArgumentException(
                    $"Revision identifier '{value}' is not a valid non-empty GUID.",
                    nameof(values));
            }

            revisionIds.Add(AssetRevisionId.From(parsed));
        }

        return PhotoListCollectionDefinition.ValidateRevisionIds(revisionIds);
    }

    private static bool TryGetId(
        Guid value,
        out PhotoListCollectionId id,
        out IResult? error)
    {
        if (value == Guid.Empty)
        {
            id = default;
            error = Results.BadRequest(new
            {
                error = "Photo-list collection identifier cannot be empty.",
            });
            return false;
        }

        id = PhotoListCollectionId.From(value);
        error = null;
        return true;
    }
}
