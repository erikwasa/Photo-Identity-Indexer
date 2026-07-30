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

Completed and human-verified on Windows and Pixel on 2026-07-30. Ranked suggestion decisions, durable rejected-pair exclusions, audited person maintenance, preview-first bulk review, combined progress filters and published-application smoke coverage all passed against the local workflow.

### WI-0028 — Export reviewed catalogues to model-lab

Completed and human-verified against the private reviewed catalogue on 2026-07-30. Repeated export and evaluation produced deterministic bytes, preserved source-photo split isolation and kept real manifests and local paths outside the repository.

### WI-0029 — Run a 500-image local acceptance pilot

Completed on 2026-07-30. The representative private subset passed batch restart/resume, Windows and Pixel review, matcher regeneration, deterministic evaluation, storage and aggregate metric capture, backup, restore and cleanup. No private media or biometric artefacts were committed.

The pilot classified sustained review speed as an S3 usability defect with disposition **fix before proceeding**. Elapsed review time was not captured, but the operator found the click-heavy workflow too slow on both device types.

### WI-0033 — Accelerate the human review workflow

Work is in progress. Native Enter submission, scope-aware Previous and Next controls, and automatic advance after suggestion acceptance are merged. Queue neighbours are calculated from the same state, processing-run, exact model revision and deterministic sort scope as the originating view; the next face is captured before mutation so removing the current face cannot shift an offset and skip work.

The suggestion-gallery slice adds a dedicated exact-model workspace. It returns the rank-one pending suggestion, score, margin and full model revision in one paged response, supports task-oriented ordering by suggested person, margin, score or missing suggestion, and allows clear matches to be accepted directly from cards. Ambiguous cases retain the same ordering in a continuous quick-details queue.

The person-audit slice adds a dedicated read-only workspace for paging every active face assigned to one person. Exact-model comparison can place likely disagreements first or show only disagreements, while rejected suggestions remain excluded and canonical labels never change automatically. Every face links back to its append-only audit history for correction through the existing workflow.

Remaining slices add preview-first grouped suggestion acceptance with linked audit actions, extend published smoke coverage, and measure a fresh 50–100-face queue on Windows and Pixel.

The improved workflow must retain explicit human confirmation, append-only assignment and suggestion audit actions, exact model provenance and privacy-limited DTOs.

## Phase B — repeat the workflow with multiple models

### WI-0019 — Add a second model adapter

Add one candidate detector or embedder through the neutral contracts after WI-0033 closes the baseline review-throughput gate. Keep baseline and candidate results separate by model ID and hash while sharing canonical people and human review history.

### WI-0030 — Run a multi-model local comparison

Process the same immutable corpus with baseline and candidate revisions. Compare detections, suggestions, held-out metrics, confusion, unknown rejection, throughput, storage and review effort. The web application must make the active model revision unmistakable.

This phase produces a recommendation, not necessarily a final production-model decision. A larger evaluation set may still be required in M11.

## Phase C — expose useful catalogue outputs and rewrite the documentation

### WI-0025 — Add collection-ready queries

Use the accepted pilot catalogue after the review-throughput gate to prove any-person, all-person, confirmed-only and opt-in suggestion queries, plus a neutral collection manifest.

### WI-0031 — Rewrite operator and architecture documentation

Create one start-here route, a complete local operator runbook, a multi-model runbook, an architecture tour, troubleshooting, recovery guidance and a glossary. Replace PR-history knowledge with durable documentation.

### WI-0032 — Validate documentation from a clean setup

Follow the rewritten documentation from a clean Windows checkout and repeat the trusted-network device workflow. Fix every hidden prerequisite or confusing instruction found during the validation pass.

## Azure deferral

WI-0020 and the later Azure milestones remain part of the architecture, but WI-0020 now depends on WI-0032. Azure work resumes only after:

1. the baseline 500-image pilot and review-throughput follow-up are accepted;
2. the multi-model local comparison is complete;
3. collection queries are exercised; and
4. the documentation is validated from a clean setup.

This is a delivery gate, not a statement that Azure is technically required by the local system. The canonical database, review application, model evaluation and collection queries all remain local-first.

## Recommended execution order

1. WI-0033.
2. WI-0019.
3. WI-0030 and then WI-0025.
4. WI-0031.
5. WI-0032.
6. WI-0020 when Azure access is available.
