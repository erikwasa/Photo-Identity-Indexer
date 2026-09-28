using System.Globalization;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace PhotoIdentity.Api;

public sealed class LauncherSettingsStore
{
    public const decimal BytesPerGigabyte = 1_073_741_824m;

    private const string MinimumFreeSpaceReserveKey =
        "PhotoIdentity__ArchiveHydration__MinimumFreeSpaceReserveBytes";
    private const string MaximumManagedHydrationKey =
        "PhotoIdentity__ArchiveHydration__MaximumManagedHydrationBytes";
    private const string MaximumConcurrentOperationsKey =
        "PhotoIdentity__ArchiveHydration__MaximumConcurrentOperations";

    private readonly string? _configurationPath;
    private readonly SemaphoreSlim _writeGate = new(1, 1);

    public LauncherSettingsStore()
        : this(ResolveConfigurationPath())
    {
    }

    public LauncherSettingsStore(string? configurationPath)
    {
        _configurationPath = string.IsNullOrWhiteSpace(configurationPath)
            ? null
            : Path.GetFullPath(Environment.ExpandEnvironmentVariables(configurationPath));
    }

    public string? ConfigurationPath => _configurationPath;

    public async Task SaveArchiveHydrationPolicyAsync(
        decimal minimumFreeSpaceReserveGb,
        decimal maximumManagedHydrationGb,
        int maximumConcurrentOperations,
        CancellationToken cancellationToken = default)
    {
        long minimumFreeSpaceReserveBytes = GigabytesToBytes(
            minimumFreeSpaceReserveGb,
            allowZero: true,
            nameof(minimumFreeSpaceReserveGb));
        long maximumManagedHydrationBytes = GigabytesToBytes(
            maximumManagedHydrationGb,
            allowZero: false,
            nameof(maximumManagedHydrationGb));
        if (maximumConcurrentOperations <= 0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(maximumConcurrentOperations),
                "Maximum concurrent operations must be greater than zero.");
        }

        if (string.IsNullOrWhiteSpace(_configurationPath))
        {
            throw new InvalidOperationException(
                "The launcher configuration path could not be resolved. Start Photo Identity from the packaged launcher before changing storage policy from the application.");
        }
        if (!File.Exists(_configurationPath))
        {
            throw new InvalidOperationException(
                $"The launcher configuration file does not exist at '{_configurationPath}'.");
        }

        await _writeGate.WaitAsync(cancellationToken);
        try
        {
            string raw = await File.ReadAllTextAsync(_configurationPath, cancellationToken);
            JsonObject root;
            try
            {
                root = JsonNode.Parse(raw) as JsonObject
                    ?? throw new InvalidDataException("Launcher configuration must contain a JSON object.");
            }
            catch (JsonException exception)
            {
                throw new InvalidDataException("Launcher configuration is not valid JSON.", exception);
            }

            JsonObject settings = root["settings"] as JsonObject ?? new JsonObject();
            root["settings"] = settings;
            settings[MinimumFreeSpaceReserveKey] = minimumFreeSpaceReserveBytes.ToString(CultureInfo.InvariantCulture);
            settings[MaximumManagedHydrationKey] = maximumManagedHydrationBytes.ToString(CultureInfo.InvariantCulture);
            settings[MaximumConcurrentOperationsKey] = maximumConcurrentOperations.ToString(CultureInfo.InvariantCulture);

            string updated = root.ToJsonString(new JsonSerializerOptions { WriteIndented = true });
            string temporaryPath = _configurationPath + ".tmp";
            try
            {
                await File.WriteAllTextAsync(
                    temporaryPath,
                    updated + Environment.NewLine,
                    new UTF8Encoding(encoderShouldEmitUTF8Identifier: false),
                    cancellationToken);
                File.Move(temporaryPath, _configurationPath, overwrite: true);
            }
            finally
            {
                if (File.Exists(temporaryPath))
                {
                    File.Delete(temporaryPath);
                }
            }
        }
        finally
        {
            _writeGate.Release();
        }
    }

    public static long GigabytesToBytes(decimal gigabytes, bool allowZero, string parameterName)
    {
        if (gigabytes < 0 || (!allowZero && gigabytes == 0))
        {
            throw new ArgumentOutOfRangeException(
                parameterName,
                allowZero
                    ? "Gigabytes must be zero or greater."
                    : "Gigabytes must be greater than zero.");
        }

        decimal bytes = decimal.Round(
            gigabytes * BytesPerGigabyte,
            0,
            MidpointRounding.AwayFromZero);
        return checked((long)bytes);
    }

    private static string? ResolveConfigurationPath()
    {
        string? explicitPath = Environment.GetEnvironmentVariable("PHOTOIDENTITY_LAUNCHER_CONFIG");
        if (!string.IsNullOrWhiteSpace(explicitPath))
        {
            return explicitPath;
        }

        string localAppData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        if (string.IsNullOrWhiteSpace(localAppData))
        {
            return null;
        }

        return Path.Combine(localAppData, "PhotoIdentity", "launcher.json");
    }
}
