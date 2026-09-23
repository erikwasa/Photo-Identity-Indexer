using PhotoIdentity.Core.Collections;

namespace PhotoIdentity.Api;

public sealed record CreativeCollectionRecipeRequest(
    int TargetCount = CreativeCollectionRecipe.DefaultTargetCount,
    string ContextStrength = "balanced",
    bool NoveltyEnabled = false,
    string? Name = null);

public sealed record CreativeCollectionRecipeResponse(
    string Id,
    string Name,
    string AnchorCollectionId,
    string AnchorCollectionName,
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
    DateTimeOffset UpdatedAtUtc);

public static class CreativeCollectionRecipeEndpoints
{
    public static IEndpointRouteBuilder MapCreativeCollectionRecipeEndpoints(
        this IEndpointRouteBuilder endpoints)
    {
        // First-class WI-0158 routes.
        endpoints.MapGet("/api/creative-collections", ListAsync);
        endpoints.MapGet("/api/creative-collections/{id:guid}", GetByIdAsync);
        endpoints.MapPut("/api/creative-collections/{id:guid}", UpdateByIdAsync);
        endpoints.MapDelete("/api/creative-collections/{id:guid}", DeleteByIdAsync);
        endpoints.MapGet("/api/creative-collections/{id:guid}/preview", PreviewByIdAsync);
        endpoints.MapPost("/api/creative-collections/{id:guid}/slideshow-snapshot", CreateSlideshowSnapshotByIdAsync);
        endpoints.MapGet("/api/smart-collections/{id:guid}/creative-collections", ListForAnchorAsync);
        endpoints.MapPost("/api/smart-collections/{id:guid}/creative-collections", CreateForAnchorAsync);

        // Compatibility routes retained for the original WI-0121 single-recipe clients.
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
        return Results.Ok(items.Select(recipe => ToResponse(recipe, definition.Name)).ToArray());
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

        SmartCollectionDefinition? definition =
            await definitions.GetAsync(recipe.AnchorCollectionId, cancellationToken);
        return definition is null
            ? Results.NotFound()
            : Results.Ok(ToResponse(recipe, definition.Name));
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
            return Results.Created($"/api/creative-collections/{recipe.Id}", ToResponse(recipe, definition.Name));
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

        SmartCollectionDefinition? definition =
            await definitions.GetAsync(existing.AnchorCollectionId, cancellationToken);
        if (definition is null)
        {
            return Results.NotFound();
        }

        try
        {
            string name = CreativeCollectionName.Parse(request.Name ?? string.Empty).DisplayValue;
            CreativeCollectionRecipe recipe =
                await recipes.UpdateAsync(creativeId, name, Settings(request), cancellationToken);
            return Results.Ok(ToResponse(recipe, definition.Name));
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

        CreativeCollectionMaterialization? materialized = await materializer.MaterializeAsync(
            recipe.AnchorCollectionId,
            Settings(recipe),
            cancellationToken);
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

        CreativeCollectionMaterialization? materialized = await materializer.MaterializeAsync(
            recipe.AnchorCollectionId,
            Settings(recipe),
            cancellationToken);
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
        return recipe is null ? Results.NotFound() : Results.Ok(ToResponse(recipe, definition.Name));
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

            return Results.Ok(ToResponse(recipe, definition.Name));
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

        CreativeCollectionMaterialization? materialized = await materializer.MaterializeAsync(
            collectionId,
            Settings(recipe),
            cancellationToken);
        return materialized is null
            ? Results.NotFound()
            : Results.Ok(CreativeCollectionPreviewEndpoints.ToPreviewResponse(materialized));
    }

    private static async Task<IResult> CreateSlideshowSnapshotAsync(
        Guid id,
        ICreativeCollectionRecipeRepository recipes,
        CreativeCollectionMaterializationService materializer,
        TimeProvider timeProvider,
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

        CreativeCollectionMaterialization? materialized = await materializer.MaterializeAsync(
            collectionId,
            Settings(recipe),
            cancellationToken);
        return materialized is null
            ? Results.NotFound()
            : Results.Ok(CreativeCollectionPreviewEndpoints.ToSnapshotResponse(
                materialized,
                timeProvider.GetUtcNow().ToUniversalTime()));
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

    private static async Task<CreativeCollectionRecipeResponse[]> ToResponsesAsync(
        IReadOnlyList<CreativeCollectionRecipe> recipes,
        ISmartCollectionRepository definitions,
        CancellationToken cancellationToken)
    {
        List<CreativeCollectionRecipeResponse> responses = [];
        foreach (CreativeCollectionRecipe recipe in recipes)
        {
            SmartCollectionDefinition? definition =
                await definitions.GetAsync(recipe.AnchorCollectionId, cancellationToken);
            if (definition is not null)
            {
                responses.Add(ToResponse(recipe, definition.Name));
            }
        }

        return responses.ToArray();
    }

    private static CreativeCollectionRecipeResponse ToResponse(
        CreativeCollectionRecipe recipe,
        string anchorCollectionName) => new(
        recipe.Id.ToString(),
        recipe.Name,
        recipe.AnchorCollectionId.ToString(),
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
        recipe.UpdatedAtUtc);

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
