using PhotoIdentity.Core.Collections;

namespace PhotoIdentity.Api;

public sealed record CreativeCollectionRecipeRequest(
    int TargetCount = CreativeCollectionRecipe.DefaultTargetCount,
    string ContextStrength = "balanced",
    bool NoveltyEnabled = false);

public sealed record CreativeCollectionRecipeResponse(
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
        endpoints.MapGet(
            "/api/smart-collections/{id:guid}/creative-recipe",
            GetAsync);
        endpoints.MapPut(
            "/api/smart-collections/{id:guid}/creative-recipe",
            UpsertAsync);
        endpoints.MapDelete(
            "/api/smart-collections/{id:guid}/creative-recipe",
            DeleteAsync);
        endpoints.MapGet(
            "/api/smart-collections/{id:guid}/creative-recipe/preview",
            PreviewAsync);
        endpoints.MapPost(
            "/api/smart-collections/{id:guid}/creative-recipe/slideshow-snapshot",
            CreateSlideshowSnapshotAsync);
        return endpoints;
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

        SmartCollectionDefinition? definition =
            await definitions.GetAsync(collectionId, cancellationToken);
        if (definition is null)
        {
            return Results.NotFound();
        }

        CreativeCollectionRecipe? recipe =
            await recipes.GetAsync(collectionId, cancellationToken);
        return recipe is null
            ? Results.NotFound()
            : Results.Ok(ToResponse(recipe, definition.Name));
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

        SmartCollectionDefinition? definition =
            await definitions.GetAsync(collectionId, cancellationToken);
        if (definition is null)
        {
            return Results.NotFound();
        }

        try
        {
            CreativeCollectionContextPolicy contextPolicy =
                CreativeCollectionContextPolicy.FromStrength(request.ContextStrength);
            CreativeCollectionRecipeSettings settings =
                CreativeCollectionRecipeSettings.Create(
                    request.TargetCount,
                    contextPolicy.Version,
                    request.NoveltyEnabled);
            CreativeCollectionRecipe recipe =
                await recipes.UpsertAsync(collectionId, settings, cancellationToken);
            return Results.Ok(ToResponse(recipe, definition.Name));
        }
        catch (Exception exception) when (
            exception is ArgumentException or InvalidDataException)
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

        CreativeCollectionRecipe? recipe =
            await recipes.GetAsync(collectionId, cancellationToken);
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

        CreativeCollectionRecipe? recipe =
            await recipes.GetAsync(collectionId, cancellationToken);
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

    private static CreativeCollectionRecipeSettings Settings(CreativeCollectionRecipe recipe) => new(
        recipe.TargetCount,
        recipe.MomentGapMinutes,
        recipe.MomentPolicyVersion,
        recipe.ContextPolicyVersion,
        recipe.SelectionPolicyVersion,
        recipe.OrderingPolicyVersion,
        recipe.NoveltyEnabled);

    private static CreativeCollectionRecipeResponse ToResponse(
        CreativeCollectionRecipe recipe,
        string anchorCollectionName) => new(
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

    private static bool TryGetId(
        Guid id,
        out SmartCollectionId collectionId,
        out IResult? error)
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
}
