namespace PhotoIdentity.Core.Review;
public static class CatalogueSuggestionGallerySorts
{
    public const string CreatedDescending = "created-desc";
    public const string SuggestedPerson = "suggested-person";
    public const string ConfidenceGroup = "confidence-group";
    public const string ScoreMarginDescending = "margin-desc";
    public const string ScoreMarginAscending = "margin-asc";
    public const string ScoreDescending = "score-desc";
    public const string NoSuggestionFirst = "no-suggestion-first";
}

public static class CatalogueSuggestionConfidenceFilters
{
    public const string All = "all";
    public const string High = ReviewIdentitySuggestionConfidenceGroups.High;
    public const string Medium = ReviewIdentitySuggestionConfidenceGroups.Medium;
    public const string Low = ReviewIdentitySuggestionConfidenceGroups.Low;
}
