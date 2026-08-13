# Build context

This file is intentionally a short handoff for the next development or verification session. It should describe only the current focus, the next concrete step and the small set of documents needed to continue.

Formal work-item lifecycle status and evidence live in `docs/delivery/status/work-items.yaml`. Durable product, architecture and operating decisions belong in the linked documentation rather than being repeated here.

## Current focus

**WI-0049 — Experiment with visible-content image tagging** is the active implementation boundary for M19.

PR #138 / WI-0056 is merged and its full automated workflow passed. Human real-archive verification of the manual fallback/correction controls is intentionally deferred by the maintainer, so WI-0056 must remain `in_review` rather than being treated as completed. That deferred UI verification does not block the automatic-tagging experiment because the canonical tag/API boundary is merged.

Automatic visible-content tagging is the intended normal/default M19 path. Manual tagging is only the fallback/correction mechanism. The first WI-0049 slice therefore establishes a reproducible controlled-vocabulary OpenCLIP experiment rather than extending manual tagging.

The isolated harness below `tools/model-lab/visible-content-tagging/` pins OpenCLIP 3.3.0 and a specific LAION ViT-B/32 model revision, records resolved model/checkpoint/configuration provenance, keeps real manifests/vocabularies/model files/reports private, evaluates independent cosine scores over threshold sweeps and compares durable review proxies against the same photos' originals. `run_experiment.py` is the public entry point: it mirrors the production tag-normalization shape closely and refuses Windows originals marked offline or recall-on-data-access before opening image bytes. The slice deliberately does not use vocabulary softmax as multi-label confidence and does not persist model evidence into the production catalogue yet.

## Next concrete step

Run the first representative private experiment before selecting any production automatic-evidence schema:

1. Create a private canonical vocabulary with a mix of object, scene/place, activity/event, absent and deliberately confusable concepts.
2. Select a bounded representative sample with durable review proxies and human-labelled expected tags. Include paired originals that are already local, or use an explicitly bounded hydration subset. The public runner rejects Windows offline/recall-on-data-access originals so comparison cannot silently hydrate a placeholder.
3. Prepare the pinned public model snapshot with `run_experiment.py prepare-model` and run the proxy/original comparison locally.
4. Review privacy-safe aggregate precision/recall/F1, runtime and proxy/original agreement. Keep sample-level labels/scores private.
5. If controlled-vocabulary quality is promising, define the held-out validation and effective-tag/manual-override policy needed for a production follow-on. If it is weak, compare the next bounded purpose-built tagger before considering captioning.
6. Record the selected production direction or explicit blocker/next experiment. Manual-only tagging is not an acceptable automatic-tagging completion state.

## Relevant files

- `docs/delivery/work-items/WI-0049-visible-content-tagging-experiment.md`
- `docs/delivery/milestones/M19-library-intelligence.md`
- `tools/model-lab/visible-content-tagging/README.md`
- `tools/model-lab/visible-content-tagging/run_experiment.py`
- `tools/model-lab/visible-content-tagging/experiment.py`
- `tools/model-lab/visible-content-tagging/test_experiment.py`
- `tools/model-lab/visible-content-tagging/test_run_experiment.py`
- `tools/model-lab/visible-content-tagging/example-manifest.json`
- `tools/model-lab/visible-content-tagging/example-vocabulary.json`
- `docs/delivery/work-items/WI-0056-manual-photo-tags.md`

## Repository validation

```powershell
./build.ps1
./test.ps1
cd tools/model-lab/visible-content-tagging
python -m unittest test_experiment.py test_run_experiment.py
cd ../../..
dotnet run --project tools/PhotoIdentity.Docs -- validate
dotnet run --project tools/PhotoIdentity.Docs -- generate --check
```
