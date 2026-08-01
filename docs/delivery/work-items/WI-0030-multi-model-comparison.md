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

- [x] Both models process the same immutable source revisions and retain separate provenance.
- [x] People, labels and review history are shared canonical data rather than copied per model.
- [x] The web interface can select or clearly distinguish model revisions and their suggestions.
- [x] Suggestions from different models cannot overwrite or be mistaken for each other.
- [x] The same gallery, validation and held-out test split is evaluated for each compatible embedder.
- [x] Detector counts, identification metrics, unknown rejection, confusion, throughput, storage and operator review effort are compared.
- [x] Representative differences are reviewed manually without using test results to tune thresholds.
- [x] The outcome records a recommendation, remaining uncertainty and whether a larger evaluation set is required.

## Comparison boundary

The comparison is between these exact embedding revisions while keeping the detector and alignment protocol fixed:

| Role | Model ID |
| --- | --- |
| Detector | `yunet-2023mar-fp32` |
| Baseline embedder | `sface-2021dec-fp32` |
| Candidate embedder | `sface-2021dec-int8` |

Both runs used the existing reviewed catalogue and the same immutable source revisions. People, manual labels and append-only review history remained canonical shared data. Model scores, thresholds, suggestions, reports and recommendations remained scoped to an exact model ID and SHA-256 hash.

## Reproducible workflow

[`Invoke-MultiModelComparison.ps1`](../../../Invoke-MultiModelComparison.ps1) was used with a private configuration outside Git. The workflow supports Windows PowerShell 5.1 and PowerShell 7, writes every private artefact below one configured workspace and can resume completed per-model phases.

The workflow automated source snapshotting, SQLite backup, pinned model verification, complete-corpus processing, exact-model suggestion regeneration, repeated deterministic export/evaluation, identical-split checks and aggregate reporting. Human review remained the explicit practical gate. See [reproducible multi-model comparison](../../operations/multi-model-comparison.md).

## Completion evidence

WI-0030 was completed and human-verified on 2026-08-01.

The private same-corpus workflow confirmed:

- both exact embedder revisions processed the same immutable source scope;
- detector counts and evaluation splits were identical;
- suggestions, manifests and evaluation reports remained scoped to exact model revisions and were reproducible;
- people, labels, assignments and append-only review history were unchanged by model switching;
- the review application distinguished the FP32 and INT8 suggestion contexts;
- the detailed manifests, reports, database, paths and manual worksheet remained outside Git.

The operator manually reviewed 20 representative faces. FP32 and INT8 selected the correct canonical person in all 20 cases. No top-person disagreement or material review difference was observed. All observed differences were neutral score or margin changes; there were no useful or harmful candidate outcomes in the reviewed sample.

## Recommendation

Retain `sface-2021dec-fp32` as the current default embedding model.

The INT8 candidate reduced model-file size and remained functionally compatible, but it did not provide a material identification or review-quality advantage on the accepted private corpus. Changing the default would therefore add migration and operational change without a demonstrated product benefit.

The candidate remains a valid pinned comparison revision and can be reconsidered if later deployment measurements show a meaningful cost, memory or throughput advantage.

## Remaining uncertainty and larger-evaluation decision

The manual review sample was intentionally representative rather than exhaustive, and the accepted corpus is private and limited in diversity. No larger local evaluation is required before continuing to collection-ready queries because the conservative decision is to retain the established FP32 baseline.

Production model selection remains a later M11 decision and must still consider Azure consistency, deployment cost, broader data diversity and any future candidate models. The held-out test results were not used to tune thresholds during this comparison.

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

The repository retains only aggregate conclusions. It does not contain private photos, names, face identifiers, crops, embeddings, SQLite catalogues, real manifests, reports, source snapshots, local paths, the manual review workbook or per-person confusion details.

## Completion gate

Completed on 2026-08-01 after the full accepted corpus comparison, deterministic same-split evaluation, exact-model browser verification, representative manual review and privacy-safe recommendation were recorded.
