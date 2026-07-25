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
                             FileMode.CreateNew,
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
        finally
        {
            if (File.Exists(temporaryPath))
            {
                File.Delete(temporaryPath);
            }
        }
    }
}
