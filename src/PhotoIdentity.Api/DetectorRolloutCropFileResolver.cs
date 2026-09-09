using System.Text.Json;
using PhotoIdentity.Core.Identifiers;
using PhotoIdentity.Core.Processing;

namespace PhotoIdentity.Api;

public sealed class DetectorRolloutCropFileResolver
{
    private readonly IProcessingRunRepository _runs;

    public DetectorRolloutCropFileResolver(IProcessingRunRepository runs)
    {
        ArgumentNullException.ThrowIfNull(runs);
        _runs = runs;
    }

    public async Task<string?> ResolveAsync(
        ProcessingRunId processingRunId,
        string storagePath,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(storagePath))
        {
            return null;
        }

        try
        {
            string[] segments = storagePath.Split(
                ['/', '\\'],
                StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
            if (segments.Length < 3 ||
                !string.Equals(segments[0], "rollouts", StringComparison.OrdinalIgnoreCase) ||
                !Guid.TryParse(segments[1], out Guid pathRunId) ||
                pathRunId == Guid.Empty ||
                pathRunId != Guid.Parse(processingRunId.ToString()))
            {
                return null;
            }

            string? outputRoot = await GetOutputRootAsync(processingRunId, cancellationToken);
            if (string.IsNullOrWhiteSpace(outputRoot))
            {
                return null;
            }

            string root = Path.GetFullPath(outputRoot);
            string relativePath = string.Join(Path.DirectorySeparatorChar, segments);
            string candidate = Path.GetFullPath(Path.Combine(root, relativePath));
            if (!IsBelowRoot(root, candidate))
            {
                return null;
            }

            return File.Exists(candidate) ? candidate : null;
        }
        catch (Exception exception) when (
            exception is ArgumentException or
            NotSupportedException or
            PathTooLongException)
        {
            return null;
        }
    }

    private async Task<string?> GetOutputRootAsync(
        ProcessingRunId runId,
        CancellationToken cancellationToken)
    {
        CatalogueProcessingRun? run = await _runs.GetRunAsync(runId, cancellationToken);
        if (run is null)
        {
            return null;
        }

        try
        {
            using JsonDocument document = JsonDocument.Parse(run.ConfigurationJson);
            foreach (JsonProperty property in document.RootElement.EnumerateObject())
            {
                if (string.Equals(property.Name, "outputRoot", StringComparison.OrdinalIgnoreCase) &&
                    property.Value.ValueKind == JsonValueKind.String)
                {
                    return property.Value.GetString();
                }
            }
        }
        catch (JsonException)
        {
            return null;
        }

        return null;
    }

    private static bool IsBelowRoot(string root, string candidate)
    {
        string relative = Path.GetRelativePath(root, candidate);
        return !Path.IsPathFullyQualified(relative) &&
               !string.Equals(relative, "..", StringComparison.Ordinal) &&
               !relative.StartsWith(".." + Path.DirectorySeparatorChar, StringComparison.Ordinal) &&
               !relative.StartsWith(".." + Path.AltDirectorySeparatorChar, StringComparison.Ordinal);
    }
}
