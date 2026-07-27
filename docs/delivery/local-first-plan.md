# Local-first delivery plan

## Why the sequence changed

Azure resources will be unavailable for a period. The project therefore prioritises everything that can be proven on the trusted Windows control plane before cloud execution resumes.

The goal is not merely to make individual commands pass. The goal is to use the system as an operator would: process a representative private subset, review it from both device types, create and maintain people, accept and reject identity suggestions, compare model revisions, query the resulting catalogue and decide whether the product behaviour is useful.

## Target dataset

Use a private subset of approximately 500 images, with an acceptance range of 450–550. It should include:

- frequent people with several appearances;
- people who should remain unknown;
- group photos and single-person photos;
- small, profile, low-light and partially occluded faces;
- age variation and visually similar relatives where available;
- JPEG and PNG files plus a small number of unsupported or unavailable files to exercise reporting.

Record only privacy-safe aggregate counts in the repository. Photos, names, crops, embeddings, databases and real evaluation manifests remain local.

## Phase A — complete and prove the baseline locally

### WI-0027 — Complete the local review workflow

Ranked suggestion display and decisions, durable rejected-pair exclusions, audited person maintenance, preview-first bulk review and combined progress filters are implemented. The remaining workflow work is final Windows/Pixel usability and complete smoke verification.

Bulk assignment and face rejection display affected and skipped counts before commit. Commits require the exact server-generated preview token and fail without partial changes when the eligible set becomes stale.

The progress view combines review state, processing-run scope and exact ranked-suggestion model ID and SHA-256 revision. It exposes aggregate counts and opaque identifiers without source roots, crop paths or embeddings.

### WI-0028 — Export reviewed catalogues to model-lab

Generate deterministic gallery, validation and held-out test manifests directly from the reviewed SQLite catalogue. This closes the current gap between operational review data and the evaluation harness.

These two items can proceed in parallel.

### WI-0029 — Run a 500-image local acceptance pilot

After WI-0027 and WI-0028, process and review the baseline corpus end to end. Exercise restart, resume, backup, restore, matcher regeneration, suggestion acceptance/rejection, person maintenance, evaluation export and privacy-safe evidence capture.

The pilot is complete only when every observed defect or usability gap has an explicit fix, defer or accept decision.

## Phase B — repeat the workflow with multiple models

### WI-0019 — Add a second model adapter

Add one candidate detector or embedder through the neutral contracts. Keep baseline and candidate results separate by model ID and hash while sharing canonical people and human review history.

### WI-0030 — Run a multi-model local comparison

Process the same immutable corpus with baseline and candidate revisions. Compare detections, suggestions, held-out metrics, confusion, unknown rejection, throughput, storage and review effort. The web application must make the active model revision unmistakable.

This phase produces a recommendation, not necessarily a final production-model decision. A larger evaluation set may still be required in M11.

## Phase C — expose useful catalogue outputs and rewrite the documentation

### WI-0025 — Add collection-ready queries

Use the accepted pilot catalogue to prove any-person, all-person, confirmed-only and opt-in suggestion queries, plus a neutral collection manifest.

### WI-0031 — Rewrite operator and architecture documentation

Create one start-here route, a complete local operator runbook, a multi-model runbook, an architecture tour, troubleshooting, recovery guidance and a glossary. Replace PR-history knowledge with durable documentation.

### WI-0032 — Validate documentation from a clean setup

Follow the rewritten documentation from a clean Windows checkout and repeat the trusted-network device workflow. Fix every hidden prerequisite or confusing instruction found during the validation pass.

## Azure deferral

WI-0020 and the later Azure milestones remain part of the architecture, but WI-0020 now depends on WI-0032. Azure work resumes only after:

1. the baseline 500-image pilot is accepted;
2. the multi-model local comparison is complete;
3. collection queries are exercised; and
4. the documentation is validated from a clean setup.

This is a delivery gate, not a statement that Azure is technically required by the local system. The canonical database, review application, model evaluation and collection queries all remain local-first.

## Recommended execution order

1. WI-0027 and WI-0028 in parallel.
2. WI-0029.
3. WI-0019.
4. WI-0030 and then WI-0025.
5. WI-0031.
6. WI-0032.
7. WI-0020 when Azure access is available.
