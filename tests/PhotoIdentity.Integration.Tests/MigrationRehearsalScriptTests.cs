using System.Diagnostics;

namespace PhotoIdentity_Integration_Tests;

public sealed class MigrationRehearsalScriptTests
{
    [Fact]
    public async Task Rehearsal_scripts_parse_without_PowerShell_errors_on_Windows()
    {
        if (!OperatingSystem.IsWindows())
        {
            return;
        }

        string repositoryRoot = FindRepositoryRoot();
        foreach (string fileName in new[] { "rehearse-postgres-migration.ps1", "review-postgres-rehearsal.ps1" })
        {
            string scriptPath = Path.Combine(repositoryRoot, fileName);
            Assert.True(File.Exists(scriptPath), $"Expected rehearsal script at {scriptPath}.");

            string escapedScriptPath = EscapePowerShellLiteral(scriptPath);
            string parserCommand =
                "$tokens=$null; $errors=$null; " +
                $"[System.Management.Automation.Language.Parser]::ParseFile('{escapedScriptPath}',[ref]$tokens,[ref]$errors) | Out-Null; " +
                "if ($errors.Count -ne 0) { $errors | ForEach-Object { Write-Error $_.Message }; exit 1 }";

            ProcessResult result = await RunPowerShellAsync(parserCommand);
            Assert.True(
                result.ExitCode == 0,
                $"PowerShell parser rejected {fileName}. stdout: {result.StandardOutput} stderr: {result.StandardError}");
        }
    }

    [Fact]
    public async Task Rehearsal_launcher_configuration_disables_inherited_mobile_certificate_requirements_on_Windows()
    {
        if (!OperatingSystem.IsWindows())
        {
            return;
        }

        string repositoryRoot = FindRepositoryRoot();
        string scriptPath = Path.Combine(repositoryRoot, "rehearse-postgres-migration.ps1");
        string directory = Path.Combine(Path.GetTempPath(), $"photoidentity-rehearsal-config-{Guid.NewGuid():N}");
        Directory.CreateDirectory(directory);
        string baseConfigurationPath = Path.Combine(directory, "base.json");
        string outputConfigurationPath = Path.Combine(directory, "rehearsal.json");

        try
        {
            await File.WriteAllTextAsync(
                baseConfigurationPath,
                """
                {
                  "url": "http://127.0.0.1:5080",
                  "mobileAccess": {
                    "enabled": true,
                    "listenUrl": "https://192.168.1.10:5443",
                    "phoneUrl": "https://photoidentity.test:5443",
                    "certificatePath": "C:\\PhotoIdentity\\private\\mobile.pfx",
                    "certificatePasswordEnvironmentVariable": "PHOTOIDENTITY_MOBILE_CERT_PASSWORD"
                  },
                  "settings": {
                    "PhotoIdentity__CatalogueProvider": "sqlite",
                    "PhotoIdentity__ReviewProxyProfileId": "review-v1"
                  }
                }
                """);

            string escapedScriptPath = EscapePowerShellLiteral(scriptPath);
            string escapedBasePath = EscapePowerShellLiteral(baseConfigurationPath);
            string escapedOutputPath = EscapePowerShellLiteral(outputConfigurationPath);
            string command =
                "$tokens=$null; $errors=$null; " +
                $"$ast=[System.Management.Automation.Language.Parser]::ParseFile('{escapedScriptPath}',[ref]$tokens,[ref]$errors); " +
                "if ($errors.Count -ne 0) { throw 'Rehearsal script did not parse.' }; " +
                "$function=$ast.Find({ param($node) $node -is [System.Management.Automation.Language.FunctionDefinitionAst] -and $node.Name -eq 'New-RehearsalLauncherConfiguration' }, $true); " +
                "if ($null -eq $function) { throw 'Rehearsal launcher function was not found.' }; " +
                "Invoke-Expression $function.Extent.Text; " +
                $"New-RehearsalLauncherConfiguration -BaseConfigurationPath '{escapedBasePath}' -OutputPath '{escapedOutputPath}' -PostgresEnvironmentName 'PHOTOIDENTITY_REHEARSAL_RUNTIME_CONNECTION_STRING' -Url 'http://127.0.0.1:5080'; " +
                $"$configuration=Get-Content -LiteralPath '{escapedOutputPath}' -Raw | ConvertFrom-Json; " +
                "if ([bool]$configuration.mobileAccess.enabled) { throw 'mobileAccess remained enabled.' }; " +
                "if ($null -ne $configuration.mobileAccess.PSObject.Properties['certificatePath']) { throw 'certificatePath leaked into rehearsal config.' }; " +
                "if ($null -ne $configuration.mobileAccess.PSObject.Properties['certificatePasswordEnvironmentVariable']) { throw 'certificate password variable leaked into rehearsal config.' }; " +
                "if ([string]$configuration.settings.PhotoIdentity__CatalogueProvider -ne 'postgresql') { throw 'PostgreSQL provider was not selected.' }; " +
                "if ([string]$configuration.postgresConnectionEnvironmentVariable -ne 'PHOTOIDENTITY_REHEARSAL_RUNTIME_CONNECTION_STRING') { throw 'PostgreSQL connection environment was not selected.' }; " +
                "if ([string]$configuration.settings.PhotoIdentity__ReviewProxyProfileId -ne 'review-v1') { throw 'Unrelated launcher settings were not preserved.' }";

            ProcessResult result = await RunPowerShellAsync(command);
            Assert.True(
                result.ExitCode == 0,
                $"Rehearsal launcher configuration retained unrelated mobile certificate requirements. stdout: {result.StandardOutput} stderr: {result.StandardError}");
        }
        finally
        {
            if (Directory.Exists(directory))
            {
                Directory.Delete(directory, recursive: true);
            }
        }
    }

    [Fact]
    public void Rehearsal_review_publishes_current_api_and_overrides_package_path()
    {
        string repositoryRoot = FindRepositoryRoot();
        string script = File.ReadAllText(Path.Combine(repositoryRoot, "rehearse-postgres-migration.ps1"));

        Assert.Contains("dotnet publish $apiProject", script, StringComparison.Ordinal);
        Assert.Contains("-PublishPathOverride $rehearsalPublishPath", script, StringComparison.Ordinal);
        Assert.Contains("PhotoIdentity.Api.dll", script, StringComparison.Ordinal);
    }

    private static async Task<ProcessResult> RunPowerShellAsync(string command)
    {
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
        startInfo.ArgumentList.Add(command);

        using Process process = Process.Start(startInfo)
            ?? throw new InvalidOperationException("Could not start Windows PowerShell for rehearsal-script verification.");
        string standardOutput = await process.StandardOutput.ReadToEndAsync();
        string standardError = await process.StandardError.ReadToEndAsync();
        await process.WaitForExitAsync();
        return new(process.ExitCode, standardOutput, standardError);
    }

    private static string EscapePowerShellLiteral(string value) =>
        value.Replace("'", "''", StringComparison.Ordinal);

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

    private sealed record ProcessResult(int ExitCode, string StandardOutput, string StandardError);
}
