using System.Reflection;
using PhotoIdentity.Api;
using PhotoIdentity.Worker;
using Xunit;

namespace PhotoIdentity_Integration_Tests;

public sealed class ArchiveRuntimePathUpgradeTests
{
    [Fact]
    public void Different_package_runtime_paths_are_treated_as_stale()
    {
        string root = Path.Combine(Path.GetTempPath(), "PhotoIdentity.Integration.Tests", Guid.NewGuid().ToString("N"));
        string sourceRoot = Path.Combine(root, "archive");
        string outputRoot = Path.Combine(root, "durable-analysis");
        string oldPackage = Path.Combine(root, "v1.2", "app");
        string newPackage = Path.Combine(root, "v1.3", "app");

        LocalBatchConfiguration saved = new(
            sourceRoot,
            outputRoot,
            oldPackage,
            Path.Combine(oldPackage, "models", "files"));
        LocalBatchConfiguration current = new(
            sourceRoot,
            outputRoot,
            newPackage,
            Path.Combine(newPackage, "models", "files"));

        Assert.False(AnalysisRuntimePathsEqual(saved, current));
    }

    [Fact]
    public void Same_package_runtime_paths_remain_resumable()
    {
        string root = Path.Combine(Path.GetTempPath(), "PhotoIdentity.Integration.Tests", Guid.NewGuid().ToString("N"));
        string sourceRoot = Path.Combine(root, "archive");
        string outputRoot = Path.Combine(root, "durable-analysis");
        string packageRoot = Path.Combine(root, "v1.3", "app");
        string modelRoot = Path.Combine(packageRoot, "models", "files");

        LocalBatchConfiguration saved = new(sourceRoot, outputRoot, packageRoot, modelRoot);
        LocalBatchConfiguration current = new(sourceRoot, outputRoot, packageRoot, modelRoot);

        Assert.True(AnalysisRuntimePathsEqual(saved, current));
    }

    private static bool AnalysisRuntimePathsEqual(
        LocalBatchConfiguration saved,
        LocalBatchConfiguration current)
    {
        MethodInfo method = typeof(ArchiveBoundedAnalysisService).GetMethod(
            "AnalysisRuntimePathsEqual",
            BindingFlags.NonPublic | BindingFlags.Static)
            ?? throw new InvalidOperationException("Archive runtime-path comparison helper was not found.");
        return (bool)(method.Invoke(null, [saved, current])
            ?? throw new InvalidOperationException("Archive runtime-path comparison returned no value."));
    }
}
