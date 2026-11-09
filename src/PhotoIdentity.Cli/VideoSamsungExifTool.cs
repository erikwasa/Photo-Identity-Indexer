using System.Diagnostics;
using System.Text;
using System.Text.Json;

namespace PhotoIdentity.Cli;

/// <summary>
/// An explicitly opt-in, camera-model-only adapter for the Samsung smta
/// metadata group. ExifTool may see SamsungModel where FFprobe sees no
/// matching tag. It never reads location, capture time or arbitrary tags.
/// </summary>
public sealed record SamsungVideoCameraMetadata(string CameraMake, string CameraModel);

public static class SamsungVideoCameraMetadataParser
{
    // ExifTool -G1 -j yields "Samsung:SamsungModel" for Samsung smta/QuickTime.
    // Reject any conflicting or malformed group entries instead of guessing.
    public static SamsungVideoCameraMetadata? Parse(string json)
    {
        if (json.Length > 64 * 1024)
            throw new InvalidDataException("Vendor camera metadata output exceeded the limit.");

        using JsonDocument doc = JsonDocument.Parse(json, new JsonDocumentOptions
        {
            MaxDepth = 8
        });
        if (doc.RootElement.ValueKind != JsonValueKind.Array ||
            doc.RootElement.GetArrayLength() != 1 ||
            doc.RootElement[0].ValueKind != JsonValueKind.Object)
            throw new InvalidDataException("Vendor camera metadata response was not a single source.");

        string? accepted = null;
        foreach (JsonProperty property in doc.RootElement[0].EnumerateObject())
        {
            if (!property.Name.Equals("Samsung:SamsungModel", StringComparison.OrdinalIgnoreCase))
                continue;

            if (property.Value.ValueKind != JsonValueKind.String)
                return null;
            string raw = property.Value.GetString() ?? "";
            if (raw.Length is < 2 or > 128 || raw.Any(char.IsControl))
                return null;
            string model = raw.Trim();
            if (model.Length < 2 || !model.Any(char.IsLetterOrDigit))
                return null;
            if (accepted is not null && !string.Equals(model, accepted, StringComparison.Ordinal))
                return null;
            accepted = model;
        }

        return accepted is null ? null : new("Samsung", accepted);
    }
}

public sealed class VideoSamsungExifTool(string approvedExe, TimeSpan timeout)
{
    private readonly string _exe = VideoPilotMediaTools.ValidateExecutable(approvedExe);
    private readonly TimeSpan _timeout = timeout >= TimeSpan.FromSeconds(1) &&
        timeout <= TimeSpan.FromMinutes(2) ? timeout
        : throw new ArgumentOutOfRangeException(nameof(timeout));

    public async Task<SamsungVideoCameraMetadata?> ProbeAsync(
        string sourcePath, CancellationToken token = default)
    {
        // Disable user/global ExifTool config and request only the observed
        // Samsung model tag. No write option, recursion or sidecar discovery.
        ProcessStartInfo start = new()
        {
            FileName = _exe,
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            WorkingDirectory = Path.GetDirectoryName(_exe)!
        };
        foreach (string argument in new[]
        {
            "-config", "", "-json", "-G1", "-s", "-SamsungModel", sourcePath
        })
            start.ArgumentList.Add(argument);

        using Process process = new() { StartInfo = start };
        using CancellationTokenSource deadline = CancellationTokenSource.CreateLinkedTokenSource(token);
        deadline.CancelAfter(_timeout);
        try
        {
            if (!process.Start())
                throw new IOException("Approved camera metadata tool did not start.");
            // Stream stdout with a strict size bound, while draining stderr
            // without keeping private filenames or vendor metadata in memory.
            Task<string> standard = ReadLimitedAsync(
                process.StandardOutput, 64 * 1024, deadline.Token);
            Task discardErrors = DiscardAsync(process.StandardError, deadline.Token);
            string data = await standard;
            await process.WaitForExitAsync(deadline.Token);
            await discardErrors;
            if (process.ExitCode != 0)
                throw new InvalidDataException("Vendor camera inspection failed.");
            return SamsungVideoCameraMetadataParser.Parse(data);
        }
        catch
        {
            try
            {
                if (!process.HasExited)
                    process.Kill(entireProcessTree: true);
            }
            catch (InvalidOperationException) { } // process may not have started
            catch (System.ComponentModel.Win32Exception) { } // already stopped
            throw;
        }
    }

    private static async Task<string> ReadLimitedAsync(
        StreamReader reader, int maximumChars, CancellationToken token)
    {
        StringBuilder buffer = new();
        char[] block = new char[2048];
        int count;
        while ((count = await reader.ReadAsync(block.AsMemory(), token)) > 0)
        {
            if (buffer.Length + count > maximumChars)
                throw new InvalidDataException("Vendor camera metadata output exceeded the limit.");
            buffer.Append(block, 0, count);
        }
        return buffer.ToString();
    }

    private static async Task DiscardAsync(StreamReader reader, CancellationToken token)
    {
        char[] block = new char[2048];
        while (await reader.ReadAsync(block.AsMemory(), token) > 0) { }
    }
}
