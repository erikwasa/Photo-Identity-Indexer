# WI-0211 — read-only poster coverage and playable browsing

This checkpoint does **not** produce new posters. It adds a whole-catalogue
aggregate of current, nonexcluded video revisions and a server-paged
`Playable videos only` filter. The existing poster service and native
playback remain unchanged. No video original, cache file, or OneDrive resource
is opened by the coverage endpoint or browse filter. These routes do not
enqueue work, create files, delete proxies, or mutate the catalogue.

The six aggregate fields describe **registered database evidence**, not a
filesystem audit of actual poster or MP4 bytes:

- `totalVideos`: current, nondeleted, nonexcluded registered videos
- `playableVideos`: current videos with an accepted published proxy receipt
- `videosWithPosters`: current videos with WI-0151 verified poster evidence
- `playableWithPosters`: intersection of those two groups
- `playableMissingPosters`: registered playable proxies with no WI-0151 poster
- `uninspectedOrUnsupported`: missing or non-supported video inspection

A `hasPlayableProxy` flag does not guarantee the cached file still exists
or passes live SHA-256 validation; playback continues to perform that check.
A `hasPoster` flag means registered evidence; the poster endpoint still
checks revision, exclusion, path containment, size, signature and hash.

## Windows acceptance (after CI and merge)

Use your **existing** local Photo Identity server and launcher; do not
guess a port, start a worker, change processing settings, or process media.

```powershell
cd "C:\Kod\codex\Photo Identity Indexer"
git switch main
git pull --ff-only origin main

# Use your existing approved Photo Identity API URL.
$apiBase = (Read-Host "Existing local API base URL").TrimEnd('/')
$coverage = Invoke-RestMethod "$apiBase/api/videos/coverage"
$coverage | Select-Object totalVideos, playableVideos,
    videosWithPosters, playableWithPosters, playableMissingPosters,
    uninspectedOrUnsupported

# Independent consistency checks using aggregate numbers only.
if ($coverage.playableWithPosters + $coverage.playableMissingPosters -ne $coverage.playableVideos) {
    throw "WI-0211 coverage intersection mismatch."
}
if ($coverage.playableVideos -gt $coverage.totalVideos -or
    $coverage.videosWithPosters -gt $coverage.totalVideos) {
    throw "WI-0211 aggregate exceeds visible catalogue."
}
```

Do not paste video titles, revision IDs, source paths, GPS, camera metadata or
raw API result pages into logs or chat. On the `/videos` browser page,
check that whole-catalogue counts appear above the list, that selecting
`Playable videos only` filters **across pages**, toggling it off restores
unplayable entries, and playback controls still work. Confirm no video
processing, new OneDrive downloads, original reads, or cache changes occur.

The **next** WI-0211 implementation must supply an explicit,
count-bounded proxy-derived poster producer with new versioned evidence and
secure delivery; this checkpoint deliberately does not silently claim
missing posters are fixed.
