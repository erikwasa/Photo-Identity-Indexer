# WI-0190 checkpoint 12 — private live-catalogue CLIP retrieval benchmark

Checkpoint 11 created a fully offline, hand-assembled vector manifest
evaluator. This checkpoint lets a maintainer instead evaluate **current
published FrameEvidence** batches directly from private, loopback
PostgreSQL v38. It uses the **same unchanged three diagnostic policies**
(max frame cosine, top-k frame mean, deduplicated normalized pooled
cosine), with numeric corpus-aggregate Hit@K, MRR@K, Recall@K,
MAP@K and nDCG@K. This is **not** a production search endpoint or
ranking policy, and does **not** change the standard video workflow.

The private file contains **only opaque FrameEvidence job UUIDs,
text queries and human judgments**; there are no exported caption or
embedding arrays. The CLI reads the approved model/tokenizer files
locally, verifies their explicitly supplied SHA-256 hashes and encodes
query text using the project's own CLIP ONNX tokenizer/encoder.
The stored frame batches must advertise that **same verified CLIP
model SHA-256**, model identifier and compatible image preprocessing
version. A tokenizer digest is pinned locally but **was not stored
in the existing v38 evidence**, so the operator must ensure these
are the original approved CLIP vocabulary and merges used by the
project. This is stricter than checkpoint 11's self-asserted model
digest but not an independent tokenizer provenance attestation.

The database connection **must** be loopback, the schema must be
already current, and the entire bounded read occurs inside a
**single repeatable-read, read-only PostgreSQL transaction**.
Each selected job must have succeeded and contain a complete current
published frame batch, from a latest unexcluded local-folder SDR
video revision with valid frame ordinals, timestamps, dimensions and
payload lengths. The command selects **only embedding bytea**, never
caption text, source locator, original bytes, published MP4 or
extracted JPEG. It rejects stale, excluded, unsupported, missing,
partial, cross-model or duplicate-source inputs. It neither writes
to PostgreSQL nor calls FFmpeg, Ollama or Azure, hydrates OneDrive,
starts workers, or creates any video job.

A successful report is **point-in-time**, not a guarantee that the
catalogue stays unchanged after its transaction commits.

## Private judgments

Save a physical local file outside Git, OneDrive and any cloud
synced directory, e.g. under `$env:LOCALAPPDATA`. This example
is **synthetic**: substitute FrameEvidence **job UUIDs**, not source
revision UUIDs, proxy job UUIDs, paths or filenames. The UUIDs must
identify at least two **different video assets**. Every query needs
a human-judged relevant subset of those job UUIDs. The text itself
never appears in stdout, but is present in the private JSON input
and briefly in local process memory.

~~~json
{
  "protocol": "wi0190-live-retrieval-judgments-v1",
  "jobIds": [
    "00000000-0000-0000-0000-000000000001",
    "00000000-0000-0000-0000-000000000002"
  ],
  "queries": [
    {
      "queryId": "10000000-0000-0000-0000-000000000001",
      "text": "a bright outdoor scene",
      "relevantJobIds": [
        "00000000-0000-0000-0000-000000000002"
      ]
    }
  ]
}
~~~

Limits: 2–32 different video assets and selected current batches,
1–16 nonempty queries up to 200 characters, at most 24 stored frames
per clip and 2,048 vector dimensions, max 64 KiB judgments file,
explicit frame-top-k 1–24 and ranking-k 1–32. Relevance should be
**independently and comprehensively judged** for the evaluated
candidate corpus: if a query is relevant to another unmarked video,
metrics will be misleading. Binary relevance makes a binary nDCG
rather than an ordinal human rating. Small or cherry-picked test
sets are not representative acceptance evidence.

## Maintained-host baseline (2026-11-02)

Two different approved local SDR videos now have four persisted
current FrameEvidence rows each. Both independent read-only frame
audits and the two-video acceptance report exited 0; the latter
verified eight frames, 16,996 caption/vector payload bytes,
and 266,321 ms observed execution wall time. Human frame-by-frame
caption review judged 7/8 accurate, with one low-visibility
invented held-object claim. **The separate local retrieval benchmark
has now also passed on this exact small two-video corpus**; see
the observed numeric results below. These tests do not establish
representative retrieval quality across a larger library.

| Private video label | Current FrameEvidence job UUID | Frames |
| --- | --- | ---: |
| Run 1 | Look up locally in Step 2 | 4 |
| Run 2 | Look up locally in Step 2 | 4 |

Use the FrameEvidence UUIDs above, **not the PlayableProxy IDs**.
This two-video cohort is a limited first retrieval baseline,
not representative quality acceptance.

## Maintainer-verified live retrieval evidence (2026-11-02)

The operator independently inspected both already-published local
videos, wrote two **private distinct** visual-content query phrases,
and designated one different relevant video for each query.
The approved WI-0126 local CLIP package and current
photoidentity_main PostgreSQL v38 evidence were used.
**video frames evaluate-live exited 0** and returned:
clips=2, queries=2, relevantJudgments=2, frameVectors=8,
embeddingDimensions=512, frameTopK=2, rankingK=2,
modelDigestMatched=true, readOnly=true, currentSnapshot=true,
localTextInference=true, humanJudgments=true,
loopbackPostgresReadPerformed=true and
externalNetworkRequestsMade=false. No original, OneDrive
hydration, media decode, Azure/Ollama request, job enqueue,
schema change or caption/vector/query disclosure occurred.

| Retrieval policy | Hit@2 | MRR@2 | Recall@2 | MAP@2 | nDCG@2 |
| --- | ---: | ---: | ---: | ---: | ---: |
| Maximum frame cosine | 1.00 | 1.00 | 1.00 | 1.00 | 1.00 |
| Top-2 frame mean | 1.00 | 1.00 | 1.00 | 1.00 | 1.00 |
| Deduplicated normalized pooled cosine | 1.00 | 1.00 | 1.00 | 1.00 | 1.00 |

With one independently relevant video per query,
MRR@2=1.00 means both relevant videos ranked first under
all three policies. At two candidates Hit@2 and Recall@2
are not discriminating. The test does not establish which
strategy is preferable or support broad quality claims.

Phase measurements: model preflight 608 ms, local text
inference 17,320 ms, repeatable-read PostgreSQL snapshot
276 ms and scoring 27 ms. Total of measured phases:
18,231 ms. These are wall times, **not** CPU/GPU time or
Azure cost. Private queries, exact job IDs, original source
paths and judgment manifests remain local-only. The success
is now accepted as the **small-corpus live retrieval baseline**;
do not repeat it solely for this work-item checkpoint.

## Windows maintained-host live retrieval — exact steps

### Step 1 — Independently choose queries and relevance labels

Review the two already-published local MP4 proxies, which were
previously used for human caption review; no original-media or
OneDrive-only file is needed. Write at least two *different*
short text queries that describe **actually visible sampled-frame
content**, preferably one distinctive to each video. Do not
assume sound, invisible movement, identity or event type.

For **every query**, independently judge relevance against
**both** videos: Run 1, Run 2, or both if genuinely relevant.
A query must have at least one relevant video; if neither
matches, choose a different query rather than fabricating a
label. The CLIP model must not supply its own ground truth.
Keep the real queries, filenames, descriptions and private
judgments out of repository files, PRs and chat.

### Step 2 — Create a physical private JSON manifest (PowerShell)

Run the following **read-only SQL locally** against
photoidentity_main before choosing the two FrameEvidence job IDs.
The result shows opaque frame-job identities and their underlying
asset identities, with no source paths or caption text. Select
the two previously audited four-frame jobs, and confirm that
their asset IDs differ; do not choose the PlayableProxy job IDs.

~~~sql
SELECT
    b.job_id AS frame_job_id,
    revision.asset_id,
    j.status,
    b.frame_count,
    MIN(f.timestamp_ms) AS first_frame_ms,
    MAX(f.timestamp_ms) AS last_frame_ms,
    b.published_at_utc
FROM video_frame_evidence_batches b
JOIN video_processing_jobs j ON j.id = b.job_id
JOIN asset_revisions revision ON revision.id = b.asset_revision_id
JOIN video_frame_evidence_frames f ON f.job_id = b.job_id
WHERE j.derivative_kind = 'FrameEvidence'
GROUP BY b.job_id, revision.asset_id, j.status,
         b.frame_count, b.published_at_utc
ORDER BY b.published_at_utc DESC
LIMIT 12;
~~~

Use those two locally copied UUIDs in PowerShell.
Run 1 has four timestamps beginning near 6,171 ms and ending
near 43,196 ms; Run 2 begins near 5,186 ms and ends near
36,300 ms. These numeric timestamps can help identify the
right two published jobs without printing original media names.

Run from the current merged main repository and use the real
maintainer-local evaluation directory under LocalAppData.
Read-Host accepts the query text locally and no query is
printed by the evaluation command.

~~~powershell
cd "C:\Kod\codex\Photo Identity Indexer"
git switch main
git pull --ff-only origin main

$ErrorActionPreference = "Stop"
$run1Job = (Read-Host "Run 1 FrameEvidence job UUID from local SQL").Trim()
$run2Job = (Read-Host "Run 2 FrameEvidence job UUID from local SQL").Trim()
[guid]$g1 = [guid]::Empty
[guid]$g2 = [guid]::Empty
if (-not [guid]::TryParse($run1Job, [ref]$g1) -or
    -not [guid]::TryParse($run2Job, [ref]$g2) -or
    $g1 -eq [guid]::Empty -or $g2 -eq [guid]::Empty -or $g1 -eq $g2) {
    throw "Select two different valid FrameEvidence job UUIDs from local SQL."
}
$privateDir = Join-Path $env:LOCALAPPDATA "PhotoIdentity\PrivateEvaluation"
New-Item -ItemType Directory -Path $privateDir -Force | Out-Null
$judgments = Join-Path $privateDir "wi0190-live-retrieval-judgments.json"

$query1 = Read-Host "Private visible-content search phrase 1"
$query2 = Read-Host "Different private visible-content search phrase 2"
if ([string]::IsNullOrWhiteSpace($query1) -or
    [string]::IsNullOrWhiteSpace($query2) -or
    $query1.Length -gt 200 -or $query2.Length -gt 200 -or
    $query1 -ceq $query2) {
    throw "Enter two different nonempty queries of at most 200 characters."
}

function Get-RelevantJobs {
    param([string]$Question)
    switch ((Read-Host $Question).Trim().ToLowerInvariant()) {
        "1"    { return $run1Job }
        "2"    { return $run2Job }
        "both" { return $run1Job, $run2Job }
        default { throw "Select 1, 2, or both after viewing BOTH videos." }
    }
}
$relevant1 = @(Get-RelevantJobs "Relevant to query 1: 1 / 2 / both?")
$relevant2 = @(Get-RelevantJobs "Relevant to query 2: 1 / 2 / both?")

$judgmentData = @{
    protocol = "wi0190-live-retrieval-judgments-v1"
    jobIds = @($run1Job, $run2Job)
    queries = @(
        @{
            queryId = [guid]::NewGuid().ToString()
            text = $query1
            relevantJobIds = $relevant1
        },
        @{
            queryId = [guid]::NewGuid().ToString()
            text = $query2
            relevantJobIds = $relevant2
        }
    )
}
[IO.File]::WriteAllText(
    $judgments,
    ($judgmentData | ConvertTo-Json -Depth 8),
    [Text.UTF8Encoding]::new($false)
)
Write-Host "Private live retrieval manifest created; text not printed."
~~~

The tool accepts 2–32 different video assets, 1–16 queries
(maximum 200 characters each), and a local regular manifest
no larger than 64 KiB. UNC files and reparse/symlink paths
(including parent directories) are rejected. Do not substitute
the synthetic illustrative JSON above for real judgments.

### Step 3 — Resolve the real maintained PostgreSQL connection

Your private launcher uses the name
PHOTOIDENTITY_POSTGRES_CONNECTION_STRING; the actual v38
database is photoidentity_main. Supply the **environment
variable name**, never the secret connection string.
The CLI needs it in its inherited process environment.

~~~powershell
$launcherPath = Join-Path $env:LOCALAPPDATA "PhotoIdentity\launcher.json"
$launcher = Get-Content -LiteralPath $launcherPath -Raw | ConvertFrom-Json
$postgresEnv = [string]$launcher.postgresConnectionEnvironmentVariable
$database = "photoidentity_main"

$conn = [Environment]::GetEnvironmentVariable($postgresEnv, "Process")
if ([string]::IsNullOrWhiteSpace($conn)) {
    foreach ($scope in @("User", "Machine")) {
        $conn = [Environment]::GetEnvironmentVariable($postgresEnv, $scope)
        if (-not [string]::IsNullOrWhiteSpace($conn)) { break }
    }
}
if ([string]::IsNullOrWhiteSpace($conn)) {
    throw "Approved local PostgreSQL connection variable is missing."
}
[Environment]::SetEnvironmentVariable($postgresEnv, $conn, "Process")
Remove-Variable conn -ErrorAction SilentlyContinue
~~~

### Step 4 — Reuse the verified, pinned WI-0126 CLIP package

The local CLIP model, vocabulary and merges must be the
**same approved installation** used to create the eight
stored embeddings. The approved ONNX SHA-256 is checked
against the known WI-0126 digest and the stored v38 model
provenance; the catalogue does **not** contain the tokenizer
hashes, so review their installation separately.

~~~powershell
$clipDir = Join-Path $env:LOCALAPPDATA "PhotoIdentity\Models\WI-0126\clip-vit-base-patch32-b318363"
$clipModel = Join-Path $clipDir "model.onnx"
$vocab = Join-Path $clipDir "vocab.json"
$merges = Join-Path $clipDir "merges.txt"
foreach ($file in @($clipModel, $vocab, $merges)) {
    if (-not (Test-Path -LiteralPath $file -PathType Leaf)) {
        throw "Approved WI-0126 CLIP file is missing."
    }
}
$expectedClipSha = "42b5dac8f0a73e45b1626ae7a69b41a4bc111c6fe1edafcceda8c2a5506dc6db"
$clipSha = (Get-FileHash -LiteralPath $clipModel -Algorithm SHA256).Hash.ToLowerInvariant()
if ($clipSha -cne $expectedClipSha) {
    throw "CLIP model does not match the approved WI-0126 version."
}
$vocabSha = (Get-FileHash -LiteralPath $vocab -Algorithm SHA256).Hash.ToLowerInvariant()
$mergesSha = (Get-FileHash -LiteralPath $merges -Algorithm SHA256).Hash.ToLowerInvariant()
~~~

When the **optional centralized inference settings have
already been approved** in launcher.json, prefer
Get-PhotoIdentityApprovedInference.ps1 to resolve the
pinned model, vocabulary and merges paths and all three
digests. Do **not** invent or add unreviewed launcher keys
merely to run this test. The CLI refuses missing, invalid
or linked physical model files.

### Step 5 — Execute read-only current-v38 live retrieval

Use frame-top-k=2 and rank-k=2 for the two-video
baseline. The diagnostic ranking policies still use
their reviewed definitions; no production search policy
or automatic media processing is activated.

~~~powershell
$evaluationArgs = @(
    "run", "--project", "src/PhotoIdentity.Cli", "-c", "Release", "--",
    "video", "frames", "evaluate-live",
    "--postgres-connection-env", $postgresEnv,
    "--database", $database,
    "--judgments", $judgments,
    "--clip-model", $clipModel,
    "--clip-model-sha256", $clipSha,
    "--tokenizer-vocab", $vocab,
    "--tokenizer-vocab-sha256", $vocabSha,
    "--tokenizer-merges", $merges,
    "--tokenizer-merges-sha256", $mergesSha,
    "--frame-top-k", "2",
    "--rank-k", "2"
)

$retrievalOutput = dotnet @evaluationArgs
$retrievalExit = $LASTEXITCODE
$retrievalOutput
Write-Host "Exit code: $retrievalExit"
if ($retrievalExit -ne 0) {
    throw "Current live retrieval check failed or was refused."
}
~~~

**Expected gates on success, not yet maintainer-tested:**
exit 0; protocol wi0190-live-retrieval-evaluation-v1;
readOnly=true, currentSnapshot=true,
localTextInference=true, modelDigestMatched=true,
humanJudgments=true, loopbackPostgresReadPerformed=true,
externalNetworkRequestsMade=false, originalsOpened=false,
oneDriveHydrationRequested=false, mediaDecoded=false,
captionsReturned=false, embeddingsReturned=false,
rawQueryTextReturned=false, videoJobsEnqueued=false,
schemaChanged=false. A failed currentness, exclusion,
provenance, input or vector check should be investigated,
**not** bypassed by rewriting catalogue state.

This CLI performs **local CLIP text encoding**, reads only
stored frame vectors in one repeatable-read **READ ONLY**
loopback PostgreSQL snapshot, and returns numeric aggregates.
No FFmpeg, Ollama, Azure bridge, original/proxy decoding,
OneDrive hydration, frame processing or network AI service
is needed.

### Step 6 — Interpret metrics without overclaiming

Compare the result's three ranking policies: max-frame cosine,
top-k frame mean, and deduplicated normalized pooled cosine.
Record numeric Hit@K, MRR@K, Recall@K, MAP@K and nDCG@K
against the *human-labeled* relevance sets. These metrics are
**not caption accuracy**. With only two candidate videos,
Hit@2 can be trivial: emphasize top-1 ordering and MRR and
record the queries' discriminatory limitations. The result
contains aggregate metrics, not per-query private video
rankings; do not infer an individual query's rank from an
aggregate-only report.

Record modelPreflightWallMs, textInferenceWallMs,
snapshotReadWallMs and evaluationWallMs as four different
*wall times*, not full CPU/GPU resource costs. Report back
the sanitized numeric JSON and exit code only; retain
the real query text, relevance judgments, source names,
and input manifest on the maintainer host. Passing a
two-clip study is a useful functional demonstration, not
complete WI-0190 relevance or representative acceptance.

## Verification and remaining acceptance

The PR's xUnit tests use synthetic two-dimensional embeddings
with disposable PostgreSQL, validating current complete selection,
matching stored CLIP digest, unsupported/stale and excluded rejection,
duplicate/invalid private labels and strict flag parsing. They do
**not** require actual ONNX model files or videos. Windows GitHub CI
is the first integration gate.

Two distinct real-video four-frame FrameEvidence jobs **passed**
the independent current-v38 audit and the two-video acceptance
report; the maintainer has **also completed** a separate real
local CLIP retrieval benchmark with two independent human
queries and first-ranked relevant results under all three
policies. Keep this as a functional, two-video baseline,
not general representative-quality evidence. The guide
above remains reproducible for additional future clips and
judgments without republishing already-current frames.

For a consolidated local study after the development series, record
a representative set of queries/videos and metrics and the four
wall-time fields, without pasting private input. Correlate results
with separately measured CPU/GPU and Azure inference costs, sampling
missed-scene rates, rotated/low-light cases and interruption safety.
These full workitem criteria **remain 0/4 accepted**, although
the retrieval-quality instrumentation and provenance checks are
implemented. No new PostgreSQL migration or automatic Azure fallback.
