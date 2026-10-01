using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using PhotoIdentity.Api;
using PhotoIdentity.Core.Catalogue;
using PhotoIdentity.Core.Collections;
using PhotoIdentity.Core.Imaging;
using PhotoIdentity.Core.People;
using PhotoIdentity.Core.Places;
using PhotoIdentity.Core.Processing;
using PhotoIdentity.Core.Recognition;
using PhotoIdentity.Core.Review;
using PhotoIdentity.Core.Sources;
using PhotoIdentity.Persistence.Postgres;
using Xunit;

namespace PhotoIdentity_Integration_Tests;

public sealed class CataloguePersistenceCompositionTests
{
    [Fact]
    public void Runtime_pooling_preserves_configured_capacity_and_other_connection_settings()
    {
        Npgsql.NpgsqlConnectionStringBuilder result = new(CataloguePersistenceComposition.RuntimeConnectionString(
            "Host=example.test;Database=catalogue;Username=operator;Pooling=false;Maximum Pool Size=12;Timeout=7;Command Timeout=31"));
        Assert.True(result.Pooling);
        Assert.Equal(12, result.MaxPoolSize);
        Assert.Equal(7, result.Timeout);
        Assert.Equal(31, result.CommandTimeout);
        Assert.Equal("example.test", result.Host);
        Assert.Equal("catalogue", result.Database);
        Assert.Equal("operator", result.Username);
    }

    [Fact]
    public async Task PostgreSQL_composition_binds_authoritative_domains_without_SQLite_catalogue()
    {
        ServiceCollection services = new();
        services.AddSingleton(TimeProvider.System);
        PostgresCatalogueDatabase database = CataloguePersistenceComposition.AddPostgres(
            services,
            "Host=localhost;Database=photo_identity_composition_test");

        Assert.DoesNotContain(services, descriptor => descriptor.ServiceType == typeof(PostgresTestCatalogueDatabase));

        Type[] authoritativeContracts = services
            .Select(descriptor => descriptor.ServiceType)
            .Where(serviceType => serviceType.Namespace?.StartsWith("PhotoIdentity.Core", StringComparison.Ordinal) == true)
            .Distinct()
            .ToArray();
        Assert.NotEmpty(authoritativeContracts);

        await using ServiceProvider provider = services.BuildServiceProvider();
        foreach (Type contract in authoritativeContracts)
        {
            object resolved = provider.GetRequiredService(contract);
            Assert.NotEqual(
                typeof(PostgresTestCatalogueDatabase).Assembly,
                resolved.GetType().Assembly);
        }

        Assert.Same(database, provider.GetRequiredService<PostgresCatalogueDatabase>());
        Assert.Same(database, provider.GetRequiredService<ICatalogueStoreInitializer>());

        Assert.IsType<PostgresReviewQueryRepository>(provider.GetRequiredService<PostgresReviewQueryRepository>());
        Assert.IsType<ExclusionAwareReviewFaceRepository>(provider.GetRequiredService<IReviewFaceRepository>());
        Assert.IsType<PostgresReviewActionRepository>(provider.GetRequiredService<PostgresReviewActionRepository>());
        Assert.IsType<ExclusionAwareReviewActionRepository>(provider.GetRequiredService<IReviewActionRepository>());
        Assert.IsType<PostgresSourceCopyExclusionRepository>(provider.GetRequiredService<ISourceCopyExclusionRepository>());
        Assert.IsType<PostgresIdentityMatchModelRepository>(provider.GetRequiredService<IIdentityMatchModelRepository>());
        Assert.IsType<PostgresIdentityMatchRegenerationRepository>(provider.GetRequiredService<IIdentityMatchRegenerationRepository>());
        Assert.IsType<PostgresIdentityMatchRegenerationScorer>(provider.GetRequiredService<IIdentityMatchRegenerationScorer>());
        Assert.IsType<PostgresPersonPresentationRepository>(provider.GetRequiredService<IPersonFeaturedFaceRepository>());
        Assert.IsType<ExclusionAwareCollectionQueryRepository>(provider.GetRequiredService<ICollectionQueryRepository>());
        Assert.IsType<ExclusionAwareSmartCollectionQueryRepository>(provider.GetRequiredService<ISmartCollectionQueryRepository>());
        Assert.IsType<PostgresPhotoMetadataInspectionRepository>(provider.GetRequiredService<IPhotoMetadataInspectionRepository>());
        Assert.IsType<PostgresPhotoCaptureDateRepository>(provider.GetRequiredService<IPhotoCaptureDateRepository>());
        Assert.IsType<PostgresPhotoPlaceRepository>(provider.GetRequiredService<IPhotoPlaceRepository>());
        Assert.IsType<PostgresDetectorEvaluationCatalogueRepository>(provider.GetRequiredService<IDetectorEvaluationCatalogueRepository>());
        Assert.IsType<PostgresAssetRevisionLookupRepository>(provider.GetRequiredService<IAssetRevisionLookupRepository>());
        Assert.IsType<PostgresProcessingRepository>(provider.GetRequiredService<PostgresProcessingRepository>());
        Assert.IsType<ExclusionAwareProcessingExecutionRepository>(provider.GetRequiredService<IProcessingExecutionRepository>());
        Assert.IsType<PostgresFaceInspectionRepository>(provider.GetRequiredService<IFaceInspectionRepository>());
        Assert.IsType<PostgresArchiveCoverageRepository>(provider.GetRequiredService<IArchiveCoverageRepository>());
        Assert.IsType<PostgresFaceReviewDerivativeRepository>(provider.GetRequiredService<IFaceReviewDerivativeRepository>());
        Assert.IsType<PostgresArchiveAdvancementControlRepository>(provider.GetRequiredService<IArchiveAdvancementControlRepository>());
    }

    [Fact]
    public void Runtime_configuration_requires_PostgreSQL_connection_string()
    {
        ConfigurationManager configuration = new();
        InvalidOperationException missing = Assert.Throws<InvalidOperationException>(
            () => CataloguePersistenceComposition.GetRequiredPostgresConnectionString(configuration));
        Assert.Contains("PostgreSQL is the only supported runtime catalogue", missing.Message);

        configuration["PhotoIdentity:Postgres:ConnectionString"] = " Host=localhost;Database=photo_identity ";
        Assert.Equal(
            " Host=localhost;Database=photo_identity ",
            CataloguePersistenceComposition.GetRequiredPostgresConnectionString(configuration));
    }
}
