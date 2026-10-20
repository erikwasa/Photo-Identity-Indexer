# WI-0188 — self-contained synthetic proxy roundtrip

**Development-only, dry-run-first, Windows-only. No catalogue access.**
This command is deliberately not a video worker, not an admission switch and
not a source-file transcode feature. It exists to validate the experimental
WI-0188/WI-0189 720p H.264/AAC encoder + independent FFprobe contract on
operator-installed binaries before asking the maintainer to test private MOV/MP4.

## What it exercises

The optional explicit-call CLI `video synthetic-proxy-pilot`:

1. Requires a dedicated otherwise-empty, marked, **nonlinked direct/indirect
   child of the OS temporary directory**. It refuses network scratch, archive
   paths, existing files and any root without the exact lab marker.
2. Creates two short artificial moving-picture **source fixtures** in
   app-owned transient scratch using a SHA-256-approved, separately installed
   FFmpeg: 1280×720 landscape with sine audio and 720×1280 portrait without
   audio. It cannot accept a user-media input path and never reads the archive.
3. Runs each generated fixture through the *same*
   `VideoProxyCandidateProfile.BuildArguments` fixed H.264/AAC 720p
   argument builder used by the unregistered experimental video adapter.
4. Independently validates MP4 box layout, fast-start `moov` before
   `mdat`, duration/size, codec, Rec.709 transfer/color metadata,
   normalized orientation and optional AAC audio. The validator uses a
   separately SHA-256-approved FFprobe with its own guarded native Job Object.
5. Verifies the landscape output is landscape with audio, the portrait
   output is portrait without audio, and both are roughly three seconds.
   All generated video and JSON files are deleted by nested supervisors
   before returning. It emits only sanitized orientation/size/duration/
   encode-time measurements and no media paths, tags, names or hashes.

**Containment:** native suspended-launch single-process Windows Job Objects
for each external process, 768 MiB memory, 15% CPU rate hard cap,
120-second per-tool wall budget, 32 MiB each workspace, no inherited
credentials, bounded stdout/stderr, source-free FFmpeg lavfi fixtures and
local-file-only FFprobe. The existing scratch-capacity guard checks at
least 128 MiB of estimated free space plus safety headroom. There is
**no hard disk or I/O quota and no independent network sandbox**. Only
synthetic input is permitted and no application/video worker is enabled.

**No schema requirement for the standalone pilot:** it neither opens
PostgreSQL nor invokes catalogue migration, the source scanner, OneDrive
or any photo/video API. That is why it can be reviewed separately from the
later maintained-host schema v34→v36 rehearsal. Never use it to evaluate
production source media; there is no option to supply such a source.

## Optional operator procedure — not yet requested

This may be run on a Windows development environment with operator-approved,
separately installed FFmpeg/FFprobe tools. The manifest of tool binaries
and their prior approved SHA-256 values is part of accepted ADR-0012.
Do not mistake a freshly computed hash alone for approval.

In PowerShell from the repository root:

~~~powershell
$ErrorActionPreference = "Stop"

# Confirm these point to your separately installed, previously approved
# binaries; no installers or downloads are part of this procedure.
$ffmpeg = (Get-Command ffmpeg.exe -ErrorAction Stop).Source
$ffprobe = (Get-Command ffprobe.exe -ErrorAction Stop).Source
$ffmpegHash = (Get-FileHash -LiteralPath $ffmpeg -Algorithm SHA256).Hash.ToLowerInvariant()
$ffprobeHash = (Get-FileHash -LiteralPath $ffprobe -Algorithm SHA256).Hash.ToLowerInvariant()

# Independently compare both hashes with the accepted ADR-0012 fingerprints.
# Stop and review any mismatch before using the --apply switch.

$lab = Join-Path $env:TEMP ("photoidentity-video-lab-" + [guid]::NewGuid().ToString("N"))
New-Item -ItemType Directory -Path $lab | Out-Null
[IO.File]::WriteAllText(
    (Join-Path $lab ".photoidentity-synthetic-video-lab"),
    "wi0188-synthetic-video-lab-v1"
)

$argsPilot = @(
    "video", "synthetic-proxy-pilot",
    "--scratch-root", $lab,
    "--ffmpeg", $ffmpeg, "--ffmpeg-sha256", $ffmpegHash,
    "--ffprobe", $ffprobe, "--ffprobe-sha256", $ffprobeHash
)

dotnet run --project src/PhotoIdentity.Cli -c Release -- @argsPilot
if ($LASTEXITCODE -ne 0) { throw "Synthetic pilot preview failed." }

# Only after the binary fingerprints match the accepted approved values:
# dotnet run --project src/PhotoIdentity.Cli -c Release -- @argsPilot --apply
~~~

Preview must report `dryRun=true`, `status=not-run`, no results and
`schemaUsed=false`. With explicit `--apply`, expect two sanitized
results, `status=validated`, `dryRun=false`, and both orientations
correct. The command itself deletes its tool/input/probe workspaces. It
leaves the operator-created empty marked lab root in place for inspection.

An optional environment-gated xUnit integration test can perform this
roundtrip with exactly four variables:
`PHOTOIDENTITY_WI0188_TEST_FFMPEG`,
`PHOTOIDENTITY_WI0188_TEST_FFMPEG_SHA256`,
`PHOTOIDENTITY_WI0188_TEST_FFPROBE` and
`PHOTOIDENTITY_WI0188_TEST_FFPROBE_SHA256`. Normal CI builds do not need
these variables or third-party encoder installations; ordinary tests
exercise CLI parsing, marker/empty-root checks, exact arguments, and a
database-free dry-run.

## Windows synthetic-pilot failure triage (WI-0188 follow-up)

The maintainer's reported v37 runtime checks passed **all nine
photo regression checks**, read-only video status (disabled/paused,
no queued/running jobs) and the opt-in video API default-off 404 check.
The synthetic pilot's dry run also passed, but actual `--apply` yielded
only `status=pilot-failed` with zero validated results. The original
runner caught all exceptions without retaining a failure category. This
is **not evidence that source media or PostgreSQL were accessed**; the
pilot is deliberately independent of them.

A subsequent diagnostic revision adds sanitized *failure evidence* to the
same existing JSON response, without exposing original paths, arguments,
child stdout/stderr, stack traces, or private catalogue identifiers:

- `failureOrientation`: `landscape` or `portrait`; the first case
  is landscape with sine audio, and portrait is deliberately silent
- `failureStage`: `scratch-preflight`, `fixture-generation`,
  `proxy-encode`, `independent-probe` or `output-contract`
- `failureClass`: a strict allowlist such as
  `configuration-rejected`, `native-process-setup`,
  `filesystem-io`, `execution-safety-rejected`, `timeout`,
  `invalid-media-evidence`, `probe-tool-nonzero`,
  `probe-not-started`, `probe-preempted`,
  `probe-missing-output` or `unexpected-failure`
- `toolExitCode` and `nativeErrorCode`: numeric process/Win32 codes
  only, when available; both may be null

The CLI's existing success/no-media access declarations remain unchanged.
No broad logging mode or retention of failed media was introduced.

**Next step:** After the diagnostic PR passes CI and merges, inspect the
existing marked `$lab` directory *without recursively deleting unknown
files*. If it contains only the expected marker, rerun the already
constructed `$argsPilot` with `--apply`, and share only the returned
sanitized JSON. If it contains retained workspaces or an unfamiliar entry,
stop and investigate separately; `ValidateRoot` deliberately refuses a
nonempty root. Do not attempt `video one-shot run` to work around a
synthetic failure.

Never replace an approved FFmpeg/FFprobe executable or loosen Windows
Job Object limits merely to make a test pass. Review the failure stage
and numeric code first. The repo's optional environment-gated Windows
real-tool test remains the targeted regression once approved binaries
are provided; normal CI does not supply either executable.

## Follow-up: Windows fixture generation safety checkpoint

The subsequent maintainer rerun with the diagnostic revision returned:

```json
{"status":"pilot-failed","failureStage":"fixture-generation",
 "failureOrientation":"landscape","failureClass":"execution-safety-rejected",
 "toolExitCode":null,"nativeErrorCode":null,"results":[]}
```

The video source was *generated* test footage; no catalogue/original files
were opened and no video jobs activated. There is no successful synthetic
codec evidence yet. The `execution-safety-rejected` classification still
covers multiple separate .NET fail-closed guards and is not proof that FFmpeg
was even launched. Do not relax CPU/memory caps or approve real-video trials.

The next revision adds an **additional optional** `failureCheckpoint`
field to the same sanitized pilot JSON, drawn exclusively from fixed
source-authored codes, without changing the existing failureClass contract.
Useful values include `tool-digest-mismatch`, `linked-tool-path`,
`linked-executable`, `job-object-setup`,
`suspended-process-launch`, `process-observation`,
`scratch-entry-limit`, `scratch-linked-entry`,
`scratch-byte-limit`, `post-exit-validation` and
`process-shutdown-unverified`. It returns `null` for unrecognized
exceptions rather than copying an exception message. The first inner
failure checkpoint wins when a bounded tool is nested within another
supervisor. Nothing writes, reads or reveals private video paths, hashes,
tool output or exception text.

Once the checkpoint PR passes CI and merges, the operator can reuse the
previously approved `$argsPilot` array if and only if the dedicated scratch
root still contains **only** its ownership marker:

~~~powershell
Get-ChildItem -LiteralPath $lab -Force | Select-Object Name
dotnet run --project src/PhotoIdentity.Cli -c Release -- @argsPilot --apply
~~~

Report **only the JSON** (not the FFmpeg paths, source folders, hashes
or detailed Windows logs). Checkpoints are diagnostics, not a guarantee
that the Windows tool chain is validated. Avoid repeated `--apply`
once a new failure classification is available.

## Explicit H.264 SDR bitstream color evidence

A local FFmpeg/FFprobe smoke test revealed that container-level Rec.709
flags alone could yield an H.264 stream with `color_space=bt709` but
missing `color_transfer` and `color_primaries`. The candidate encoder
now pins libx264 bitstream VUI using the fixed
`colorprim=bt709:transfer=bt709:colormatrix=bt709` parameter string.
The strict independent validator still requires *all three* color fields
rather than relaxing the SDR contract. Pure tests pin this encoder flag,
and the optional installed-binary roundtrip verifies its real output.

## Boundaries and future verification

A successful roundtrip is useful proof that those **two synthetic SDR
cases** survived the local encoder/validator pipeline. It is not proof of
compatibility with real camera HEVC, 4K, HDR/HLG, rotation metadata,
variable frame rate, longer-duration clips, OneDrive online-only files,
browser seeking or quality. Real-media and retention approval remains
WI-0189; full video resource isolation and opt-in worker activation remain
WI-0188. The single consolidated maintained Windows acceptance must still
include photo-vs-mixed throughput, search/slideshow latency, source hashes,
lease revocation and no-hydration evidence.

The last accepted maintained PostgreSQL schema remains v34. Before any
v36 application or catalogue CLI command opens it, the independently
required **fresh backup and isolated v34→v36 rehearsal** from
[the v36 publication runbook](video-publication-and-v36-rehearsal.md)
must pass. This standalone database-free synthetic pilot does not satisfy
or bypass that gate.
