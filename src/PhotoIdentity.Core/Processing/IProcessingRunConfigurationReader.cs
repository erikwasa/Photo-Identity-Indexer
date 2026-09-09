using PhotoIdentity.Core.Identifiers;

namespace PhotoIdentity.Core.Processing;

/// <summary>
/// Reads saved artifact configuration independently of run lifecycle validation,
/// including historical runs whose terminal metadata is incomplete.
/// </summary>
public interface IProcessingRunConfigurationReader
{
    Task<string?> GetRunConfigurationAsync(ProcessingRunId id, CancellationToken cancellationToken = default);
}
