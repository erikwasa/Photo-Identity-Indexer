namespace PhotoIdentity.Core.Collections;

public interface ICreativeCollectionRecipeRepository
{
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
