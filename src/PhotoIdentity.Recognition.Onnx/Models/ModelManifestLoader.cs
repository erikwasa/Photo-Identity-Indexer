using System.Text.Json;
using System.Text.Json.Serialization;

namespace PhotoIdentity.Recognition.Onnx.Models;

public sealed class ModelManifestLoader
{
    private static readonly JsonSerializerOptions SerializerOptions = new()
    {
        PropertyNameCaseInsensitive = false,
        AllowTrailingCommas = false,
        ReadCommentHandling = JsonCommentHandling.Disallow,
        UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow,
    };

    public async Task<ModelManifest> LoadAsync(
        string path,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);

        try
        {
            await using FileStream stream = new(
                path,
                FileMode.Open,
                FileAccess.Read,
                FileShare.Read,
                bufferSize: 64 * 1024,
                useAsync: true);

            ModelManifest? manifest = await JsonSerializer.DeserializeAsync<ModelManifest>(
                stream,
                SerializerOptions,
                cancellationToken);

            if (manifest is null)
            {
                throw new ModelManifestException($"Model manifest '{path}' is empty.");
            }

            ModelManifestValidator.Validate(manifest);
            return manifest;
        }
        catch (ModelManifestException)
        {
            throw;
        }
        catch (JsonException exception)
        {
            throw new ModelManifestException(
                $"Model manifest '{path}' is not valid JSON: {exception.Message}",
                exception);
        }
    }

    public async Task<IReadOnlyList<ModelManifest>> LoadDirectoryAsync(
        string directory,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(directory);

        if (!Directory.Exists(directory))
        {
            throw new DirectoryNotFoundException(
                $"Model manifest directory '{directory}' does not exist.");
        }

        List<ModelManifest> manifests = [];
        foreach (string path in Directory
                     .EnumerateFiles(directory, "*.json", SearchOption.TopDirectoryOnly)
                     .OrderBy(value => value, StringComparer.Ordinal))
        {
            manifests.Add(await LoadAsync(path, cancellationToken));
        }

        if (manifests.Count == 0)
        {
            throw new ModelManifestException(
                $"Model manifest directory '{directory}' contains no JSON manifests.");
        }

        string? duplicate = manifests
            .GroupBy(manifest => manifest.ModelId, StringComparer.Ordinal)
            .FirstOrDefault(group => group.Count() > 1)
            ?.Key;

        if (duplicate is not null)
        {
            throw new ModelManifestException(
                $"Model ID '{duplicate}' is declared by more than one manifest.");
        }

        return manifests;
    }
}
