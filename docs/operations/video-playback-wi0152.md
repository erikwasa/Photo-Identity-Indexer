# WI-0152 — retained video proxy playback acceptance

**State: implementation checkpoint pending Windows/phone acceptance.** This
does not authorize bulk video processing, hydrating originals or exposing the
cache to a public network. It uses the existing opt-in video library and
operator-configured trusted-LAN HTTPS mode.

## Maintained host results and targeted clipping fix — 2026-11-03

The maintainer reported **PASS** for disposable PostgreSQL playback tests,
HTTP range tests, docs validate/generate, desktop functional play/pause/seek,
206/416/404, zero new video processing, OneDrive online-only retention,
phone trusted-LAN HTTPS playback and photo viewer/search regression. However,
**both desktop and phone clipped the lower part of the native video player**:
the frame and bottom play/pause controls were partly invisible. This is a
**WI-0152 acceptance blocker**, not a pass simply because seeking works.

PR #720's follow-up CSS/markup correction gives the active player its own
expanded card, removes the fixed 16:10 poster constraint during playback,
allows natural video-element height, and caps very tall portrait players
to 75% of the viewport without cropping (`object-fit: contain`).
The CSS layout contract test does not substitute for actual phone/desktop
visual review.

### Focused retest after PR #720 follow-up

Update the existing WI-0152 checkout and execute:

```powershell
cd "C:\\Kod\\codex\\Photo Identity Indexer"
git fetch origin
git switch wi0152-retained-proxy-playback-v1
git pull --ff-only origin wi0152-retained-proxy-playback-v1

dotnet test tests/PhotoIdentity.Integration.Tests/PhotoIdentity.Integration.Tests.csproj `
    --filter "FullyQualifiedName~VideoPlaybackLayoutContractTests" `
    -p:WasmEnableWebcil=false
if ($LASTEXITCODE -ne 0) { throw "Video player layout contract failed." }

dotnet publish src/PhotoIdentity.Api/PhotoIdentity.Api.csproj `
    --configuration Release `
    --output ".artifacts/wi0152-test-app"
if ($LASTEXITCODE -ne 0) { throw "WI-0152 republish failed." }
```

Stop the currently running Photo Identity server normally. Restart
`Start-PhotoIdentity.ps1` using the **same previously validated private
launcher.json** and `-PublishPathOverride` pointing at
`$PWD\.artifacts\wi0152-test-app` (the exact startup commands are in
the existing verification procedure). Do not start a second server on
the same port, and do not change the stored PostgreSQL or video settings.

Hard-reload `/videos` on the Windows browser and HTTPS phone, open a
retained proxy (ideally both landscape and portrait if available), and
check **all of these**:

- The entire image, including its bottom edge, is visible—no poster
  cropping, forced 16:10 player height or half-frame cutoff.
- The native control bar and play/pause button are visible/reachable
  on desktop and phone without opening browser developer tools.
- Playback, pause, seeking and closing the player still work;
  controls remain visible after phone orientation change if supported.
- Closing restores the normal fixed-size poster tile. Opening a second
  card does not leave the previous player running or damage gallery layout.
- No changed original state or new video job is observed. The already
  accepted 206/416/404 and privacy tests do not need to be repeated
  solely for this CSS change unless something else regresses.

Report **desktop and phone visual PASS/FAIL separately**, with device
orientation and any still-hidden controls. Do not share personal video,
private media URLs, OneDrive paths or screenshots of identifying footage.
WI-0152 remains `in_progress` until this is visually accepted.

## Configuration

1. Update to the WI-0152 branch/package on the maintained Windows host, using
   the already-approved PostgreSQL launcher and existing source exclusions.
2. Keep `PhotoIdentity__VideoLibraryEnabled=true`. The pre-existing
   `PhotoIdentity__VideoPosterRoot` must remain the verified poster root.
3. The new read-only playback endpoint uses the **existing**
   `PhotoIdentity__VideoProcessing__CacheRoot`, or the normal application-local
   `%LOCALAPPDATA%\PhotoIdentity\video-worker-cache` default. It must
   be the actual *marked* location used for approved playable proxies.
   Playback never creates a cache, file, derivative, processing job or lease
   for an original.
4. No need to turn on the video processing Settings toggle to view a proxy.
   Existing loopback-only and trusted-LAN HTTPS access policies remain in effect.

## Focused automated checks

Run from the updated repository root with the normal Windows prerequisites.
The isolated PostgreSQL test is opt-in: set the **already-supported**
`PHOTOIDENTITY_TEST_POSTGRES_ADMIN_CONNECTION_STRING` in the private shell
only; never commit or share it.

```powershell
dotnet test tests/PhotoIdentity.Persistence.Tests/PhotoIdentity.Persistence.Tests.csproj `
    --filter "FullyQualifiedName~PostgresVideoPlaybackRepositoryTests" `
    -p:WasmEnableWebcil=false

dotnet test tests/PhotoIdentity.Integration.Tests/PhotoIdentity.Integration.Tests.csproj `
    --filter "FullyQualifiedName~VideoPlaybackRangeResultTests" `
    -p:WasmEnableWebcil=false

dotnet run --project tools/PhotoIdentity.Docs -- validate
dotnet run --project tools/PhotoIdentity.Docs -- generate --check
```

These tests exercise canonical-path refusal, genuine source-free publication
and stream reads, sha/size tamper rejection, evicted file denial, source-copy
exclusion and the ASP.NET file result's bounded 206/416 byte-range semantics.
The real browser and original-availability checks still require the host.

## Desktop browser acceptance

Use the normal local API base URL (examples use port 5080) and browse
`/videos`. Select an indexed video showing **Open player**. The standard
HTML video controls should play, pause, seek forward/backward, show duration,
and allow closing the player. A card without a retained receipt displays
**Playable proxy unavailable** instead of a link to an original.

For a selected revision, obtain the opaque revision ID only from the local
`/api/videos` response. Do **not** copy source locators or personal paths to
a PR. These requests should be made against the same host/session as the page.

```powershell
$base = "http://127.0.0.1:5080"
$page = Invoke-RestMethod "$base/api/videos?offset=0&limit=60"
$video = $page.items | Where-Object hasPlayableProxy | Select-Object -First 1
if (-not $video) { throw "No retained proxy on this page; choose another page or prepare explicitly." }
$play = "$base/api/videos/$($video.revisionId)/play"

curl.exe --silent --show-error --dump-header - --output NUL `
    --header "Range: bytes=0-1023" $play
# Expected: HTTP 206; Content-Type video/mp4; Content-Range bytes 0-1023/TOTAL
# If TOTAL is under 1024 bytes, choose a shorter satisfiable range.

curl.exe --silent --show-error --dump-header - --output NUL `
    --header "Range: bytes=9999999999-" $play
# Expected: HTTP 416; Content-Range bytes */TOTAL

curl.exe --silent --show-error --dump-header - --output NUL `
    "$base/api/videos/00000000-0000-0000-0000-000000000000/play"
# Expected: 404; no private local path disclosure
```

Check the same tests with `VideoLibraryEnabled=false` (404), a deliberately
missing/evicted registered proxy (404), and a source-copy-excluded video
(404). Avoid destructive changes to the personal archive or deleting a
real proxy just to simulate these failures: automated fixtures cover them.

A registered proxy whose file is subsequently missing may still show an
**Open player** button until the video list refreshes; the actual play
request is authoritative and must fail closed. A hash mismatch likewise
never sends even an initial media byte. No response includes cache paths.

## No original hydration or extra processing

Choose an already-retained proxy whose original is OneDrive online-only,
with that availability independently verified through safe filesystem
metadata (do not open original bytes to check). Record aggregate video
processing counters and online-only availability before playback. Play,
seek and reload the browser page, then compare counters and availability:
no new processing attempt, no hydration, no original-source access.
Never change OneDrive pin state just for this test.

## Phone, regression and closeout

If the existing WI-0082 opt-in trusted-LAN HTTPS phone setup is available,
open `/videos` on the maintained phone and perform a short play/pause/seek
test over that established HTTPS endpoint. Do not add a public listener or
new phone authentication mode. If unavailable, record the phone criterion
as explicitly unverified.

Verify an ordinary image gallery/viewer and a photo-only search or
slideshow remain unchanged. Share aggregate pass/fail, response codes and
device type only—no video URLs with private IDs, screenshots of private
content, source paths, tokens or footage.

WI-0152 acceptance and status closure require maintainer evidence from
these checks; merged code alone is not acceptance.
