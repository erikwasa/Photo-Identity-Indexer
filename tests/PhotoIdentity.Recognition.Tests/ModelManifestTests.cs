using PhotoIdentity.Core.Geometry;
using PhotoIdentity.Core.Recognition;
using PhotoIdentity.Recognition.Onnx.Models;
using Xunit;

namespace PhotoIdentity_Recognition_Tests;

public sealed class ModelManifestTests
{
    [Fact]
    public async Task Repository_manifests_are_valid_and_convert_to_descriptors()
    {
        string root = FindRepositoryRoot();
        ModelManifestLoader loader = new();

        IReadOnlyList<ModelManifest> manifests = await loader.LoadDirectoryAsync(
            Path.Combine(root, "models", "manifests"));

        Assert.Equal(3, manifests.Count);
        Assert.Contains(manifests, value => value.ModelId == "yunet-2023mar-fp32");
        Assert.Contains(manifests, value => value.ModelId == "sface-2021dec-fp32");
        Assert.Contains(manifests, value => value.ModelId == "sface-2021dec-int8");

        ModelDescriptor yunet = manifests
            .Single(value => value.ModelId == "yunet-2023mar-fp32")
            .ToDescriptor();

        Assert.Equal(ModelRole.FaceDetection, yunet.Role);
        Assert.Equal(new ImageSize(640, 640), yunet.InputSize);

        ModelManifest baselineManifest = manifests
            .Single(value => value.ModelId == "sface-2021dec-fp32");
        ModelManifest candidateManifest = manifests
            .Single(value => value.ModelId == "sface-2021dec-int8");
        ModelDescriptor baseline = baselineManifest.ToDescriptor();
        ModelDescriptor candidate = candidateManifest.ToDescriptor();

        AssertSFaceDescriptor(baseline);
        AssertSFaceDescriptor(candidate);
        Assert.NotEqual(baseline.ModelHash, candidate.ModelHash);
        Assert.Equal(38_696_353, baselineManifest.SizeBytes);
        Assert.Equal(9_896_933, candidateManifest.SizeBytes);
        Assert.Contains("INT8", candidateManifest.Output.Semantics, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Unknown_fields_are_rejected()
    {
        string path = CreateTemporaryManifest(
            """
            {
              "schemaVersion": 1,
              "unexpected": true
            }
            """);

        try
        {
            ModelManifestLoader loader = new();
            await Assert.ThrowsAsync<ModelManifestException>(
                () => loader.LoadAsync(path));
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public void Embedding_manifest_requires_alignment_and_output_semantics()
    {
        ModelManifest manifest = ValidEmbeddingManifest() with
        {
            AlignmentProtocol = null,
            Output = ValidEmbeddingManifest().Output with
            {
                Dimensions = null,
                Semantics = "",
            },
        };

        Assert.Throws<ModelManifestException>(
            () => ModelManifestValidator.Validate(manifest));
    }

    private static void AssertSFaceDescriptor(ModelDescriptor descriptor)
    {
        Assert.Equal(ModelRole.FaceEmbedding, descriptor.Role);
        Assert.Equal(new ImageSize(112, 112), descriptor.InputSize);
        Assert.Equal(128, descriptor.OutputDimensions);
        Assert.Equal(DistanceMetric.Cosine, descriptor.DistanceMetric);
        Assert.Equal("sface-five-point-v1", descriptor.AlignmentProtocol?.ToString());
        Assert.Equal("Apache-2.0", descriptor.Licence);
    }

    private static ModelManifest ValidEmbeddingManifest() =>
        new()
        {
            SchemaVersion = 1,
            ModelId = "test-embedding",
            Role = "faceEmbedding",
            Format = "onnx",
            FileName = "test.onnx",
            DownloadUri = new Uri("https://example.test/test.onnx"),
            Sha256 = new string('a', 64),
            SizeBytes = 4,
            Runtime = "onnxruntime",
            SourceVersion = "test@1",
            Input = new ModelInputManifest
            {
                Width = 112,
                Height = 112,
                ColourOrder = "BGR",
                DataType = "float32",
                Normalisation = new ModelNormalisationManifest
                {
                    Scale = 1,
                    Mean = [0, 0, 0],
                },
            },
            Output = new ModelOutputManifest
            {
                Kind = "embedding",
                Dimensions = 128,
                Normalisation = "l2-by-adapter",
                DistanceMetric = "cosine",
                Semantics = "test embedding",
            },
            AlignmentProtocol = "test-v1",
            Licences = new ModelLicenceManifest
            {
                Code = new LicenceRecord
                {
                    Spdx = "MIT",
                    Source = new Uri("https://example.test/code-license"),
                },
                Weights = new LicenceRecord
                {
                    Spdx = "MIT",
                    Source = new Uri("https://example.test/weights-license"),
                },
                TrainingData = new TrainingDataRecord
                {
                    Name = "test",
                    Licence = "test",
                    Notes = "test",
                },
            },
        };

    private static string CreateTemporaryManifest(string content)
    {
        string path = Path.Combine(
            Path.GetTempPath(),
            $"photoidentity-{Guid.NewGuid():N}.json");
        File.WriteAllText(path, content);
        return path;
    }

    private static string FindRepositoryRoot()
    {
        DirectoryInfo? current = new(AppContext.BaseDirectory);
        while (current is not null)
        {
            if (File.Exists(Path.Combine(current.FullName, "PhotoIdentity.slnx")))
            {
                return current.FullName;
            }

            current = current.Parent;
        }

        throw new DirectoryNotFoundException("Could not locate repository root.");
    }
}
