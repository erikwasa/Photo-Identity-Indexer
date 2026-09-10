using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using PhotoIdentity.Api;
using PhotoIdentity.Core.Catalogue;
using PhotoIdentity.Core.Collections;
using PhotoIdentity.Core.People;
using PhotoIdentity.Core.Places;
using PhotoIdentity.Core.Processing;
using PhotoIdentity.Core.Recognition;
using PhotoIdentity.Core.Review;
using PhotoIdentity.Persistence.Postgres;
using PhotoIdentity.Persistence.Sqlite;
using Xunit;

namespace PhotoIdentity_Integration_Tests;

public sealed class CataloguePersistenceCompositionTests
{
    [Fact]
    public async Task PostgreSQL_composition_binds_authoritative_domains_without_SQLite_catalogue()
    {
        ServiceCollection services = new();
        services.AddSingleton(TimeProvider.System);
        PostgresCatalogueDatabase database = CataloguePersistenceComposition.AddPostgres(
            services,
            "Host=localhost;Database=photo_identity_composition_test");

        Assert.DoesNotContain(services, descriptor => descriptor.ServiceType == typeof(SqliteCatalogueDatabase));

        await using ServiceProvider provider = services.BuildServiceProvider();
        Assert.Same(database, provider.GetRequiredService<PostgresCatalogueDatabase>());
        Assert.Same(database, provider.GetRequiredService<ICatalogueStoreInitializer>());

        Assert.IsType<PostgresReviewQueryRepository>(provider.GetRequiredService<IReviewFaceRepository>());
        Assert.IsType<PostgresReviewActionRepository>(provider.GetRequiredService<IReviewActionRepository>());
        Assert.IsType<PostgresIdentityMatchModelRepository>(provider.GetRequiredService<IIdentityMatchModelRepository>());
        Assert.IsType<PostgresIdentityMatchRegenerationRepository>(provider.GetRequiredService<IIdentityMatchRegenerationRepository>());
        Assert.IsType<PostgresIdentityMatchRegenerationScorer>(provider.GetRequiredService<IIdentityMatchRegenerationScorer>());
        Assert.IsType<PostgresPersonPresentationRepository>(provider.GetRequiredService<IPersonFeaturedFaceRepository>());
        Assert.IsType<PostgresCollectionQueryRepository>(provider.GetRequiredService<ICollectionQueryRepository>());
        Assert.IsType<PostgresPhotoMetadataInspectionRepository>(provider.GetRequiredService<IPhotoMetadataInspectionRepository>());
        Assert.IsType<PostgresPhotoPlaceRepository>(provider.GetRequiredService<IPhotoPlaceRepository>());
        Assert.IsType<PostgresDetectorEvaluationCatalogueRepository>(provider.GetRequiredService<IDetectorEvaluationCatalogueRepository>());
        Assert.IsType<PostgresAssetRevisionLookupRepository>(provider.GetRequiredService<IAssetRevisionLookupRepository>());
        Assert.IsType<PostgresProcessingRepository>(provider.GetRequiredService<IProcessingExecutionRepository>());
        Assert.IsType<PostgresFaceInspectionRepository>(provider.GetRequiredService<IFaceInspectionRepository>());
        Assert.IsType<PostgresArchiveCoverageRepository>(provider.GetRequiredService<IArchiveCoverageRepository>());
        Assert.IsType<PostgresFaceReviewDerivativeRepository>(provider.GetRequiredService<IFaceReviewDerivativeRepository>());
        Assert.IsType<PostgresArchiveAdvancementControlRepository>(provider.GetRequiredService<IArchiveAdvancementControlRepository>());
    }

    [Fact]
    public void Provider_selection_defaults_to_SQLite_and_requires_known_value()
    {
        ConfigurationManager configuration = new();
        Assert.Equal(CatalogueProviderKind.Sqlite, CataloguePersistenceComposition.ResolveProvider(configuration));

        configuration["PhotoIdentity:CatalogueProvider"] = "postgresql";
        Assert.Equal(CatalogueProviderKind.Postgres, CataloguePersistenceComposition.ResolveProvider(configuration));

        configuration["PhotoIdentity:CatalogueProvider"] = "unexpected";
        Assert.Throws<InvalidOperationException>(() => CataloguePersistenceComposition.ResolveProvider(configuration));
    }
}
