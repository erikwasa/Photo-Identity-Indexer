using System.Text.Json;
using PhotoIdentity.Cli;
using PhotoIdentity.Worker;
using Xunit;

namespace PhotoIdentity.Integration.Tests;

public sealed class MultiScaleBatchConfigurationTests
{
    [Fact]
    public void Batch_options_parse_multiscale_provenance()
    {
        BatchCommandOptions options = BatchCommandOptions.Parse(
        [
            "start",
            "--database", "catalogue.db",
            "--source", "sample",
            "--detector-pipeline", LocalBatchConfiguration.MultiScaleDetectorPipeline,
            "--tile-size", "960",
            "--tile-overlap", "0.25",
            "--merge-nms", "0.35",
        ]);

        Assert.Equal(LocalBatchConfiguration.MultiScaleDetectorPipeline, options.DetectorPipeline);
        Assert.Equal(960, options.TileSize);
        Assert.Equal(0.25, options.TileOverlap, 6);
        Assert.Equal(0.35, options.MergeNmsThreshold, 6);
    }

    [Fact]
    public void Batch_options_reject_complete_tile_overlap()
    {
        ArgumentException exception = Assert.Throws<ArgumentException>(() =>
            BatchCommandOptions.Parse(
            [
                "start",
                "--database", "catalogue.db",
                "--source", "sample",
                "--tile-overlap", "1",
            ]));

        Assert.Contains("less than one", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Multiscale_configuration_round_trips_detector_pipeline_provenance()
    {
        (string source, string output, string repository) = Paths();
        LocalBatchConfiguration configuration = new(
            source,
            output,
            repository,
            detectorPipeline: LocalBatchConfiguration.MultiScaleDetectorPipeline,
            tileSize: 960,
            tileOverlap: 0.25,
            mergeNmsThreshold: 0.35,
            confidenceThreshold: 0.6);

        LocalBatchConfiguration restored = LocalBatchConfiguration.FromJson(configuration.ToJson());

        Assert.Equal(LocalBatchConfiguration.MultiScaleDetectorPipeline, restored.DetectorPipeline);
        Assert.Equal(960, restored.TileSize);
        Assert.Equal(0.25, restored.TileOverlap, 6);
        Assert.Equal(0.35, restored.MergeNmsThreshold, 6);
        Assert.Equal(0.6, restored.ConfidenceThreshold, 6);
    }

    [Fact]
    public void Legacy_configuration_defaults_to_the_single_pass_pipeline()
    {
        (string source, string output, string repository) = Paths();
        string json = JsonSerializer.Serialize(new
        {
            sourceRoot = source,
            outputRoot = output,
            repositoryRoot = repository,
            modelDirectory = (string?)null,
            recursive = true,
            confidenceThreshold = 0.9,
            paddingRatio = 0.25,
            detectorModelId = LocalBatchConfiguration.DefaultDetectorModelId,
            embedderModelId = LocalBatchConfiguration.DefaultEmbedderModelId,
        });

        LocalBatchConfiguration restored = LocalBatchConfiguration.FromJson(json);

        Assert.Equal(LocalBatchConfiguration.SinglePassDetectorPipeline, restored.DetectorPipeline);
        Assert.Equal(LocalBatchConfiguration.DefaultTileSize, restored.TileSize);
        Assert.Equal(LocalBatchConfiguration.DefaultTileOverlap, restored.TileOverlap, 6);
        Assert.Equal(LocalBatchConfiguration.DefaultMergeNmsThreshold, restored.MergeNmsThreshold, 6);
    }

    private static (string Source, string Output, string Repository) Paths()
    {
        string root = Path.Combine(Path.GetTempPath(), "PhotoIdentity.Tests", Guid.NewGuid().ToString("N"));
        return (
            Path.Combine(root, "source"),
            Path.Combine(root, "output"),
            Path.Combine(root, "repository"));
    }
}
