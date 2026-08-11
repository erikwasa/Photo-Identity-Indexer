# Build context

## Current product target

Version 1 is reached when the permanent catalogue can safely **begin** processing the real full archive. Version 1 does not require every archive asset to have finished processing.

The version-1 archive-readiness gates are:

1. WI-0042 — bounded hydration, source re-verification and durable review proxies;
2. WI-0041 — stable permanent archive identity and incremental no-repeat ingestion after WI-0042 is accepted; and
3. WI-0053 — HEIC/HEIF plus every RAW variant required by the real archive, with explicit unsupported state for any deliberate exception.

All three archive-readiness work-item gates are human-verified. WI-0054, the post-acceptance archive viewer/progress/availability polish discovered during real Windows/OneDrive verification, is also human-verified. The remaining version-1 step is the final real-environment product-success confirmation.

See `docs/product/success-criteria.md` and `docs/delivery/local-first-plan.md` for the durable product/delivery definition.

## Current milestones

- **M17 — Identity review automation**: `completed` and human-verified on 2026-08-11. All four M17 work items are merged to `main` and accepted on Windows laptop and Pixel.
- **M12 — Full archive processing**: `proposed` under the repository status rules. Its archive-readiness work (WI-0041, WI-0042, WI-0053 and WI-0054) is completed and human-verified; the later full-coverage WI-0023 remains separate and is not yet ready.
- **M09 — Azure VM pilot without identities**: `ready` and optional; Azure is not required for version 1.
- **M18 — Operator application experience**: `ready` in the canonical `main` delivery registry.
- **M19 — Photo metadata and semantic collections**: `ready` in the canonical `main` delivery registry.

## Canonical active work

The `main` delivery registry currently has no work items in `in_progress`, `in_review` or `blocked` state after completion of M17 and WI-0054.

## Completed M17 identity review automation

M17 is complete after integrated automated verification and milestone-wide human review on 2026-08-11.

- **WI-0043** provides exact-model configurable High/Medium/Low confidence groups and optional canonical High-confidence automatic assignment. High requires both the configured rank-1 score and rank-1/rank-2 margin. Automatic decisions are auditable, use one fixed exemplar snapshot per regeneration, and can be superseded by manual correction.
- **WI-0044** adds favorite people with stable favorite-first ordering across normal selectors and maintenance surfaces without changing matcher evidence or scores.
- **WI-0045** moves exact-model suggestion regeneration into the browser with durable progress, restart recovery, duplicate-run exclusion and stale-evidence protection while retaining the CLI path for diagnostics/automation.
- **WI-0047** adds Unknown as a reversible real-face review state distinct from false detection, excludes Unknown from normal identity evidence/collections, and preserves history when a later assignment supersedes it.

The maintainer accepted the integrated M17 workflow on Windows laptop and Pixel, including confidence grouping and threshold tuning, optional automatic assignment and audit/correction behavior, favorite ordering and controls, Unknown/false-detection distinction and later assignment, and browser regeneration with progress/stale-state feedback.

## Minor Faces UI follow-ups

Two non-blocking presentation issues were observed during M17 verification and may be fixed directly without creating work items:

- on laptop, the `Unknown person`, `Assign` and `False detection` buttons do not fit comfortably inside a face card;
- on Pixel, the persistent menu consumes roughly half the screen and remains fixed while scrolling, which is unacceptable for normal mobile use.

These observations do not reopen any M17 acceptance criterion.

## Completed WI-0054 archive polish

WI-0054 is `completed` and human-verified as of 2026-08-11. The accepted behavior includes:

- safe review-sized fallback viewing from already-local, exact-revision-verified originals when no durable proxy exists;
- no implicit hydration when normal viewing encounters an online-only original without a proxy;
- clear no-preview UI instead of broken images;
- stable operator-facing Archive advancement stage counters rather than replaceable internal batch progress;
- persisted reconciliation of live OneDrive availability observed during explicit original status/hydrate/release operations; and
- stale queued/running revision cancellation only when immutable identity for that same asset actually changes.

The accepted WI-0042 managed-release and bounded-storage policy remains unchanged.

## WI-0053 archive formats

WI-0053 is `completed` and human-verified as of 2026-08-11. HEIC/HEIF is supported through the production image contract and review-proxy path, privacy-safe archive inventory/reporting is in place, representative private HEIC files were successfully decoded with correct visual output and unchanged originals, and the current archive has no known RAW variants. A future newly observed RAW format reopens only that format-specific decoder/verification requirement rather than invalidating the current completion evidence.

## Selected permanent archive analysis profile

M16 is complete. The governed permanent archive profile uses:

- CenterFace `centerface-2019-fp32`;
- confidence `0.5`;
- `single-pass` detector pipeline;
- SFace `sface-2021dec-fp32`; and
- `sface-five-point-v1` alignment.

The generic historical `batch` defaults still use YuNet and must not be mistaken for the permanent archive profile.

The previous FP32-versus-INT8 embedding comparison used the earlier YuNet face population. If production model selection is reaffirmed after the detector change, evaluate the exact models on the selected CenterFace population before treating the old comparison as definitive for the permanent archive.

## Accepted identity-automation direction

ADR-0006 supersedes the earlier mandatory-human-confirmation rule and is implemented and human-verified by M17. Suggestion policy is scoped to an exact embedding-model revision, High confidence requires both absolute rank-1 score and a configurable rank-1/rank-2 gap, and optional automatic assignments use the canonical acceptance boundary with audit provenance. Automatic assignments can become exemplars only on later regeneration runs and can be superseded by manual reassignment. Automatic assignment remains an explicit user-controlled setting and is disabled by default.

ADR-0007 records the stable archive root plus bounded local materialization architecture.

## Next concrete sequence

1. Apply the two minor Faces responsive-layout fixes directly without creating work items.
2. Confirm the version-1 product success criteria on the real Windows/OneDrive environment.
3. Continue permanent-catalogue archive coverage incrementally when the remaining M12 full-coverage prerequisites are satisfied; do not create a replacement production database.
4. Continue the planned operator-experience and library-intelligence milestones from their canonical ready states; Azure remains optional.

## Completed gates

- M17 identity review automation is merged to `main` and human-verified on Windows laptop and Pixel.
- WI-0054 archive viewer/progress/availability polish is human-verified on the real Windows/OneDrive archive.
- WI-0042 bounded archive hydration, source re-verification, durable review proxies and managed-release behavior are human-verified on the real Windows/OneDrive archive.
- WI-0041 stable permanent archive identity, overlapping coverage, incremental rescan and no-repeat processing are human-verified on the real Windows/OneDrive archive.
- WI-0053 HEIC/HEIF support, explicit RAW visibility policy and private HEIC verification are human-verified; no RAW variants are currently known in the maintained archive.
- The representative local acceptance pilot passed restart/resume, Windows/Pixel review, deterministic evaluation, backup and restore.
- SFace FP32 remains the current selected embedder pending any later CenterFace-population reaffirmation.
- CenterFace confidence `0.5`, single-pass, passed the governed M16 detector gate and migration-safety pilot.
- Collection-ready queries and neutral manifests are implemented.
- Operator/architecture documentation and clean-setup validation were previously completed; the later archive-readiness pass realigned them with the permanent-archive product direction.

## Relevant planning and operation files

- `docs/product/success-criteria.md`
- `docs/delivery/local-first-plan.md`
- `docs/delivery/milestones/M17-review-automation.md`
- `docs/delivery/work-items/WI-0043-confidence-auto-assignment.md`
- `docs/delivery/work-items/WI-0044-favorite-people.md`
- `docs/delivery/work-items/WI-0045-web-match-regeneration.md`
- `docs/delivery/work-items/WI-0047-unknown-review-state.md`
- `docs/delivery/work-items/WI-0054-archive-ui-polish.md`
- `docs/delivery/work-items/WI-0042-bounded-archive-storage.md`
- `docs/delivery/work-items/WI-0041-incremental-archive-ingestion.md`
- `docs/delivery/work-items/WI-0053-heic-raw-support.md`
- `docs/operations/index.md`
- `docs/operations/local-operator-guide.md`
- `docs/operations/bounded-archive-acceptance.md`
- `docs/delivery/status/work-items.yaml`

## Automated validation

```powershell
./build.ps1
./test.ps1
./verify-local.ps1 -InstallModels
./verify-review.ps1 -Mode Smoke -Configuration Release
dotnet run --project tools/PhotoIdentity.Docs -- validate
dotnet run --project tools/PhotoIdentity.Docs -- generate --check
```
