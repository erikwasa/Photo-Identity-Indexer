# Build context

## Current product target

Version 1 is reached when the permanent catalogue can safely **begin** processing the real full archive. Version 1 does not require every archive asset to have finished processing.

The version-1 archive-readiness gates are:

1. WI-0042 — bounded hydration, source re-verification and durable review proxies;
2. WI-0041 — stable permanent archive identity and incremental no-repeat ingestion after WI-0042 is accepted; and
3. WI-0053 — HEIC/HEIF plus every RAW variant required by the real archive, with explicit unsupported state for any deliberate exception.

All three archive-readiness work-item gates are now human-verified. The remaining version-1 step is to confirm the product success criteria on the real Windows/OneDrive environment after the active archive-UI follow-up is accepted.

See `docs/product/success-criteria.md` and `docs/delivery/local-first-plan.md` for the durable product/delivery definition.

## Current milestones

- **M12 — Full archive processing**: `in_progress`. The version-1 archive-readiness work-item gates are complete, while M12 continues through later full-coverage completion.
- **M17 — Identity review automation**: `in_progress`. Integrated review automation remains under milestone-wide human verification.
- **M18 — Operator application experience**: implementation is progressing on the isolated `m18` integration branch while M17 verification continues. WI-0048 and WI-0046 are merged into `m18`; WI-0051 is the active implementation item and WI-0052 follows it.
- **M09 — Azure VM pilot without identities**: `ready` and optional; Azure is not required for version 1.

## Active work

**WI-0051 — Add a one-click Windows launcher** is the active M18 work item on `agent/WI-0051-windows-launcher`, based on the merged WI-0046 `m18` head. It wraps the existing framework-dependent published API/Blazor application rather than introducing a new runtime. The intended operator path is a double-clickable `.cmd` entry point backed by a reviewable PowerShell launcher, private local bootstrap configuration, loopback-only hosting, `/health` readiness, duplicate-instance prevention and actionable startup errors. A dedicated Windows CI verification publishes to disposable output, launches twice and asserts one healthy server process remains. Human Windows Explorer verification against a clean publish remains required before completion.

**WI-0054 — Polish archive viewing, progress and availability** is `in_review`. Its implementation was merged in PR #118 after the maintainer completed real Windows/OneDrive verification of WI-0042 and WI-0041 on 2026-08-10; human verification of the three follow-up cases remains the completion step.

The focused follow-up fixes three post-acceptance usability inconsistencies: local verified pending revisions need a safe viewer fallback before their durable proxy exists; latest-run progress must be distinguished from cumulative archive analysis; and explicit original status/hydration observations must reconcile the persisted Archive availability state. The accepted WI-0042 managed-release and bounded-storage policy remains unchanged.

**M17 integrated review automation** remains under milestone-wide verification. WI-0043 provides exact-model configurable confidence groups and optional canonical High-confidence auto-assignment using both rank-1 score and rank-1/rank-2 gap; automatic assignment remains disabled by default pending representative private-sample threshold tuning. WI-0044 adds favorite people without changing matcher evidence, and WI-0047 adds the reversible Unknown review state without creating synthetic identities. Desktop and narrow/mobile integrated UI verification remains before these items are completed.

**WI-0053 — Add HEIC and archive RAW image support** is `completed` and human-verified as of 2026-08-11. HEIC/HEIF is supported through the production image contract and review-proxy path, privacy-safe archive inventory/reporting is in place, representative private HEIC files were successfully decoded with correct visual output and unchanged originals, and the current archive has no known RAW variants. A future newly observed RAW format reopens only that format-specific decoder/verification requirement rather than invalidating the current completion evidence.

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

ADR-0006 supersedes the earlier mandatory-human-confirmation rule and is implemented by WI-0043. Suggestion policy is scoped to an exact embedding-model revision, High confidence requires both absolute rank-1 score and a configurable rank-1/rank-2 gap, and optional automatic assignments use the canonical acceptance boundary with audit provenance. Automatic assignments can become exemplars only on later regeneration runs and can be superseded by manual reassignment. The feature remains disabled by default until the maintainer accepts tuned thresholds on a representative private reviewed sample.

ADR-0007 records the stable archive root plus bounded local materialization architecture.

## Next concrete sequence

1. Complete WI-0051 automated validation, merge it into `m18`, then verify the one-click launcher from Windows Explorer against a clean published output.
2. Proceed to WI-0052 packaged/self-contained Windows application work after WI-0051 is accepted, continuing to keep M18 outside `main` until the milestone integration boundary is ready.
3. Continue milestone-wide M17 verification of confidence grouping/auto-assignment safety, favorite people, Unknown behavior and web regeneration on desktop and narrow/mobile review surfaces.
4. Complete human verification of WI-0054 archive viewer/progress/availability polish for the three reported post-acceptance cases.
5. Confirm the version-1 success criteria on the real Windows/OneDrive environment and continue permanent-catalogue archive coverage incrementally; do not create a replacement production database.

## Completed gates

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
- `docs/delivery/milestones/M18-application-experience.md`
- `docs/delivery/work-items/WI-0043-confidence-auto-assignment.md`
- `docs/delivery/work-items/WI-0044-favorite-people.md`
- `docs/delivery/work-items/WI-0045-web-match-regeneration.md`
- `docs/delivery/work-items/WI-0046-simplified-application-shell.md`
- `docs/delivery/work-items/WI-0048-configuration-page.md`
- `docs/delivery/work-items/WI-0051-one-click-windows-launcher.md`
- `docs/delivery/work-items/WI-0052-packaged-windows-application.md`
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
./verify-launcher.ps1 -Configuration Release
dotnet run --project tools/PhotoIdentity.Docs -- validate
dotnet run --project tools/PhotoIdentity.Docs -- generate --check
```
