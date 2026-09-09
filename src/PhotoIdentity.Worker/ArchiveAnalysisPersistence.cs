using PhotoIdentity.Core.Catalogue;
using PhotoIdentity.Core.Processing;
using PhotoIdentity.Core.Recognition;
using PhotoIdentity.Core.Sources;

namespace PhotoIdentity.Worker;

/// <summary>Complete persistence composition for governed archive analysis; select one provider for every member.</summary>
public sealed record ArchiveAnalysisPersistence(
    ICatalogueStoreInitializer Store,
    IArchiveCoverageRepository Coverage,
    IAssetRevisionLookupRepository Assets,
    IFaceInspectionRepository Faces,
    IProcessingRunRepository Runs,
    IProcessingExecutionRepository Execution,
    IArchiveAnalysisStateRepository Analysis);
