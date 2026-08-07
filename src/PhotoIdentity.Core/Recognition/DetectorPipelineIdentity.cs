using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using PhotoIdentity.Core.Identifiers;

namespace PhotoIdentity.Core.Recognition;

/// <summary>
/// Canonical description of detector behaviour that is material to face population and geometry.
/// </summary>
public sealed record DetectorPipelineDefinition
{
    public DetectorPipelineDefinition(
        string implementationId,
        ModelId detectorModelId,
        Sha256Digest detectorModelHash,
        string runtime,
        double confidenceThreshold,
        string pipelineMode,
        string resizePolicy,
        int inputWidth,
        int inputHeight,
        string inputShapePolicy,
        int? inputMultipleOf,
        int? maximumLongEdge,
        string colourOrder,
        string dataType,
        double inputScale,
        IReadOnlyList<double> inputMean,
        double detectorNmsThreshold,
        int detectorTopK,
        int? tileSize,
        double? tileOverlap,
        double? mergeNmsThreshold,
        string rotationPolicy)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(implementationId);
        ArgumentException.ThrowIfNullOrWhiteSpace(runtime);
        ArgumentException.ThrowIfNullOrWhiteSpace(pipelineMode);
        ArgumentException.ThrowIfNullOrWhiteSpace(resizePolicy);
        ArgumentException.ThrowIfNullOrWhiteSpace(inputShapePolicy);
        ArgumentException.ThrowIfNullOrWhiteSpace(colourOrder);
        ArgumentException.ThrowIfNullOrWhiteSpace(dataType);
        ArgumentException.ThrowIfNullOrWhiteSpace(rotationPolicy);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(inputWidth);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(inputHeight);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(detectorTopK);

        ValidateUnitInterval(confidenceThreshold, nameof(confidenceThreshold));
        ValidateUnitInterval(detectorNmsThreshold, nameof(detectorNmsThreshold));
        ValidateOptionalUnitInterval(tileOverlap, nameof(tileOverlap), upperExclusive: true);
        ValidateOptionalUnitInterval(mergeNmsThreshold, nameof(mergeNmsThreshold), upperExclusive: false);
        if (!double.IsFinite(inputScale) || inputScale <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(inputScale), "Input scale must be finite and positive.");
        }

        if (inputMean is null || inputMean.Count != 3 || inputMean.Any(value => !double.IsFinite(value)))
        {
            throw new ArgumentException("Input mean must contain exactly three finite values.", nameof(inputMean));
        }

        if (inputMultipleOf is <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(inputMultipleOf), "Input multiple must be positive when supplied.");
        }

        if (maximumLongEdge is <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(maximumLongEdge), "Maximum long edge must be positive when supplied.");
        }

        if (tileSize is <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(tileSize), "Tile size must be positive when supplied.");
        }

        ImplementationId = implementationId.Trim();
        DetectorModelId = detectorModelId;
        DetectorModelHash = detectorModelHash;
        Runtime = runtime.Trim();
        ConfidenceThreshold = confidenceThreshold;
        PipelineMode = pipelineMode.Trim();
        ResizePolicy = resizePolicy.Trim();
        InputWidth = inputWidth;
        InputHeight = inputHeight;
        InputShapePolicy = inputShapePolicy.Trim();
        InputMultipleOf = inputMultipleOf;
        MaximumLongEdge = maximumLongEdge;
        ColourOrder = colourOrder.Trim();
        DataType = dataType.Trim();
        InputScale = inputScale;
        InputMean = inputMean.ToArray();
        DetectorNmsThreshold = detectorNmsThreshold;
        DetectorTopK = detectorTopK;
        TileSize = tileSize;
        TileOverlap = tileOverlap;
        MergeNmsThreshold = mergeNmsThreshold;
        RotationPolicy = rotationPolicy.Trim();
    }

    public string ImplementationId { get; }
    public ModelId DetectorModelId { get; }
    public Sha256Digest DetectorModelHash { get; }
    public string Runtime { get; }
    public double ConfidenceThreshold { get; }
    public string PipelineMode { get; }
    public string ResizePolicy { get; }
    public int InputWidth { get; }
    public int InputHeight { get; }
    public string InputShapePolicy { get; }
    public int? InputMultipleOf { get; }
    public int? MaximumLongEdge { get; }
    public string ColourOrder { get; }
    public string DataType { get; }
    public double InputScale { get; }
    public IReadOnlyList<double> InputMean { get; }
    public double DetectorNmsThreshold { get; }
    public int DetectorTopK { get; }
    public int? TileSize { get; }
    public double? TileOverlap { get; }
    public double? MergeNmsThreshold { get; }
    public string RotationPolicy { get; }

    /// <summary>
    /// Computes the versioned SHA-256 identity used to distinguish materially different detector behaviour.
    /// </summary>
    public Sha256Digest ComputeHash()
    {
        byte[] bytes = Encoding.UTF8.GetBytes(ToCanonicalText());
        return new Sha256Digest(Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant());
    }

    public string ToCanonicalText()
    {
        string Number(double value) => value.ToString("R", CultureInfo.InvariantCulture);
        string Optional(double? value) => value.HasValue ? Number(value.Value) : "-";
        string Optional(int? value) => value?.ToString(CultureInfo.InvariantCulture) ?? "-";

        return string.Join('\n',
        [
            "detector-pipeline-v1",
            $"implementation={ImplementationId}",
            $"model-id={DetectorModelId}",
            $"model-sha256={DetectorModelHash}",
            $"runtime={Runtime}",
            $"confidence={Number(ConfidenceThreshold)}",
            $"pipeline={PipelineMode}",
            $"resize={ResizePolicy}",
            $"input-size={InputWidth.ToString(CultureInfo.InvariantCulture)}x{InputHeight.ToString(CultureInfo.InvariantCulture)}",
            $"input-shape-policy={InputShapePolicy}",
            $"input-multiple={Optional(InputMultipleOf)}",
            $"maximum-long-edge={Optional(MaximumLongEdge)}",
            $"colour-order={ColourOrder}",
            $"data-type={DataType}",
            $"input-scale={Number(InputScale)}",
            $"input-mean={string.Join(',', InputMean.Select(Number))}",
            $"detector-nms={Number(DetectorNmsThreshold)}",
            $"detector-top-k={DetectorTopK.ToString(CultureInfo.InvariantCulture)}",
            $"tile-size={Optional(TileSize)}",
            $"tile-overlap={Optional(TileOverlap)}",
            $"merge-nms={Optional(MergeNmsThreshold)}",
            $"rotation={RotationPolicy}",
        ]);
    }

    private static void ValidateUnitInterval(double value, string parameterName)
    {
        if (!double.IsFinite(value) || value < 0 || value > 1)
        {
            throw new ArgumentOutOfRangeException(parameterName, "Value must be between zero and one.");
        }
    }

    private static void ValidateOptionalUnitInterval(double? value, string parameterName, bool upperExclusive)
    {
        if (!value.HasValue)
        {
            return;
        }

        bool invalid = !double.IsFinite(value.Value) || value.Value < 0 ||
                       (upperExclusive ? value.Value >= 1 : value.Value > 1);
        if (invalid)
        {
            throw new ArgumentOutOfRangeException(
                parameterName,
                upperExclusive
                    ? "Value must be at least zero and less than one."
                    : "Value must be between zero and one.");
        }
    }
}
