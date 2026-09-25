namespace PhotoIdentity.Web.Contracts;

public sealed record SmartCollectionLocationRequest(
    double? South = null,
    double? West = null,
    double? North = null,
    double? East = null,
    string? Place = null,
    string[]? Places = null);

public sealed record SmartCollectionDateRangeRequest(
    string From,
    string To);

public sealed record SmartCollectionAgeRequest(
    string PersonId,
    int MinimumYears,
    int MaximumYears);

public sealed record SmartCollectionRelationshipRequest(
    string PersonId,
    string[] Kinds);

public sealed record SmartCollectionDefinitionRequest(
    string Name,
    string[]? People = null,
    string? PeopleMatch = null,
    string[]? Tags = null,
    string? TagMatch = null,
    SmartCollectionLocationRequest? Location = null,
    string? Taken = null,
    SmartCollectionDateRangeRequest? TakenRange = null,
    SmartCollectionAgeRequest? Age = null,
    SmartCollectionRelationshipRequest? Relationship = null);

public sealed record SmartCollectionQueryRequest(
    string[]? People = null,
    string? PeopleMatch = null,
    string[]? Tags = null,
    string? TagMatch = null,
    SmartCollectionLocationRequest? Location = null,
    string? Taken = null,
    int Offset = 0,
    int Limit = 40,
    SmartCollectionDateRangeRequest? TakenRange = null,
    SmartCollectionAgeRequest? Age = null,
    SmartCollectionRelationshipRequest? Relationship = null);

public sealed record SmartCollectionDateRangeResponse(
    string From,
    string To);

public sealed record SmartCollectionFilterResponse(
    string[] People,
    string PeopleMatch,
    string[] Tags,
    string TagMatch,
    SmartCollectionLocationRequest? Location,
    SmartCollectionDateRangeResponse? Taken,
    SmartCollectionAgeRequest? Age = null,
    SmartCollectionRelationshipRequest? Relationship = null);

public sealed record SmartCollectionDefinitionResponse(
    string Id,
    string Name,
    SmartCollectionFilterResponse Filter,
    DateTimeOffset CreatedAtUtc,
    DateTimeOffset UpdatedAtUtc);

public sealed record SmartCollectionPhotoResponse(
    string RevisionId,
    string AssetId,
    string ThumbnailUrl,
    string PreviewUrl,
    string OriginalUrl,
    DateTimeOffset ObservedAtUtc,
    string? MediaType,
    int? Width,
    int? Height,
    DateTime? TakenAtLocal,
    double? Latitude,
    double? Longitude,
    string? EffectivePlace = null);

public sealed record SmartCollectionPageResponse(
    SmartCollectionPhotoResponse[] Items,
    int Offset,
    int Limit,
    int Total,
    SmartCollectionFilterResponse Filter,
    string? CollectionId = null,
    string? CollectionName = null);

public sealed record SmartCollectionSlideshowSnapshotItemResponse(
    string RevisionId,
    string? MomentId = null,
    string? VisualGroupId = null);

public sealed record SmartCollectionSlideshowSnapshotResponse(
    string CollectionId,
    string CollectionName,
    DateTimeOffset CreatedAtUtc,
    SmartCollectionSlideshowSnapshotItemResponse[] Items,
    int Total,
    string? MomentPolicyVersion = null,
    string? VisualRedundancyPolicyVersion = null);

public sealed record SmartCollectionErrorResponse(string Error);

public sealed record CreativeCollectionRecipeRequest(
    int TargetCount = 50,
    string ContextStrength = "balanced",
    bool NoveltyEnabled = false,
    string? Name = null);

public sealed record CreativeCollectionRecipeResponse(
    string Id,
    string Name,
    string AnchorCollectionId,
    string AnchorCollectionName,
    int TargetCount,
    int MomentGapMinutes,
    string MomentPolicyVersion,
    string ContextStrength,
    string ContextPolicyVersion,
    string SelectionPolicyVersion,
    string OrderingPolicyVersion,
    bool NoveltyEnabled,
    string NoveltyPolicyVersion,
    DateTimeOffset CreatedAtUtc,
    DateTimeOffset UpdatedAtUtc);

public sealed record CreativeCollectionContextReasonResponse(
    string MomentId,
    string[] AnchorRevisionIds);

public sealed record CreativeCollectionSelectionReasonResponse(
    string Code,
    int ScoreDelta,
    string Detail);

public sealed record CreativeCollectionPreviewCandidateResponse(
    string RevisionId,
    string Kind,
    DateTime? TakenAtLocal,
    string ThumbnailUrl,
    int ShowCount,
    DateTimeOffset? LastShownAtUtc,
    CreativeCollectionContextReasonResponse[] ContextReasons);

public sealed record CreativeCollectionSelectedCandidateResponse(
    string RevisionId,
    string Kind,
    DateTime? TakenAtLocal,
    string ThumbnailUrl,
    string? MomentId,
    string? PeopleCombinationKey,
    int SelectionScore,
    int ShowCount,
    DateTimeOffset? LastShownAtUtc,
    CreativeCollectionSelectionReasonResponse[] SelectionReasons,
    CreativeCollectionContextReasonResponse[] ContextReasons);

public sealed record CreativeCollectionPreviewResponse(
    string CollectionId,
    string CollectionName,
    string MomentPolicyVersion,
    string ContextPolicyVersion,
    int DirectAnchorCount,
    int AddedContextCount,
    int TotalCandidateCount,
    bool NoAnchors,
    string SelectionPolicyVersion,
    bool NoveltyEnabled,
    string NoveltyPolicyVersion,
    int RequestedTargetCount,
    int SelectedDirectAnchorCount,
    int SelectedContextCount,
    int SelectedCount,
    int RepresentedMomentCount,
    int RepresentedTimePeriodCount,
    CreativeCollectionPreviewCandidateResponse[] Candidates,
    CreativeCollectionSelectedCandidateResponse[] SelectedCandidates);
