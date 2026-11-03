# WI-0210 — archive census and Rec.709 full-range evaluation

**State:** PR #721 merged; maintainer accepted all seven local checkpoint-1 checks on 2026-11-03, including disposable PostgreSQL. The operator's current catalogue has 58 nonexcluded current videos, with 45 worker metadata-blocked. This checkpoint still admits no additional media class. WI-0207 separately owns OneDrive hydration.

## Scope of the census

The new CLI subcommand **video compatibility survey** reads one repeatable-read PostgreSQL snapshot of the latest revisions of live video assets. A read-only transaction and current-schema guard prevent mutations and upgrades. It queries existing technical inspection, recorded source availability and registered poster, playable-proxy and frame-evidence receipts. Excluded current videos count separately and do not contribute to the compatibility population.

Output contains only aggregated counts: source kind, last-recorded availability, inspection status, container, video/audio codecs, transfer, pixel format, rotation, coded resolution, duration and size. Rare or unrecognized values are bucketed as other; missing values are unknown. It shows stage receipt counts, the current shared worker's **metadata admission** gate, a narrow poster metadata gate, and frame proxy-receipt precondition.

The worker comparison uses the existing Rec.709 H.264/HEVC, selected audio/pixel formats, known rotations, coded dimensions 16–4096, duration 0.2–600 seconds and 256 MiB source cap. The **overlappingCandidateGaps** are deliberately nonexclusive: a large HLG video counts as both large and non-bt709. The **workerPrimaryGates** name the first failure per item; neither ranking establishes an independently unlockable number or implementation effort.

The next read-only checkpoint adds **singleGateProjections**, computed by
substituting just one *metadata* field into the existing worker gate while
leaving every other guard intact. It reports candidate classes and
last-recorded availability buckets, without individual filenames or IDs:
- `rec709-yuvj420p-pixel-only`: explicitly Rec.709 `yuvj420p`
  whose other metadata passes if this *one* format gate changes;
- `hlg-transfer-only`: HLG where the transfer check alone fails;
  **not** proof an HDR-to-SDR conversion exists or is visually safe;
- `unknown-transfer-only`: missing colour transfer alone blocks;
  never assume it means SDR;
- `missing-rotation-only`: absent rotation is the sole gate;
  substituting zero is a counting device, **not** evidence of zero rotation.

A projection is an **upper bound on metadata candidates**, not a
guarantee of local originals, decoded frames, colour correctness,
retained proxies or processing success. In particular `yuvj420p` is
usually full-range-coded; before a new proxy profile is admitted, check
an approved local example's stream `color_range` with FFprobe and
visually validate conversion to limited-range, tagged BT.709 H.264.
The current schema does not persist stream colour range.
No worker SQL, poster policy, FFmpeg output profile, or default is
changed by this checkpoint.

**Coverage limitation:** This census counts only video assets already registered in
Photo Identity's PostgreSQL catalogue. It does **not** enumerate the broader
OneDrive/archive tree, nor can it infer codecs for files never inspected.
The 2026-10-14 inventory saw 1,309 MOV/MP4 entries but probed only 18
already-local samples. Do not treat a small catalogue count as the whole
archive or rank universal codec coverage from it. For a wider **separate**
read-only source-side count, the existing
[WI-0150 archive survey](video-codec-survey.md) can enumerate filesystem
attributes and safely skip online-only placeholders; its optional bounded
FFprobe sampling opens *only* approved locally available files, so review
its parameters separately before running. Compare source-side metadata
totals with the catalogue before selecting an expansion. Both are partial
signals, not full-archive codec knowledge.

**Limitations:** The catalogue's availability is last-observed, not a fresh filesystem check. A registered proxy/poster/frame receipt does not prove the cached bytes are present, authentic or playable. No original bytes are read, no FFprobe or local-file traversal is performed, and there is no OneDrive recall, job enqueue, derivative write, search or migration. The SQL does not select identifiers, paths, filenames, capture dates, camera labels, coordinates, captions, embeddings or identities.

## Windows verification on merged main / checkpoint branch

From an existing repo clone with the usual approved *local* PostgreSQL environment variable and an already migrated database:

~~~powershell
cd "C:\Kod\codex\Photo Identity Indexer"
git switch main
git pull --ff-only origin main
# To verify the next proposed checkpoint PR before merge, switch to
# wi0210-pixel-range-candidate-evidence after fetching that branch.

dotnet test tests/PhotoIdentity.Integration.Tests --filter "FullyQualifiedName~VideoCompatibilitySurveyCommandTests"

$envName = "PHOTOIDENTITY_POSTGRES_CONNECTION_STRING"
$raw = [Environment]::GetEnvironmentVariable($envName)
if ([string]::IsNullOrWhiteSpace($raw)) {
    throw "Approved local PostgreSQL environment variable is not set."
}
# The maintainer confirmed that the real production catalogue is
# photoidentity_main. The deploy/postgres/.env value "photoidentity" is
# only the container's initial database, NOT this catalogue.
# Do not infer the production database name from the connection string:
# in this maintained installation it does not specify that key.
$db = "photoidentity_main"

$output = & dotnet run --project src/PhotoIdentity.Cli --configuration Release -- video compatibility survey --postgres-connection-env $envName --database $db
if ($LASTEXITCODE -ne 0) { throw "WI-0210 survey failed; do not save incomplete output." }
$json = $output -join "`n"
$report = $json | ConvertFrom-Json -ErrorAction Stop
$outDir = Join-Path $env:LOCALAPPDATA "PhotoIdentity\diagnostics"
New-Item -ItemType Directory -Path $outDir -Force | Out-Null
$outFile = Join-Path $outDir "wi0210-compatibility-inventory.json"
$json | Set-Content -LiteralPath $outFile -Encoding utf8
$report | Select-Object protocol, readOnly, currentNonExcludedVideoCount, excludedCurrentVideoCount, originalBytesOpened, filesystemAvailabilityChecked, oneDriveHydrationRequested, jobsEnqueued
$report.overlappingCandidateGaps | Select-Object -First 10 | Format-Table reason, affected
$report.workerPrimaryGates | Format-Table reason, count
$report.stages | Format-Table stage, state, count
$report.dimensions |
    Where-Object { $_.dimension -in 'pixel-format','transfer','rotation','last-recorded-availability' } |
    Format-Table dimension, bucket, count
# After the next PR: single-change projections and recorded availability
$report.singleGateProjections |
    Format-Table candidate, recordedAvailability, count
~~~

Keep the report in the local diagnostics folder, outside the repo. Aggregates may still expose rare combinations of private formats, so share only manually reviewed coarse counts. Existing sources and OneDrive Files On-Demand states should not change.

## Maintainer's current sanitized catalogue evidence (2026-11-03)

- **58** latest live nonexcluded videos; **0** excluded.
- Worker first-gate partition: **26** transfer, **13** pixel,
  **5** rotation, **1** unsupported inspection; **13** metadata-eligible.
- Overlapping gaps: **26** transfer non-BT.709/unknown, **13** other
  pixel format, **10** unknown/other rotation, **1** unsupported.
- Current receipts: **13** playable proxies, **3** frame evidence and
  **1** poster. Counts at different stages do not identify the same rows.
- This evidence covers the 58 registered videos, *not* the previously
  source-enumerated 1,309 MOV/MP4 entries.
- The exact `pixel-format`, `transfer`, `rotation`, and
  `last-recorded-availability` dimensions, and independent single-gate
  projections, must be inspected before estimating the reachable
  already-local subset.

**Chosen engineering target, pending actual opt-in input approval:**
Rec.709 `yuvj420p`/full-range conversion, **playable proxy stage first**.
Do not broaden `bt709` transfer admission, interpret missing rotation
as zero, or auto-generate poster/frame evidence. HLG requires a separately
reviewed tone mapping policy. Existing Rec.709 poster eligibility is a
distinct gate and remains unchanged.

## Checkpoint 2 — confirmed catalogue, selected input class (2026-11-03)

PR #722 merged and the maintainer successfully ran the read-only survey
against the **actual production database `photoidentity_main`**. Do not
mistake the container's initial `photoidentity` database or a historical
`photoidentity_catalogue` example for that database. The survey never
migrates a database with the wrong schema.

Actual current counts: **58** active video revisions, **0** excluded;
**32** Rec.709 transfer, **25** HLG, **1** unknown transfer;
**20** `yuv420p`, **25** `yuv420p10le`, **13** `yuvj420p`;
**47** rotations in the 90-degree bucket, **1** in the 180-degree
bucket and **10** unknown.

The **mutually exclusive**, *single-gate* projection results are:

| Metadata-only change | Candidate count | Last-recorded availability |
| --- | ---: | --- |
| Rec.709 `yuvj420p` pixel gate only | **12** | unknown |
| HLG transfer gate only | 22 | unknown |
| Missing rotation gate only | 5 | unknown |

All **58** current video rows report `last-recorded-availability:
unknown`. No actual local existence, OneDrive Files On-Demand state,
source range or achievable new proxy count is demonstrated by this
survey. The initial **12/58 (~20.7%)** upper bound selects only the
Rec.709 full-range candidate class for the first playable-proxy
evaluation. Do not infer the format's `color_range` from its pixel
format alone.

### Checkpoint 3 — isolated evaluation contract (not production admission)

`VideoRec709FullRangeEvaluationProfile` is an unregistered code
contract, never exposed by the app's profile selector or enqueue SQL:

- Strict independent FFprobe **input** checks require one approved
  video stream, at most one approved audio stream, explicit
  `yuvj420p`, `color_range=pc`, Rec.709 primaries/transfer/matrix,
  known matching rotation, current inspection metadata, duration,
  dimensions and source-byte ceiling. Unknown or contradictory tags
  fail closed; the reader does not open originals.
- Its separate fixed version
  `wi0210-rec709-fullrange-720p-evaluation-v1` derives software-only
  H.264/AAC 720p args from the unchanged baseline, but forces
  `scale=in_range=full:out_range=limited` with BT.709 in/out
  matrices and `-color_range tv`. It remains unregistered and cannot
  enqueue jobs.
- **Output** contract adds an independent
  `color_range=tv` requirement to the existing H.264/AAC,
  tagged-Rec.709, bounded MP4 validator contract.
- Unit tests cover valid/invalid metadata and FFprobe JSON,
  range-signalling, orientation, no accidental input stream admission,
  unchanged normal baseline and the unregistered profile.
- This does **not** yet prove how real source colours appear, that
  FFmpeg produces a compliant output on the operator's installed
  build, or that any of the 12 current originals are locally available.

To verify the contract on Windows without media or PostgreSQL:

~~~powershell
cd "C:\Kod\codex\Photo Identity Indexer"
dotnet test tests/PhotoIdentity.Integration.Tests --configuration Release --filter "FullyQualifiedName~VideoRec709FullRangeEvaluationProfileTests|FullyQualifiedName~VideoProxyEncodingProfilesTests|FullyQualifiedName~VideoProxyCandidateProfileTests"
if ($LASTEXITCODE -ne 0) { throw "Full-range evaluation contract tests failed." }
dotnet run --project tools/PhotoIdentity.Docs -- validate
dotnet run --project tools/PhotoIdentity.Docs -- generate --check
~~~

**Do not** widen worker SQL/preset selection or alter persisted video
jobs based on this test alone. Next: identify **one operator-approved,
already-local** Rec.709 full-range example (do not hydrate anything);
use hash-approved FFprobe on that source to confirm all four colour
tags and known orientation; test the fixed conversion in owned private
scratch under the existing Windows media-tool budgets; inspect output
`color_range=tv`, true colour/contrast, rotation, bounded playback
and zero source mutation. Once these pass and the maintainer explicitly
approves this stage/class, register a **new** policy/version, preserving
existing SDR/HDR/unknown input policy and exclusion controls.

## Checkpoint 2 decision gate

1. Review **both** actual current catalogue counts and the independent
   archive's filesystem-only availability/extension counts; note unknown
   metadata, missing registrations and cached availability separately. The 2026-10-14 18-file sample is **not** representative of the full archive.
2. Compare two or three candidate gaps for real-user usefulness, overlapping limitations, effort and approved already-local test media. Gap counts are upper bounds, not a promise of unlocks.
3. Maintainer explicitly approves **one** selected input class and target stages (poster, retained playable proxy, and/or timestamped frame evidence).
4. Then change only the necessary policy/profile, using a few approved real local originals and focused revision/exclusion/scratch/colour and photo regressions. Leave unsupported/HDR formats explicit and do not invoke WI-0207 hydration.

**Confirmed:** checkpoint-1 maintainer-host inventory and seven local
checks passed. **Still pending:** next-checkpoint CI, exact isolated
candidate counts, review of one approved already-local full-range sample,
explicit production-stage/class approval, any real new-proxy result,
or altered admission policy. WI-0210 remains in progress.

See [WI-0210](../delivery/work-items/WI-0210-archive-driven-video-compatibility-expansion.md) and [staged matrix](video-compatibility-matrix.md).
