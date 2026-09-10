using System.Diagnostics;

namespace PhotoIdentity_Integration_Tests;

public sealed class MigrationRehearsalScriptTests
{
    [Fact]
    public async Task Rehearsal_script_parses_without_PowerShell_errors_on_Windows()
    {
        if (!OperatingSystem.IsWindows())
        {
            return;
        }

        string repositoryRoot = FindRepositoryRoot();
        string scriptPath = Path.Combine(repositoryRoot, "rehearse-postgres-migration.ps1");
        Assert.True(File.Exists(scriptPath), $"Expected rehearsal script at {scriptPath}.");

        string escapedScriptPath = scriptPath.Replace("'", "''", StringComparison.Ordinal);
        string parserCommand =
            "$tokens=$null; $errors=$null; " +
            $"[System.Management.Automation.Language.Parser]::ParseFile('{escapedScriptPath}',[ref]$tokens,[ref]$errors) | Out-Null; " +
            "if ($errors.Count -ne 0) { $errors | ForEach-Object { Write-Error $_.Message }; exit 1 }";

        ProcessStartInfo startInfo = new("powershell.exe")
        {
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true,
        };
        startInfo.ArgumentList.Add("-NoLogo");
        startInfo.ArgumentList.Add("-NoProfile");
        startInfo.ArgumentList.Add("-Command");
        startInfo.ArgumentList.Add(parserCommand);

        using Process process = Process.Start(startInfo)
            ?? throw new InvalidOperationException("Could not start Windows PowerShell for script syntax verification.");
        string standardOutput = await process.StandardOutput.ReadToEndAsync();
        string standardError = await process.StandardError.ReadToEndAsync();
        await process.WaitForExitAsync();

        Assert.True(
            process.ExitCode == 0,
            $"PowerShell parser rejected rehearse-postgres-migration.ps1. stdout: {standardOutput} stderr: {standardError}");
    }

    private static string FindRepositoryRoot()
    {
        DirectoryInfo? directory = new(AppContext.BaseDirectory);
        while (directory is not null)
        {
            if (File.Exists(Path.Combine(directory.FullName, "PhotoIdentity.slnx")))
            {
                return directory.FullName;
            }

            directory = directory.Parent;
        }

        throw new DirectoryNotFoundException("The repository root could not be found from the test output directory.");
    }
}
