using System.Globalization;
using PhotoIdentity.Core.Identifiers;
using PhotoIdentity.Core.Recognition;
using PhotoIdentity.Persistence.Sqlite;

namespace PhotoIdentity.Cli;

internal sealed record MatchCommandOptions(
    string DatabasePath,
    ModelId EmbedderModelId,
    Sha256Digest EmbedderModelHash,
    bool? AutoAssign,
    double? HighScoreThreshold,
    double? HighMarginThreshold,
    double? MediumScoreThreshold)
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
        string? autoAssign = null;
        string? highScoreThreshold = null;
        string? highMarginThreshold = null;
        string? mediumScoreThreshold = null;

        for (int index = 1; index < args.Length; index++)
        {
            string option = args[index];
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
                case "--auto-assign":
                    autoAssign = Single(autoAssign, value, option);
                    break;
                case "--high-score-threshold":
                    highScoreThreshold = Single(highScoreThreshold, value, option);
                    break;
                case "--high-margin-threshold":
                    highMarginThreshold = Single(highMarginThreshold, value, option);
                    break;
                case "--medium-score-threshold":
                    mediumScoreThreshold = Single(mediumScoreThreshold, value, option);
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

        return new MatchCommandOptions(
            Path.GetFullPath(databasePath),
            new ModelId(embedderId.Trim()),
            new Sha256Digest(normalizedHash),
            ParseToggle(autoAssign),
            ParseThreshold(highScoreThreshold, "--high-score-threshold", 1),
            ParseThreshold(highMarginThreshold, "--high-margin-threshold", 2),
            ParseThreshold(mediumScoreThreshold, "--medium-score-threshold", 1));
    }

    private static bool? ParseToggle(string? value)
    {
        if (value is null)
        {
            return null;
        }

        return value.Trim().ToLowerInvariant() switch
        {
            "on" or "true" or "enabled" => true,
            "off" or "false" or "disabled" => false,
            _ => throw new ArgumentException(
                "Option '--auto-assign' must be one of: on, off, true, false, enabled, disabled."),
        };
    }

    private static double? ParseThreshold(string? value, string option, double maximum)
    {
        if (value is null)
        {
            return null;
        }

        if (!double.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out double parsed)
            || !double.IsFinite(parsed)
            || parsed < 0
            || parsed > maximum)
        {
            throw new ArgumentException(
                $"Option '{option}' must be a number between 0 and {maximum.ToString("0.###", CultureInfo.InvariantCulture)}.");
        }

        return parsed;
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
        SqliteIdentitySuggestionPolicyRepository policyRepository = new(database);
        IdentitySuggestionPolicy policy = await policyRepository.GetAsync(
            options.EmbedderModelId,
            options.EmbedderModelHash,
            cancellationToken);
        if (options.AutoAssign is not null
            || options.HighScoreThreshold is not null
            || options.HighMarginThreshold is not null
            || options.MediumScoreThreshold is not null)
        {
            policy = await policyRepository.UpdateAsync(
                options.EmbedderModelId,
                options.EmbedderModelHash,
                options.AutoAssign ?? policy.AutoAssignEnabled,
                options.HighScoreThreshold ?? policy.HighScoreThreshold,
                options.HighMarginThreshold ?? policy.HighMarginThreshold,
                options.MediumScoreThreshold ?? policy.MediumScoreThreshold,
                "cli:match-regenerate",
                cancellationToken);
        }

        SqliteIdentityMatcher matcher = new(database);
        IdentityMatchSummary summary = await matcher.RegenerateAsync(
            options.EmbedderModelId,
            options.EmbedderModelHash,
            cancellationToken);

        SqliteIdentityAutoAssignmentService autoAssignmentService = new(database);
        IdentityAutoAssignmentSummary autoAssignmentSummary = await autoAssignmentService.ApplyAsync(
            options.EmbedderModelId,
            options.EmbedderModelHash,
            policy,
            cancellationToken);

        output.WriteLine($"model-id: {options.EmbedderModelId}");
        output.WriteLine($"model-hash: {options.EmbedderModelHash}");
        output.WriteLine($"targets: {summary.TargetCount}");
        output.WriteLine($"suggested-targets: {summary.SuggestedTargetCount}");
        output.WriteLine($"suggestions: {summary.SuggestionCount}");
        output.WriteLine($"policy-version: {policy.Version}");
        output.WriteLine($"auto-assignment-enabled: {policy.AutoAssignEnabled.ToString().ToLowerInvariant()}");
        output.WriteLine($"high-score-threshold: {policy.HighScoreThreshold.ToString("0.###", CultureInfo.InvariantCulture)}");
        output.WriteLine($"high-margin-threshold: {policy.HighMarginThreshold.ToString("0.###", CultureInfo.InvariantCulture)}");
        output.WriteLine($"medium-score-threshold: {policy.MediumScoreThreshold.ToString("0.###", CultureInfo.InvariantCulture)}");
        output.WriteLine($"auto-assignment-candidates: {autoAssignmentSummary.CandidateCount}");
        output.WriteLine($"auto-assigned: {autoAssignmentSummary.AssignedCount}");
        output.WriteLine($"auto-assignment-skipped: {autoAssignmentSummary.SkippedCount}");
        return 0;
    }
}
