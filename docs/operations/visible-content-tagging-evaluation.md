# Visible-content tagging evaluation

WI-0126 evaluates whether local whole-photo semantic evidence improves Creative Collections enough to justify a production automatic-tagging pipeline. The first slice is deliberately read-only and experiment-only: it does **not** write automatic tags or model evidence into the catalogue.

## Experiment boundary

The evaluator:

- reads one existing saved Smart Collection from PostgreSQL;
- derives the same 30-minute-by-default moment catalogue and Balanced Creative context candidates used by M26;
- scores a bounded number of existing durable review proxies with a local CLIP-compatible ONNX model;
- assigns the top bounded visible-content concepts in memory only;
- compares the existing Creative selector against the experimental 'm26-visible-content-diversity-v1' bonus;
- optionally compares the same photos through safely accessible originals when the operator explicitly requests it; and
- writes only a privacy-safe aggregate JSON report when '--report' is supplied.

The experiment never changes manual photo tags, Smart Collection membership, people, Places, presentation preferences, slideshow history or source state.

## Controlled vocabulary

The checked-in experiment vocabulary is:

'experiments/visible-content/wi-0126-vocabulary-v1.json'

It currently contains a deliberately small family-archive vocabulary covering examples such as birthday, cake, meals, pets, playgrounds, snow, beach/water activity, sports, vehicles, indoor/outdoor scenes, nature, babies, holidays, school and weddings.

The vocabulary file has its own version and prompt-template version. The evaluator hashes the exact vocabulary bytes into the report.

## Model/tokenizer contract

The first evaluator targets a CLIP-style zero-shot image/text model with the standard ONNX inputs:

- 'input_ids' — int64, one 77-token row per concept prompt;
- 'pixel_values' — float32, one RGB 3 x 224 x 224 image;
- optional 'attention_mask' — int64;
- optional 'position_ids' — int64.

The model must expose float32 'logits_per_image' with shape 1 x concept_count.

Use the tokenizer 'vocab.json' and 'merges.txt' from the same CLIP model family. The evaluator implements the OpenAI CLIP byte-level BPE path locally and does not download tokenizer/model assets at runtime.

The image path is versioned as 'clip-rgb-opencv-cubic-shortest-edge-center-crop-224-v1': shortest edge resized to 224 using cubic interpolation, centered 224-pixel crop, RGB conversion, 1/255 scaling and CLIP mean/std normalization.

A practical starting candidate is an ONNX export of OpenAI CLIP ViT-B/32. Model assets are intentionally **not** committed or packaged by Photo Identity in this experiment. Keep the model/tokenizer files outside the repository and review their upstream licence/redistribution terms before any later production packaging decision. The report records SHA-256 hashes of the exact model, tokenizer vocabulary and merges used.

## Run the bounded experiment

Use a representative saved Smart Collection that has more candidates than the requested Creative target and existing durable review proxies.

~~~powershell
$env:PHOTOIDENTITY_SEMANTIC_TEST = "<current PostgreSQL connection string>"

dotnet run --project src/PhotoIdentity.Cli -c Release -- ^
  semantic-tags evaluate ^
  --postgres-connection-env PHOTOIDENTITY_SEMANTIC_TEST ^
  --collection "<saved Smart Collection GUID>" ^
  --proxy-root "<archive derivative root>" ^
  --proxy-profile "<current review proxy profile id>" ^
  --model "<local CLIP ONNX path>" ^
  --tokenizer-vocab "<matching vocab.json>" ^
  --tokenizer-merges "<matching merges.txt>" ^
  --concept-vocabulary experiments/visible-content/wi-0126-vocabulary-v1.json ^
  --target-count 50 ^
  --max-candidates 200 ^
  --concepts-per-photo 2 ^
  --report artifacts/wi-0126-visible-content-report.json
~~~

The line continuations above are illustrative; PowerShell users may place the command on one line or use normal PowerShell backtick continuation.

By default **no source original is opened**. To explicitly compare proxy inference against originals, rerun with a small bounded count such as '--compare-originals 20'.

Only originals that resolve safely under their recorded source root, exist at the expected byte length and are not reparse points are considered. Unavailable originals are skipped; the evaluator does not request archive hydration.

## Report interpretation

The console and JSON report contain aggregate evidence only. They do not contain Smart Collection names, source paths, filenames, revision ids or PostgreSQL connection strings.

Key fields:

- 'Sample.ProxyScoredCount' versus 'Sample.SampledCandidateCount' — whether review-proxy coverage is sufficient for the experiment.
- 'ProxyRuntime' — average, median and p95 local CLIP time per scored proxy.
- 'OriginalComparison.Top1AgreementRate' — fraction where proxy and original chose the same strongest controlled-vocabulary concept.
- 'OriginalComparison.MeanTopKJaccard' — overlap of the top configured concepts between proxy and original.
- 'Selection.SelectionOverlapCount' and 'SemanticReplacementCount' — how much semantic diversity changes the bounded Creative result.
- 'Selection.BaselineDistinctConceptCount' versus 'SemanticDistinctConceptCount' — whether the semantic variant actually broadens visible-content coverage.
- 'Selection.TopConceptCounts' — aggregate concept distribution over scored candidates; concept ids come only from the public checked-in experiment vocabulary.
- 'CatalogueWrites' — must remain 0.

The semantic selector bonus is deliberately modest: up to two concepts not yet represented receive +25 each. It is weaker than established moment/time diversity and much weaker than explicit Prefer; Avoid remains a hard presentation exclusion.

## Maintainer comparison

For the private acceptance pass, inspect representative thumbnails from both the ordinary and semantic selections rather than judging only the aggregate concept count.

Record whether:

1. the top visible-content labels are useful often enough to trust as derived evidence;
2. proxy results are materially close enough to originals to avoid routine original hydration;
3. the semantic selection is visibly more varied/useful than the metadata-only baseline rather than merely different; and
4. runtime/model footprint is acceptable for a local optional analysis job.

A positive outcome should define the smallest production boundary: persist versioned automatic tag evidence separately from manual actions, generate it from durable proxies where quality permits, and make any effective-tag policy explicitly respect manual corrections.

A negative outcome is also a valid WI-0126 result. If semantic labels are unreliable, proxy degradation is material, or Creative output does not improve enough, record a no-go and leave manual tags plus metadata-first Creative selection unchanged.
