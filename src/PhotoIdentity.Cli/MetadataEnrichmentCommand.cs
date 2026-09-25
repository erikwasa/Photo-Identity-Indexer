using System.Globalization;
using System.Text.Json;
using System.Text.RegularExpressions;
using Npgsql;
using PhotoIdentity.Core.Identifiers;
using PhotoIdentity.Core.Places;
using PhotoIdentity.Core.Sources;
using PhotoIdentity.Persistence.Postgres;

namespace PhotoIdentity.Cli;

internal sealed record MetadataEnrichmentCommandOptions(
    string PostgresConnectionEnvironment,
    string RulesPath,
    bool Apply,
    string? ReportPath)
{
    public static MetadataEnrichmentCommandOptions Parse(string[] args)
    {
        string? postgresEnvironment = null;
        string? rulesPath = null;
        string? reportPath = null;
        bool apply = false;

        for (int index = 0; index < args.Length; index++)
        {
            string option = args[index];
            if (option == "--apply")
            {
                if (apply)
                {
                    throw new ArgumentException("Option '--apply' may be supplied only once.");
                }

                apply = true;
                continue;
            }

            string value = index + 1 < args.Length
                ? args[++index]
                : throw new ArgumentException($"Option '{option}' requires a value.");

            switch (option)
            {
                case "--postgres-connection-env":
                    postgresEnvironment = Single(postgresEnvironment, value, option);
                    break;
                case "--rules":
                    rulesPath = Single(rulesPath, value, option);
                    break;
                case "--report":
                    reportPath = Single(reportPath, value, option);
                    break;
                default:
                    throw new ArgumentException($"Unknown metadata-enrichment option '{option}'.");
            }
        }

        if (postgresEnvironment is null)
        {
            throw new ArgumentException("Option '--postgres-connection-env' is required.");
        }

        if (rulesPath is null)
        {
            throw new ArgumentException("Option '--rules' is required.");
        }

        return new MetadataEnrichmentCommandOptions(
            postgresEnvironment,
            Path.GetFullPath(rulesPath),
            apply,
            reportPath is null ? null : Path.GetFullPath(reportPath));
    }

    private static string Single(string? current, string value, string option)
    {
        if (current is not null)
        {
            throw new ArgumentException($"Option '{option}' may be supplied only once.");
        }

        return string.IsNullOrWhiteSpace(value)
            ? throw new ArgumentException($"Option '{option}' requires a non-empty value.")
            : value.Trim();
    }
}

internal sealed class MetadataEnrichmentRuleSet
{
    public bool InferDateFromFilename { get; init; } = true;
    public bool InferDateFromDirectory { get; init; } = true;
    public IReadOnlyList<string> ExcludedDateDirectories { get; init; } = ["1970"];
    public IReadOnlyList<MetadataPlaceRuleDefinition> PlaceRules { get; init; } = [];
}

internal sealed class MetadataPlaceRuleDefinition
{
    public string? Name { get; init; }
    public string From { get; init; } = "";
    public string To { get; init; } = "";
    public string Place { get; init; } = "";
}

internal sealed record MetadataPlaceRule(
    string Name,
    DateOnly From,
    DateOnly To,
    string Place);

internal sealed record MetadataEnrichmentCandidate(
    AssetRevisionId RevisionId,
    string SourceKey,
    PhotoCaptureDateRange? ExistingDateRange,
    string? ExistingDateDisplay,
    bool HasLocation);

internal sealed record MetadataEnrichmentPlanItem(
    AssetRevisionId RevisionId,
    string SourceKey,
    string? ExistingDate,
    bool HasLocation,
    PhotoCaptureDateValue? ProposedDate,
    string? DateReason,
    string? ProposedPlace,
    string? PlaceRule,
    string? Ambiguity)
{
    public bool HasProposal => ProposedDate is not null || ProposedPlace is not null;
}

internal sealed record MetadataEnrichmentPlan(
    IReadOnlyList<MetadataEnrichmentPlanItem> Items,
    int TotalPhotos,
    int MissingDate,
    int MissingLocation,
    int DateProposals,
    int PlaceProposals,
    int Ambiguous)
{
    public IReadOnlyList<MetadataEnrichmentPlanItem> ReviewItems => Items
        .Where(item => item.HasProposal || item.Ambiguity is not null)
        .ToArray();
}

internal static partial class MetadataEnrichmentPlanner
{
    private static readonly DateOnly MinimumUnixFilenameDate = new(2000, 1, 1);
    private static readonly DateOnly MaximumUnixFilenameDate = new(2100, 12, 31);

    [GeneratedRegex("^(?<date>[0-9]{8})(?:[_-].*)?$", RegexOptions.CultureInvariant)]
    private static partial Regex LeadingDateRegex();

    [GeneratedRegex("^(?:IMG|Screenshot|PXL)[_-](?<date>[0-9]{8})(?:[_-].*)?$", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex PrefixedDateRegex();

    [GeneratedRegex("^[0-9]{13}$", RegexOptions.CultureInvariant)]
    private static partial Regex UnixMillisecondsRegex();

    public static MetadataEnrichmentPlan Build(
        IReadOnlyList<MetadataEnrichmentCandidate> candidates,
        MetadataEnrichmentRuleSet rules)
    {
        ArgumentNullException.ThrowIfNull(candidates);
        ArgumentNullException.ThrowIfNull(rules);

        HashSet<string> excludedDirectories = rules.ExcludedDateDirectories
            .Where(value => !string.IsNullOrWhiteSpace(value))
            .Select(value => value.Trim().Trim('/', '\\'))
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        IReadOnlyList<MetadataPlaceRule> placeRules = ParsePlaceRules(rules.PlaceRules);

        List<MetadataEnrichmentPlanItem> items = new(candidates.Count);
        foreach (MetadataEnrichmentCandidate candidate in candidates)
        {
            PhotoCaptureDateValue? proposedDate = null;
            string? dateReason = null;
            string? ambiguity = null;

            if (candidate.ExistingDateRange is null)
            {
                DateInferenceResult inferred = InferDate(candidate.SourceKey, rules, excludedDirectories);
                proposedDate = inferred.Value;
                dateReason = inferred.Reason;
                ambiguity = inferred.Ambiguity;
            }

            PhotoCaptureDateRange? effectiveRange = candidate.ExistingDateRange ?? proposedDate?.InclusiveRange;
            string? proposedPlace = null;
            string? placeRule = null;

            if (!candidate.HasLocation && effectiveRange is not null)
            {
                PlaceInferenceResult place = InferPlace(effectiveRange, placeRules);
                proposedPlace = place.Place;
                placeRule = place.RuleName;
                ambiguity ??= place.Ambiguity;
            }

            items.Add(new MetadataEnrichmentPlanItem(
                candidate.RevisionId,
                candidate.SourceKey,
                candidate.ExistingDateDisplay,
                candidate.HasLocation,
                proposedDate,
                dateReason,
                proposedPlace,
                placeRule,
                ambiguity));
        }

        return new MetadataEnrichmentPlan(
            items,
            candidates.Count,
            candidates.Count(candidate => candidate.ExistingDateRange is null),
            candidates.Count(candidate => !candidate.HasLocation),
            items.Count(item => item.ProposedDate is not null),
            items.Count(item => item.ProposedPlace is not null),
            items.Count(item => item.Ambiguity is not null));
    }

    private static DateInferenceResult InferDate(
        string sourceKey,
        MetadataEnrichmentRuleSet rules,
        IReadOnlySet<string> excludedDirectories)
    {
        string normalized = sourceKey.Replace('\\', '/');
        string[] segments = normalized.Split('/', StringSplitOptions.RemoveEmptyEntries);
        string fileName = segments.Length == 0 ? normalized : segments[^1];
        string baseName = Path.GetFileNameWithoutExtension(fileName);

        PhotoCaptureDateValue? directoryValue = rules.InferDateFromDirectory
            ? InferDirectoryDate(segments, excludedDirectories)
            : null;
        PhotoCaptureDateValue? filenameValue = rules.InferDateFromFilename
            ? InferFilenameDate(baseName)
            : null;

        if (filenameValue is not null && directoryValue is not null &&
            (filenameValue.Year != directoryValue.Year || filenameValue.Month != directoryValue.Month))
        {
            return new DateInferenceResult(
                null,
                null,
                $"filename date {filenameValue} conflicts with directory date {directoryValue}");
        }

        if (filenameValue is not null)
        {
            return new DateInferenceResult(filenameValue, "filename", null);
        }

        return directoryValue is null
            ? new DateInferenceResult(null, null, null)
            : new DateInferenceResult(directoryValue, "directory", null);
    }

    private static PhotoCaptureDateValue? InferDirectoryDate(
        string[] segments,
        IReadOnlySet<string> excludedDirectories)
    {
        if (segments.Length < 3 || excludedDirectories.Contains(segments[0]))
        {
            return null;
        }

        if (!int.TryParse(segments[0], NumberStyles.None, CultureInfo.InvariantCulture, out int year) ||
            year is < 1 or > 9999 ||
            !int.TryParse(segments[1], NumberStyles.None, CultureInfo.InvariantCulture, out int month) ||
            month is < 1 or > 12)
        {
            return null;
        }

        return new PhotoCaptureDateValue(year, month);
    }

    private static PhotoCaptureDateValue? InferFilenameDate(string baseName)
    {
        Match match = LeadingDateRegex().Match(baseName);
        if (!match.Success)
        {
            match = PrefixedDateRegex().Match(baseName);
        }

        if (match.Success && TryParseCompactDate(match.Groups["date"].Value, out PhotoCaptureDateValue? compact))
        {
            return compact;
        }

        if (!UnixMillisecondsRegex().IsMatch(baseName) ||
            !long.TryParse(baseName, NumberStyles.None, CultureInfo.InvariantCulture, out long milliseconds))
        {
            return null;
        }

        try
        {
            DateOnly date = DateOnly.FromDateTime(DateTimeOffset.FromUnixTimeMilliseconds(milliseconds).UtcDateTime);
            if (date < MinimumUnixFilenameDate || date > MaximumUnixFilenameDate)
            {
                return null;
            }

            return new PhotoCaptureDateValue(date.Year, date.Month, date.Day);
        }
        catch (ArgumentOutOfRangeException)
        {
            return null;
        }
    }

    private static bool TryParseCompactDate(string text, out PhotoCaptureDateValue? value)
    {
        value = null;
        if (text.Length != 8 ||
            !int.TryParse(text.AsSpan(0, 4), NumberStyles.None, CultureInfo.InvariantCulture, out int year) ||
            !int.TryParse(text.AsSpan(4, 2), NumberStyles.None, CultureInfo.InvariantCulture, out int month) ||
            !int.TryParse(text.AsSpan(6, 2), NumberStyles.None, CultureInfo.InvariantCulture, out int day))
        {
            return false;
        }

        try
        {
            value = new PhotoCaptureDateValue(year, month, day);
            return true;
        }
        catch (ArgumentOutOfRangeException)
        {
            return false;
        }
    }

    private static IReadOnlyList<MetadataPlaceRule> ParsePlaceRules(
        IReadOnlyList<MetadataPlaceRuleDefinition> definitions)
    {
        List<MetadataPlaceRule> rules = [];
        for (int index = 0; index < definitions.Count; index++)
        {
            MetadataPlaceRuleDefinition definition = definitions[index]
                ?? throw new ArgumentException($"Place rule {index + 1} is null.");
            if (!DateOnly.TryParseExact(
                    definition.From,
                    "yyyy-MM-dd",
                    CultureInfo.InvariantCulture,
                    DateTimeStyles.None,
                    out DateOnly from) ||
                !DateOnly.TryParseExact(
                    definition.To,
                    "yyyy-MM-dd",
                    CultureInfo.InvariantCulture,
                    DateTimeStyles.None,
                    out DateOnly to))
            {
                throw new ArgumentException(
                    $"Place rule {index + 1} must use yyyy-MM-dd for both 'from' and 'to'.");
            }

            if (from > to)
            {
                throw new ArgumentException($"Place rule {index + 1} has 'from' later than 'to'.");
            }

            string place = PhotoPlacePath.Parse(definition.Place).DisplayValue;
            string name = string.IsNullOrWhiteSpace(definition.Name)
                ? $"{from:yyyy-MM-dd}..{to:yyyy-MM-dd}"
                : definition.Name.Trim();
            rules.Add(new MetadataPlaceRule(name, from, to, place));
        }

        return rules;
    }

    private static PlaceInferenceResult InferPlace(
        PhotoCaptureDateRange range,
        IReadOnlyList<MetadataPlaceRule> rules)
    {
        MetadataPlaceRule[] contained = rules
            .Where(rule => range.From >= rule.From && range.To <= rule.To)
            .ToArray();

        if (contained.Length > 0)
        {
            string[] places = contained
                .Select(rule => rule.Place)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToArray();
            if (places.Length == 1)
            {
                string names = string.Join(", ", contained.Select(rule => rule.Name));
                return new PlaceInferenceResult(places[0], names, null);
            }

            return new PlaceInferenceResult(
                null,
                null,
                "multiple place rules with different Places fully contain the effective date range");
        }

        bool overlaps = rules.Any(rule => range.From <= rule.To && range.To >= rule.From);
        return overlaps
            ? new PlaceInferenceResult(
                null,
                null,
                "effective date precision overlaps a place rule but is not fully contained by it")
            : new PlaceInferenceResult(null, null, null);
    }

    private sealed record DateInferenceResult(
        PhotoCaptureDateValue? Value,
        string? Reason,
        string? Ambiguity);

    private sealed record PlaceInferenceResult(
        string? Place,
        string? RuleName,
        string? Ambiguity);
}

internal static class MetadataEnrichmentCommandRunner
{
    private const string Actor = "wi-0163-bulk-metadata-enrichment";

    public static async Task<int> RunAsync(
        MetadataEnrichmentCommandOptions options,
        TextWriter output,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(output);

        string? connectionString =
            Environment.GetEnvironmentVariable(options.PostgresConnectionEnvironment);
        if (string.IsNullOrWhiteSpace(connectionString))
        {
            throw new ArgumentException(
                "The selected PostgreSQL connection environment variable is empty or missing.");
        }

        if (!File.Exists(options.RulesPath))
        {
            throw new ArgumentException("The metadata-enrichment rules file does not exist.");
        }

        MetadataEnrichmentRuleSet rules = await LoadRulesAsync(options.RulesPath, cancellationToken);
        await using PostgresCatalogueDatabase database = new(connectionString);
        IReadOnlyList<MetadataEnrichmentCandidate> candidates =
            await ReadCandidatesAsync(database, cancellationToken);
        MetadataEnrichmentPlan plan = MetadataEnrichmentPlanner.Build(candidates, rules);

        int appliedDates = 0;
        int appliedPlaces = 0;
        int skippedBecauseChanged = 0;
        if (options.Apply)
        {
            PostgresPhotoCaptureDateRepository captureDates = new(database, TimeProvider.System);
            PostgresPhotoPlaceRepository places = new(database, TimeProvider.System);

            foreach (MetadataEnrichmentPlanItem item in plan.Items.Where(item => item.HasProposal))
            {
                cancellationToken.ThrowIfCancellationRequested();

                if (item.ProposedDate is not null)
                {
                    PhotoCaptureDateState current = await captureDates.GetStateAsync(
                        item.RevisionId,
                        cancellationToken);
                    if (current.EffectiveRange is null)
                    {
                        await captureDates.SetManualAsync(
                            item.RevisionId,
                            item.ProposedDate,
                            Actor,
                            cancellationToken);
                        appliedDates++;
                    }
                    else
                    {
                        skippedBecauseChanged++;
                    }
                }

                if (item.ProposedPlace is not null)
                {
                    PhotoPlaceState current = await places.GetStateAsync(
                        item.RevisionId,
                        cancellationToken);
                    if (current.Place is null)
                    {
                        await places.SetManualPlaceAsync(
                            item.RevisionId,
                            item.ProposedPlace,
                            Actor,
                            cancellationToken);
                        appliedPlaces++;
                    }
                    else
                    {
                        skippedBecauseChanged++;
                    }
                }
            }
        }

        output.WriteLine($"mode: {(options.Apply ? "apply" : "dry-run")}");
        output.WriteLine($"photos-scanned: {plan.TotalPhotos}");
        output.WriteLine($"missing-date: {plan.MissingDate}");
        output.WriteLine($"missing-location: {plan.MissingLocation}");
        output.WriteLine($"date-proposals: {plan.DateProposals}");
        output.WriteLine($"place-proposals: {plan.PlaceProposals}");
        output.WriteLine($"ambiguous: {plan.Ambiguous}");
        if (options.Apply)
        {
            output.WriteLine($"dates-applied: {appliedDates}");
            output.WriteLine($"places-applied: {appliedPlaces}");
            output.WriteLine($"skipped-because-changed: {skippedBecauseChanged}");
        }

        if (options.ReportPath is not null)
        {
            await WriteReportAsync(
                options.ReportPath,
                options.Apply,
                plan,
                appliedDates,
                appliedPlaces,
                skippedBecauseChanged,
                cancellationToken);
            output.WriteLine("private-report-written: true");
        }

        return 0;
    }

    private static async Task<MetadataEnrichmentRuleSet> LoadRulesAsync(
        string path,
        CancellationToken cancellationToken)
    {
        await using FileStream stream = File.OpenRead(path);
        MetadataEnrichmentRuleSet? rules = await JsonSerializer.DeserializeAsync<MetadataEnrichmentRuleSet>(
            stream,
            new JsonSerializerOptions
            {
                PropertyNameCaseInsensitive = true,
                ReadCommentHandling = JsonCommentHandling.Skip,
                AllowTrailingCommas = true,
            },
            cancellationToken);
        return rules ?? throw new ArgumentException("The metadata-enrichment rules file is empty.");
    }

    private static async Task<IReadOnlyList<MetadataEnrichmentCandidate>> ReadCandidatesAsync(
        PostgresCatalogueDatabase database,
        CancellationToken cancellationToken)
    {
        await using NpgsqlConnection connection = await database.OpenConnectionAsync(cancellationToken);
        await using NpgsqlCommand command = connection.CreateCommand();
        command.CommandText = """
            WITH current_revisions AS (
                SELECT DISTINCT ON (a.id)
                    a.id AS asset_id,
                    a.source_key,
                    r.id AS revision_id,
                    r.media_type
                FROM assets a
                INNER JOIN asset_revisions r
                    ON r.asset_id = a.id
                WHERE a.deleted_at_utc IS NULL
                ORDER BY a.id, r.observed_at_utc DESC, r.id DESC
            ),
            latest_date_action AS (
                SELECT DISTINCT ON (asset_revision_id)
                    asset_revision_id,
                    action_kind,
                    precision,
                    capture_year,
                    capture_month,
                    capture_day
                FROM photo_capture_date_actions
                ORDER BY asset_revision_id, id DESC
            ),
            latest_place_action AS (
                SELECT DISTINCT ON (asset_revision_id)
                    asset_revision_id,
                    action_kind
                FROM photo_place_actions
                ORDER BY asset_revision_id, id DESC
            )
            SELECT
                cr.revision_id,
                cr.source_key,
                pcm.taken_at_local,
                lda.action_kind,
                lda.precision,
                lda.capture_year,
                lda.capture_month,
                lda.capture_day,
                lpa.action_kind,
                CASE
                    WHEN pcm.latitude IS NOT NULL
                     AND pcm.longitude IS NOT NULL
                     AND NOT (pcm.latitude = 0 AND pcm.longitude = 0)
                    THEN TRUE
                    ELSE FALSE
                END AS has_valid_gps
            FROM current_revisions cr
            LEFT JOIN photo_capture_metadata pcm
                ON pcm.asset_revision_id = cr.revision_id
            LEFT JOIN latest_date_action lda
                ON lda.asset_revision_id = cr.revision_id
            LEFT JOIN latest_place_action lpa
                ON lpa.asset_revision_id = cr.revision_id
            WHERE cr.media_type IS NULL OR cr.media_type LIKE 'image/%'
            ORDER BY cr.source_key, cr.revision_id;
            """;

        List<MetadataEnrichmentCandidate> candidates = [];
        await using NpgsqlDataReader reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            AssetRevisionId revisionId = AssetRevisionId.From(reader.GetGuid(0));
            string sourceKey = reader.GetString(1);
            DateTime? extracted = reader.IsDBNull(2)
                ? null
                : DateTime.SpecifyKind(reader.GetDateTime(2), DateTimeKind.Unspecified);
            string? dateActionKind = reader.IsDBNull(3) ? null : reader.GetString(3);
            PhotoCaptureDateValue? manual = dateActionKind == PhotoCaptureDateActionKinds.Set
                ? ReadManualDate(reader)
                : null;
            PhotoCaptureDateRange? range = manual?.InclusiveRange;
            string? display = manual?.ToString();
            if (range is null && extracted is not null)
            {
                DateOnly date = DateOnly.FromDateTime(extracted.Value);
                range = new PhotoCaptureDateRange(date, date);
                display = date.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
            }

            string? placeActionKind = reader.IsDBNull(8) ? null : reader.GetString(8);
            bool hasValidGps = reader.GetBoolean(9);
            bool hasLocation = placeActionKind == "set" || hasValidGps;

            candidates.Add(new MetadataEnrichmentCandidate(
                revisionId,
                sourceKey,
                range,
                display,
                hasLocation));
        }

        return candidates;
    }

    private static PhotoCaptureDateValue ReadManualDate(NpgsqlDataReader reader)
    {
        string precision = reader.GetString(4);
        int year = reader.GetInt16(5);
        int? month = reader.IsDBNull(6) ? null : reader.GetInt16(6);
        int? day = reader.IsDBNull(7) ? null : reader.GetInt16(7);
        return precision switch
        {
            PhotoCaptureDatePrecisions.Year => new PhotoCaptureDateValue(year),
            PhotoCaptureDatePrecisions.Month when month is not null =>
                new PhotoCaptureDateValue(year, month),
            PhotoCaptureDatePrecisions.Day when month is not null && day is not null =>
                new PhotoCaptureDateValue(year, month, day),
            _ => throw new InvalidOperationException(
                $"Stored capture-date precision '{precision}' is inconsistent with its components."),
        };
    }

    private static async Task WriteReportAsync(
        string path,
        bool apply,
        MetadataEnrichmentPlan plan,
        int appliedDates,
        int appliedPlaces,
        int skippedBecauseChanged,
        CancellationToken cancellationToken)
    {
        string? directory = Path.GetDirectoryName(path);
        if (!string.IsNullOrEmpty(directory))
        {
            Directory.CreateDirectory(directory);
        }

        object report = new
        {
            generatedAtUtc = DateTimeOffset.UtcNow,
            mode = apply ? "apply" : "dry-run",
            summary = new
            {
                plan.TotalPhotos,
                plan.MissingDate,
                plan.MissingLocation,
                plan.DateProposals,
                plan.PlaceProposals,
                plan.Ambiguous,
                AppliedDates = appliedDates,
                AppliedPlaces = appliedPlaces,
                SkippedBecauseChanged = skippedBecauseChanged,
            },
            items = plan.ReviewItems.Select(item => new
            {
                revisionId = item.RevisionId.ToString(),
                sourceKey = item.SourceKey,
                existingDate = item.ExistingDate,
                hasLocation = item.HasLocation,
                proposedDate = item.ProposedDate?.ToString(),
                dateReason = item.DateReason,
                proposedPlace = item.ProposedPlace,
                placeRule = item.PlaceRule,
                ambiguity = item.Ambiguity,
            }),
        };

        await using FileStream stream = File.Create(path);
        await JsonSerializer.SerializeAsync(
            stream,
            report,
            new JsonSerializerOptions { WriteIndented = true },
            cancellationToken);
    }
}
