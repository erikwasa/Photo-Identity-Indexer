namespace PhotoIdentity.Api;

public static class PhotoMetadataEndpoints
{
    public static IEndpointRouteBuilder MapPhotoMetadataEndpoints(this IEndpointRouteBuilder endpoints)
    {
        endpoints.MapPost("/api/photo-metadata/backfill", BackfillAsync);
        return endpoints;
    }

    private static async Task<IResult> BackfillAsync(
        int? limit,
        int? offset,
        bool? force,
        PhotoMetadataBackfillService service,
        CancellationToken cancellationToken)
    {
        try
        {
            PhotoMetadataBackfillReport report = await service.ExecuteBatchAsync(
                limit ?? 250,
                offset ?? 0,
                force ?? false,
                cancellationToken);
            return Results.Ok(report);
        }
        catch (ArgumentException exception)
        {
            return Results.BadRequest(new { error = exception.Message });
        }
    }
}
