# WI-0210 — read-only video compatibility census (checkpoint 1)

**State:** code proposed, pending Windows CI and maintained-host inventory. This checkpoint admits no additional media class and does not select an expansion. WI-0207 separately owns OneDrive hydration.

## Scope of the census

The new CLI subcommand **video compatibility survey** reads one repeatable-read PostgreSQL snapshot of the latest revisions of live video assets. A read-only transaction and current-schema guard prevent mutations and upgrades. It queries existing technical inspection, recorded source availability and registered poster, playable-proxy and frame-evidence receipts. Excluded current videos count separately and do not contribute to the compatibility population.

Output contains only aggregated counts: source kind, last-recorded availability, inspection status, container, video/audio codecs, transfer, pixel format, rotation, coded resolution, duration and size. Rare or unrecognized values are bucketed as other; missing values are unknown. It shows stage receipt counts, the current shared worker's **metadata admission** gate, a narrow poster metadata gate, and frame proxy-receipt precondition.

The worker comparison uses the existing Rec.709 H.264/HEVC, selected audio/pixel formats, known rotations, coded dimensions 16–4096, duration 0.2–600 seconds and 256 MiB source cap. The **overlappingCandidateGaps** are deliberately nonexclusive: a large HLG video counts as both large and non-bt709. The **workerPrimaryGates** name the first failure per item; neither ranking establishes an independently unlockable number or implementation effort.

**Limitations:** The catalogue's availability is last-observed, not a fresh filesystem check. A registered proxy/poster/frame receipt does not prove the cached bytes are present, authentic or playable. No original bytes are read, no FFprobe or local-file traversal is performed, and there is no OneDrive recall, job enqueue, derivative write, search or migration. The SQL does not select identifiers, paths, filenames, capture dates, camera labels, coordinates, captions, embeddings or identities.

## Windows verification before merge

From an existing repo clone with the usual approved *local* PostgreSQL environment variable and an already migrated database:

~~~powershell
cd "C:\Kod\codex\Photo Identity Indexer"
git fetch origin wi0210-readonly-archive-compatibility-survey
git switch -c wi0210-readonly-archive-compatibility-survey --track origin/wi0210-readonly-archive-compatibility-survey
# If that local branch already exists: git switch wi0210-readonly-archive-compatibility-survey; git pull --ff-only

dotnet test tests/PhotoIdentity.Integration.Tests --filter "FullyQualifiedName~VideoCompatibilitySurveyCommandTests"

$envName = "PHOTOIDENTITY_POSTGRES_CONNECTION_STRING"
$db = "photoidentity_catalogue"  # Verify this matches your active launcher catalogue.
if ([string]::IsNullOrWhiteSpace([Environment]::GetEnvironmentVariable($envName))) {
    throw "Approved local PostgreSQL environment variable is not set."
}
$outDir = Join-Path $env:LOCALAPPDATA "PhotoIdentity\diagnostics"
New-Item -ItemType Directory -Path $outDir -Force | Out-Null
$outFile = Join-Path $outDir "wi0210-compatibility-inventory.json"
dotnet run --project src/PhotoIdentity.Cli -- video compatibility survey --postgres-connection-env $envName --database $db | Set-Content -LiteralPath $outFile -Encoding utf8
$report = Get-Content -LiteralPath $outFile -Raw | ConvertFrom-Json
$report | Select-Object protocol, readOnly, currentNonExcludedVideoCount, excludedCurrentVideoCount, originalBytesOpened, filesystemAvailabilityChecked, oneDriveHydrationRequested, jobsEnqueued
$report.overlappingCandidateGaps | Select-Object -First 10 | Format-Table reason, affected
$report.workerPrimaryGates | Format-Table reason, count
$report.stages | Format-Table stage, state, count
~~~

Keep the report in the local diagnostics folder, outside the repo. Aggregates may still expose rare combinations of private formats, so share only manually reviewed coarse counts. Existing sources and OneDrive Files On-Demand states should not change.

## Checkpoint 2 decision gate

1. Review actual current counts, unknown metadata and cached availability separately. The 2026-10-14 18-file sample is **not** representative of the full archive.
2. Compare two or three candidate gaps for real-user usefulness, overlapping limitations, effort and approved already-local test media. Gap counts are upper bounds, not a promise of unlocks.
3. Maintainer explicitly approves **one** selected input class and target stages (poster, retained playable proxy, and/or timestamped frame evidence).
4. Then change only the necessary policy/profile, using a few approved real local originals and focused revision/exclusion/scratch/colour and photo regressions. Leave unsupported/HDR formats explicit and do not invoke WI-0207 hydration.

**Not yet verified:** Windows CI for the branch, an actual operator-host census, a selected compatibility class, any new real-media processing or altered admission policy. The work item remains in progress rather than completed.

See [WI-0210](../delivery/work-items/WI-0210-archive-driven-video-compatibility-expansion.md) and [staged matrix](video-compatibility-matrix.md).
