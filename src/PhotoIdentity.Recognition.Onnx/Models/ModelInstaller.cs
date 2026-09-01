using System.Net;

namespace PhotoIdentity.Recognition.Onnx.Models;

public enum ModelInstallStatus
{
    AlreadyInstalled,
    Downloaded,
}

public sealed record InstalledModel(
    ModelManifest Manifest,
    string Path,
    ModelInstallStatus Status);

public sealed class ModelInstaller
{
    public const int MaximumDownloadAttempts = 3;

    private readonly HttpClient _httpClient;
    private readonly ModelFileVerifier _verifier;

    public ModelInstaller(HttpClient httpClient, ModelFileVerifier? verifier = null)
    {
        _httpClient = httpClient ?? throw new ArgumentNullException(nameof(httpClient));
        _verifier = verifier ?? new ModelFileVerifier();
    }

    public async Task<InstalledModel> InstallAsync(
        ModelManifest manifest,
        string destinationDirectory,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(manifest);
        ArgumentException.ThrowIfNullOrWhiteSpace(destinationDirectory);
        ModelManifestValidator.Validate(manifest);

        Directory.CreateDirectory(destinationDirectory);
        string destinationPath = Path.Combine(destinationDirectory, manifest.FileName);

        ModelFileVerification existing = await _verifier.VerifyAsync(
            destinationPath,
            manifest,
            cancellationToken);

        if (existing.IsValid)
        {
            return new InstalledModel(
                manifest,
                destinationPath,
                ModelInstallStatus.AlreadyInstalled);
        }

        string temporaryPath =
            destinationPath + "." + Guid.NewGuid().ToString("N") + ".download";

        try
        {
            for (int attempt = 1; ; attempt++)
            {
                try
                {
                    return await DownloadAndInstallAsync(
                        manifest,
                        destinationPath,
                        temporaryPath,
                        cancellationToken);
                }
                catch (Exception exception) when (
                    attempt < MaximumDownloadAttempts &&
                    IsTransientDownloadFailure(exception))
                {
                    DeleteTemporaryFile(temporaryPath);
                    await Task.Delay(
                        RetryDelay(attempt),
                        cancellationToken);
                }
            }
        }
        finally
        {
            DeleteTemporaryFile(temporaryPath);
        }
    }

    private async Task<InstalledModel> DownloadAndInstallAsync(
        ModelManifest manifest,
        string destinationPath,
        string temporaryPath,
        CancellationToken cancellationToken)
    {
        using HttpResponseMessage response = await _httpClient.GetAsync(
            manifest.DownloadUri,
            HttpCompletionOption.ResponseHeadersRead,
            cancellationToken);
        response.EnsureSuccessStatusCode();

        if (response.Content.Headers.ContentLength is long contentLength &&
            contentLength != manifest.SizeBytes)
        {
            throw new ModelIntegrityException(
                $"Model '{manifest.ModelId}' download declared {contentLength} bytes, " +
                $"but the manifest requires {manifest.SizeBytes} bytes.");
        }

        await using Stream input = await response.Content.ReadAsStreamAsync(cancellationToken);
        await using (FileStream output = new(
                         temporaryPath,
                         FileMode.Create,
                         FileAccess.Write,
                         FileShare.None,
                         bufferSize: 128 * 1024,
                         useAsync: true))
        {
            await input.CopyToAsync(output, cancellationToken);
            await output.FlushAsync(cancellationToken);
        }

        await _verifier.VerifyOrThrowAsync(
            temporaryPath,
            manifest,
            cancellationToken);

        File.Move(temporaryPath, destinationPath, overwrite: true);

        return new InstalledModel(
            manifest,
            destinationPath,
            ModelInstallStatus.Downloaded);
    }

    private static bool IsTransientDownloadFailure(Exception exception) =>
        exception switch
        {
            HttpRequestException http when http.StatusCode is null => true,
            HttpRequestException http when http.StatusCode is HttpStatusCode statusCode =>
                IsTransientStatusCode(statusCode),
            IOException => true,
            _ => false,
        };

    private static bool IsTransientStatusCode(HttpStatusCode statusCode) =>
        statusCode is
            HttpStatusCode.RequestTimeout or
            HttpStatusCode.TooManyRequests or
            HttpStatusCode.InternalServerError or
            HttpStatusCode.BadGateway or
            HttpStatusCode.ServiceUnavailable or
            HttpStatusCode.GatewayTimeout;

    private static TimeSpan RetryDelay(int failedAttempt) =>
        TimeSpan.FromMilliseconds(250 * failedAttempt);

    private static void DeleteTemporaryFile(string temporaryPath)
    {
        if (File.Exists(temporaryPath))
        {
            File.Delete(temporaryPath);
        }
    }
}
