using System.Net;
using System.Security.Cryptography;
using PhotoIdentity.Recognition.Onnx.Models;
using Xunit;

namespace PhotoIdentity_Recognition_Tests;

public sealed class ModelInstallerTests
{
    [Fact]
    public async Task Matching_download_is_installed_atomically()
    {
        byte[] content = [1, 2, 3, 4];
        ModelManifest manifest = CreateManifest(content);
        using HttpClient httpClient = new(new StaticResponseHandler(content));
        ModelInstaller installer = new(httpClient);
        string directory = CreateTemporaryDirectory();

        try
        {
            InstalledModel installed = await installer.InstallAsync(manifest, directory);

            Assert.Equal(ModelInstallStatus.Downloaded, installed.Status);
            Assert.Equal(content, await File.ReadAllBytesAsync(installed.Path));
            Assert.Empty(Directory.EnumerateFiles(directory, "*.download"));
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [Fact]
    public async Task Mismatched_download_is_not_promoted()
    {
        byte[] expected = [1, 2, 3, 4];
        byte[] downloaded = [1, 2, 3, 5];
        ModelManifest manifest = CreateManifest(expected);
        using HttpClient httpClient = new(new StaticResponseHandler(downloaded));
        ModelInstaller installer = new(httpClient);
        string directory = CreateTemporaryDirectory();
        string destination = Path.Combine(directory, manifest.FileName);

        try
        {
            await Assert.ThrowsAsync<ModelIntegrityException>(
                () => installer.InstallAsync(manifest, directory));

            Assert.False(File.Exists(destination));
            Assert.Empty(Directory.EnumerateFiles(directory));
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [Fact]
    public async Task Valid_existing_file_is_not_downloaded_again()
    {
        byte[] content = [1, 2, 3, 4];
        ModelManifest manifest = CreateManifest(content);
        CountingResponseHandler handler = new(content);
        using HttpClient httpClient = new(handler);
        ModelInstaller installer = new(httpClient);
        string directory = CreateTemporaryDirectory();
        string destination = Path.Combine(directory, manifest.FileName);
        await File.WriteAllBytesAsync(destination, content);

        try
        {
            InstalledModel installed = await installer.InstallAsync(manifest, directory);

            Assert.Equal(ModelInstallStatus.AlreadyInstalled, installed.Status);
            Assert.Equal(0, handler.RequestCount);
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    private static ModelManifest CreateManifest(byte[] content)
    {
        string digest = Convert.ToHexStringLower(SHA256.HashData(content));

        return new ModelManifest
        {
            SchemaVersion = 1,
            ModelId = "test-model",
            Role = "faceDetection",
            Format = "onnx",
            FileName = "test.onnx",
            DownloadUri = new Uri("https://example.test/test.onnx"),
            Sha256 = digest,
            SizeBytes = content.Length,
            Runtime = "onnxruntime",
            SourceVersion = "test@1",
            Input = new ModelInputManifest
            {
                Width = 320,
                Height = 320,
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
                Kind = "detections",
                Dimensions = null,
                Normalisation = "none",
                DistanceMetric = null,
                Semantics = "test detections",
            },
            AlignmentProtocol = null,
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
    }

    private static string CreateTemporaryDirectory()
    {
        string directory = Path.Combine(
            Path.GetTempPath(),
            $"photoidentity-{Guid.NewGuid():N}");
        Directory.CreateDirectory(directory);
        return directory;
    }

    private sealed class StaticResponseHandler(byte[] content) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken) =>
            Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new ByteArrayContent(content),
            });
    }

    private sealed class CountingResponseHandler(byte[] content) : HttpMessageHandler
    {
        public int RequestCount { get; private set; }

        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            RequestCount++;
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new ByteArrayContent(content),
            });
        }
    }
}
