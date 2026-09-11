using System.ComponentModel;
using System.Diagnostics;
using Xunit;

namespace PhotoIdentity_Integration_Tests;

public sealed class SlideshowJavascriptTests
{
    [Fact]
    public async Task Prefetch_lifecycle_javascript_regressions_pass()
    {
        string repositoryRoot = ResolveRepositoryRoot();
        string testPath = Path.Combine(
            repositoryRoot,
            "tests",
            "javascript",
            "slideshow-prefetch.test.js");

        ProcessStartInfo startInfo = new()
        {
            FileName = "node",
            WorkingDirectory = repositoryRoot,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true,
        };
        startInfo.ArgumentList.Add("--test");
        startInfo.ArgumentList.Add(testPath);

        Process? process;
        try
        {
            process = Process.Start(startInfo);
        }
        catch (Win32Exception) when (!IsGitHubActions())
        {
            // Local .NET-only development environments are allowed to omit Node.js.
            // GitHub-hosted validation must execute the browser-side regression test.
            return;
        }

        Assert.NotNull(process);
        Task<string> stdout = process.StandardOutput.ReadToEndAsync();
        Task<string> stderr = process.StandardError.ReadToEndAsync();
        await process.WaitForExitAsync();

        string output = await stdout;
        string errors = await stderr;
        Assert.True(
            process.ExitCode == 0,
            $"Slideshow JavaScript regression tests failed with exit code {process.ExitCode}.{Environment.NewLine}{output}{Environment.NewLine}{errors}");
    }

    private static bool IsGitHubActions() =>
        string.Equals(
            Environment.GetEnvironmentVariable("GITHUB_ACTIONS"),
            "true",
            StringComparison.OrdinalIgnoreCase);

    private static string ResolveRepositoryRoot()
    {
        foreach (string candidate in new[] { Directory.GetCurrentDirectory(), AppContext.BaseDirectory })
        {
            DirectoryInfo? directory = new(Path.GetFullPath(candidate));
            for (int depth = 0; directory is not null && depth < 12; depth++, directory = directory.Parent)
            {
                if (File.Exists(Path.Combine(directory.FullName, "PhotoIdentity.slnx")))
                {
                    return directory.FullName;
                }
            }
        }

        throw new DirectoryNotFoundException("Could not resolve the Photo Identity repository root for JavaScript tests.");
    }
}
