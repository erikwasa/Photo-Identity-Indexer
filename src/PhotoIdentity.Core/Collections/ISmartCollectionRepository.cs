namespace PhotoIdentity.Core.Collections;

public sealed class SmartCollectionNameConflictException : Exception
{
    public SmartCollectionNameConflictException(string name)
        : base($"A smart collection named '{name}' already exists.")
    {
    }
}

public interface ISmartCollectionRepository
{
    Task<SmartCollectionDefinition> CreateAsync(
        string name,
        SmartCollectionFilter filter,
        CancellationToken cancellationToken = default);

    Task<IReadOnlyList<SmartCollectionDefinition>> ListAsync(
        CancellationToken cancellationToken = default);

    Task<SmartCollectionDefinition?> GetAsync(
        SmartCollectionId id,
        CancellationToken cancellationToken = default);

    Task<SmartCollectionDefinition?> UpdateAsync(
        SmartCollectionId id,
        string name,
        SmartCollectionFilter filter,
        CancellationToken cancellationToken = default);

    Task<bool> DeleteAsync(
        SmartCollectionId id,
        CancellationToken cancellationToken = default);
}
