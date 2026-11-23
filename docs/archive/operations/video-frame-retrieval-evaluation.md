# WI-0190 checkpoint 11 — offline frame retrieval quality evaluation

The project has a **diagnostic** CLIP comparison for sampled frame vectors
(max similarity, mean of top-k frame similarities, and deduplicated pooled
similarity). This checkpoint adds a **bounded, repeatable, offline**
relevance-ranking evaluation of those **three existing policies**.

No media, video proxy, original image, PostgreSQL, caption, FFmpeg,
Ollama, Azure, internet endpoint, hosted worker or application search
index is accessed. The evaluator reads **one explicit private JSON
manifest on the operator's own computer**, computes all results in
memory, and prints **only aggregate numeric metrics**. It does not
modify or select the later WI-0191 production video search policy.

## Manifest contract

The manifest is a local file containing precomputed CLIP image-frame
vectors, corresponding CLIP text-query vectors, and independent human
judgments of which clip UUIDs are relevant to each query. Do not
upload the manifest to GitHub or paste real vectors, clip identifiers,
query text, filenames, model secrets or source paths into a chat or PR.

This **fully synthetic two-dimensional example** is for parser and
metric verification only. Real CLIP embeddings from the existing
approved model are 512-dimensional in the accepted local checkpoint.
The digest below is an **illustrative placeholder**, not a verified
model hash.

~~~json
{
  "protocol": "wi0190-retrieval-manifest-v1",
  "clipModelSha256": "aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa",
  "imagePreprocessingVersion": "clip-image-preprocessing-v1",
  "textPreprocessingVersion": "clip-text-preprocessing-v1",
  "clips": [
    {
      "clipId": "00000000-0000-0000-0000-000000000001",
      "frameEmbeddings": [[1, 0], [-1, 0]]
    },
    {
      "clipId": "00000000-0000-0000-0000-000000000002",
      "frameEmbeddings": [[0.8, 0.6], [0.8, 0.6]]
    }
  ],
  "queries": [
    {
      "queryId": "10000000-0000-0000-0000-000000000001",
      "textEmbedding": [1, 0],
      "relevantClipIds": [
        "00000000-0000-0000-0000-000000000002"
      ]
    }
  ]
}
~~~

Use **opaque UUIDs only**, not names encoding people or locations.
The manifest fingerprint and preprocessing labels are operator
assertions: the evaluator does **not** authenticate the CLIP model or
tokenizer or prove that text and image vectors share one joint embedding
space. **Use one reviewed CLIP model and compatible preprocessing on
both sides.** Do not combine separately revised model spaces.

Limits: **2–32 clips**, **1–32 queries**, **1–24 frames per clip**,
1–2,048 matching nonzero finite vector dimensions, bounded top-k and
rank-k, and at most 16 MiB on disk. Each query must have one or more
unique judged-relevant clip UUIDs **within the evaluated corpus**;
corpus-wide relevance must be labeled sufficiently comprehensively for
metrics to be meaningful. Ambiguous or incomplete judgments make the
results optimistic or pessimistic and cannot be automatically detected.

## Windows invocation

After merging and retrieving updated main, create the private JSON file
locally. It may contain sensitive embeddings; place it in a protected
local directory, not in the repository, OneDrive, or cloud-synced
folders. This command cannot build or export real manifests on its
own; a separate vetted **read-only live-catalogue evaluation workflow**
is needed before the full acceptance study can be automated without
manually handling private vectors.

~~~powershell
git switch main
git pull --ff-only origin main

$manifest = Join-Path $env:LOCALAPPDATA "PhotoIdentity\PrivateEvaluation\retrieval.json"
$evalArgs = @(
    "run", "--project", "src/PhotoIdentity.Cli", "-c", "Release", "--",
    "video", "frames", "evaluate-retrieval",
    "--manifest", $manifest,
    "--frame-top-k", "3",
    "--rank-k", "5"
)
$result = dotnet @evalArgs
$exitCode = $LASTEXITCODE
$result
"Exit code: $exitCode"
~~~

The evaluator requires a **physical absolute local regular file** (on
Windows, network/UNC and reparse-point manifest paths are refused).
The only result includes the number of clips, queries, relevant
judgments, frame vectors, dimensions and ranking bounds, plus:
- `maximum`: max frame/query cosine similarity per clip.
- `topKMean`: mean cosine among the best frame-top-k.
- `deduplicatedPooled`: cosine to the normalized centroid of
  cosine-deduplicated frame vectors, with opposing directions handled
  conservatively.

Each contains **Hit@K** (fraction of queries with any judged relevant
clip in top K), **MRR@K** (mean reciprocal position of first relevant
clip), **Recall@K** (fraction of relevant clips retrieved), **MAP@K**
(average precision within K, denominator min(K, relevant clips)) and
**nDCG@K** (binary-relevance normalized discounted gain). Every score
is in [0,1], rounded to six decimals; larger is better **under the
provided judgments**, not necessarily on future data. Rank ties use
the opaque clip UUID ordering deterministically. If fewer clips than
rank K exist, the reported K is the actual clip count.

**Do not compare raw cosine scores from different CLIP models** or
infer frame/action recall, causal motion, sound, OCR correctness,
caption factuality or video-search usefulness from ranking metrics
alone. Retrieval ranking is measured against human judgments, not
verified truths. A small unrepresentative corpus cannot establish
the production policy.

No individual query, clip, judgment, model digest, vector or private
manifest path is returned. Invalid private JSON or oversize inputs
are rejected without echoing parser excerpts or file locations.
The input stays on disk until the operator safely removes it; this
command performs no automatic deletion.

## Verification and acceptance boundary

~~~powershell
dotnet test tests/PhotoIdentity.Core.Tests/PhotoIdentity.Core.Tests.csproj -c Release --filter "FullyQualifiedName~VideoFrameRetrievalEvaluationTests|FullyQualifiedName~VideoFrameRetrievalScoringTests"
dotnet test tests/PhotoIdentity.Integration.Tests/PhotoIdentity.Integration.Tests.csproj -c Release --filter "FullyQualifiedName~VideoFrameRetrievalEvaluationCommandTests"
dotnet run --project tools/PhotoIdentity.Docs -- validate
dotnet run --project tools/PhotoIdentity.Docs -- generate --check
~~~

Synthetic regression tests demonstrate policy disagreements, rank
cutoffs, relevance metrics, stable tie handling, invalid vectors and
identifiers, private-JSON secrecy and bounded file input. They do not
constitute human-quality evidence or measure CPU/GPU/Azure cost.
Existing WI-0190 maintained-host **two-frame publication and audit**
remain accepted and need not be rerun for this checkpoint.

**Still needed:** private current-PostgreSQL evaluation without
exporting vector data, local embedding generation for representative
text queries using the exact approved model, verified cross-modal
provenance, a sufficiently diverse human-judged corpus, and actual
cost/performance/scene coverage and Windows interruption testing.
Formal WI-0190 acceptance remains **0/4** pending these gates.
