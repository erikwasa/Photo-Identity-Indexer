using System.Text.Json;

namespace PhotoIdentity.Api;

/// <summary>
/// Private, non-destructive, operator-controlled review decisions for existing
/// photo-quality suggestions. No source-copy, collection or catalogue writes.
/// Persisted separately from the catalogue to avoid a schema migration.
/// </summary>
public sealed class QualitySuggestionWorkspace
{
    internal const string Protocol = "wi0629-native-manual-suggestions-v1";
    public static readonly string[] Scopes =
        ["archive-cleanup", "collection-curation", "slideshow-curation"];
    public static readonly string[] Decisions =
        ["keep", "dismiss", "consider-exclusion"];
    public static readonly IReadOnlyDictionary<string, (string Label, int Weight)> Reasons =
        new Dictionary<string, (string, int)>(StringComparer.Ordinal)
        {
            ["candidate-severe-underexposure"] = ("Severe underexposure", 4),
            ["candidate-severe-overexposure"] = ("Severe overexposure", 4),
            ["candidate-low-sharpness"] = ("Possible blur or missed focus", 2),
            ["candidate-shadow-clipping"] = ("Shadow clipping", 1),
            ["candidate-highlight-clipping"] = ("Highlight clipping", 1),
            ["candidate-low-contrast"] = ("Low contrast", 1),
            ["candidate-low-effective-proxy-resolution"] = ("Low effective resolution", 2)
        };

    private readonly string _path;
    private readonly SemaphoreSlim _gate = new(1, 1);
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web)
    {
        WriteIndented = true
    };

    public QualitySuggestionWorkspace(string root)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(root);
        _path = Path.Combine(Path.GetFullPath(root), "native-quality-suggestions-v1.json");
    }

    public async Task<QualitySuggestionSnapshot> ReadAsync(CancellationToken cancellationToken = default)
    {
        await _gate.WaitAsync(cancellationToken);
        try { return await LoadAsync(cancellationToken); }
        finally { _gate.Release(); }
    }

    public async Task<QualitySuggestionSnapshot> ReplaceCandidatesAsync(
        int scanned, IReadOnlyList<QualitySuggestionCandidate> suggestions,
        CancellationToken cancellationToken = default)
    {
        if (scanned is < 0 or > 5000 || suggestions.Count > 1500 ||
            suggestions.Select(x => x.RevisionId).Distinct(StringComparer.OrdinalIgnoreCase).Count()
                != suggestions.Count ||
            suggestions.Any(x => !Guid.TryParse(x.RevisionId, out Guid id) ||
                id == Guid.Empty || x.Reasons.Count == 0 ||
                x.Reasons.Any(reason => !Reasons.ContainsKey(reason))))
        {
            throw new ArgumentException("Invalid bounded suggestions.");
        }
        await _gate.WaitAsync(cancellationToken);
        try
        {
            QualitySuggestionSnapshot before = await LoadAsync(cancellationToken);
            // Keep human decisions across rescans. A revision that disappears
            // from the current population will not appear as a new suggestion.
            QualitySuggestionSnapshot after = before with
            {
                Scanned = scanned,
                GeneratedAtUtc = DateTimeOffset.UtcNow,
                Candidates = suggestions.ToList()
            };
            await PersistAsync(after, cancellationToken);
            return after;
        }
        finally { _gate.Release(); }
    }

    public async Task<QualitySuggestionSnapshot> DecideAsync(
        string scope, string revisionId, string choice, CancellationToken cancellationToken = default)
    {
        if (!Scopes.Contains(scope) || !Decisions.Contains(choice) ||
            !Guid.TryParse(revisionId, out Guid revision) || revision == Guid.Empty)
            throw new ArgumentException("Unsupported manual suggestion decision.");
        await _gate.WaitAsync(cancellationToken);
        try
        {
            QualitySuggestionSnapshot current = await LoadAsync(cancellationToken);
            if (!current.EnabledScopes.GetValueOrDefault(scope) ||
                !current.Candidates.Any(x =>
                    string.Equals(x.RevisionId, revisionId, StringComparison.OrdinalIgnoreCase)))
                throw new InvalidOperationException("Suggestion is no longer eligible for review.");
            var decisions = new Dictionary<string, string>(current.Decisions, StringComparer.Ordinal);
            decisions[Key(scope, revisionId)] = choice;
            QualitySuggestionSnapshot next = current with { Decisions = decisions };
            await PersistAsync(next, cancellationToken);
            return next;
        }
        finally { _gate.Release(); }
    }

    public async Task<QualitySuggestionSnapshot> SetScopeAsync(
        string scope, bool enabled, CancellationToken cancellationToken = default)
    {
        if (!Scopes.Contains(scope))
            throw new ArgumentException("Unknown review context.");
        await _gate.WaitAsync(cancellationToken);
        try
        {
            QualitySuggestionSnapshot current = await LoadAsync(cancellationToken);
            var settings = new Dictionary<string, bool>(current.EnabledScopes, StringComparer.Ordinal)
            {
                [scope] = enabled
            };
            QualitySuggestionSnapshot next = current with { EnabledScopes = settings };
            await PersistAsync(next, cancellationToken);
            return next;
        }
        finally { _gate.Release(); }
    }

    public static string Key(string scope, string revisionId) =>
        scope + "/" + revisionId.ToLowerInvariant();

    public static QualitySuggestionPage Page(
        QualitySuggestionSnapshot snapshot, string scope, string reason,
        string filter, int offset, int limit)
    {
        if (!Scopes.Contains(scope) || reason != "all" && !Reasons.ContainsKey(reason) ||
            filter is not ("all" or "unreviewed" or "consider-exclusion") ||
            offset < 0 || limit is < 1 or > 100)
            throw new ArgumentException("Invalid suggestion review filters.");

        bool enabled = snapshot.EnabledScopes.GetValueOrDefault(scope);
        IReadOnlyList<QualitySuggestionCandidate> visible = !enabled
            ? []
            : snapshot.Candidates.Where(candidate =>
                (reason == "all" || candidate.Reasons.Contains(reason)) &&
                (filter == "all" ||
                 (filter == "unreviewed" &&
                  !snapshot.Decisions.ContainsKey(Key(scope, candidate.RevisionId))) ||
                 (filter == "consider-exclusion" &&
                  snapshot.Decisions.GetValueOrDefault(Key(scope, candidate.RevisionId))
                  == "consider-exclusion"))).ToArray();

        return new QualitySuggestionPage(
            snapshot.Scanned, snapshot.GeneratedAtUtc, snapshot.Candidates.Count,
            snapshot.EnabledScopes, scope, enabled, reason, filter,
            visible.Count, offset, visible.Skip(offset).Take(limit)
                .Select(candidate => new QualitySuggestionView(
                    candidate.RevisionId,
                    candidate.Reasons.Select(code => new QualitySuggestionReason(
                        code, Reasons[code].Label)).ToArray(),
                    snapshot.Decisions.GetValueOrDefault(Key(scope, candidate.RevisionId)),
                    $"/api/collections/photos/{candidate.RevisionId}/proxy",
                    $"/photo/{candidate.RevisionId}")).ToArray(),
            snapshot.Candidates.Count(x =>
                snapshot.Decisions.ContainsKey(Key(scope, x.RevisionId))),
            snapshot.Candidates.Count(x =>
                snapshot.Decisions.GetValueOrDefault(Key(scope, x.RevisionId))
                == "consider-exclusion"));
    }

    private async Task<QualitySuggestionSnapshot> LoadAsync(CancellationToken cancellationToken)
    {
        if (!File.Exists(_path))
            return QualitySuggestionSnapshot.Empty();
        await using FileStream stream = new(_path, FileMode.Open, FileAccess.Read, FileShare.Read);
        QualitySuggestionSnapshot loaded =
            await JsonSerializer.DeserializeAsync<QualitySuggestionSnapshot>(
                stream, Json, cancellationToken)
            ?? throw new InvalidDataException("Empty quality suggestion state.");
        if (loaded.Version != 1 || loaded.Protocol != Protocol ||
            loaded.Candidates is null || loaded.EnabledScopes is null || loaded.Decisions is null ||
            loaded.Candidates.Count > 1500 || loaded.Scanned is < 0 or > 5000 ||
            loaded.EnabledScopes.Count != Scopes.Length ||
            Scopes.Any(s => !loaded.EnabledScopes.ContainsKey(s)) ||
            loaded.Candidates.Any(c => !Guid.TryParse(c.RevisionId, out _) ||
                c.Reasons.Any(reason => !Reasons.ContainsKey(reason))) ||
            loaded.Decisions.Any(pair =>
                !Decisions.Contains(pair.Value) ||
                !Scopes.Any(scope => pair.Key.StartsWith(scope + "/", StringComparison.Ordinal))))
            throw new InvalidDataException("Unsafe or incompatible manual-suggestion state.");
        return loaded;
    }

    private async Task PersistAsync(
        QualitySuggestionSnapshot snapshot, CancellationToken cancellationToken)
    {
        string dir = Path.GetDirectoryName(_path)!;
        Directory.CreateDirectory(dir);
        string temporary = _path + ".tmp-" + Guid.NewGuid().ToString("N");
        try
        {
            await using (FileStream stream = new(temporary, FileMode.CreateNew,
                FileAccess.Write, FileShare.None))
                await JsonSerializer.SerializeAsync(stream, snapshot, Json, cancellationToken);
            File.Move(temporary, _path, overwrite: true);
        }
        finally { if (File.Exists(temporary)) File.Delete(temporary); }
    }
}

public sealed record QualitySuggestionCandidate(
    string RevisionId, IReadOnlyList<string> Reasons, int Priority);

public sealed record QualitySuggestionSnapshot(
    int Version, string Protocol, DateTimeOffset? GeneratedAtUtc, int Scanned,
    List<QualitySuggestionCandidate> Candidates,
    Dictionary<string, bool> EnabledScopes,
    Dictionary<string, string> Decisions)
{
    public static QualitySuggestionSnapshot Empty() => new(
        1, QualitySuggestionWorkspace.Protocol, null, 0, [],
        new Dictionary<string, bool>(StringComparer.Ordinal)
        {
            ["archive-cleanup"] = true,
            ["collection-curation"] = false,
            ["slideshow-curation"] = false
        }, new Dictionary<string, string>(StringComparer.Ordinal));
}

public sealed record QualitySuggestionReason(string Code, string Label);
public sealed record QualitySuggestionView(
    string RevisionId, IReadOnlyList<QualitySuggestionReason> Reasons,
    string? Decision, string ProxyUrl, string PhotoUrl);
public sealed record QualitySuggestionPage(
    int Scanned, DateTimeOffset? GeneratedAtUtc, int Suggested,
    Dictionary<string, bool> EnabledScopes,
    string Scope, bool Enabled, string Reason, string Filter,
    int Total, int Offset, IReadOnlyList<QualitySuggestionView> Items,
    int ReviewedInScope, int ConsiderExclusionInScope);
