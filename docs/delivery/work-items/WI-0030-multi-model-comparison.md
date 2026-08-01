---
id: WI-0030
title: Run a multi-model local comparison
milestone: M08
status_source: ../status/work-items.yaml
depends_on: [WI-0019, WI-0029]
affected_modules: [PhotoIdentity.Cli, PhotoIdentity.Api, PhotoIdentity.Web, PhotoIdentity.Persistence.Sqlite, tools/model-lab]
---

# WI-0030: Run a multi-model local comparison

## Objective

Repeat the accepted local workflow with baseline and candidate model revisions on the same 500-image corpus and compare their practical and measured behaviour.

## Acceptance criteria

- [ ] Both models process the same immutable source revisions and retain separate provenance.
- [ ] People, labels and review history are shared canonical data rather than copied per model.
- [ ] The web interface can select or clearly distinguish model revisions and their suggestions.
- [ ] Suggestions from different models cannot overwrite or be mistaken for each other.
- [ ] The same gallery, validation and held-out test split is evaluated for each compatible embedder.
- [ ] Detector counts, identification metrics, unknown rejection, confusion, throughput, storage and operator review effort are compared.
- [ ] Representative disagreements are reviewed manually without using test results to tune thresholds.
- [ ] The outcome records a recommendation, remaining uncertainty and whether a larger evaluation set is required.

## Comparison boundary

The comparison is between these exact embedding revisions while keeping the detector and alignment protocol fixed:

| Role | Model ID |
| --- | --- |
| Detector | `yunet-2023mar-fp32` |
| Baseline embedder | `sface-2021dec-fp32` |
| Candidate embedder | `sface-2021dec-int8` |

Both runs must use the existing reviewed catalogue and the same immutable source revisions. People, manual labels and append-only review history remain canonical shared data. Model scores, thresholds, suggestions, reports and recommendations remain scoped to an exact model ID and SHA-256 hash.

## Reproducible workflow

The active preparation branch is `agent/WI-0030-reproducible-workflow`.

Use [`Invoke-MultiModelComparison.ps1`](../../../Invoke-MultiModelComparison.ps1) with a private copy of the [example configuration](../../operations/examples/multi-model-comparison.example.json). The workflow supports Windows PowerShell 5.1 and PowerShell 7, accepts any number of embedder model IDs, writes every private artefact below one configured workspace and can resume completed per-model state.

The workflow automates source snapshotting, SQLite backup, pinned model verification, complete-corpus processing, exact-model suggestion regeneration, repeated deterministic export/evaluation, identical-split checks and aggregate reporting. Windows/Pixel visual checks, representative disagreement judgments and the recommendation remain explicit human gates. See [reproducible multi-model comparison](../../operations/multi-model-comparison.md).

## Active execution slice

1. Create a private workflow configuration with the accepted source, catalogue, dataset ID, pipeline version, split seed, split sizes, thresholds and exact model IDs.
2. Run the configurable workflow over the complete accepted source scope and retain its local backup, snapshot, logs, manifests, reports and aggregate summary outside Git.
3. Confirm every configured model processed the same immutable revisions with equal detector counts and separate exact-model provenance.
4. Confirm suggestion regeneration is stable for every exact revision.
5. Confirm every manifest and report is byte-deterministic and every model uses the same gallery, validation and held-out test split.
6. Confirm the browser UI always exposes the active suggestion revision and that switching revisions cannot overwrite another revision's suggestions or human decisions.
7. Compare identification precision, known-person recall, unknown rejection, balanced identity score, confusion, throughput, model and derived-storage sizes, and operator review effort.
8. Review a privacy-safe sample of model disagreements manually and classify whether each difference is useful, neutral or harmful without recording private identities in Git.
9. Record a recommendation, uncertainty and whether M11 needs a larger or more diverse evaluation set.

## Reproducibility requirements

- Use the same private dataset ID and split seed for every export.
- Keep the same detector ID/hash, pipeline version, immutable source scope and reviewed people.
- Store each model's output, manifest and report under a separate local path.
- Repeat each export and evaluation command and compare SHA-256 hashes to confirm deterministic bytes.
- Treat processing-job timing fallback as invalid for comparative throughput; rerun or record the limitation instead.
- Do not copy, rename or rewrite people and labels per model.
- Do not use test-split results to select thresholds.
- Do not automatically convert any model's score into a canonical label.

## Privacy-safe evidence

The repository may retain only aggregate conclusions such as:

- all runs covered the same immutable revision count;
- exact model IDs and hashes used;
- aggregate detector and evaluation metrics;
- aggregate throughput and storage measurements;
- aggregate review-time or interaction comparison;
- counts of reviewed disagreements by broad disposition;
- the final recommendation and stated uncertainty.

Do not commit private photos, names, face identifiers, crops, embeddings, SQLite catalogues, real manifests, reports, source snapshots, local paths or per-person confusion details.

## Completion gate

WI-0030 remains open until the full accepted corpus has been processed and the operator confirms that exact-model suggestions are distinguishable in the browser, all deterministic evaluations use the same split, representative disagreements were reviewed, and a privacy-safe recommendation has been recorded.
