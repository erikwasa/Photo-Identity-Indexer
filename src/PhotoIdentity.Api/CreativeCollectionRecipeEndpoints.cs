using PhotoIdentity.Core.Collections;

namespace PhotoIdentity.Api;

public sealed record CreativeCollectionSearchAnchorRequest(
    string Query,
    string Mode = PhotoSearchModes.Combined,
    string? SmartCollectionId = null,
    int AnchorLimit = CreativeCollectionSearchAnchor.DefaultLimit);

public sealed record CreativeCollectionRecipeRequest(
    int TargetCount = CreativeCollectionRecipe.DefaultTargetCount,
    string ContextStrength = "balanced",
    bool NoveltyEnabled = false,
    string? Name = null,
    CreativeCollectionSearchAnchorRequest? SearchAnchor = null);

public sealed record CreativeCollectionSearchAnchorResponse(
    string Query,
    string Mode,
    string? SmartCollectionId,
    string? SmartCollectionName,
    int AnchorLimit,
    string PolicyVersion);

public sealed record CreativeCollectionRecipeResponse(
    string Id,
    string Name,
    string? AnchorCollectionId,
    string? AnchorCollectionName,
    int TargetCount,
    int MomentGapMinutes,
    string MomentPolicyVersion,
    string ContextStrength,
    string ContextPolicyVersion,
    string SelectionPolicyVersion,
    string OrderingPolicyVersion,
    bool NoveltyEnabled,
    string NoveltyPolicyVersion,
    DateTimeOffset CreatedAtUtc,
    DateTimeOffset UpdatedAtUtc,
    string AnchorKind = CreativeCollectionAnchorKinds.SmartCollection,
    CreativeCollectionSearchAnchorResponse? SearchAnchor = null);

public static class CreativeCollectionRecipeEndpoints
{
    public static IEndpointRouteBuilder MapCreativeCollectionRecipeEndpoints(
        this IEndpointRouteBuilder endpoints)
    {
        endpoints.MapGet("/api/creative-collections", ListAsync);
        endpoints.MapPost("/api/creative-collections", CreateSearchAsync);
        endpoints.MapPost("/api/creative-collections/preview", PreviewSearchAsync);
        endpoints.MapGet("/api/creative-collections/{id:guid}", GetByIdAsync);
        endpoints.MapPut("/api/creative-collections/{id:guid}", UpdateByIdAsync);
        endpoints.MapDelete("/api/creative-collections/{id:guid}", DeleteByIdAsync);
        endpoints.MapGet("/api/creative-collections/{id:guid}/preview", PreviewByIdAsync);
        endpoints.MapPost("/api/creative-collections/{id:guid}/slideshow-snapshot", CreateSlideshowSnapshotByIdAsync);
        endpoints.MapGet("/api/smart-collections/{id:guid}/creative-collections", ListForAnchorAsync);
        endpoints.MapPost("/api/smart-collections/{id:guid}/creative-collections", CreateForAnchorAsync);

        // Compatibility routes retained for original WI-0121 callers and the existing slideshow player.
        endpoints.MapGet("/api/smart-collections/{id:guid}/creative-recipe", GetAsync);
        endpoints.MapPut("/api/smart-collections/{id:guid}/creative-recipe", UpsertAsync);
        endpoints.MapDelete("/api/smart-collections/{id:guid}/creative-recipe", DeleteAsync);
        endpoints.MapGet("/api/smart-collections/{id:guid}/creative-recipe/preview", PreviewAsync);
        endpoints.MapPost("/api/smart-collections/{id:guid}/creative-recipe/slideshow-snapshot", CreateSlideshowSnapshotAsync);
        return endpoints;
    }

    private static async Task<IResult> ListAsync(
        ISmartCollectionRepository definitions,
        ICreativeCollectionRecipeRepository recipes,
        CancellationToken cancellationToken)
    {
        IReadOnlyList<CreativeCollectionRecipe> items = await recipes.ListAsync(cancellationToken);
        return Results.Ok(await ToResponsesAsync(items, definitions, cancellationToken));
    }

    private static async Task<IResult> ListForAnchorAsync(
        Guid id,
        ISmartCollectionRepository definitions,
        ICreativeCollectionRecipeRepository recipes,
        CancellationToken cancellationToken)
    {
        if (!TryGetId(id, out SmartCollectionId collectionId, out IResult? error))
        {
            return error!;
        }

        SmartCollectionDefinition? definition = await definitions.GetAsync(collectionId, cancellationToken);
        if (definition is null)
        {
            return Results.NotFound();
        }

        IReadOnlyList<CreativeCollectionRecipe> items =
            await recipes.ListForAnchorAsync(collectionId, cancellationToken);
        return Results.Ok(items.Select(recipe => ToResponse(recipe, definition.Name, null)).ToArray());
    }

    private static async Task<IResult> GetByIdAsync(
        Guid id,
        ISmartCollectionRepository definitions,
        ICreativeCollectionRecipeRepository recipes,
        CancellationToken cancellationToken)
    {
        if (!TryGetCreativeId(id, out CreativeCollectionId creativeId, out IResult? error))
        {
            return error!;
        }

        CreativeCollectionRecipe? recipe = await recipes.GetAsync(creativeId, cancellationToken);
        if (recipe is null)
        {
            return Results.NotFound();
        }

        CreativeCollectionRecipeResponse? response =
            await ToResponseAsync(recipe, definitions, cancellationToken);
        return response is null ? Results.NotFound() : Results.Ok(response);
    }

    private static async Task<IResult> CreateSearchAsync(
        CreativeCollectionRecipeRequest request,
        ISmartCollectionRepository definitions,
        ICreativeCollectionRecipeRepository recipes,
        CancellationToken cancellationToken)
    {
        if (request.SearchAnchor is null)
        {
            return Results.BadRequest(new
            {
                error = "Creating a Creative Collection without a Smart Collection route requires a search anchor.",
            });
        }

        try
        {
            string name = CreativeCollectionName.Parse(request.Name ?? string.Empty).DisplayValue;
            CreativeCollectionSearchAnchor searchAnchor = ParseSearchAnchor(request.SearchAnchor);
            SmartCollectionDefinition? scope = await ResolveScopeDefinitionAsync(
                searchAnchor,
                definitions,
                cancellationToken);
            if (searchAnchor.ScopeCollectionId is not null && scope is null)
            {
                return Results.NotFound(new { error = "The selected Smart Collection search scope was not found." });
            }

            CreativeCollectionRecipe recipe = await recipes.CreateSearchAsync(
                name,
                searchAnchor,
                Settings(request),
                cancellationToken);
            return Results.Created(
                $"/api/creative-collections/{recipe.Id}",
                ToResponse(recipe, null, scope?.Name));
        }
        catch (Exception exception) when (exception is ArgumentException or InvalidDataException)
        {
            return Results.BadRequest(new { error = exception.Message });
        }
    }

    private static async Task<IResult> PreviewSearchAsync(
        CreativeCollectionRecipeRequest request,
        ISmartCollectionRepository definitions,
        CreativeCollectionMaterializationService materializer,
        TimeProvider timeProvider,
        CancellationToken cancellationToken)
    {
        if (request.SearchAnchor is null)
        {
            return Results.BadRequest(new { error = "A search anchor is required for a search Creative Collection preview." });
        }

        try
        {
            CreativeCollectionSearchAnchor searchAnchor = ParseSearchAnchor(request.SearchAnchor);
            SmartCollectionDefinition? scope = await ResolveScopeDefinitionAsync(
                searchAnchor,
                definitions,
                cancellationToken);
            if (searchAnchor.ScopeCollectionId is not null && scope is null)
            {
                return Results.NotFound(new { error = "The selected Smart Collection search scope was not found." });
            }

            CreativeCollectionRecipeSettings settings = Settings(request);
            DateTimeOffset now = timeProvider.GetUtcNow().ToUniversalTime();
            CreativeCollectionRecipe transient = new(
                CreativeCollectionId.New(),
                string.IsNullOrWhiteSpace(request.Name)
                    ? "Search Creative preview"
                    : CreativeCollectionName.Parse(request.Name).DisplayValue,
                AnchorCollectionId: null,
                settings.TargetCount,
                settings.MomentGapMinutes,
                settings.MomentPolicyVersion,
                settings.ContextPolicyVersion,
                settings.SelectionPolicyVersion,
                settings.OrderingPolicyVersion,
                settings.NoveltyEnabled,
                now,
                now,
                searchAnchor);

            return await CreativeCollectionPreviewEndpoints.WithMaterializationDeadlineAsync(async token =>
            {
                CreativeCollectionMaterialization? materialized =
                    await materializer.MaterializeAsync(transient, token);
                return materialized is null
                    ? Results.NotFound()
                    : Results.Ok(CreativeCollectionPreviewEndpoints.ToPreviewResponse(materialized));
            }, cancellationToken);
        }
        catch (PhotoSearchScopeNotFoundException)
        {
            return Results.NotFound(new { error = "The selected Smart Collection search scope was not found." });
        }
        catch (Exception exception) when (exception is ArgumentException or InvalidDataException)
        {
            return Results.BadRequest(new { error = exception.Message });
        }
    }

    private static async Task<IResult> CreateForAnchorAsync(
        Guid id,
        CreativeCollectionRecipeRequest request,
        ISmartCollectionRepository definitions,
        ICreativeCollectionRecipeRepository recipes,
        CancellationToken cancellationToken)
    {
        if (!TryGetId(id, out SmartCollectionId collectionId, out IResult? error))
        {
            return error!;
        }
        if (request.SearchAnchor is not null)
        {
            return Results.BadRequest(new
            {
                error = "A Smart-anchored Creative Collection cannot also define a search anchor.",
            });
        }

        SmartCollectionDefinition? definition = await definitions.GetAsync(collectionId, cancellationToken);
        if (definition is null)
        {
            return Results.NotFound();
        }

        try
        {
            string name = CreativeCollectionName.Parse(request.Name ?? string.Empty).DisplayValue;
            CreativeCollectionRecipe recipe = await recipes.CreateAsync(
                collectionId,
                name,
                Settings(request),
                cancellationToken);
            return Results.Created(
                $"/api/creative-collections/{recipe.Id}",
                ToResponse(recipe, definition.Name, null));
        }
        catch (Exception exception) when (exception is ArgumentException or InvalidDataException)
        {
            return Results.BadRequest(new { error = exception.Message });
        }
    }

    private static async Task<IResult> UpdateByIdAsync(
        Guid id,
        CreativeCollectionRecipeRequest request,
        ISmartCollectionRepository definitions,
        ICreativeCollectionRecipeRepository recipes,
        CancellationToken cancellationToken)
    {
        if (!TryGetCreativeId(id, out CreativeCollectionId creativeId, out IResult? error))
        {
            return error!;
        }

        CreativeCollectionRecipe? existing = await recipes.GetAsync(creativeId, cancellationToken);
        if (existing is null)
        {
            return Results.NotFound();
        }

        try
        {
            string name = CreativeCollectionName.Parse(request.Name ?? string.Empty).DisplayValue;
            CreativeCollectionRecipe recipe;
            string? smartAnchorName = null;
            string? searchScopeName = null;

            if (existing.SearchAnchor is not null)
            {
                if (request.SearchAnchor is null)
                {
                    return Results.BadRequest(new
                    {
                        error = "A search-anchored Creative Collection must retain its search anchor.",
                    });
                }

                CreativeCollectionSearchAnchor searchAnchor = ParseSearchAnchor(request.SearchAnchor);
                SmartCollectionDefinition? scope = await ResolveScopeDefinitionAsync(
                    searchAnchor,
                    definitions,
                    cancellationToken);
                if (searchAnchor.ScopeCollectionId is not null && scope is null)
                {
                    return Results.NotFound(new { error = "The selected Smart Collection search scope was not found." });
                }

                recipe = await recipes.UpdateSearchAsync(
                    creativeId,
                    name,
                    searchAnchor,
                    Settings(request),
                    cancellationToken);
                searchScopeName = scope?.Name;
            }
            else
            {
                if (request.SearchAnchor is not null)
                {
                    return Results.BadRequest(new
                    {
                        error = "Changing an existing Smart anchor into a search anchor is not supported. Create a new Creative Collection instead.",
                    });
                }

                SmartCollectionId anchorId = existing.AnchorCollectionId
                    ?? throw new InvalidDataException("Smart-anchored Creative Collection is missing its anchor.");
                SmartCollectionDefinition? definition =
                    await definitions.GetAsync(anchorId, cancellationToken);
                if (definition is null)
                {
                    return Results.NotFound();
                }

                recipe = await recipes.UpdateAsync(
                    creativeId,
                    name,
                    Settings(request),
                    cancellationToken);
                smartAnchorName = definition.Name;
            }

            return Results.Ok(ToResponse(recipe, smartAnchorName, searchScopeName));
        }
        catch (KeyNotFoundException)
        {
            return Results.NotFound();
        }
        catch (Exception exception) when (exception is ArgumentException or InvalidDataException)
        {
            return Results.BadRequest(new { error = exception.Message });
        }
    }

    private static async Task<IResult> DeleteByIdAsync(
        Guid id,
        ICreativeCollectionRecipeRepository recipes,
        CancellationToken cancellationToken)
    {
        if (!TryGetCreativeId(id, out CreativeCollectionId creativeId, out IResult? error))
        {
            return error!;
        }

        return await recipes.DeleteAsync(creativeId, cancellationToken)
            ? Results.NoContent()
            : Results.NotFound();
    }

    private static async Task<IResult> PreviewByIdAsync(
        Guid id,
        ICreativeCollectionRecipeRepository recipes,
        CreativeCollectionMaterializationService materializer,
        CancellationToken cancellationToken)
    {
        if (!TryGetCreativeId(id, out CreativeCollectionId creativeId, out IResult? error))
        {
            return error!;
        }

        CreativeCollectionRecipe? recipe = await recipes.GetAsync(creativeId, cancellationToken);
        if (recipe is null)
        {
            return Results.NotFound();
        }

        return await CreativeCollectionPreviewEndpoints.WithMaterializationDeadlineAsync(async token =>
        {
            CreativeCollectionMaterialization? materialized =
                await materializer.MaterializeAsync(recipe, token);
            if (materialized is null)
            {
                return Results.NotFound();
            }

            CreativeCollectionPreviewResponse response =
                CreativeCollectionPreviewEndpoints.ToPreviewResponse(materialized) with
                {
                    CollectionId = recipe.Id.ToString(),
                    CollectionName = recipe.Name,
                };
            return Results.Ok(response);
        }, cancellationToken);
    }

    private static async Task<IResult> CreateSlideshowSnapshotByIdAsync(
        Guid id,
        ICreativeCollectionRecipeRepository recipes,
        CreativeCollectionMaterializationService materializer,
        TimeProvider timeProvider,
        CancellationToken cancellationToken)
    {
        if (!TryGetCreativeId(id, out CreativeCollectionId creativeId, out IResult? error))
        {
            return error!;
        }

        CreativeCollectionRecipe? recipe = await recipes.GetAsync(creativeId, cancellationToken);
        if (recipe is null)
        {
            return Results.NotFound();
        }

        return await MaterializeSnapshotAsync(recipe, materializer, timeProvider, cancellationToken);
    }

    private static async Task<IResult> GetAsync(
        Guid id,
        ISmartCollectionRepository definitions,
        ICreativeCollectionRecipeRepository recipes,
        CancellationToken cancellationToken)
    {
        if (!TryGetId(id, out SmartCollectionId collectionId, out IResult? error))
        {
            return error!;
        }

        SmartCollectionDefinition? definition = await definitions.GetAsync(collectionId, cancellationToken);
        if (definition is null)
        {
            return Results.NotFound();
        }

        CreativeCollectionRecipe? recipe = await recipes.GetAsync(collectionId, cancellationToken);
        return recipe is null
            ? Results.NotFound()
            : Results.Ok(ToResponse(recipe, definition.Name, null));
    }

    private static async Task<IResult> UpsertAsync(
        Guid id,
        CreativeCollectionRecipeRequest request,
        ISmartCollectionRepository definitions,
        ICreativeCollectionRecipeRepository recipes,
        CancellationToken cancellationToken)
    {
        if (!TryGetId(id, out SmartCollectionId collectionId, out IResult? error))
        {
            return error!;
        }
        if (request.SearchAnchor is not null)
        {
            return Results.BadRequest(new
            {
                error = "The legacy Smart Collection recipe route does not accept a search anchor.",
            });
        }

        SmartCollectionDefinition? definition = await definitions.GetAsync(collectionId, cancellationToken);
        if (definition is null)
        {
            return Results.NotFound();
        }

        try
        {
            CreativeCollectionRecipe? existing = await recipes.GetAsync(collectionId, cancellationToken);
            CreativeCollectionRecipe recipe;
            if (!string.IsNullOrWhiteSpace(request.Name) && existing is not null)
            {
                recipe = await recipes.UpdateAsync(existing.Id, request.Name, Settings(request), cancellationToken);
            }
            else if (!string.IsNullOrWhiteSpace(request.Name))
            {
                recipe = await recipes.CreateAsync(collectionId, request.Name, Settings(request), cancellationToken);
            }
            else
            {
                recipe = await recipes.UpsertAsync(collectionId, Settings(request), cancellationToken);
            }

            return Results.Ok(ToResponse(recipe, definition.Name, null));
        }
        catch (Exception exception) when (exception is ArgumentException or InvalidDataException)
        {
            return Results.BadRequest(new { error = exception.Message });
        }
    }

    private static async Task<IResult> DeleteAsync(
        Guid id,
        ICreativeCollectionRecipeRepository recipes,
        CancellationToken cancellationToken)
    {
        if (!TryGetId(id, out SmartCollectionId collectionId, out IResult? error))
        {
            return error!;
        }

        return await recipes.DeleteAsync(collectionId, cancellationToken)
            ? Results.NoContent()
            : Results.NotFound();
    }

    private static async Task<IResult> PreviewAsync(
        Guid id,
        ICreativeCollectionRecipeRepository recipes,
        CreativeCollectionMaterializationService materializer,
        CancellationToken cancellationToken)
    {
        if (!TryGetId(id, out SmartCollectionId collectionId, out IResult? error))
        {
            return error!;
        }

        CreativeCollectionRecipe? recipe = await recipes.GetAsync(collectionId, cancellationToken);
        if (recipe is null)
        {
            return Results.NotFound();
        }

        return await CreativeCollectionPreviewEndpoints.WithMaterializationDeadlineAsync(async token =>
        {
            CreativeCollectionMaterialization? materialized =
                await materializer.MaterializeAsync(collectionId, Settings(recipe), token);
            return materialized is null
                ? Results.NotFound()
                : Results.Ok(CreativeCollectionPreviewEndpoints.ToPreviewResponse(materialized));
        }, cancellationToken);
    }

    private static async Task<IResult> CreateSlideshowSnapshotAsync(
        Guid id,
        ICreativeCollectionRecipeRepository recipes,
        CreativeCollectionMaterializationService materializer,
        TimeProvider timeProvider,
        CancellationToken cancellationToken)
    {
        if (id == Guid.Empty)
        {
            return Results.BadRequest(new { error = "Creative collection identifier cannot be empty." });
        }

        SmartCollectionId anchorId = SmartCollectionId.From(id);
        CreativeCollectionRecipe? recipe = await recipes.GetAsync(anchorId, cancellationToken);
        if (recipe is null)
        {
            recipe = await recipes.GetAsync(CreativeCollectionId.From(id), cancellationToken);
        }

        return recipe is null
            ? Results.NotFound()
            : await MaterializeSnapshotAsync(recipe, materializer, timeProvider, cancellationToken);
    }

    private static async Task<IResult> MaterializeSnapshotAsync(
        CreativeCollectionRecipe recipe,
        CreativeCollectionMaterializationService materializer,
        TimeProvider timeProvider,
        CancellationToken cancellationToken)
    {
        return await CreativeCollectionPreviewEndpoints.WithMaterializationDeadlineAsync(async token =>
        {
            CreativeCollectionMaterialization? materialized =
                await materializer.MaterializeAsync(recipe, token);
            if (materialized is null)
            {
                return Results.NotFound();
            }

            SmartCollectionSlideshowSnapshotResponse response =
                CreativeCollectionPreviewEndpoints.ToSnapshotResponse(
                    materialized,
                    timeProvider.GetUtcNow().ToUniversalTime()) with
                {
                    CollectionId = recipe.Id.ToString(),
                    CollectionName = recipe.Name,
                };
            return Results.Ok(response);
        }, cancellationToken);
    }

    private static CreativeCollectionRecipeSettings Settings(CreativeCollectionRecipeRequest request)
    {
        CreativeCollectionContextPolicy contextPolicy =
            CreativeCollectionContextPolicy.FromStrength(request.ContextStrength);
        return CreativeCollectionRecipeSettings.Create(
            request.TargetCount,
            contextPolicy.Version,
            request.NoveltyEnabled);
    }

    private static CreativeCollectionRecipeSettings Settings(CreativeCollectionRecipe recipe) => new(
        recipe.TargetCount,
        recipe.MomentGapMinutes,
        recipe.MomentPolicyVersion,
        recipe.ContextPolicyVersion,
        recipe.SelectionPolicyVersion,
        recipe.OrderingPolicyVersion,
        recipe.NoveltyEnabled);

    private static CreativeCollectionSearchAnchor ParseSearchAnchor(
        CreativeCollectionSearchAnchorRequest request)
    {
        SmartCollectionId? scopeId = null;
        if (!string.IsNullOrWhiteSpace(request.SmartCollectionId))
        {
            if (!Guid.TryParse(request.SmartCollectionId.Trim(), out Guid parsed) || parsed == Guid.Empty)
            {
                throw new ArgumentException(
                    "Search anchor Smart Collection scope must be a valid non-empty GUID.",
                    nameof(request));
            }

            scopeId = SmartCollectionId.From(parsed);
        }

        return CreativeCollectionSearchAnchor.Create(
            request.Query,
            request.Mode,
            scopeId,
            request.AnchorLimit);
    }

    private static async Task<SmartCollectionDefinition?> ResolveScopeDefinitionAsync(
        CreativeCollectionSearchAnchor searchAnchor,
        ISmartCollectionRepository definitions,
        CancellationToken cancellationToken) =>
        searchAnchor.ScopeCollectionId is SmartCollectionId scopeId
            ? await definitions.GetAsync(scopeId, cancellationToken)
            : null;

    private static async Task<CreativeCollectionRecipeResponse[]> ToResponsesAsync(
        IReadOnlyList<CreativeCollectionRecipe> recipes,
        ISmartCollectionRepository definitions,
        CancellationToken cancellationToken)
    {
        List<CreativeCollectionRecipeResponse> responses = [];
        foreach (CreativeCollectionRecipe recipe in recipes)
        {
            CreativeCollectionRecipeResponse? response =
                await ToResponseAsync(recipe, definitions, cancellationToken);
            if (response is not null)
            {
                responses.Add(response);
            }
        }

        return responses.ToArray();
    }

    private static async Task<CreativeCollectionRecipeResponse?> ToResponseAsync(
        CreativeCollectionRecipe recipe,
        ISmartCollectionRepository definitions,
        CancellationToken cancellationToken)
    {
        if (recipe.AnchorCollectionId is SmartCollectionId anchorId)
        {
            SmartCollectionDefinition? definition =
                await definitions.GetAsync(anchorId, cancellationToken);
            return definition is null
                ? null
                : ToResponse(recipe, definition.Name, null);
        }

        string? scopeName = null;
        if (recipe.SearchAnchor?.ScopeCollectionId is SmartCollectionId scopeId)
        {
            SmartCollectionDefinition? scope =
                await definitions.GetAsync(scopeId, cancellationToken);
            if (scope is null)
            {
                return null;
            }
            scopeName = scope.Name;
        }

        return ToResponse(recipe, null, scopeName);
    }

    private static CreativeCollectionRecipeResponse ToResponse(
        CreativeCollectionRecipe recipe,
        string? anchorCollectionName,
        string? searchScopeName)
    {
        CreativeCollectionSearchAnchorResponse? searchAnchor = recipe.SearchAnchor is null
            ? null
            : new CreativeCollectionSearchAnchorResponse(
                recipe.SearchAnchor.Query,
                recipe.SearchAnchor.Mode,
                recipe.SearchAnchor.ScopeCollectionId?.ToString(),
                searchScopeName,
                recipe.SearchAnchor.Limit,
                recipe.SearchAnchor.PolicyVersion);

        return new CreativeCollectionRecipeResponse(
            recipe.Id.ToString(),
            recipe.Name,
            recipe.AnchorCollectionId?.ToString(),
            anchorCollectionName,
            recipe.TargetCount,
            recipe.MomentGapMinutes,
            recipe.MomentPolicyVersion,
            CreativeCollectionContextPolicy.StrengthForVersion(recipe.ContextPolicyVersion),
            recipe.ContextPolicyVersion,
            recipe.SelectionPolicyVersion,
            recipe.OrderingPolicyVersion,
            recipe.NoveltyEnabled,
            recipe.NoveltyEnabled
                ? CreativeCollectionNoveltyPolicies.BalancedV1
                : CreativeCollectionNoveltyPolicies.Disabled,
            recipe.CreatedAtUtc,
            recipe.UpdatedAtUtc,
            recipe.AnchorKind,
            searchAnchor);
    }

    private static bool TryGetId(Guid id, out SmartCollectionId collectionId, out IResult? error)
    {
        if (id == Guid.Empty)
        {
            collectionId = default;
            error = Results.BadRequest(new { error = "Smart collection identifier cannot be empty." });
            return false;
        }

        collectionId = SmartCollectionId.From(id);
        error = null;
        return true;
    }

    private static bool TryGetCreativeId(Guid id, out CreativeCollectionId collectionId, out IResult? error)
    {
        if (id == Guid.Empty)
        {
            collectionId = default;
            error = Results.BadRequest(new { error = "Creative collection identifier cannot be empty." });
            return false;
        }

        collectionId = CreativeCollectionId.From(id);
        error = null;
        return true;
    }
}
