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

PR #111 merged the source re-verification slice. The first combined Windows/OneDrive acceptance attempt was paused on 2026-08-10 after the maintainer found three acceptance blockers: archive advancement required repeated per-transition clicks, Collections opened the fixed thumbnail instead of a proxy-first viewer and had no original controls, and analysed online-only revisions disappeared from the Archive analysed filter.

WI-0042 Slice 5 is active on `agent/WI-0042-slice5-unattended-archive` / draft PR #115. It adds durable one-click unattended advancement, proxy-first viewing with explicit original controls and orthogonal archive status. The combined acceptance remains pending and is documented in `docs/operations/bounded-archive-acceptance.md` plus `docs/operations/bounded-archive-slice5.md`.

**WI-0041 — Add incremental permanent archive ingestion** remains blocked on WI-0042 acceptance.

**WI-0053 — Add HEIC and archive RAW image support** is now being implemented on `agent/WI-0053-heic-support` / draft PR #114. Current maintainer evidence says the full archive contains a few HEIC files and no known RAW files. Slice 1 therefore makes HEIC/HEIF a first-class permanent-archive input while deliberately leaving RAW variants unsupported until an actual archive RAW format and private representative sample exist.

The Slice 1 implementation:

- recognizes HEIC/HEIF in local and OneDrive-aware source scanning;
- uses a bundled HEIF decoder behind the existing `IImageDecoder` boundary while preserving the established JPEG/PNG OpenCV path;
- shares that decoded-pixel path with durable review-proxy rendering;
- includes HEIC/HEIF in archive proxy measurement;
- adds a privacy-safe `archive inventory` command that reports aggregate extension/media-family/support counts without opening image content;
- keeps unverified RAW media visible as unsupported rather than silently accepting it; and
- adds automated source-recognition, inventory/privacy, HEIC read-delegate and corrupt-container coverage.

A valid real HEIC binary is intentionally not committed solely for test coverage. Do not mark WI-0053 complete from CI alone. Private real-archive HEIC verification must still confirm actual decode, orientation, downstream CenterFace/SFace processing, durable proxy behavior, color appearance and representative runtime/memory. RAW format-specific verification becomes active when the archive actually contains RAW media.

**WI-0043 — Add configurable confidence groups and canonical auto-assignment** is implemented on `wi-0043` / PR #116 targeting the M17 integration branch. The implementation persists an independent versioned suggestion policy for each exact embedding-model revision, keeps automatic assignment disabled by default, defines High using both rank-1 score and the rank-1/rank-2 score gap, exposes High/Medium/Low grouping in the unified Faces queue, and promotes only qualifying High suggestions after a fixed scoring snapshot when enabled. Automatic decisions retain exact model, score, margin, threshold and policy-version provenance; later manual reassignment becomes the active exemplar identity without deleting history. Schema version 11 owns the exact-model policy table so policy persistence follows the normal catalogue migration lifecycle.

Automated coverage includes score and rank-gap boundaries, missing rank-2 margin, exact-model policy isolation/versioning, toggle behavior, fixed-snapshot non-cascade behavior, queue filtering/order, provenance, threshold-history preservation and manual supersession. Before routine auto-assignment is enabled, the maintainer must still tune representative score and rank-gap thresholds against a private reviewed sample. Until that human verification is complete, WI-0043 should remain in review rather than completed.

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

ADR-0006 supersedes the earlier mandatory-human-confirmation rule. WI-0043 implements configurable High/Medium/Low suggestion groups plus optional canonical High-confidence automatic assignment. Each exact embedding-model revision owns its own policy/version stream. High requires both an absolute rank-1 score threshold and a configurable minimum rank-1/rank-2 gap. Automatic assignments are auditable, can become exemplars on later regeneration runs and can be superseded by manual reassignment.

ADR-0007 records the stable archive root plus bounded local materialization architecture.

## Next concrete sequence

1. Complete WI-0042 Slice 5 / PR #115 and resume the paused combined Windows/OneDrive acceptance.
2. Complete the combined WI-0042 human acceptance and deliberately accept the production proxy/hydration policy values.
3. Unblock and complete WI-0041 permanent incremental ingestion.
4. Confirm the version-1 success criteria on the real Windows/OneDrive environment, including a privacy-safe archive media inventory; if no RAW is present, record that fact rather than inventing RAW implementation evidence.
5. Begin the permanent catalogue from real archive coverage and expand it incrementally; do not create a replacement production database.
6. In parallel with the version-1 gate, review WI-0043 / PR #116 and tune High score, High rank-gap and Medium thresholds on a private reviewed sample before enabling automatic assignment for routine archive use.

M17 review automation does not change the version-1 gate above.

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
- `docs/delivery/work-items/WI-0043-confidence-auto-assignment.md`
- `docs/delivery/work-items/WI-0053-heic-raw-support.md`
- `docs/architecture/identity-matching.md`
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
