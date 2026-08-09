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

**WI-0042 — Add bounded archive hydration and review proxies** remains `in_progress` pending the combined real Windows/OneDrive human acceptance gate.

Merged implementation includes deterministic versioned review proxies, proxy-backed collection browsing, explicit original hydrate/status/view/release, managed hydration ownership, free-space/byte/concurrency policy, LRU release of Photo-Identity-owned content, source-verification state and bounded first-time online-only source verification.

PR #111 merged the source re-verification slice. The combined acceptance is documented in `docs/operations/bounded-archive-acceptance.md`; do not infer that the real-machine gate passed merely from automated coverage.

**WI-0041 — Add incremental permanent archive ingestion** remains blocked on WI-0042 acceptance.

**WI-0053 — Add HEIC and archive RAW image support** is newly scoped under M12 and is unblocked by its completed decoder/scanner prerequisites. It inventories the real archive and adds HEIC/HEIF plus every RAW variant actually present before version 1 is declared archive-ready.

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

1. Complete the combined WI-0042 human acceptance and deliberately accept the production proxy/hydration policy values.
2. Unblock and complete WI-0041 permanent incremental ingestion.
3. Implement/verify WI-0053 against private representative HEIC and RAW files from the real archive.
4. Confirm the version-1 success criteria on the real Windows/OneDrive environment.
5. Begin the permanent catalogue from real archive coverage and expand it incrementally; do not create a replacement production database.

M17 review automation may be scheduled around this work, but it does not change the version-1 gate above.

## Completed gates

- The representative local acceptance pilot passed restart/resume, Windows/Pixel review, deterministic evaluation, backup and restore.
- SFace FP32 remains the current selected embedder pending any later CenterFace-population reaffirmation.
- CenterFace confidence `0.5`, single-pass, passed the governed M16 detector gate and migration-safety pilot.
- Collection-ready queries and neutral manifests are implemented.
- Operator/architecture documentation and clean-setup validation were previously completed; this pass realigns them with the permanent-archive product direction.

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