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

## Windows invocation

Use a locally approved CLIP ONNX dual encoder, its exact vocabulary
JSON and merges TXT, and their SHA-256 digests. **Do not** paste
the queries, manifest, original paths or vectors in PRs, test
attachments or chats. Ensure your private JSON file and CLIP files
are ordinary local files (UNC, symlink/reparse files **or any
symlink/reparse parent directory** are refused).

~~~powershell
git switch main
git pull --ff-only origin main

# Use the already-configured loopback PostgreSQL environment variable.
# Example only: replace with the variable name holding your actual
# maintained local connection string, never the connection string itself.
$postgresEnv = "PhotoIdentity__Postgres__ConnectionString"
$database = "photoidentity"

$judgments = Join-Path $env:LOCALAPPDATA "PhotoIdentity\PrivateEvaluation\live-judgments.json"
# Offline hash checks; does not contact Ollama unless -VerifyOllama is set.
$inference = .\Get-PhotoIdentityApprovedInference.ps1
$clipModel = $inference.ClipModelPath
$vocab = $inference.ClipVocabularyPath
$merges = $inference.ClipMergesPath

$evaluationArgs = @(
    "run", "--project", "src/PhotoIdentity.Cli", "-c", "Release", "--",
    "video", "frames", "evaluate-live",
    "--postgres-connection-env", $postgresEnv,
    "--database", $database,
    "--judgments", $judgments,
    "--clip-model", $clipModel,
    "--clip-model-sha256", $inference.ClipModelSha256,
    "--tokenizer-vocab", $vocab,
    "--tokenizer-vocab-sha256", $inference.ClipVocabularySha256,
    "--tokenizer-merges", $merges,
    "--tokenizer-merges-sha256", $inference.ClipMergesSha256,
    "--frame-top-k", "2", "--rank-k", "5"
)
dotnet @evaluationArgs
"Exit code: $LASTEXITCODE"
~~~

The digest checks detect changes relative to **operator-provided
hashes**, not independently approve the files. Compare against your
known-good local digest before choosing them. Use the same CLIP model
used for the already published video frames; selecting a different
model, even with a correctly calculated hash, is refused against
stored provenance. No Azure bridge is needed.

The JSON report contains only bounded numeric counts, aggregate
metrics and timing components `modelPreflightWallMs`,
`textInferenceWallMs`, `snapshotReadWallMs` and
`evaluationWallMs`. These are **wall-time diagnostics**, not CPU,
GPU, energy consumption or Azure billing. The command explicitly
reports a loopback PostgreSQL read and zero external network requests.
PostgreSQL stores the frame vectors and the CLI temporarily holds
them in memory; no vectors or query text are returned by stdout.

## Verification and remaining acceptance

The PR's xUnit tests use synthetic two-dimensional embeddings
with disposable PostgreSQL, validating current complete selection,
matching stored CLIP digest, unsupported/stale and excluded rejection,
duplicate/invalid private labels and strict flag parsing. They do
**not** require actual ONNX model files or videos. Windows GitHub CI
is the first integration gate.

An actual relevance study **requires at least two separately published
current FrameEvidence jobs from different real videos**, with human
judgments and a compatible local CLIP model. The maintainer's
accepted checkpoint 8 audit is of **one** job, so it is *insufficient*
for this study. Do not repeat that successful audit solely for
PR review. This new command cannot generate additional frame jobs;
only a separately authorized and approved selected one-shot can.

For a consolidated local study after the development series, record
a representative set of queries/videos and metrics and the four
wall-time fields, without pasting private input. Correlate results
with separately measured CPU/GPU and Azure inference costs, sampling
missed-scene rates, rotated/low-light cases and interruption safety.
These full workitem criteria **remain 0/4 accepted**, although
the retrieval-quality instrumentation and provenance checks are
implemented. No new PostgreSQL migration or automatic Azure fallback.
