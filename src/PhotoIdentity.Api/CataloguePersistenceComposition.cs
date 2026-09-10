using PhotoIdentity.Core.Catalogue;
using PhotoIdentity.Core.Collections;
using PhotoIdentity.Core.People;
using PhotoIdentity.Core.Places;
using PhotoIdentity.Core.Processing;
using PhotoIdentity.Core.Recognition;
using PhotoIdentity.Core.Review;
using PhotoIdentity.Core.Sources;
using PhotoIdentity.Core.Tags;
using PhotoIdentity.Persistence.Postgres;
using PhotoIdentity.Persistence.Sqlite;

namespace PhotoIdentity.Api;

internal enum CatalogueProviderKind
{
    Sqlite,
    Postgres,
}

internal static class CataloguePersistenceComposition
{
    public static CatalogueProviderKind ResolveProvider(IConfiguration configuration)
    {
        string configured = configuration["PhotoIdentity:CatalogueProvider"]?.Trim().ToLowerInvariant() ?? "sqlite";
        return configured switch
        {
            "sqlite" => CatalogueProviderKind.Sqlite,
            "postgres" or "postgresql" => CatalogueProviderKind.Postgres,
            _ => throw new InvalidOperationException(
                "Configuration 'PhotoIdentity:CatalogueProvider' must be 'sqlite' or 'postgresql'."),
        };
    }

    public static void AddSqlite(IServiceCollection services, string databasePath)
    {
        services.AddSingleton(new SqliteCatalogueDatabase(databasePath));

        services.AddSingleton<SqliteReviewRepository>();
        services.AddSingleton<IReviewActionRepository>(sp => sp.GetRequiredService<SqliteReviewRepository>());
        services.AddSingleton<SqliteReviewFilterRepository>();
        services.AddSingleton<IReviewFaceRepository>(sp => sp.GetRequiredService<SqliteReviewRepository>());
        services.AddSingleton<IReviewFilterRepository>(sp => sp.GetRequiredService<SqliteReviewFilterRepository>());
        services.AddSingleton<SqliteReviewSuggestionRepository>();
        services.AddSingleton<IReviewSuggestionRepository>(sp => sp.GetRequiredService<SqliteReviewSuggestionRepository>());
        services.AddSingleton<SqliteSuggestionGalleryRepository>();
        services.AddSingleton<SqliteSuggestionGalleryAdapter>();
        services.AddSingleton<ISuggestionGalleryRepository>(sp => sp.GetRequiredService<SqliteSuggestionGalleryAdapter>());

        services.AddSingleton<SqliteIdentitySuggestionPolicyRepository>();
        services.AddSingleton<SqliteIdentitySuggestionPolicyAdapter>();
        services.AddSingleton<IIdentitySuggestionPolicyRepository>(sp => sp.GetRequiredService<SqliteIdentitySuggestionPolicyAdapter>());
        services.AddSingleton<SqliteIdentityMatchRegenerationModelRepository>();
        services.AddSingleton<IIdentityMatchModelRepository>(sp => sp.GetRequiredService<SqliteIdentityMatchRegenerationModelRepository>());
        services.AddSingleton<SqliteIdentityMatchRegenerationRepository>();
        services.AddSingleton<SqliteIdentityMatchRegenerationAdapter>();
        services.AddSingleton<IIdentityMatchRegenerationRepository>(sp => sp.GetRequiredService<SqliteIdentityMatchRegenerationAdapter>());
        services.AddSingleton<SqliteIdentityMatchRegenerationScorer>();
        services.AddSingleton<SqliteIdentityMatchRegenerationScorerAdapter>();
        services.AddSingleton<IIdentityMatchRegenerationScorer>(sp => sp.GetRequiredService<SqliteIdentityMatchRegenerationScorerAdapter>());
        services.AddSingleton<SqliteIdentityMatchEvidenceVersionAdapter>();
        services.AddSingleton<IIdentityMatchEvidenceVersionReader>(sp => sp.GetRequiredService<SqliteIdentityMatchEvidenceVersionAdapter>());
        services.AddSingleton<SqliteIdentityAutoAssignmentService>();
        services.AddSingleton<SqliteIdentityAutoAssignmentAdapter>();
        services.AddSingleton<IIdentityAutoAssignmentService>(sp => sp.GetRequiredService<SqliteIdentityAutoAssignmentAdapter>());

        services.AddSingleton<SqlitePersonAuditRepository>();
        services.AddSingleton<SqlitePersonAuditAdapter>();
        services.AddSingleton<IPersonAuditRepository>(sp => sp.GetRequiredService<SqlitePersonAuditAdapter>());
        services.AddSingleton<SqlitePersonMaintenanceRepository>();
        services.AddSingleton<IPersonMaintenanceRepository>(sp => sp.GetRequiredService<SqlitePersonMaintenanceRepository>());
        services.AddSingleton<SqlitePersonPhotoCountRepository>();
        services.AddSingleton<IPersonPhotoCountRepository>(sp => sp.GetRequiredService<SqlitePersonPhotoCountRepository>());
        services.AddSingleton<SqliteFavoritePeopleRepository>();
        services.AddSingleton<IFavoritePeopleRepository>(sp => sp.GetRequiredService<SqliteFavoritePeopleRepository>());
        services.AddSingleton<SqlitePersonSmartCollectionVisibilityRepository>();
        services.AddSingleton<IPersonSmartCollectionVisibilityRepository>(sp => sp.GetRequiredService<SqlitePersonSmartCollectionVisibilityRepository>());
        services.AddSingleton<SqlitePersonFeaturedFaceRepository>();
        services.AddSingleton<IPersonFeaturedFaceRepository>(sp => sp.GetRequiredService<SqlitePersonFeaturedFaceRepository>());
        services.AddSingleton<SqliteBulkReviewRepository>();
        services.AddSingleton<IBulkReviewRepository>(sp => sp.GetRequiredService<SqliteBulkReviewRepository>());
        services.AddSingleton<SqliteBulkSuggestionReviewRepository>();
        services.AddSingleton<IBulkSuggestionReviewRepository>(sp => sp.GetRequiredService<SqliteBulkSuggestionReviewRepository>());

        services.AddSingleton<SqliteCollectionQueryRepository>();
        services.AddSingleton<ICollectionQueryRepository>(sp => sp.GetRequiredService<SqliteCollectionQueryRepository>());
        services.AddSingleton<SqlitePhotoDetailsRepository>();
        services.AddSingleton<IPhotoDetailsRepository>(sp => sp.GetRequiredService<SqlitePhotoDetailsRepository>());
        services.AddSingleton<SqliteSmartCollectionQueryRepository>();
        services.AddSingleton<ISmartCollectionQueryRepository>(sp => sp.GetRequiredService<SqliteSmartCollectionQueryRepository>());
        services.AddSingleton<SqliteSmartCollectionRepository>();
        services.AddSingleton<ISmartCollectionRepository>(sp => sp.GetRequiredService<SqliteSmartCollectionRepository>());

        services.AddSingleton<SqliteAssetCatalogueRepository>();
        services.AddSingleton<IPhotoCaptureMetadataRepository>(sp => sp.GetRequiredService<SqliteAssetCatalogueRepository>());
        services.AddSingleton<SqlitePhotoMetadataBackfillRepository>();
        services.AddSingleton<IPhotoMetadataBackfillRepository>(sp => sp.GetRequiredService<SqlitePhotoMetadataBackfillRepository>());
        services.AddSingleton<SqliteExtendedPhotoMetadataRepository>();
        services.AddSingleton<IExtendedPhotoMetadataRepository>(sp => sp.GetRequiredService<SqliteExtendedPhotoMetadataRepository>());
        services.AddSingleton<SqlitePhotoMetadataInspectionRepository>();
        services.AddSingleton<IPhotoMetadataInspectionRepository>(sp => sp.GetRequiredService<SqlitePhotoMetadataInspectionRepository>());
        services.AddSingleton<SqlitePhotoTagRepository>();
        services.AddSingleton<IPhotoTagRepository>(sp => sp.GetRequiredService<SqlitePhotoTagRepository>());
        services.AddSingleton<SqlitePhotoPersonRepository>();
        services.AddSingleton<IPhotoPersonRepository>(sp => sp.GetRequiredService<SqlitePhotoPersonRepository>());
        services.AddSingleton<SqlitePhotoPlaceRepository>();
        services.AddSingleton<IPhotoPlaceRepository>(sp => sp.GetRequiredService<SqlitePhotoPlaceRepository>());
        services.AddSingleton<SqlitePhotoPlaceEnrichmentRepository>();
        services.AddSingleton<IPhotoPlaceEnrichmentStateRepository>(sp => sp.GetRequiredService<SqlitePhotoPlaceEnrichmentRepository>());
        services.AddSingleton<SqliteAutomaticPhotoPlaceRepository>();
        services.AddSingleton<IAutomaticPhotoPlaceRepository>(sp => sp.GetRequiredService<SqliteAutomaticPhotoPlaceRepository>());

        services.AddSingleton<SqliteDetectorEvaluationRepository>();
        services.AddSingleton<IDetectorEvaluationCatalogueRepository>(sp => sp.GetRequiredService<SqliteDetectorEvaluationRepository>());
        services.AddSingleton<SqliteLocalBatchRepository>();
        services.AddSingleton<IAssetRevisionLookupRepository>(sp => sp.GetRequiredService<SqliteLocalBatchRepository>());
        services.AddSingleton<ICatalogueSourceRepository>(sp => sp.GetRequiredService<SqliteLocalBatchRepository>());
        services.AddSingleton<IArchiveSourceScanPersistence, SqliteArchiveSourceScanBatchRepository>();
        services.AddSingleton<SqliteProcessingRepository>();
        services.AddSingleton<IProcessingRunConfigurationReader>(sp => sp.GetRequiredService<SqliteProcessingRepository>());
        services.AddSingleton<IProcessingRunRepository>(sp => sp.GetRequiredService<SqliteProcessingRepository>());
        services.AddSingleton<IProcessingExecutionRepository>(sp => sp.GetRequiredService<SqliteProcessingRepository>());
        services.AddSingleton<IDetectorReconciliationPlanRepository, SqliteDetectorRolloutRepository>();
        services.AddSingleton<IDetectorRolloutReviewRepository, SqliteDetectorRolloutReviewRepository>();
        services.AddSingleton<IDetectorRolloutApplicationRepository, SqliteDetectorRolloutApplicationRepository>();
        services.AddSingleton<SqliteArchiveAnalysisRepository>();
        services.AddSingleton<IArchiveAnalysisStateRepository>(sp => sp.GetRequiredService<SqliteArchiveAnalysisRepository>());
        services.AddSingleton<IFaceInspectionRepository, SqliteFaceCatalogueRepository>();
        services.AddSingleton<ICatalogueStoreInitializer>(sp => sp.GetRequiredService<SqliteCatalogueDatabase>());

        services.AddSingleton<SqliteArchiveReviewProxyRepository>();
        services.AddSingleton<IFaceReviewDerivativeRepository, SqliteFaceReviewDerivativeRepository>();
        services.AddSingleton<IFaceReviewDerivativeBackfillRepository, SqliteFaceReviewDerivativeBackfillRepository>();
        services.AddSingleton<IArchiveReviewProxyRepository>(sp => sp.GetRequiredService<SqliteArchiveReviewProxyRepository>());
        services.AddSingleton<SqliteArchivePostAnalysisRepository>();
        services.AddSingleton<IArchivePostAnalysisRepository>(sp => sp.GetRequiredService<SqliteArchivePostAnalysisRepository>());
        services.AddSingleton<SqliteArchiveHydrationRepository>();
        services.AddSingleton<IArchiveHydrationRepository>(sp => sp.GetRequiredService<SqliteArchiveHydrationRepository>());
        services.AddSingleton<SqliteArchiveSourceHydrationRepository>();
        services.AddSingleton<IArchiveSourceHydrationRepository>(sp => sp.GetRequiredService<SqliteArchiveSourceHydrationRepository>());
        services.AddSingleton<SqliteArchiveHydrationIdentityTransferRepository>();
        services.AddSingleton<IArchiveHydrationIdentityTransferRepository>(sp => sp.GetRequiredService<SqliteArchiveHydrationIdentityTransferRepository>());
        services.AddSingleton<SqliteArchiveSourceObservationRepository>();
        services.AddSingleton<IArchiveSourceObservationRepository>(sp => sp.GetRequiredService<SqliteArchiveSourceObservationRepository>());
        services.AddSingleton<SqliteArchiveSourceVerificationStateRepository>();
        services.AddSingleton<IArchiveSourceVerificationStateRepository>(sp => sp.GetRequiredService<SqliteArchiveSourceVerificationStateRepository>());
        services.AddSingleton<SqliteArchiveAvailabilityRepository>();
        services.AddSingleton<IArchiveAvailabilityRepository>(sp => sp.GetRequiredService<SqliteArchiveAvailabilityRepository>());
        services.AddSingleton<SqliteArchiveCoverageRepository>();
        services.AddSingleton<IArchiveCoverageRepository>(sp => sp.GetRequiredService<SqliteArchiveCoverageRepository>());
        services.AddSingleton<SqliteArchiveStatusRepository>();
        services.AddSingleton<IArchiveStatusRepository>(sp => sp.GetRequiredService<SqliteArchiveStatusRepository>());
        services.AddSingleton<SqliteArchiveStorageRepository>();
        services.AddSingleton<IArchiveStorageAccountingRepository>(sp => sp.GetRequiredService<SqliteArchiveStorageRepository>());
        services.AddSingleton<SqliteArchiveAdvancementRepository>();
        services.AddSingleton<IArchiveAdvancementControlRepository>(sp => sp.GetRequiredService<SqliteArchiveAdvancementRepository>());
    }

    public static PostgresCatalogueDatabase AddPostgres(IServiceCollection services, string connectionString)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(connectionString);
        PostgresCatalogueDatabase database = new(connectionString);
        services.AddSingleton(database);
        services.AddSingleton<ICatalogueStoreInitializer>(sp => sp.GetRequiredService<PostgresCatalogueDatabase>());

        services.AddSingleton<PostgresReviewActionRepository>();
        services.AddSingleton<IReviewActionRepository>(sp => sp.GetRequiredService<PostgresReviewActionRepository>());
        services.AddSingleton<PostgresReviewQueryRepository>();
        services.AddSingleton<IReviewFaceRepository>(sp => sp.GetRequiredService<PostgresReviewQueryRepository>());
        services.AddSingleton<IReviewFilterRepository>(sp => sp.GetRequiredService<PostgresReviewQueryRepository>());
        services.AddSingleton<PostgresReviewSuggestionRepository>();
        services.AddSingleton<IReviewSuggestionRepository>(sp => sp.GetRequiredService<PostgresReviewSuggestionRepository>());
        services.AddSingleton<PostgresSuggestionGalleryRepository>();
        services.AddSingleton<ISuggestionGalleryRepository>(sp => sp.GetRequiredService<PostgresSuggestionGalleryRepository>());

        services.AddSingleton<PostgresIdentitySuggestionPolicyRepository>();
        services.AddSingleton<IIdentitySuggestionPolicyRepository>(sp => sp.GetRequiredService<PostgresIdentitySuggestionPolicyRepository>());
        services.AddSingleton<PostgresIdentityMatchModelRepository>();
        services.AddSingleton<IIdentityMatchModelRepository>(sp => sp.GetRequiredService<PostgresIdentityMatchModelRepository>());
        services.AddSingleton<PostgresIdentityMatchRegenerationRepository>();
        services.AddSingleton<IIdentityMatchRegenerationRepository>(sp => sp.GetRequiredService<PostgresIdentityMatchRegenerationRepository>());
        services.AddSingleton<PostgresIdentityMatchRegenerationScorer>();
        services.AddSingleton<IIdentityMatchRegenerationScorer>(sp => sp.GetRequiredService<PostgresIdentityMatchRegenerationScorer>());
        services.AddSingleton<PostgresIdentityMatchEvidenceVersionReader>();
        services.AddSingleton<IIdentityMatchEvidenceVersionReader>(sp => sp.GetRequiredService<PostgresIdentityMatchEvidenceVersionReader>());
        services.AddSingleton<PostgresIdentityAutoAssignmentService>();
        services.AddSingleton<IIdentityAutoAssignmentService>(sp => sp.GetRequiredService<PostgresIdentityAutoAssignmentService>());

        services.AddSingleton<PostgresPersonAuditRepository>();
        services.AddSingleton<IPersonAuditRepository>(sp => sp.GetRequiredService<PostgresPersonAuditRepository>());
        services.AddSingleton<PostgresPersonMaintenanceRepository>();
        services.AddSingleton<IPersonMaintenanceRepository>(sp => sp.GetRequiredService<PostgresPersonMaintenanceRepository>());
        services.AddSingleton<PostgresPersonPresentationRepository>();
        services.AddSingleton<IPersonPhotoCountRepository>(sp => sp.GetRequiredService<PostgresPersonPresentationRepository>());
        services.AddSingleton<IFavoritePeopleRepository>(sp => sp.GetRequiredService<PostgresPersonPresentationRepository>());
        services.AddSingleton<IPersonSmartCollectionVisibilityRepository>(sp => sp.GetRequiredService<PostgresPersonPresentationRepository>());
        services.AddSingleton<IPersonFeaturedFaceRepository>(sp => sp.GetRequiredService<PostgresPersonPresentationRepository>());
        services.AddSingleton<PostgresBulkReviewRepository>();
        services.AddSingleton<IBulkReviewRepository>(sp => sp.GetRequiredService<PostgresBulkReviewRepository>());
        services.AddSingleton<PostgresBulkSuggestionReviewRepository>();
        services.AddSingleton<IBulkSuggestionReviewRepository>(sp => sp.GetRequiredService<PostgresBulkSuggestionReviewRepository>());

        services.AddSingleton<PostgresCollectionQueryRepository>();
        services.AddSingleton<ICollectionQueryRepository>(sp => sp.GetRequiredService<PostgresCollectionQueryRepository>());
        services.AddSingleton<PostgresPhotoDetailsRepository>();
        services.AddSingleton<IPhotoDetailsRepository>(sp => sp.GetRequiredService<PostgresPhotoDetailsRepository>());
        services.AddSingleton<PostgresSmartCollectionQueryRepository>();
        services.AddSingleton<ISmartCollectionQueryRepository>(sp => sp.GetRequiredService<PostgresSmartCollectionQueryRepository>());
        services.AddSingleton<PostgresSmartCollectionRepository>();
        services.AddSingleton<ISmartCollectionRepository>(sp => sp.GetRequiredService<PostgresSmartCollectionRepository>());

        services.AddSingleton<PostgresPhotoCaptureMetadataRepository>();
        services.AddSingleton<IPhotoCaptureMetadataRepository>(sp => sp.GetRequiredService<PostgresPhotoCaptureMetadataRepository>());
        services.AddSingleton<PostgresPhotoMetadataBackfillRepository>();
        services.AddSingleton<IPhotoMetadataBackfillRepository>(sp => sp.GetRequiredService<PostgresPhotoMetadataBackfillRepository>());
        services.AddSingleton<PostgresExtendedPhotoMetadataRepository>();
        services.AddSingleton<IExtendedPhotoMetadataRepository>(sp => sp.GetRequiredService<PostgresExtendedPhotoMetadataRepository>());
        services.AddSingleton<PostgresPhotoMetadataInspectionRepository>();
        services.AddSingleton<IPhotoMetadataInspectionRepository>(sp => sp.GetRequiredService<PostgresPhotoMetadataInspectionRepository>());
        services.AddSingleton<PostgresPhotoTagRepository>();
        services.AddSingleton<IPhotoTagRepository>(sp => sp.GetRequiredService<PostgresPhotoTagRepository>());
        services.AddSingleton<PostgresPhotoPersonRepository>();
        services.AddSingleton<IPhotoPersonRepository>(sp => sp.GetRequiredService<PostgresPhotoPersonRepository>());
        services.AddSingleton<PostgresPhotoPlaceRepository>();
        services.AddSingleton<IPhotoPlaceRepository>(sp => sp.GetRequiredService<PostgresPhotoPlaceRepository>());
        services.AddSingleton<IAutomaticPhotoPlaceRepository>(sp => sp.GetRequiredService<PostgresPhotoPlaceRepository>());
        services.AddSingleton<PostgresPhotoPlaceEnrichmentStateRepository>();
        services.AddSingleton<IPhotoPlaceEnrichmentStateRepository>(sp => sp.GetRequiredService<PostgresPhotoPlaceEnrichmentStateRepository>());

        services.AddSingleton<PostgresDetectorEvaluationCatalogueRepository>();
        services.AddSingleton<IDetectorEvaluationCatalogueRepository>(sp => sp.GetRequiredService<PostgresDetectorEvaluationCatalogueRepository>());
        services.AddSingleton<PostgresAssetRevisionLookupRepository>();
        services.AddSingleton<IAssetRevisionLookupRepository>(sp => sp.GetRequiredService<PostgresAssetRevisionLookupRepository>());
        services.AddSingleton<PostgresCatalogueSourceRepository>();
        services.AddSingleton<ICatalogueSourceRepository>(sp => sp.GetRequiredService<PostgresCatalogueSourceRepository>());
        services.AddSingleton<PostgresArchiveSourceScanBatchRepository>();
        services.AddSingleton<IArchiveSourceScanPersistence>(sp => sp.GetRequiredService<PostgresArchiveSourceScanBatchRepository>());
        services.AddSingleton<PostgresProcessingRepository>();
        services.AddSingleton<IProcessingRunConfigurationReader>(sp => sp.GetRequiredService<PostgresProcessingRepository>());
        services.AddSingleton<IProcessingRunRepository>(sp => sp.GetRequiredService<PostgresProcessingRepository>());
        services.AddSingleton<IProcessingExecutionRepository>(sp => sp.GetRequiredService<PostgresProcessingRepository>());
        services.AddSingleton<PostgresDetectorReconciliationPlanRepository>();
        services.AddSingleton<IDetectorReconciliationPlanRepository>(sp => sp.GetRequiredService<PostgresDetectorReconciliationPlanRepository>());
        services.AddSingleton<PostgresDetectorRolloutReviewRepository>();
        services.AddSingleton<IDetectorRolloutReviewRepository>(sp => sp.GetRequiredService<PostgresDetectorRolloutReviewRepository>());
        services.AddSingleton<PostgresDetectorRolloutApplicationRepository>();
        services.AddSingleton<IDetectorRolloutApplicationRepository>(sp => sp.GetRequiredService<PostgresDetectorRolloutApplicationRepository>());
        services.AddSingleton<PostgresArchiveAnalysisStateRepository>();
        services.AddSingleton<IArchiveAnalysisStateRepository>(sp => sp.GetRequiredService<PostgresArchiveAnalysisStateRepository>());
        services.AddSingleton<PostgresFaceInspectionRepository>();
        services.AddSingleton<IFaceInspectionRepository>(sp => sp.GetRequiredService<PostgresFaceInspectionRepository>());

        services.AddSingleton<PostgresFaceReviewDerivativeRepository>();
        services.AddSingleton<IFaceReviewDerivativeRepository>(sp => sp.GetRequiredService<PostgresFaceReviewDerivativeRepository>());
        services.AddSingleton<PostgresFaceReviewDerivativeBackfillRepository>();
        services.AddSingleton<IFaceReviewDerivativeBackfillRepository>(sp => sp.GetRequiredService<PostgresFaceReviewDerivativeBackfillRepository>());
        services.AddSingleton<PostgresArchiveReviewProxyRepository>();
        services.AddSingleton<IArchiveReviewProxyRepository>(sp => sp.GetRequiredService<PostgresArchiveReviewProxyRepository>());
        services.AddSingleton<PostgresArchivePostAnalysisRepository>();
        services.AddSingleton<IArchivePostAnalysisRepository>(sp => sp.GetRequiredService<PostgresArchivePostAnalysisRepository>());
        services.AddSingleton<PostgresArchiveHydrationRepository>();
        services.AddSingleton<IArchiveHydrationRepository>(sp => sp.GetRequiredService<PostgresArchiveHydrationRepository>());
        services.AddSingleton<PostgresArchiveSourceHydrationRepository>();
        services.AddSingleton<IArchiveSourceHydrationRepository>(sp => sp.GetRequiredService<PostgresArchiveSourceHydrationRepository>());
        services.AddSingleton<PostgresArchiveHydrationIdentityTransferRepository>();
        services.AddSingleton<IArchiveHydrationIdentityTransferRepository>(sp => sp.GetRequiredService<PostgresArchiveHydrationIdentityTransferRepository>());
        services.AddSingleton<PostgresArchiveSourceObservationRepository>();
        services.AddSingleton<IArchiveSourceObservationRepository>(sp => sp.GetRequiredService<PostgresArchiveSourceObservationRepository>());
        services.AddSingleton<PostgresArchiveSourceVerificationStateRepository>();
        services.AddSingleton<IArchiveSourceVerificationStateRepository>(sp => sp.GetRequiredService<PostgresArchiveSourceVerificationStateRepository>());
        services.AddSingleton<PostgresArchiveAvailabilityRepository>();
        services.AddSingleton<IArchiveAvailabilityRepository>(sp => sp.GetRequiredService<PostgresArchiveAvailabilityRepository>());
        services.AddSingleton<PostgresArchiveCoverageRepository>();
        services.AddSingleton<IArchiveCoverageRepository>(sp => sp.GetRequiredService<PostgresArchiveCoverageRepository>());
        services.AddSingleton<PostgresArchiveStatusRepository>();
        services.AddSingleton<IArchiveStatusRepository>(sp => sp.GetRequiredService<PostgresArchiveStatusRepository>());
        services.AddSingleton<PostgresArchiveStorageAccountingRepository>();
        services.AddSingleton<IArchiveStorageAccountingRepository>(sp => sp.GetRequiredService<PostgresArchiveStorageAccountingRepository>());
        services.AddSingleton<PostgresArchiveAdvancementControlRepository>();
        services.AddSingleton<IArchiveAdvancementControlRepository>(sp => sp.GetRequiredService<PostgresArchiveAdvancementControlRepository>());

        return database;
    }
}
