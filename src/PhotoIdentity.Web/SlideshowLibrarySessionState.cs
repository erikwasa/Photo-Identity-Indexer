using System.Net.Http.Json;
using PhotoIdentity.Web.Contracts;

namespace PhotoIdentity.Web;

/// <summary>Browser-session display metadata only. Never authorizes preparation or playback.</summary>
public sealed class SlideshowLibrarySessionState(HttpClient http, TimeProvider? timeProvider = null)
{
    private const int MaximumCovers = 256;
    private static readonly TimeSpan CoverLifetime = TimeSpan.FromMinutes(1);
    private readonly TimeProvider _time = timeProvider ?? TimeProvider.System;
    private readonly SemaphoreSlim _coverGate = new(4);
    private readonly object _sync = new();
    private readonly Dictionary<string, Cover> _covers = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, CoverLoad> _loads = new(StringComparer.OrdinalIgnoreCase);

    private readonly Dictionary<string, long> _versions = new(StringComparer.OrdinalIgnoreCase);

    public void InvalidateCover(string id)
    {
        lock (_sync)
        {
            _versions[id] = _versions.GetValueOrDefault(id) + 1;
            _covers.Remove(id);
        }
    }

    public SlideshowLibraryCollectionResponse[]? SmartCollections { get; set; }
    public PhotoListCollectionResponse[]? ManualCollections { get; set; }
    public CreativeCollectionRecipeResponse[]? CreativeCollections { get; set; }

    public SmartCollectionPageResponse? CachedCover(string id)
    {
        lock (_sync)
        {
            return _covers.GetValueOrDefault(id)?.Page;
        }
    }

    public Task<SmartCollectionPageResponse?> GetCoverAsync(string id, CancellationToken cancellationToken = default)
    {
        Task<SmartCollectionPageResponse?> load;
        lock (_sync)
        {
            if (_covers.TryGetValue(id, out Cover? cover) && _time.GetUtcNow() - cover.LoadedAt < CoverLifetime)
            {
                return Task.FromResult<SmartCollectionPageResponse?>(cover.Page);
            }
            long version = _versions.GetValueOrDefault(id);
            if (_loads.TryGetValue(id, out CoverLoad? existing) && existing.Version == version)
            {
                load = existing.Task;
            }
            else
            {
                load = LoadCoverAsync(id, version);
                _loads[id] = new CoverLoad(version, load);
            }
        }
        // One disposed card must not cancel work shared with another card for the same anchor.
        return load.WaitAsync(cancellationToken);
    }

    private async Task<SmartCollectionPageResponse?> LoadCoverAsync(string id, long version)
    {
        await Task.Yield(); // Ensure the shared task is registered before it can finish.
        try
        {
            await _coverGate.WaitAsync();
            try
            {
                SmartCollectionPageResponse? page = await http.GetFromJsonAsync<SmartCollectionPageResponse>(
                    $"api/smart-collections/{Uri.EscapeDataString(id)}/query?offset=0&limit=1");
                if (page is not null)
                {
                    lock (_sync)
                    {
                        if (_versions.GetValueOrDefault(id) != version)
                        {
                            return page;
                        }
                        _covers[id] = new Cover(page, _time.GetUtcNow());
                        if (_covers.Count > MaximumCovers)
                        {
                            string oldest = _covers.OrderBy(pair => pair.Value.LoadedAt).First().Key;
                            _covers.Remove(oldest);
                        }
                    }
                }
                return page;
            }
            finally
            {
                _coverGate.Release();
            }
        }
        finally
        {
            lock (_sync)
            {
                if (_loads.TryGetValue(id, out CoverLoad? current) && current.Version == version)
                {
                    _loads.Remove(id);
                }
            }
        }
    }

    private sealed record CoverLoad(long Version, Task<SmartCollectionPageResponse?> Task);
    private sealed record Cover(SmartCollectionPageResponse Page, DateTimeOffset LoadedAt);
}
