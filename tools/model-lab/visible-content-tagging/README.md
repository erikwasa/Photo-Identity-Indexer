# WI-0049 visible-content tagging experiment

This directory is an isolated, local-only model experiment. It is not the production tagging pipeline.

The product goal is **automatic visible-content tagging as the normal/default path**. Manual tags from WI-0056 are the fallback/correction mechanism when automatic tagging is unavailable, misses a useful concept or needs a human override.

## First baseline

The first controlled-vocabulary baseline uses OpenCLIP with:

- model repository: `laion/CLIP-ViT-B-32-laion2B-s34B-b79K`
- requested Hugging Face revision: `1a25a44`
- OpenCLIP package: `open_clip_torch==3.3.0`
- score: cosine similarity between normalized image features and normalized prompt-ensemble text features
- default prompt: `a photo of {tag}`
- default threshold sweep: `0.10` through `0.40` in `0.01` increments

`prepare-model` resolves the requested revision to the full immutable Hub commit, downloads only the OpenCLIP configuration/tokenizer files plus `open_clip_model.safetensors`, hashes the checkpoint and configuration, and writes local snapshot provenance. Model binaries belong below an ignored/private directory and must never be committed.

The baseline deliberately does **not** use a softmax over the vocabulary as a multi-label confidence. Softmax forces labels to compete and makes the value depend on the exact vocabulary. The experiment retains independent cosine scores, then measures threshold behavior against human-labelled samples.

Use `run_experiment.py` as the public entry point. It wraps the experiment core with two safety/canonicalization boundaries: tag labels receive NFKC compatibility normalization, collapsed whitespace, control-character rejection and UTF-16-length enforcement before lower-case identity; and Windows original-image inference refuses files marked offline or recall-on-data-access before any image bytes are opened.

## Privacy boundary

Keep all real manifests, vocabulary files, model snapshots, detailed score reports and photos under an ignored local directory such as:

```text
private/
  visible-content-tagging/
    manifest.json
    vocabulary.json
    models/
    reports/
```

The checked-in examples are synthetic. The aggregate report contains:

- model repository, resolved revision and checkpoint/configuration hashes;
- a digest of the path-free manifest semantics;
- a digest of the normalized vocabulary and prompts;
- exact Python/package versions;
- threshold sweep and selected threshold;
- proxy/original agreement metrics; and
- measured runtime.

It does not serialize image filesystem paths. The optional details report contains sample IDs, expected tags and raw similarity scores, so it remains private even though paths are omitted.

No photo or label is sent to an external inference service. Network access is needed only by `prepare-model` to fetch the pinned public model snapshot. Inference is local.

## Private manifest

Create `private/visible-content-tagging/manifest.json` from `example-manifest.json`.

Each sample has:

- `id`: a stable private experiment identifier;
- `proxyPath`: local durable review-proxy path;
- `originalPath`: optional already-local original path for the paired comparison;
- `expectedTags`: human-labelled canonical tags for evaluation.

For the proxy-versus-original comparison, use the same sample IDs and labels; each manifest row refers to both representations of the same immutable photo revision.

Do not hydrate online-only originals merely to fill the comparison. Select a representative subset whose originals are already local, or explicitly hydrate a bounded evaluation subset under the archive storage policy. On Windows, `run_experiment.py` checks `st_file_attributes` before original-image decoding and refuses `FILE_ATTRIBUTE_OFFLINE` or `FILE_ATTRIBUTE_RECALL_ON_DATA_ACCESS`; this prevents the experiment from silently turning a placeholder read into OneDrive hydration. If a bounded original is wanted, hydrate it explicitly first and then rerun.

## Vocabulary

Create `private/visible-content-tagging/vocabulary.json` from `example-vocabulary.json`.

The public runner mirrors the production `PhotoTagName` shape closely: NFKC normalization, repeated-whitespace collapse, control-character rejection, an 80 UTF-16-code-unit limit and lower-case canonical identity. Production C# remains the final authority for persisted canonical tags; this experiment normalization exists to prevent vocabulary/evaluation drift before production integration.

An entry may provide one or more explicit prompts. If prompts are omitted, the harness uses:

```text
a photo of {tag}
```

Prompts are part of the inference pipeline and therefore part of the vocabulary digest. Changing a prompt or tag changes the experiment input identity.

The first private sample should contain a useful mix of:

- objects;
- scenes/places;
- activities/events;
- concepts likely to be absent; and
- deliberately confusable concepts.

Keep private taxonomy choices out of Git.

## Setup on Windows

Use a dedicated Python environment. OpenCLIP 3.3.0 requires PyTorch 2.6 or newer; choose the appropriate CPU or CUDA PyTorch installation for the machine, then install the pinned OpenCLIP package:

```powershell
cd tools/model-lab/visible-content-tagging
py -3.12 -m venv .venv
.\.venv\Scripts\Activate.ps1
python -m pip install --upgrade pip
python -m pip install -r requirements.txt
```

Prepare the pinned model below an ignored directory:

```powershell
python run_experiment.py prepare-model `
  --model-dir ../../../private/visible-content-tagging/models/openclip-b32
```

The model snapshot is loaded later via OpenCLIP's local-directory loader. That prevents a normal experiment run from silently moving to a newer Hub revision.

## Run the paired experiment

```powershell
python run_experiment.py run `
  --model-dir ../../../private/visible-content-tagging/models/openclip-b32 `
  --manifest ../../../private/visible-content-tagging/manifest.json `
  --vocabulary ../../../private/visible-content-tagging/vocabulary.json `
  --output ../../../private/visible-content-tagging/reports/openclip-b32-aggregate.json `
  --details-output ../../../private/visible-content-tagging/reports/openclip-b32-details.json `
  --device cpu
```

Use `--device cuda` on a compatible configured CUDA machine.

The aggregate report selects a proxy threshold using micro F1, with precision and then the higher threshold as tie-breakers. This is an experiment selection rule, not yet a production policy. A production threshold must be validated on a held-out private sample before automatic tags are accepted as normal library state.

## Proxy versus original evidence

At the selected proxy threshold the harness reports:

- mean Jaccard agreement of thresholded tag sets;
- mean top-k tag overlap; and
- mean absolute cosine-score delta.

It also reports original-image precision/recall/F1 at the threshold chosen from proxies.

The preferred production input is the durable review proxy if it preserves useful semantic tagging closely enough. Requiring originals is a materially worse operational path because online-only archive items would then need bounded hydration. No fixed acceptance percentage is hard-coded before the first real evidence exists; record the observed trade-off and set the production gate from the private results.

## Test the harness without model downloads

The score/metric and safety tests use only the Python standard library and do not import OpenCLIP:

```powershell
python -m unittest test_experiment.py test_run_experiment.py
```

CI runs these lightweight tests only. CI must never download the model or access private photos.

## What this slice does not decide

This first baseline does not yet decide:

- that OpenCLIP is the production model;
- the final tag vocabulary;
- the final threshold/effective-tag semantics;
- how automatic suppressions and manual corrections are persisted;
- whether a purpose-built tagger such as RAM/RAM++ materially outperforms controlled-vocabulary scoring; or
- whether captioning adds enough discovery value to justify its complexity.

Those decisions require the private experiment evidence. If this baseline is weak, WI-0049 should compare the next bounded candidate instead of making manual tagging the normal path.
