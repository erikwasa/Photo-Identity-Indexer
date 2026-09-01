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
        CountingResponseHandler handler = new(downloaded);
        using HttpClient httpClient = new(handler);
        ModelInstaller installer = new(httpClient);
        string directory = CreateTemporaryDirectory();
        string destination = Path.Combine(directory, manifest.FileName);

        try
        {
            await Assert.ThrowsAsync<ModelIntegrityException>(
                () => installer.InstallAsync(manifest, directory));

            Assert.False(File.Exists(destination));
            Assert.Empty(Directory.EnumerateFiles(directory));
            Assert.Equal(1, handler.RequestCount);
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [Fact]
    public async Task Transient_download_failure_is_retried_and_valid_response_is_installed()
    {
        byte[] content = [1, 2, 3, 4];
        ModelManifest manifest = CreateManifest(content);
        InterruptedBodyThenSuccessHandler handler = new(content);
        using HttpClient httpClient = new(handler);
        ModelInstaller installer = new(httpClient);
        string directory = CreateTemporaryDirectory();

        try
        {
            InstalledModel installed = await installer.InstallAsync(manifest, directory);

            Assert.Equal(ModelInstallStatus.Downloaded, installed.Status);
            Assert.Equal(2, handler.RequestCount);
            Assert.Equal(content, await File.ReadAllBytesAsync(installed.Path));
            Assert.Empty(Directory.EnumerateFiles(directory, "*.download"));
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [Fact]
    public async Task Repeated_transient_failures_stop_after_bounded_attempts_without_partial_file()
    {
        byte[] content = [1, 2, 3, 4];
        ModelManifest manifest = CreateManifest(content);
        AlwaysTransientHandler handler = new();
        using HttpClient httpClient = new(handler);
        ModelInstaller installer = new(httpClient);
        string directory = CreateTemporaryDirectory();
        string destination = Path.Combine(directory, manifest.FileName);

        try
        {
            await Assert.ThrowsAsync<HttpRequestException>(
                () => installer.InstallAsync(manifest, directory));

            Assert.Equal(ModelInstaller.MaximumDownloadAttempts, handler.RequestCount);
            Assert.False(File.Exists(destination));
            Assert.Empty(Directory.EnumerateFiles(directory, "*.download"));
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [Fact]
    public async Task Cancellation_is_not_retried()
    {
        byte[] content = [1, 2, 3, 4];
        ModelManifest manifest = CreateManifest(content);
        CancelledResponseHandler handler = new();
        using HttpClient httpClient = new(handler);
        ModelInstaller installer = new(httpClient);
        string directory = CreateTemporaryDirectory();

        try
        {
            await Assert.ThrowsAnyAsync<OperationCanceledException>(
                () => installer.InstallAsync(manifest, directory));

            Assert.Equal(1, handler.RequestCount);
            Assert.Empty(Directory.EnumerateFiles(directory, "*.download"));
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

    private sealed class InterruptedBodyThenSuccessHandler(byte[] content) : HttpMessageHandler
    {
        public int RequestCount { get; private set; }

        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            RequestCount++;
            HttpContent responseContent = RequestCount == 1
                ? new StreamContent(new InterruptingReadStream(content))
                : new ByteArrayContent(content);

            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = responseContent,
            });
        }
    }

    private sealed class InterruptingReadStream(byte[] content) : Stream
    {
        private readonly MemoryStream _inner = new(content, writable: false);
        private bool _interruptNextRead;

        public override bool CanRead => true;
        public override bool CanSeek => false;
        public override bool CanWrite => false;
        public override long Length => _inner.Length;

        public override long Position
        {
            get => _inner.Position;
            set => throw new NotSupportedException();
        }

        public override int Read(byte[] buffer, int offset, int count)
        {
            if (_interruptNextRead)
            {
                throw new IOException("simulated response interruption");
            }

            int read = _inner.Read(buffer, offset, Math.Min(count, 1));
            _interruptNextRead = read > 0;
            return read;
        }

        public override Task<int> ReadAsync(
            byte[] buffer,
            int offset,
            int count,
            CancellationToken cancellationToken) =>
            ReadAsync(buffer.AsMemory(offset, count), cancellationToken).AsTask();

        public override async ValueTask<int> ReadAsync(
            Memory<byte> buffer,
            CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (_interruptNextRead)
            {
                throw new IOException("simulated response interruption");
            }

            int count = Math.Min(buffer.Length, 1);
            int read = await _inner.ReadAsync(buffer[..count], cancellationToken);
            _interruptNextRead = read > 0;
            return read;
        }

        public override void Flush()
        {
        }

        public override long Seek(long offset, SeekOrigin origin) =>
            throw new NotSupportedException();

        public override void SetLength(long value) =>
            throw new NotSupportedException();

        public override void Write(byte[] buffer, int offset, int count) =>
            throw new NotSupportedException();

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                _inner.Dispose();
            }

            base.Dispose(disposing);
        }
    }

    private sealed class AlwaysTransientHandler : HttpMessageHandler
    {
        public int RequestCount { get; private set; }

        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            RequestCount++;
            return Task.FromException<HttpResponseMessage>(
                new HttpRequestException("transient transport interruption"));
        }
    }

    private sealed class CancelledResponseHandler : HttpMessageHandler
    {
        public int RequestCount { get; private set; }

        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            RequestCount++;
            return Task.FromCanceled<HttpResponseMessage>(new CancellationToken(canceled: true));
        }
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
