using System.Text.Json;
using Microsoft.Data.Sqlite;
using PhotoIdentity.Core.Identifiers;
using PhotoIdentity.Persistence.Sqlite;

namespace PhotoIdentity.Api;

public sealed class DetectorRolloutCropFileResolver
{
    private readonly SqliteCatalogueDatabase _database;

    public DetectorRolloutCropFileResolver(SqliteCatalogueDatabase database)
    {
        ArgumentNullException.ThrowIfNull(database);
        _database = database;
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
        await using SqliteConnection connection = await _database.OpenConnectionAsync(cancellationToken);
        using SqliteCommand command = connection.CreateCommand();
        command.CommandText = "SELECT configuration_json FROM processing_runs WHERE id = $run_id;";
        command.Parameters.AddWithValue("$run_id", runId.ToString());
        object? value = await command.ExecuteScalarAsync(cancellationToken);
        if (value is not string configurationJson)
        {
            return null;
        }

        try
        {
            using JsonDocument document = JsonDocument.Parse(configurationJson);
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
