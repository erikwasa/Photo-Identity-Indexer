namespace PhotoIdentity.Core.Collections;

public interface ICreativeCollectionRecipeRepository
{
    Task<IReadOnlyList<CreativeCollectionRecipe>> ListAsync(
        CancellationToken cancellationToken = default);

    Task<IReadOnlyList<CreativeCollectionRecipe>> ListForAnchorAsync(
        SmartCollectionId anchorCollectionId,
        CancellationToken cancellationToken = default);

    Task<CreativeCollectionRecipe?> GetAsync(
        CreativeCollectionId id,
        CancellationToken cancellationToken = default);

    Task<CreativeCollectionRecipe> CreateAsync(
        SmartCollectionId anchorCollectionId,
        string name,
        CreativeCollectionRecipeSettings settings,
        CancellationToken cancellationToken = default);

    Task<CreativeCollectionRecipe> UpdateAsync(
        CreativeCollectionId id,
        string name,
        CreativeCollectionRecipeSettings settings,
        CancellationToken cancellationToken = default);

    Task<bool> DeleteAsync(
        CreativeCollectionId id,
        CancellationToken cancellationToken = default);

    // Compatibility surface for the original WI-0121 one-recipe-per-anchor callers.
    Task<CreativeCollectionRecipe?> GetAsync(
        SmartCollectionId anchorCollectionId,
        CancellationToken cancellationToken = default);

    Task<CreativeCollectionRecipe> UpsertAsync(
        SmartCollectionId anchorCollectionId,
        CreativeCollectionRecipeSettings settings,
        CancellationToken cancellationToken = default);

    Task<bool> DeleteAsync(
        SmartCollectionId anchorCollectionId,
        CancellationToken cancellationToken = default);
}
