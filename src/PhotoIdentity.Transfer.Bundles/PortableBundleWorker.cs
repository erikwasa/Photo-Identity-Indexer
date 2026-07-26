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
        EnsureOutsideWorkingDirectory(jobBundlePath, fullWorkingDirectory, nameof(jobBundlePath));
        EnsureOutsideWorkingDirectory(resultBundlePath, fullWorkingDirectory, nameof(resultBundlePath));
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

    private static void EnsureOutsideWorkingDirectory(string path, string workingDirectory, string parameterName)
    {
        string fullPath = Path.GetFullPath(path);
        string prefix = workingDirectory.EndsWith(Path.DirectorySeparatorChar)
            ? workingDirectory
            : workingDirectory + Path.DirectorySeparatorChar;
        StringComparison comparison = OperatingSystem.IsWindows()
            ? StringComparison.OrdinalIgnoreCase
            : StringComparison.Ordinal;
        if (fullPath.StartsWith(prefix, comparison))
        {
            throw new ArgumentException("Bundle paths must be outside the disposable working directory.", parameterName);
        }
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
