using System.Text.Json;
using Microsoft.Data.Sqlite;
using PhotoIdentity.Persistence.Sqlite;

namespace PhotoIdentity.Api;

/// <summary>
/// Resolves persisted review crop locations to existing physical files without
/// exposing storage roots through the review API.
/// </summary>
public sealed class ReviewCropFileResolver
{
    private readonly SqliteCatalogueDatabase _database;

    public ReviewCropFileResolver(SqliteCatalogueDatabase database)
    {
        ArgumentNullException.ThrowIfNull(database);
        _database = database;
    }

    public async Task<string?> ResolveAsync(
        string storagePath,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(storagePath))
        {
            return null;
        }

        try
        {
            if (Path.IsPathFullyQualified(storagePath))
            {
                string physicalPath = Path.GetFullPath(storagePath);
                return File.Exists(physicalPath) ? physicalPath : null;
            }

            string[] segments = storagePath.Split(
                ['/', '\\'],
                StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
            if (segments.Length < 3 ||
                !IsSupportedRunDirectory(segments[0]) ||
                !Guid.TryParse(segments[1], out Guid runId) ||
                runId == Guid.Empty)
            {
                return null;
            }

            string? outputRoot = await GetOutputRootAsync(runId, cancellationToken);
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

    private static bool IsSupportedRunDirectory(string value) =>
        string.Equals(value, "runs", StringComparison.OrdinalIgnoreCase) ||
        string.Equals(value, "rollouts", StringComparison.OrdinalIgnoreCase);

    private async Task<string?> GetOutputRootAsync(
        Guid runId,
        CancellationToken cancellationToken)
    {
        await using SqliteConnection connection = await _database.OpenConnectionAsync(cancellationToken);
        using SqliteCommand command = connection.CreateCommand();
        command.CommandText = """
            SELECT configuration_json
            FROM processing_runs
            WHERE id = $run_id;
            """;
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
