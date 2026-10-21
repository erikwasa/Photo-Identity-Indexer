using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using PhotoIdentity.Core.Collections;

namespace PhotoIdentity.Api;

/// <summary>
/// Non-operative human annotations on automatically discovered groups.
/// No collection filter or source exclusion reads this store.
/// Each choice is bound to its EXACT set of revision IDs so drifted groups
/// cannot inherit a decision from another collection snapshot.
/// </summary>
public sealed class CreativeSimilarityReviewStore
{
    public const string Protocol = "wi0638-creative-similarity-human-review-v1";
    public const string KeepAll = "keep-all";
    public const string PreferOne = "prefer-one";
    private readonly string _path;
    private readonly SemaphoreSlim _gate = new(1, 1);
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web)
        { WriteIndented = true };

    public CreativeSimilarityReviewStore(string localRoot)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(localRoot);
        _path = Path.Combine(Path.GetFullPath(localRoot), "creative-similarity-review-v1.json");
    }

    public static string GroupKey(IEnumerable<string> revisionIds)
    {
        string[] sorted = revisionIds.OrderBy(id => id, StringComparer.Ordinal).ToArray();
        if (sorted.Length is < 2 or > 500 ||
            sorted.Any(id => !Guid.TryParse(id, out Guid value) || value == Guid.Empty) ||
            sorted.Distinct(StringComparer.OrdinalIgnoreCase).Count() != sorted.Length)
            throw new ArgumentException("A review group needs distinct valid revision identities.");
        string message = string.Join("|", sorted);
        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(message))).ToLowerInvariant();
    }

    public static string GroupKey(PhotoVisualRedundancyGroup group) =>
        GroupKey(group.Members.Select(member => member.RevisionId.ToString().ToLowerInvariant()));

    public async Task<CreativeSimilarityHumanChoice?> GetAsync(
        string recipeId, string groupKey, CancellationToken token = default)
    {
        ValidateIdentity(recipeId, groupKey);
        await _gate.WaitAsync(token);
        try
        {
            return (await ReadAsync(token)).Choices.GetValueOrDefault(recipeId + "/" + groupKey);
        }
        finally { _gate.Release(); }
    }

    public async Task SaveAsync(
        string recipeId, string groupKey, string decision, string? preferredRevisionId,
        CancellationToken token = default)
    {
        ValidateIdentity(recipeId, groupKey);
        if (decision is not (KeepAll or PreferOne) ||
            (decision == PreferOne && (!Guid.TryParse(preferredRevisionId, out Guid parsed)
                || parsed == Guid.Empty)) ||
            (decision == KeepAll && preferredRevisionId is not null))
            throw new ArgumentException("Use Keep all or prefer exactly one valid current photo.");

        await _gate.WaitAsync(token);
        try
        {
            CreativeSimilarityReviewSnapshot snapshot = await ReadAsync(token);
            var choices = new Dictionary<string, CreativeSimilarityHumanChoice>(
                snapshot.Choices, StringComparer.Ordinal);
            choices[recipeId + "/" + groupKey] = new(
                decision, preferredRevisionId?.ToLowerInvariant(), DateTimeOffset.UtcNow);
            await WriteAsync(new CreativeSimilarityReviewSnapshot(1, Protocol, choices), token);
        }
        finally { _gate.Release(); }
    }

    private static void ValidateIdentity(string recipeId, string groupKey)
    {
        if (!Guid.TryParse(recipeId, out Guid id) || id == Guid.Empty ||
            groupKey.Length != 64 || groupKey.Any(ch =>
                !char.IsAsciiHexDigit(ch) || char.IsUpper(ch)))
            throw new ArgumentException("Unrecognized recipe or similarity group identity.");
    }

    private async Task<CreativeSimilarityReviewSnapshot> ReadAsync(CancellationToken token)
    {
        if (!File.Exists(_path))
            return new(1, Protocol, new Dictionary<string, CreativeSimilarityHumanChoice>(
                StringComparer.Ordinal));
        await using FileStream file = new(_path, FileMode.Open, FileAccess.Read, FileShare.Read);
        CreativeSimilarityReviewSnapshot snapshot =
            await JsonSerializer.DeserializeAsync<CreativeSimilarityReviewSnapshot>(
                file, Json, token) ?? throw new InvalidDataException("Empty similarity review file.");
        if (snapshot.Version != 1 || snapshot.Protocol != Protocol ||
            snapshot.Choices is null || snapshot.Choices.Count > 25000 ||
            snapshot.Choices.Any(entry =>
                entry.Value.Decision is not (KeepAll or PreferOne) ||
                (entry.Value.Decision == KeepAll && entry.Value.PreferredRevisionId is not null) ||
                (entry.Value.Decision == PreferOne &&
                 !Guid.TryParse(entry.Value.PreferredRevisionId, out _))))
            throw new InvalidDataException("Incompatible similarity review file.");
        return snapshot;
    }

    private async Task WriteAsync(CreativeSimilarityReviewSnapshot state, CancellationToken token)
    {
        string dir = Path.GetDirectoryName(_path)!;
        Directory.CreateDirectory(dir);
        string temporary = _path + ".tmp-" + Guid.NewGuid().ToString("N");
        try
        {
            await using (FileStream file = new(temporary, FileMode.CreateNew,
                FileAccess.Write, FileShare.None))
                await JsonSerializer.SerializeAsync(file, state, Json, token);
            File.Move(temporary, _path, overwrite: true);
        }
        finally { if (File.Exists(temporary)) File.Delete(temporary); }
    }
}

public sealed record CreativeSimilarityHumanChoice(
    string Decision, string? PreferredRevisionId, DateTimeOffset RecordedAtUtc);

public sealed record CreativeSimilarityReviewSnapshot(
    int Version, string Protocol,
    Dictionary<string, CreativeSimilarityHumanChoice> Choices);
