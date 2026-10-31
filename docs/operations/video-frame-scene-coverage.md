# WI-0190 checkpoint 9 — private, read-only scene coverage measurement

The existing two-frame selected-one-shot, PostgreSQL v38 publication, and
checkpoint 8 batch audit do not prove that *important scenes* were sampled.
This command measures how many **operator-annotated** scenes are covered by
the actual saved frame timestamps. It also compares two **counterfactual**
sampling schedules: interval and scene-balanced, with the same frame budget.

**No scene detection is implemented by this command.** Scene cuts must be
annotated by a person reviewing the original clip in an already authorized
local viewer. The command does not open that viewer, original, proxy, captions,
model, vectors or filesystem cache. It reads only numeric frame ordinals,
timestamps and the existing audit's metadata predicates from a local current
PostgreSQL v38 batch. No OneDrive hydration, network inference, automatic
worker, DB migration or schema mutation is permitted.

## Windows procedure

After this PR's Windows CI succeeds and it is merged, use the existing
local PostgreSQL connection environment variable and an already published
FrameEvidence **job** UUID (not a proxy UUID). Only use human-reviewed cut
positions; the numeric example below is **illustrative**, not an assertion
about the maintained 12.3-second clip.

~~~powershell
git switch main
git pull --ff-only origin main

# Example ONLY: scenes begin at 0, 3000, 6000 and 9000 milliseconds.
# Replace with manually annotated cuts for the selected local clip.
$frameJobId = "PASTE_FRAME_EVIDENCE_JOB_UUID"
$cutsMs = "3000,6000,9000"

$coverageArgs = @(
    "run", "--project", "src/PhotoIdentity.Cli", "-c", "Release", "--",
    "video", "frames", "coverage",
    "--postgres-connection-env", "PHOTOIDENTITY_WI0190_CONN",
    "--database", "photoidentity_main",
    "--job-id", $frameJobId,
    "--scene-boundaries-ms", $cutsMs
)
$coverage = dotnet @coverageArgs
$coverageExit = $LASTEXITCODE
$coverage
"Exit code: $coverageExit"
~~~

Use `--scene-boundaries-ms none` for one operator-annotated scene. Cut positions
are integer **milliseconds** from the start of the clip, excluding 0 and
the final duration; they must be strictly increasing. At most 128 cuts
are permitted. The number of scenes is the number of cuts plus one;
scene indices start at **0**, and a frame precisely at a cut belongs to
the **new** scene. The CLI checks the cuts lie inside the published clip.
Only the operator can distinguish actual edits or material scene changes
from camera movement or temporary light changes.

## Interpretation and safety

A successful run reports `readOnly: true`, `current: true`,
`reasonCode: current-complete`, numeric `actual`,
`hypotheticalInterval` and `hypotheticalSceneBalanced` summaries,
and `observedPositionsMatchIntervalPlan`. Each summary contains the
number of sampled frames, total/covered/missed scenes, a percentage,
maximum frames in any scene, and zero-based missed-scene indices.

The two hypothetical plans **do not replace persisted frame evidence**,
perform decoding or claim better semantic retrieval performance. A plan may
produce fewer frames where very short scenes violate the minimum seek
spacing. Manual annotation is incomplete by nature: missed scenes outside
the annotation cannot be discovered by this command. A scene containing a
sampled timestamp may still be inaccurately captioned or be missed by CLIP
retrieval. This is therefore **sampling coverage**, not model correctness,
temporal understanding, action recognition or a production search-ranking
policy.

Failure (exit 1) returns a sanitized `reasonCode` without a coverage
summary if the job is stale, excluded, unsupported, partial or missing.
Invalid cut parameters or a wrong database/schema fail closed.
Every database read belongs to one PostgreSQL **repeatable-read read-only**
transaction; the complete-batch predicate is checked in the same snapshot
before timestamps are read. Nothing prints captions, vectors, filenames,
private roots, model IDs or PostgreSQL connection credentials.

## Acceptance worksheet (local/private)

For each representative **already-local** clip (short, multi-scene, long,
rotated and low-light), privately record: actual clip duration and cut
positions, missed scene indices, frame count, human caption accuracy,
query relevance under max/top-k/pooled CLIP, `executionWallMs`, frame
payload bytes from `video frames audit`, and approximate CPU/GPU/Azure
resource costs. Record whether a boundary is genuine editorial cut versus
a scene estimate. Report **only coarse anonymized aggregates** into
WI-0190; do not put exact private cuts, sources, captions, vectors,
video filenames, model endpoint secrets or personal data in GitHub.

Checkpoint 9 adds a repeatable quantitative measurement for the third
acceptance criterion. It does **not** implement a scene detector or
scene-based durable real-video production. Existing selected real-video
execution remains explicit at most two frames. No local media run is
necessary to confirm a synthetic CI test; a representative human
annotation and eventual end-to-end benchmark are still needed before
accepting WI-0190.

## Automated regression / schema safety

~~~powershell
dotnet test tests/PhotoIdentity.Integration.Tests/PhotoIdentity.Integration.Tests.csproj -c Release --filter "FullyQualifiedName~VideoFrameSceneCoverageTests|FullyQualifiedName~VideoFrameEvidenceAuditTests"
dotnet run --project tools/PhotoIdentity.Docs -- validate
dotnet run --project tools/PhotoIdentity.Docs -- generate --check
~~~

The pure tests cover strict annotations, cut-boundary assignment,
short/long/many-scene limits, duplicate timestamps and policy failures.
The disposable PostgreSQL test also checks the successful two-frame
sample and stale/latest-revision and exclusion-purge fail-closed behavior,
while the worker fixture tests cancellation requested mid-production and
a new photo sync request preempting publication,
without any media, FFmpeg or model execution. As with prior tests, the
database portion needs `PHOTOIDENTITY_TEST_POSTGRES_ADMIN_CONNECTION_STRING`
and is **skipped if absent**. Windows CI must pass before merging.
