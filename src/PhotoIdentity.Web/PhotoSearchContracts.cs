namespace PhotoIdentity.Web.Contracts;

public sealed record PhotoSearchRequest(
    string Query,
    string? Mode = null,
    int Limit = 80);

public sealed record PhotoSearchResultResponse(
    string RevisionId,
    string ThumbnailUrl,
    string PreviewUrl,
    string PhotoUrl,
    double CombinedScore,
    double? SemanticScore,
    double? CaptionScore,
    string? CaptionLanguage,
    string? Caption,
    string[] Sources);

public sealed record PhotoSearchResponse(
    string Query,
    string Mode,
    bool SemanticModelAvailable,
    int IndexedPhotoCount,
    int DisplayableCaptionCount,
    double SearchMilliseconds,
    PhotoSearchResultResponse[] Items);

public sealed record PhotoSearchSaveCollectionRequest(
    string Name,
    string[] RevisionIds);

public sealed record PhotoSearchStatusResponse(
    bool SemanticModelAvailable,
    string ModelId,
    string? ModelSha256,
    string ModelDirectory,
    int CurrentPhotoCount,
    int IndexedPhotoCount,
    int DisplayableCaptionCount,
    int? EmbeddingDimensions,
    long RawEmbeddingBytes,
    double? AverageEmbeddingGenerationMilliseconds,
    bool ExactVectorSearch,
    bool AnnIndexUsed);
