using PhotoIdentity.Core.Identifiers;
using PhotoIdentity.Core.Recognition;

namespace PhotoIdentity.Api;

public static partial class DetectorEvaluationComparisonEndpoints
{
    private static async Task<IResult> GetComparisonPhotoContentAsync(
        string comparisonId,
        string revisionId,
        DetectorEvaluationComparisonStore comparisonStore,
        CollectionPhotoFileResolver resolver,
        CancellationToken cancellationToken)
    {
        if (!TryParseIdentifier(comparisonId, out Guid parsedComparisonId))
        {
            return Results.BadRequest(new { error = "The detector comparison identifier is invalid." });
        }

        if (!TryParseIdentifier(revisionId, out Guid parsedRevisionId))
        {
            return Results.BadRequest(new { error = "The asset revision identifier is invalid." });
        }

        StoredDetectorEvaluationComparison? comparison = await comparisonStore.GetAsync(
            parsedComparisonId,
            cancellationToken);
        if (comparison is null)
        {
            return Results.NotFound();
        }

        StoredDetectorEvaluationComparisonPhoto? photo = comparison.Photos.FirstOrDefault(value =>
            string.Equals(value.CandidateRevisionId, revisionId, StringComparison.OrdinalIgnoreCase));
        if (photo is null)
        {
            return Results.NotFound();
        }

        CollectionPhotoFile? file = await resolver.ResolveAsync(
            AssetRevisionId.From(parsedRevisionId),
            cancellationToken);
        if (file is null)
        {
            file = await resolver.ResolveAsync(
                photo.PhotoName,
                new Sha256Digest(photo.RevisionSha256),
                cancellationToken);
        }

        return file is null
            ? Results.NotFound(new
            {
                error = "The comparison source photo is not available in the current catalogue. Open a catalogue containing the same staged filename and SHA-256 source revision.",
            })
            : Results.File(file.Path, file.ContentType, enableRangeProcessing: true);
    }
}
