# WI-0210 — one-step Rec.709 full-range production proxy acceptance

**Operator runbook. Execute only after the production integration PR merges
to `main` and its required CI checks pass.** This is a single, explicit
Windows production-verification session; CI owns all synthetic unit,
disposable PostgreSQL, package, launcher and documentation tests.

## What is already accepted, and what is not

On 2026-11-04 the maintainer explicitly authorized reading one current,
already-local Rec.709 full-range `yuvj420p` archive original. The isolated
real-video evaluation passed independent full-range BT.709 input
inspection, 720p limited-range H.264/AAC output, original SHA recheck,
and **human visual colour/orientation/audio/play/seek** acceptance.
It reported no hydration, catalogue mutation, job or published
derivative. One original is accepted; **zero newly published and
played production proxies** are yet verified. Do not rerun that
experimental conversion or independently hash the original merely
to repeat accepted evidence.

This integration adds a *new* selected encoding profile,
`rec709-fullrange-720p`, with a distinct durable
`wi0210-rec709-fullrange-720p-production-v1` preset version.
The existing `standard-720p` and `compact-540p` profiles remain
unchanged. The new preset demands exactly `yuvj420p` metadata
followed by independent full-range/BT.709 proof against the
already SHA-verified staged original **before** FFmpeg starts.
Only one matching video, optional one matching audio and at most
two unselected data streams are admissible. The fixed encoder
maps **only** video/audio, strips source metadata, normalizes to
limited-range Rec.709; independent output verification rejects
additional streams and format/geometry errors before the same
receipt publisher used by normal playable proxies. Photo-first,
current revision and exclusion fences, Windows resource budgets,
pause/preemption and no-recall local sources remain required.
No HDR, HLG, online-only hydration or poster/frame policy expansion.

## 1. Update the merged code and prepare your normal app

~~~powershell
cd "C:\Kod\codex\Photo Identity Indexer"
git switch main
git pull --ff-only origin main
~~~

Start the normal PostgreSQL server and your established
`Start-PhotoIdentity.ps1` application launcher with its
already validated local PostgreSQL and pinned FFmpeg/FFprobe
configuration. **Do not change credentials or tool hashes.**
Do not enable a separate video daemon. The Archive's normal
photo-first Advance and Pause operations own the worker.

## 2. Record a private baseline before authorizing any original read

Open the **Video processing** Settings panel. Record privately:
`Succeeded`, `Pending`, `Failed`, `Last processing result`
and whether any video work is currently running.
Make sure you are on the expected archive source/coverage and
no unrelated video job is in progress. Keep identifiers,
source paths, media names and job IDs off GitHub and chat.

Optional loopback-only read-only API snapshot if your existing
application API endpoint is available:

~~~powershell
# Use your existing approved API address. Do not guess a new port.
$apiBase = (Read-Host "Enter the existing local PhotoIdentity API base URL").TrimEnd('/')
$before = Invoke-RestMethod "$apiBase/api/video-processing/settings"
$before | Select-Object succeeded, queued, failed, processing, enabled,
    includeOnlineOnly, encodingProfile, stopAfterOneVideoStepPerAdvance
~~~

Do not paste the full response (it can contain private queue IDs).
The snapshot is observational and does **not** open media.

## 3. Deliberately authorize exactly one local video step

In Settings > Video processing, use these exact choices:

- **Process videos:** off while editing, then on only for the
  explicitly authorized acceptance step
- **Include online-only videos:** **off** (required)
- **Stop each Archive advance after one video step:** **on** (required)
- **Video proxy encoding:** **Rec.709 full-range 720p**
- Save Settings. If the app rejects settings, stop and inspect
  the nonprivate error; do not edit the preferences JSON to bypass it.

Merely saving the profile does not open an original. **Enabling
video processing and then pressing `Advance archive` explicitly
authorizes the existing photo-first archive path to select and
process at most one currently eligible, locally available video
step.** The selected source may be one of the other 12 metadata
candidates; each selected original is independently reprobed,
not assumed compatible. Never silently authorize bulk processing
or OneDrive hydration. If photo work is pending, the Archive
must finish photos first.

Click **Advance archive** once. The new full-range preset
must not be selected or executed by standard/compact settings.
If this single step only registers/inspects a video rather
than encoding a proxy, report that outcome rather than
assuming a derivative was published. Another Advance
requires a fresh deliberate choice.

## 4. Check the one-step outcome and actual playback

After the Advance step returns, inspect Video processing Status
and **Recent video jobs**. If an attempt is failed, note only
its **sanitized reason/stage**, not filenames, job IDs or hashes,
and do not blindly retry another video.

If a new `PlayableProxy` receipt succeeded, locate the
corresponding entry privately in the existing **Videos** UI.
Verify that the generated, durable proxy actually plays,
pauses and seeks; that orientation and aspect ratio are correct;
and that colour/brightness/contrast and audio are acceptable.
Use the existing trusted-LAN phone/HTTPS playback only if
it materially helps verify the new media, not to repeat all
previous WI-0152 browser/player regression tests.
A `Succeeded` job without observed playable output is
**not** accepted browser playback.

Optional read-only after snapshot:

~~~powershell
$after = Invoke-RestMethod "$apiBase/api/video-processing/settings"
[pscustomobject]@{
    BeforeSucceeded = $before.succeeded
    AfterSucceeded = $after.succeeded
    DeltaSucceeded = $after.succeeded - $before.succeeded
    BeforeQueued = $before.queued
    AfterQueued = $after.queued
    LastResult = $after.lastResult
}
~~~

The aggregate job count alone is not proof that a particular
source gained a playable proxy; combine it with the private
observed receipt and browser play/seek. Do not share the
full API response.

## 5. Stop and preserve the registered proxy

Click **Pause archive** and disable **Process videos** in
Settings. Verify no video job remains running. Restore the
previous selected encoding preset if desired, **while disabled**.
Do not run retention cleanup, remove new derivatives, delete
originals, or change the stored SHA evidence to finish this check.
Temporary worker scratch is automatically managed by the
existing publisher. A successfully registered derivative should
remain registered and playable after an app restart.

## 6. Report only sanitized acceptance evidence

Provide:
- Production profile selected: yes/no; local-only: yes/no;
  single-step: yes/no
- Before/after aggregate `Succeeded` counts and step
  status/reason (no paths, UUIDs, hashes or filenames)
- New registered playable proxy: yes/no
- Browser/desktop play-pause-seek, colour, orientation,
  audio: pass/fail; optional trusted-LAN phone playback
- No unintended hydration, other-source processing or
  photo-work regression observed: yes/no

Expected acceptance is **one additional genuinely playable
production proxy**, not all 12 candidates. Continue to report
**12 metadata-potential, one real evaluation accepted, and
zero production additions verified** until this step is actually
performed and confirmed. If one source fails independent probe,
stop that exact step rather than weakening colour gates,
processing other sources automatically, or claiming WI-0210
completed.
