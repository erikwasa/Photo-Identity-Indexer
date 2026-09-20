using System.Text.RegularExpressions;
using PhotoIdentity.Core.Identifiers;

namespace PhotoIdentity.Core.Collections;

public static class CreativeCollectionGeneratedTextPolicies
{
    public const string VisibleCaptionV1 = "wi-0128-visible-caption-v1";
    public const string DeterministicCaptionV1 = "wi-0128-deterministic-caption-v1";
}

public static class GeneratedCreativeTextRiskCodes
{
    public const string PossibleProperNameOrLocation = "possible-proper-name-or-location";
    public const string RelationshipClaim = "relationship-claim";
    public const string EventIdentityClaim = "event-identity-claim";
    public const string DateOrYearClaim = "date-or-year-claim";
    public const string AgeClaim = "age-claim";
}

/// <summary>
/// Regenerable generated presentation text for one immutable asset revision.
/// It is derived model output and must never be treated as canonical photo metadata.
/// </summary>
public sealed record GeneratedCreativeTextEvidence
{
    public GeneratedCreativeTextEvidence(
        AssetRevisionId revisionId,
        string modelId,
        string modelDigest,
        string promptVersion,
        string content,
        IReadOnlyList<string> riskFlags)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(modelId);
        ArgumentException.ThrowIfNullOrWhiteSpace(modelDigest);
        ArgumentException.ThrowIfNullOrWhiteSpace(promptVersion);
        ArgumentException.ThrowIfNullOrWhiteSpace(content);
        ArgumentNullException.ThrowIfNull(riskFlags);

        string normalizedDigest = modelDigest.Trim().ToLowerInvariant();
        if (normalizedDigest.Length != 64 ||
            normalizedDigest.Any(character => !Uri.IsHexDigit(character)))
        {
            throw new ArgumentException(
                "Generated-text model digest must contain exactly 64 hexadecimal characters.",
                nameof(modelDigest));
        }

        string[] normalizedFlags = riskFlags
            .Where(flag => !string.IsNullOrWhiteSpace(flag))
            .Select(flag => flag.Trim())
            .Distinct(StringComparer.Ordinal)
            .OrderBy(flag => flag, StringComparer.Ordinal)
            .ToArray();

        RevisionId = revisionId;
        ModelId = modelId.Trim();
        ModelDigest = normalizedDigest;
        PromptVersion = promptVersion.Trim();
        Content = content.Trim();
        RiskFlags = normalizedFlags;
    }

    public AssetRevisionId RevisionId { get; }
    public string ModelId { get; }
    public string ModelDigest { get; }
    public string PromptVersion { get; }
    public string Content { get; }
    public IReadOnlyList<string> RiskFlags { get; }
    public bool PassesGuard => RiskFlags.Count == 0;
}

public static partial class GeneratedCreativeTextGuard
{
    private static readonly string[] RelationshipTerms =
    [
        "mother", "mom", "mum", "father", "dad", "parent",
        "son", "daughter", "sister", "brother", "sibling",
        "grandmother", "grandma", "grandfather", "grandpa",
        "wife", "husband", "spouse", "couple", "family",
        "mamma", "mor", "pappa", "far", "förälder",
        "son", "dotter", "syster", "bror", "syskon",
        "mormor", "farmor", "morfar", "farfar",
        "fru", "make", "maka", "par", "familj",
    ];

    private static readonly string[] EventIdentityTerms =
    [
        "birthday", "wedding", "graduation", "funeral",
        "christmas", "easter", "halloween", "anniversary",
        "concert", "festival", "party", "ceremony",
        "födelsedag", "bröllop", "student", "examen", "begravning",
        "jul", "påsk", "halloween", "årsdag",
        "konsert", "festival", "fest", "ceremoni",
    ];

    public static IReadOnlyList<string> Evaluate(string content)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(content);
        string normalized = content.Trim();
        List<string> flags = [];

        if (ContainsTerm(normalized, RelationshipTerms))
        {
            flags.Add(GeneratedCreativeTextRiskCodes.RelationshipClaim);
        }

        if (ContainsTerm(normalized, EventIdentityTerms))
        {
            flags.Add(GeneratedCreativeTextRiskCodes.EventIdentityClaim);
        }

        if (DateOrYearRegex().IsMatch(normalized))
        {
            flags.Add(GeneratedCreativeTextRiskCodes.DateOrYearClaim);
        }

        if (AgeRegex().IsMatch(normalized))
        {
            flags.Add(GeneratedCreativeTextRiskCodes.AgeClaim);
        }

        MatchCollection possibleProperNames = PossibleProperNameRegex().Matches(normalized);
        for (int index = 0; index < possibleProperNames.Count; index++)
        {
            if (!IsSentenceInitialCapitalizedWord(
                    normalized,
                    possibleProperNames[index].Index))
            {
                flags.Add(GeneratedCreativeTextRiskCodes.PossibleProperNameOrLocation);
                break;
            }
        }

        return flags
            .Distinct(StringComparer.Ordinal)
            .OrderBy(value => value, StringComparer.Ordinal)
            .ToArray();
    }

    private static bool IsSentenceInitialCapitalizedWord(
        string content,
        int matchIndex)
    {
        if (matchIndex <= 0)
        {
            return true;
        }

        for (int index = matchIndex - 1; index >= 0; index--)
        {
            char character = content[index];
            if (char.IsWhiteSpace(character) ||
                character is '"' or '\'' or '“' or '”' or '‘' or '’' or
                    '(' or ')' or '[' or ']' or '{' or '}')
            {
                continue;
            }

            return character is '.' or '!' or '?';
        }

        return true;
    }

    private static bool ContainsTerm(string content, IEnumerable<string> terms) =>
        terms.Any(term =>
            Regex.IsMatch(
                content,
                $@"\b{Regex.Escape(term)}\b",
                RegexOptions.IgnoreCase | RegexOptions.CultureInvariant));

    [GeneratedRegex(
        @"\b(?:19|20)\d{2}\b|\b(?:january|february|march|april|may|june|july|august|september|october|november|december|januari|februari|mars|april|maj|juni|juli|augusti|september|oktober|november|december)\b",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex DateOrYearRegex();

    [GeneratedRegex(
        @"\b(?:\d{1,3}\s*(?:-|\s)?\s*(?:year|years|yr|yrs)(?:\s|-)?old|\d{1,3}\s*(?:-|\s)?\s*år(?:ig|iga|igt|ing)?|\d{1,3}\s+år\s+gammal)\b",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex AgeRegex();

    [GeneratedRegex(
        @"\b[A-ZÅÄÖ][a-zåäö]{2,}\b",
        RegexOptions.CultureInvariant)]
    private static partial Regex PossibleProperNameRegex();
}

public static class CreativeCollectionDeterministicCaption
{
    public static string Build(
        CreativeCollectionCandidate candidate,
        IReadOnlyCollection<string>? peopleKeys)
    {
        ArgumentNullException.ThrowIfNull(candidate);

        string provenance = candidate.Kind == CreativeCollectionCandidateKinds.DirectAnchor
            ? "Direct collection match"
            : "Context photo from the same inferred moment";

        string date = candidate.TakenAtLocal is DateTime taken
            ? $"captured {taken:yyyy-MM-dd}"
            : "capture date unavailable";

        int identifiedPeople = peopleKeys?
            .Where(value => !string.IsNullOrWhiteSpace(value))
            .Distinct(StringComparer.Ordinal)
            .Count() ?? 0;
        string people = identifiedPeople switch
        {
            0 => "no identified people recorded",
            1 => "1 identified person recorded",
            _ => $"{identifiedPeople} identified people recorded",
        };

        return $"{provenance} · {date} · {people}.";
    }
}
