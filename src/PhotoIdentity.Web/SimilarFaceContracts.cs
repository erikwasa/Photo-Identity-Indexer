namespace PhotoIdentity.Web.Contracts;

public sealed record SimilarFaceResponse(
    ReviewFaceResponse Face,
    double Similarity);

public sealed record SimilarFacePageResponse(
    string SourceFaceId,
    string ModelId,
    string ModelHash,
    bool IncludeUnknown,
    int ScannedFaceCount,
    long QueryElapsedMilliseconds,
    IReadOnlyList<SimilarFaceResponse> Items);
