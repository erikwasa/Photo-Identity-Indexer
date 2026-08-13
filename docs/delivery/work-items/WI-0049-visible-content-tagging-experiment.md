---
id: WI-0049
title: Experiment with visible-content image tagging
milestone: M19
status_source: ../status/work-items.yaml
depends_on: [WI-0056]
related_adrs: []
affected_modules: [PhotoIdentity.Core, PhotoIdentity.Imaging.OpenCv, PhotoIdentity.Recognition.Onnx, PhotoIdentity.Persistence.Sqlite]
---

# WI-0049: Experiment with visible-content image tagging

## Objective

Evaluate practical local approaches for producing automatic visible-content tags for whole photos, using the canonical tag representation established by WI-0056, and select the primary production direction for M19.

## Why

Automatic tagging is the intended normal path for scalable discovery across a large archive. Manual tagging exists as a fallback/correction mechanism when automation is unavailable, misses a useful concept or produces a result the maintainer needs to correct. The automatic approach therefore needs enough quality, repeatability and runtime practicality to carry the normal workload rather than depending on maintainers to label the archive by hand.

Model quality, vocabulary behavior, runtime, storage, privacy, packaging and redistribution implications should still be measured before committing to a production architecture. Explicit manual interventions must remain separate from model evidence and, in the eventual effective-tag policy, must be able to override a conflicting automatic result for the specific tag without destroying the underlying model evidence.

## In scope

- Define a small representative private evaluation sample and useful canonical tag vocabulary/activity examples.
- Reuse maintainer-assigned fallback/correction tags from WI-0056 as evaluation evidence where appropriate without committing private photo content or private labels.
- Evaluate one or more local image-semantic approaches without sending private photos to external services.
- Use controlled-vocabulary image/text similarity as the first integration baseline because it maps directly onto canonical tags and can fit the existing ONNX/C# runtime shape.
- Compare a purpose-built image tagger when feasible if it offers materially better object/scene/activity coverage at acceptable runtime and packaging cost.
- Treat generative captioning as optional comparative evidence rather than a required production path.
- Compare inference from the existing durable review proxy with inference from the original on the same representative sample. Prefer the proxy path if semantic usefulness is materially preserved because it avoids unnecessary archive hydration; otherwise record the bounded-hydration requirement explicitly.
- Measure practical precision/usefulness, missed concepts, confusing concepts, runtime and storage footprint with the goal of determining whether the approach can be the normal/default tagging path.
- Record model provenance, licence/redistribution constraints and hardware/runtime requirements.
- Define automatic-evidence provenance at the complete inference-pipeline level. For controlled-vocabulary scoring this includes at least exact model revision, image preprocessing, tokenizer/text preprocessing, prompt templates and vocabulary/version; model hash plus a scalar score alone is insufficient for reproducibility.
- Determine what effective-tag policy the production integration needs, including confidence/threshold behavior and how explicit manual additions or suppressions should override conflicting automatic output without deleting model evidence.
- Prototype model-produced tag evidence separately from manual intervention history only after the experiment has established the evidence shape that needs to be persisted.
- Produce a recommendation that selects the next production implementation boundary. If no candidate is acceptable, document the blocking deficiency and the next bounded experiment rather than treating manual-only tagging as the M19 target state.

## Active first slice: controlled-vocabulary OpenCLIP baseline

The first implemented experiment slice uses `open_clip_torch` 3.3.0 with `laion/CLIP-ViT-B-32-laion2B-s34B-b79K`. The model preparation command resolves pinned Hub revision `1a25a44` to its full immutable commit, downloads only the OpenCLIP configuration/tokenizer files and safetensors checkpoint, and records checkpoint/configuration SHA-256 hashes before local inference.

The harness lives below `tools/model-lab/visible-content-tagging/` and deliberately keeps real manifests, vocabulary choices, model files and detailed reports out of Git. It records a path-free manifest digest, vocabulary/prompt digest, exact runtime package versions, independent normalized image/text cosine scores, proxy-threshold precision/recall/F1 sweeps, original-image metrics at the proxy-selected threshold, runtime and paired proxy/original agreement.

A vocabulary softmax is not treated as a multi-label confidence because its values depend on which other vocabulary entries participate. The baseline instead retains independent cosine scores and uses threshold sweeps as experiment evidence. The current threshold-selection rule (proxy micro F1, then precision, then higher threshold) is an experiment rule only; it does not become production policy without held-out private validation.

This slice does not hydrate originals automatically. The paired comparison should use already-local originals or an explicitly bounded evaluation subset under the existing archive storage policy.

## Candidate families to investigate

- CLIP/OpenCLIP-style image/text similarity for a bounded canonical vocabulary and activity phrases.
- RAM/RAM++-style purpose-built image tagging if a practical local packaging/runtime path can be demonstrated.
- Florence-2-style captioning only if captions materially improve discovery beyond canonical-tag scoring enough to justify generative runtime and integration complexity.

Candidate names are an experiment starting point, not a production-model decision.

## Out of scope

- Shipping the final production automatic-tagging pipeline directly from this experiment item unless the evidence and implementation boundary are small enough to be explicitly approved.
- Replacing or mutating manual intervention history.
- Treating manual fallback tagging as the normal library-tagging workflow.
- Freezing a production automatic-evidence schema before the experiment establishes the required pipeline provenance and output shape.
- Building the final smart-collection UI.
- Uploading the private archive to a third-party vision API.
- Generating tag descriptions through an external LLM using private photo content.

## Acceptance criteria

- [ ] A bounded representative tag experiment can be reproduced locally.
- [ ] The experiment includes object/scene and activity-style concepts relevant to the intended library use.
- [ ] At least one controlled-vocabulary approach is measured against the canonical tag representation.
- [ ] Review-proxy versus original-image inference is measured on the same sample and the chosen production-input recommendation is recorded.
- [ ] Quality, runtime and storage findings are recorded without committing private photo content or private tag labels.
- [ ] Candidate model provenance/licensing, packaging and redistribution constraints are documented.
- [ ] Any prototype automatic evidence cannot overwrite manual intervention history and records enough exact pipeline provenance to reproduce its scores/outputs.
- [ ] The recommended effective-tag policy explains how explicit manual fallback/correction actions interact with automatic output.
- [ ] A clear primary automatic-tagging production direction and next implementation boundary are recorded; if no candidate is acceptable, the blocking reason and next experiment are explicit.

## Verification requirements

Automated smoke tests for any prototype pipeline plus maintainer review of privacy-safe aggregate experiment results on private photos.

## Planned slices

1. **Current** — reproducible controlled-vocabulary OpenCLIP baseline, path-safe private manifest/vocabulary contracts, score/threshold metrics and proxy-versus-original comparison output.
2. Run the first representative private sample and record privacy-safe aggregate findings plus operational input recommendation.
3. If needed, compare the next bounded candidate (purpose-built tagger first; captioning only when justified), then select the production implementation boundary and effective-tag/manual-override policy.

## Completion notes

- Files changed: first model-lab controlled-vocabulary experiment harness, synthetic manifest/vocabulary examples, model/runtime provenance capture and lightweight metric tests.
- Trade-offs: Python is isolated to the model experiment because OpenCLIP is native there; no Python dependency is introduced into the production application. Independent cosine scores are retained rather than vocabulary softmax probabilities. Model files and private sample details remain local.
- Deferred work: private experiment run and evidence; candidate comparison if required; production automatic evidence schema/generation; final threshold/effective-tag/manual-override policy; smart-collection integration.
- Commands run: lightweight Python metric tests were exercised locally without model downloads; normal repository CI remains required for the branch.
