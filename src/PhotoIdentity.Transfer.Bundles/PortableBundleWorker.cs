namespace PhotoIdentity.Transfer.Bundles;

/// <summary>
/// Executes one self-contained job bundle through an injected processor. This type has no database dependency.
/// </summary>
public sealed class PortableBundleWorker
{
    private readonly IPortableBundleProcessor _processor;

    public PortableBundleWorker(IPortableBundleProcessor processor)
    {
        ArgumentNullException.ThrowIfNull(processor);
        _processor = processor;
    }

    public async Task<PortableResultManifest> ProcessAsync(
        string jobBundlePath,
        string resultBundlePath,
        string workingDirectory,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(jobBundlePath);
        ArgumentException.ThrowIfNullOrWhiteSpace(resultBundlePath);
        ArgumentException.ThrowIfNullOrWhiteSpace(workingDirectory);

        string fullWorkingDirectory = Path.GetFullPath(workingDirectory);
        string jobDirectory = Path.Combine(fullWorkingDirectory, "job");
        string processorOutputDirectory = Path.Combine(fullWorkingDirectory, "processor-output");
        ResetDirectory(fullWorkingDirectory);
        Directory.CreateDirectory(processorOutputDirectory);

        ExtractedPortableJob job = await PortableBundleArchive.ExtractJobAsync(
            jobBundlePath,
            jobDirectory,
            cancellationToken);
        PortableProcessingOutput output = await _processor.ProcessAsync(
            job,
            processorOutputDirectory,
            cancellationToken);
        return await PortableBundleArchive.CreateResultAsync(
            resultBundlePath,
            job,
            output,
            cancellationToken);
    }

    private static void ResetDirectory(string directory)
    {
        if (Directory.Exists(directory))
        {
            Directory.Delete(directory, recursive: true);
        }
        Directory.CreateDirectory(directory);
    }
}
