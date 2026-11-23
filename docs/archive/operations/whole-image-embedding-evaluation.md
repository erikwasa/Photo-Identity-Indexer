# Whole-image embedding evaluation

This runbook covers the bounded WI-0127 experiment for deciding whether whole-image embeddings add enough retrieval and Creative Collection value to justify production vector storage or indexing.

The experiment is deliberately **read-only**:

- it reads one saved Smart Collection from PostgreSQL;
- it embeds only existing durable review proxies;
- it reuses the pinned CLIP ViT-B/32 model/tokenizer assets already used for WI-0126;
- it keeps image embeddings in memory only;
- it uses exact cosine comparisons, not pgvector or ANN;
- it does not write vectors, tags or any other evidence to the catalogue; and
- the optional visual review output stays local/private.

WI-0126 rejected fixed zero-shot controlled-vocabulary tags because the labels were not reliable enough. WI-0127 asks a different question: whether the same image/text representation is useful when used directly for free-text retrieval, nearest-image retrieval and continuous visual-diversity evidence without converting the result into human-readable tags.

## Embedding evidence contract

`PhotoEmbeddingEvidence` is explicitly derived evidence tied to:

- immutable asset revision id;
- model id;
- exact model SHA-256;
- image preprocessing version;
- vector encoding (`float32-l2-normalized-v1`); and
- embedding dimension.

The experiment never treats that evidence as canonical photo metadata and never persists it.

## Prepare the model

Use the pinned helper introduced for WI-0126. If the files are already present and verified, the helper reuses them.

~~~powershell
$clip = .\models\Get-WI0126ClipModel.ps1
~~~

The expected model is the full float32 CLIP graph because the experiment requires both `image_embeds` and `text_embeds` outputs.

## Choose useful retrieval queries

Use the same representative saved Smart Collection used for other M26 review where practical. Supply 3–5 short natural-language queries describing things you know are actually present in that private collection.

Good queries are concrete enough to judge visually but do not need to match a fixed taxonomy, for example:

~~~text
people outdoors
food on a table
children playing
a snowy landscape
a car or other vehicle
~~~

Do not add a query merely because it appears in an example. The useful test is whether the model retrieves photos that match *your known collection contents*.

Query text is included only in the private local review page. It is intentionally omitted from the aggregate JSON report.

## Run the bounded experiment

The current maintained review-proxy profile is `jpeg-1600-q78`. Set the connection string through an environment variable rather than placing it on the command line.

~~~powershell
$env:PHOTOIDENTITY_EMBEDDING_TEST = $env:PHOTOIDENTITY_POSTGRES_CONNECTION_STRING

if ([string]::IsNullOrWhiteSpace($env:PHOTOIDENTITY_EMBEDDING_TEST)) {
    $env:PHOTOIDENTITY_EMBEDDING_TEST =
        [Environment]::GetEnvironmentVariable(
            "PHOTOIDENTITY_POSTGRES_CONNECTION_STRING",
            "User")
}

$proxyRoot = Join-Path $env:LOCALAPPDATA "PhotoIdentity\review-proxies"
$collectionId = "<saved Smart Collection GUID>"

dotnet run --project src/PhotoIdentity.Cli -c Release -- image-embeddings evaluate `
  --postgres-connection-env PHOTOIDENTITY_EMBEDDING_TEST `
  --collection $collectionId `
  --proxy-root $proxyRoot `
  --proxy-profile "jpeg-1600-q78" `
  --model $clip.ModelPath `
  --tokenizer-vocab $clip.TokenizerVocabularyPath `
  --tokenizer-merges $clip.TokenizerMergesPath `
  --query "people outdoors" `
  --query "food on a table" `
  --query "<another concept known to exist>" `
  --target-count 50 `
  --max-candidates 200 `
  --retrieval-count 8 `
  --similar-seeds 4 `
  --neighbors-per-seed 5 `
  --report artifacts/wi-0127-image-embedding-report.json `
  --review-output "$env:TEMP\PhotoIdentity\WI-0127-review"
~~~

Replace the example queries with concepts known to exist in the selected collection.

Open the local review page:

~~~powershell
Start-Process "$env:TEMP\PhotoIdentity\WI-0127-review\index.html"
~~~

## What the evaluator measures

### Runtime and storage

For every readable proxy it records local embedding runtime. A 512-dimensional float32 vector requires 2,048 raw bytes before database/index overhead. The report also projects raw vector bytes for 100,000 photos so storage cost is visible before any persistence design is considered.

### Semantic retrieval

Each supplied text query is embedded once. Every image embedding is then compared using exact cosine similarity and the top results are shown in the private HTML page.

The aggregate report records counts and similarity statistics only. Query strings, revision ids, filenames and paths are omitted.

### Similar-photo retrieval

The evaluator chooses a deterministic spread of seed photos from the ordinary metadata-only Creative selection and finds their nearest image embeddings by exact cosine comparison.

Use the review page to judge whether the nearest results are genuinely visually or semantically related rather than merely sharing broad colour/layout characteristics.

### Creative Collection diversity

The ordinary metadata-only selector is compared with an experimental `m26-image-embedding-diversity-v1` variant. The variant leaves all existing eligibility, Prefer/Avoid and metadata scoring intact, then adds a bounded penalty when a candidate is very similar to an already-selected embedding.

This is experiment-only. Production selection remains unchanged.

The report records:

- selection overlap and replacement count;
- mean pairwise cosine similarity for both selections; and
- p95 pairwise cosine similarity for both selections.

Lower pairwise similarity is evidence that the embedding variant is selecting more visually diverse material, but it is not sufficient by itself: the visual review must confirm that replacements are useful rather than merely different.

## Exact search boundary

The evaluator performs direct vector comparisons over the bounded candidate sample. `ExactVectorSearch` must be `true` and `AnnIndexUsed` must be `false`.

Do not add pgvector, HNSW, IVF or another ANN dependency as part of this experiment. If exact search is useful but later proves too slow at production scale, indexing is a separate design decision supported by measured requirements.

## Maintainer review

Judge three things independently:

1. **Semantic retrieval:** for each query, are most top results genuinely relevant?
2. **Similar-photo retrieval:** are nearest neighbors meaningfully related to the seed?
3. **Creative diversity:** are embedding-only replacements useful and less repetitive, rather than simply different?

Also note obvious failure modes such as person identity dominating scene similarity, colour/style dominating semantics, or retrieval returning confidently unrelated photos.

After review, retain only privacy-safe aggregate findings in Git. Do not commit the HTML page or copied proxy images.

A go decision should still define the smallest production boundary and storage/index requirements. A no-go is valid if retrieval or diversity quality does not materially beat the cheaper metadata/manual-tag approach.
