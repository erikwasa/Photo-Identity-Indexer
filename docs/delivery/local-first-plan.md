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

Completed and human-verified on Windows and Pixel on 2026-08-01. Queue-aware details, automatic advance, suggestion-aware ordering, person audit, grouped acceptance, continuous loading, expanded published smoke and privacy-safe session reporting are implemented.

Two device-led corrective passes fixed Audit and Progress query crashes, restored accepted suggestions after undo, added any-person and create-and-assign correction, consolidated suggestion review into Faces, removed mobile overflow and ensured manual assignment advances without losing queue scope. The operator confirmed that WI-0033 works as intended and that manual reviewing is improved.

The required 50–100-face review-time and interaction evidence was captured locally for both devices and retained outside Git. Only the privacy-safe completion conclusion is recorded in the repository.

## Phase B — repeat the workflow with multiple models

### WI-0019 — Add a second model adapter

Completed and human-verified locally on Windows on 2026-08-01 after PR #52 merged.

The first candidate is the pinned upstream `sface-2021dec-int8` embedder. It deliberately retains YuNet detection, SFace five-point alignment, 112×112 input and 128-dimensional cosine embeddings so the comparison isolates the quantised embedding revision.

Batch runs persist explicit detector and embedder model IDs. New runs can select `--embedder-model sface-2021dec-int8`; resume reloads the saved exact selection. The same immutable revision reuses its face occurrence and aligned crop while baseline and candidate embeddings coexist by model ID and exact hash. Integration coverage protects people, labels and review actions from candidate processing.

Local verification confirmed the pinned model file, same-revision processing, exact-model coexistence, persisted resume selection, baseline readability without the candidate file and unchanged human review data. Only the privacy-safe conclusion is retained in Git.

### WI-0030 — Run a multi-model local comparison

Completed and human-verified on 2026-08-01.

The reproducible workflow processed and evaluated `sface-2021dec-fp32` and `sface-2021dec-int8` over the same accepted immutable source scope while keeping the detector, alignment protocol, dataset ID, pipeline version, split seed and split settings fixed. Detector counts, source revisions and deterministic evaluation splits matched, and each model retained separate exact provenance, suggestions, manifests and reports.

The review application distinguished the exact FP32 and INT8 suggestion contexts without changing canonical people, assignments, rejections or append-only audit history. A private manual review of 20 representative faces found both revisions correct in every case. No top-person disagreement or material review difference was observed; all practical differences were neutral score or margin changes.

The recommendation is to retain `sface-2021dec-fp32` as the current default embedding model. The INT8 candidate remains a valid governed revision but did not demonstrate a material product advantage on the accepted corpus. No larger local comparison is required before continuing to collection-ready queries. Final production selection remains deferred to M11, where Azure consistency, deployment cost and broader diversity can be considered.

Detailed photos, identities, local paths, databases, manifests, reports and the manual review record remain outside Git.

## Phase C — expose useful catalogue outputs and rewrite the documentation

### WI-0025 — Add collection-ready queries

This is the next ready work item. Use the accepted pilot catalogue after the completed review and multi-model gates to prove any-person, all-person, confirmed-only and opt-in suggestion queries, plus a neutral collection manifest.

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
