using PhotoIdentity.Core.Catalogue;
using PhotoIdentity.Core.Identifiers;
using PhotoIdentity.Web.Contracts;

namespace PhotoIdentity.Api;

public static class PhotoCaptionEndpoints
{
    public static IEndpointRouteBuilder MapPhotoCaptionEndpoints(
        this IEndpointRouteBuilder endpoints)
    {
        endpoints.MapGet(
            "/api/caption-enrichment/status",
            GetStatusAsync);
        endpoints.MapPut(
            "/api/caption-enrichment/settings",
            UpdateSettingsAsync);
        endpoints.MapGet(
            "/api/photos/{revisionId}/caption",
            GetCurrentCaptionAsync);
        endpoints.MapGet(
            "/api/photos/{revisionId}/captions/{language}",
            GetCaptionAsync);
        return endpoints;
    }

    private static async Task<IResult> GetStatusAsync(
        IPhotoCaptionRepository repository,
        PhotoCaptionEnrichmentWorkerState workerState,
        PhotoCaptionGenerationConfiguration generation,
        CancellationToken cancellationToken)
    {
        PhotoCaptionEnrichmentSettings settings =
            await repository.GetSettingsAsync(cancellationToken);
        PhotoCaptionEnrichmentWorkerSnapshot worker = workerState.GetSnapshot();
        return Results.Ok(new PhotoCaptionEnrichmentStatusResponse(
            settings.Enabled,
            settings.Language,
            worker.State,
            worker.Message,
            worker.LastActivityAtUtc,
            worker.NextAttemptAtUtc,
            generation.Model,
            PhotoCaptionGenerationConfiguration.GenerationVersion));
    }

    private static async Task<IResult> UpdateSettingsAsync(
        PhotoCaptionEnrichmentSettingsRequest request,
        IPhotoCaptionRepository repository,
        PhotoCaptionEnrichmentWorkerState workerState,
        PhotoCaptionGenerationConfiguration generation,
        TimeProvider timeProvider,
        CancellationToken cancellationToken)
    {
        if (!PhotoCaptionLanguages.TryNormalize(request.Language, out string language))
        {
            return Results.BadRequest(new
            {
                error = "Caption language must be 'sv' or 'en'.",
            });
        }

        PhotoCaptionEnrichmentSettings settings =
            await repository.UpdateSettingsAsync(
                request.Enabled,
                language,
                timeProvider.GetUtcNow(),
                cancellationToken);
        PhotoCaptionEnrichmentWorkerSnapshot worker = workerState.GetSnapshot();
        return Results.Ok(new PhotoCaptionEnrichmentStatusResponse(
            settings.Enabled,
            settings.Language,
            worker.State,
            worker.Message,
            worker.LastActivityAtUtc,
            worker.NextAttemptAtUtc,
            generation.Model,
            PhotoCaptionGenerationConfiguration.GenerationVersion));
    }

    private static async Task<IResult> GetCurrentCaptionAsync(
        string revisionId,
        IPhotoCaptionRepository repository,
        CancellationToken cancellationToken)
    {
        PhotoCaptionEnrichmentSettings settings =
            await repository.GetSettingsAsync(cancellationToken);
        return await GetCaptionAsync(
            revisionId,
            settings.Language,
            repository,
            cancellationToken);
    }

    private static async Task<IResult> GetCaptionAsync(
        string revisionId,
        string language,
        IPhotoCaptionRepository repository,
        CancellationToken cancellationToken)
    {
        if (!Guid.TryParse(revisionId, out Guid revisionGuid) ||
            revisionGuid == Guid.Empty ||
            !PhotoCaptionLanguages.TryNormalize(language, out string normalizedLanguage))
        {
            return Results.BadRequest(new
            {
                error = "Caption revision or language is invalid.",
            });
        }

        AssetRevisionId parsed = AssetRevisionId.From(revisionGuid);
        PhotoGeneratedCaption? caption = await repository.GetLatestAsync(
            parsed,
            normalizedLanguage,
            cancellationToken);
        if (caption is null)
        {
            return Results.Ok(new PhotoCaptionResponse(
                parsed.ToString(),
                normalizedLanguage,
                "missing",
                null,
                null));
        }

        return Results.Ok(new PhotoCaptionResponse(
            parsed.ToString(),
            normalizedLanguage,
            caption.IsDisplayable ? "available" : "blocked",
            caption.DisplayableContent,
            caption.GeneratedAtUtc));
    }
}
