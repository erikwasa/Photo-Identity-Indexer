# Semantic + caption photo search evaluation

WI-0162 productizes only the successful part of WI-0127: **natural-language text-to-photo retrieval**. It combines that local visual signal with displayable WI-0128 generated captions and lets a search result be frozen as an explicit WI-0145 slideshow collection.

This runbook is also the acceptance measurement. WI-0127 used 185 photos and at most 8 text queries. The WI-0162 review therefore uses materially more indexed photos, a broader query suite and query-level relevance judgments.

## Product boundary

The search implementation deliberately does **not** revive the rejected WI-0127 directions:

- no similar-photo product search;
- no embedding-driven Creative Collection diversity;
- no inferred semantic tags;
- no pgvector, HNSW, IVF or other ANN dependency at this stage.

Semantic image embeddings are versioned, regenerable derived evidence tied to immutable revision, model SHA-256, preprocessing version and vector encoding. PostgreSQL stores the 512-dimensional float32 vectors in ordinary `real[]` rows. The API loads the current model-version vectors into memory and performs exact cosine search. This keeps the first production search boundary measurable before selecting dedicated vector infrastructure.

Caption retrieval uses only displayable WI-0128 evidence: stored caption rows with content and no risk flags. Search never triggers caption generation.

Combined results use reciprocal-rank fusion. Semantic cosine and caption text scores remain independently visible instead of being presented as if they share one calibrated numeric scale.

## Supported query language

The archive-scale WI-0162 evaluation measured the pinned CLIP path on the same paired English and Swedish concepts. English visual search was strong while raw Swedish text through the same CLIP encoder was not consistently useful.

The product decision is therefore:

- **Visual/CLIP search is supported for English queries.**
- Swedish CLIP input is not a supported quality target for WI-0162.
- No translation layer or multilingual replacement model is added.
- Existing image embeddings remain valid; this decision does not require re-indexing the archive.
- Caption-only search can still use the language in which captions were generated.

This is an evidence-based scope decision, not a statement that Swedish text cannot technically be submitted to the encoder.

## Install the pinned local semantic model

Search does not download a model merely because the page is opened. Caption-only search remains available without CLIP.

To enable visual semantic indexing, use the pinned model helper:

~~~powershell
.\models\Get-SemanticSearchClipModel.ps1
~~~

The helper reuses the exact WI-0126/WI-0127 CLIP ViT-B/32 ONNX assets and verifies the pinned model SHA-256.

The normal default directory is:

~~~text
%LOCALAPPDATA%\PhotoIdentity\Models\WI-0126\clip-vit-base-patch32-b318363
~~~

A custom directory can be configured with `PhotoIdentity:SemanticSearch:ModelDirectory`.

Restart the application after installing the assets. The background semantic indexer then processes current revisions that already have durable review proxies. It never downloads an original merely to create a search embedding.

## Check index status

Open the Search page at `/search`. Its status shows:

- current photo count;
- indexed semantic-vector count;
- displayable caption count.

The corresponding API is:

~~~text
GET /api/photo-search/status
~~~

For formal WI-0162 review, wait until the representative indexed set is materially larger than WI-0127's 185 photos. The evaluator refuses a smaller sample by default.

Raw vector storage is reported directly. A 512-dimensional float32 vector is 2,048 raw bytes before PostgreSQL row/index overhead, matching the WI-0127 representation.

## Query-suite purpose

The repository contains a 24-query bilingual evaluation template:

~~~text
experiments/photo-search/wi-0162-query-suite-template.json
~~~

This file is **test/evaluation material only**. It is not runtime configuration, a production query dictionary or a list that constrains what operators may search for.

The bilingual pairs are intentionally retained so the measured English-versus-Swedish decision remains reproducible. A new query suite is not required for normal product use. Future search changes can reuse this suite for regression comparison or introduce a purpose-specific suite when a different behavior needs measurement.

For a private evaluation, copy the template to a local working location and replace or remove concepts that are not actually present in the archive sample. The suite should cover several of these categories:

- ordinary family-photo language;
- activities;
- objects;
- people-free visual scenes;
- seasons/weather;
- indoor/outdoor context.

Do not manufacture relevance merely to keep an example query. A query should describe something that can be judged against the private archive.

## Collect search evidence

Choose the URL of the running local application and a private output directory:

~~~powershell
$baseUrl = 'http://localhost:<port>'
$out = Join-Path $env:TEMP 'PhotoIdentity\WI-0162-search-review'
$querySuite = '<private path to edited query-suite.json>'

.\scripts\evaluate-photo-search.ps1 `
  -BaseUrl $baseUrl `
  -QuerySuite $querySuite `
  -TopK 10 `
  -OutputDirectory $out
~~~

The script runs every query separately in three modes:

1. `semantic` — visual CLIP evidence only;
2. `caption` — displayable generated captions only;
3. `combined` — reciprocal-rank fusion of both.

It writes local/private evidence:

- `wi-0162-search-status.json` — indexing/storage state;
- `wi-0162-search-results.json` — per-query latency, provenance and returned revision ids;
- `wi-0162-relevance.csv` — one row per query/mode/rank with a blank `Relevant` field.

These files can contain private query and revision information. **Do not commit them.**

## Judge relevance

Open `wi-0162-relevance.csv`. For each row, inspect the corresponding photo in the application and set `Relevant` to `yes` or `no`.

Judge the result against the actual natural-language query, not against whether the model score looks high. The goal is per-query precision evidence rather than an aggregate similarity distribution.

After every row is marked, summarize:

~~~powershell
.\scripts\evaluate-photo-search.ps1 `
  -OutputDirectory $out `
  -Summarize
~~~

This creates `wi-0162-summary.json` with:

- per-query precision@K;
- quality grouped by search mode and language;
- server/client query latency by mode/language;
- current/indexed/caption counts;
- embedding dimension, raw vector bytes and average embedding-generation runtime;
- exact-search/ANN flags.

## Measured WI-0162 language decision — 2026-09-25

The maintainer evaluated 24 paired English/Swedish queries at top-10 over a private archive snapshot with **10,585 indexed photos** and **517 displayable captions**.

Visual semantic precision@10 was:

- English: **107 / 120 relevant = 0.89**;
- Swedish: **35 / 120 relevant = 0.29**.

English semantic quality was strong across most evaluated concepts. Swedish quality varied sharply by concept, including several 0/10 cases, despite some strong queries such as summer and snow.

Average semantic server latency remained practical at this scale:

- English: about **270 ms**;
- Swedish: about **284 ms**.

The same evaluation showed caption retrieval contributing very little to this particular query suite: only three caption-only rows were returned, all Swedish, so combined aggregate quality matched semantic-only quality. Do not interpret the existence of 517 stored captions as evidence that current caption retrieval materially improves arbitrary natural-language searches; that behavior should be assessed separately when caption retrieval changes.

Based on the measured quality and maintainer preference, WI-0162 adopts **English-only supported Visual/CLIP queries** rather than adding translation or a multilingual semantic model.

## Product verification

After the measurement is satisfactory, verify the normal UI on desktop and phone:

1. Open **Search** from the primary navigation.
2. Run an English query in Visual + captions mode and inspect the Visual/Caption provenance badges.
3. Switch to Visual only and verify photos without generated captions can still appear.
4. Switch to Captions only and verify blocked/missing captions do not appear as caption evidence; use the language in which the available captions were generated.
5. Select a useful ordered result subset, give it a name and save it.
6. Save a second search as a different named collection.
7. Open `/slideshows` and launch both normal explicit slideshow collections.
8. Restart the application and confirm both collections still contain the same ordered revision membership.
9. Change a search query or allow new caption/embedding evidence to appear; verify the already-saved collections do not silently change.

The save boundary is the existing WI-0145 explicit photo-list model. A save captures immutable revision membership at that moment; it does not store a live search definition.
