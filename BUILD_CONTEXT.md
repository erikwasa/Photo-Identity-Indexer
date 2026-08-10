# Build context

## Current product target

Version 1 is reached when the permanent catalogue can safely **begin** processing the real full archive. Version 1 does not require every archive asset to have finished processing.

The version-1 archive-readiness gates are:

1. WI-0042 — bounded hydration, source re-verification and durable review proxies;
2. WI-0041 — stable permanent archive identity and incremental no-repeat ingestion after WI-0042 is accepted; and
3. WI-0053 — HEIC/HEIF plus every RAW variant required by the real archive, with explicit unsupported state for any deliberate exception.

See `docs/product/success-criteria.md` and `docs/delivery/local-first-plan.md` for the durable product/delivery definition.

## Current milestones

- **M12 — Full archive processing**: `in_progress`. It contains the version-1 archive-readiness work and later full-coverage completion.
- **M17 — Identity review automation**: `ready`, but it is not a version-1 gate.
- **M09 — Azure VM pilot without identities**: `ready` and optional; Azure is not required for version 1.

## Active work

**WI-0054 — Polish archive viewing, progress and availability** is `in_progress` on `agent/WI-0054-archive-ui-polish` after the maintainer completed real Windows/OneDrive verification of WI-0042 and WI-0041 on 2026-08-10.

The focused follow-up fixes three post-acceptance usability inconsistencies: local verified pending revisions need a safe viewer fallback before their durable proxy exists; latest-run progress must be distinguished from cumulative archive analysis; and explicit original status/hydration observations must reconcile the persisted Archive availability state. The accepted WI-0042 managed-release and bounded-storage policy remains unchanged.

**WI-0053 — Add HEIC and archive RAW image support** remains a version-1 archive-readiness gate. Current maintainer evidence says the full archive contains a few HEIC files and no known RAW files; representative private HEIC verification and the privacy-safe archive media inventory remain the required completion evidence.

## Selected permanent archive analysis profile

M16 is complete. The governed permanent archive profile uses:

- CenterFace `centerface-2019-fp32`;
- confidence `0.5`;
- `single-pass` detector pipeline;
- SFace `sface-2021dec-fp32`; and
- `sface-five-point-v1` alignment.

The generic historical `batch` defaults still use YuNet and must not be mistaken for the permanent archive profile.

The previous FP32-versus-INT8 embedding comparison used the earlier YuNet face population. If production model selection is reaffirmed after the detector change, evaluate the exact models on the selected CenterFace population before treating the old comparison as definitive for the permanent archive.

## Accepted future direction

ADR-0006 supersedes the earlier mandatory-human-confirmation rule. WI-0043 will add configurable confidence groups and optional canonical High-confidence automatic assignment. Automatic assignments will be auditable, can become exemplars on later regeneration runs and can be superseded by manual reassignment.

ADR-0007 records the stable archive root plus bounded local materialization architecture.

## Next concrete sequence

1. Complete WI-0054 archive viewer/progress/availability polish and verify the three reported post-acceptance cases.
2. Complete WI-0053 private real-archive HEIC verification and privacy-safe media inventory; if no RAW is present, record that fact rather than inventing RAW implementation evidence.
3. Confirm the version-1 success criteria on the real Windows/OneDrive environment.
4. Begin/continue the permanent catalogue from real archive coverage and expand it incrementally; do not create a replacement production database.

M17 review automation may be scheduled around this work, but it does not change the version-1 gate above.

## Completed gates

- The representative local acceptance pilot passed restart/resume, Windows/Pixel review, deterministic evaluation, backup and restore.
- SFace FP32 remains the current selected embedder pending any later CenterFace-population reaffirmation.
- CenterFace confidence `0.5`, single-pass, passed the governed M16 detector gate and migration-safety pilot.
- Collection-ready queries and neutral manifests are implemented.
- Operator/architecture documentation and clean-setup validation were previously completed; the later archive-readiness pass realigned them with the permanent-archive product direction.

## Relevant planning and operation files

- `docs/product/success-criteria.md`
- `docs/delivery/local-first-plan.md`
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
