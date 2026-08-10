using System.Globalization;
using PhotoIdentity.Core.Identifiers;
using PhotoIdentity.Core.Recognition;
using PhotoIdentity.Persistence.Sqlite;

namespace PhotoIdentity.Cli;

internal sealed record MatchCommandOptions(
    string DatabasePath,
    ModelId EmbedderModelId,
    Sha256Digest EmbedderModelHash,
    bool AutoAssign,
    double AutoAssignThreshold)
{
    public static MatchCommandOptions Parse(string[] args)
    {
        if (args.Length == 0 || !string.Equals(args[0], "regenerate", StringComparison.Ordinal))
        {
            throw new ArgumentException("The match command requires 'regenerate'.");
        }

        string? databasePath = null;
        string? embedderId = null;
        string? embedderHash = null;
        string? autoAssignThreshold = null;
        bool autoAssign = false;
        bool autoAssignSeen = false;

        for (int index = 1; index < args.Length; index++)
        {
            string option = args[index];
            if (string.Equals(option, "--auto-assign", StringComparison.Ordinal))
            {
                if (autoAssignSeen)
                {
                    throw new ArgumentException("Option '--auto-assign' may be supplied only once.");
                }

                autoAssign = true;
                autoAssignSeen = true;
                continue;
            }

            string value = index + 1 < args.Length
                ? args[++index]
                : throw new ArgumentException($"Option '{option}' requires a value.");

            switch (option)
            {
                case "--database":
                    databasePath = Single(databasePath, value, option);
                    break;
                case "--embedder-id":
                    embedderId = Single(embedderId, value, option);
                    break;
                case "--embedder-hash":
                    embedderHash = Single(embedderHash, value, option);
                    break;
                case "--auto-assign-threshold":
                    autoAssignThreshold = Single(autoAssignThreshold, value, option);
                    break;
                default:
                    throw new ArgumentException($"Unknown option '{option}'.");
            }
        }

        if (databasePath is null || embedderId is null || embedderHash is null)
        {
            throw new ArgumentException(
                "Options '--database', '--embedder-id' and '--embedder-hash' are required.");
        }

        string normalizedHash = embedderHash.Trim().ToLowerInvariant();
        if (normalizedHash.Length != 64 || !normalizedHash.All(Uri.IsHexDigit))
        {
            throw new ArgumentException("Option '--embedder-hash' must be a 64-character SHA-256 value.");
        }

        double threshold = IdentityAutoAssignmentOptions.DefaultHighConfidenceThreshold;
        if (autoAssignThreshold is not null
            && (!double.TryParse(
                    autoAssignThreshold,
                    NumberStyles.Float,
                    CultureInfo.InvariantCulture,
                    out threshold)
                || !double.IsFinite(threshold)
                || threshold < 0
                || threshold > 1))
        {
            throw new ArgumentException(
                "Option '--auto-assign-threshold' must be a number between 0 and 1.");
        }

        return new MatchCommandOptions(
            Path.GetFullPath(databasePath),
            new ModelId(embedderId.Trim()),
            new Sha256Digest(normalizedHash),
            autoAssign,
            threshold);
    }

    private static string Single(string? current, string value, string option)
    {
        if (current is not null)
        {
            throw new ArgumentException($"Option '{option}' may be supplied only once.");
        }

        string normalized = value.Trim();
        if (normalized.Length == 0)
        {
            throw new ArgumentException($"Option '{option}' requires a non-empty value.");
        }

        return normalized;
    }
}

internal static class MatchCommandRunner
{
    public static async Task<int> RunAsync(
        MatchCommandOptions options,
        TextWriter output,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(output);

        if (!File.Exists(options.DatabasePath))
        {
            throw new FileNotFoundException(
                "The catalogue database was not found; matcher regeneration will not create an empty catalogue.");
        }

        SqliteCatalogueDatabase database = new(options.DatabasePath);
        SqliteIdentityMatcher matcher = new(database);
        IdentityMatchSummary summary = await matcher.RegenerateAsync(
            options.EmbedderModelId,
            options.EmbedderModelHash,
            cancellationToken);

        SqliteIdentityAutoAssignmentService autoAssignmentService = new(database);
        IdentityAutoAssignmentSummary autoAssignmentSummary = await autoAssignmentService.ApplyAsync(
            options.EmbedderModelId,
            options.EmbedderModelHash,
            new IdentityAutoAssignmentOptions(options.AutoAssign, options.AutoAssignThreshold),
            cancellationToken);

        output.WriteLine($"model-id: {options.EmbedderModelId}");
        output.WriteLine($"model-hash: {options.EmbedderModelHash}");
        output.WriteLine($"targets: {summary.TargetCount}");
        output.WriteLine($"suggested-targets: {summary.SuggestedTargetCount}");
        output.WriteLine($"suggestions: {summary.SuggestionCount}");
        output.WriteLine($"auto-assignment-enabled: {options.AutoAssign.ToString().ToLowerInvariant()}");
        output.WriteLine($"auto-assignment-threshold: {options.AutoAssignThreshold.ToString("0.###", CultureInfo.InvariantCulture)}");
        output.WriteLine($"auto-assignment-candidates: {autoAssignmentSummary.CandidateCount}");
        output.WriteLine($"auto-assigned: {autoAssignmentSummary.AssignedCount}");
        output.WriteLine($"auto-assignment-skipped: {autoAssignmentSummary.SkippedCount}");
        return 0;
    }
}
