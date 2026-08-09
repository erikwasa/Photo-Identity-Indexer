# Build context

## Current milestones

- **M15 — Operator documentation and system guide**: `completed`
- **M16 — Face detection recall**: `completed`
- **M12 — Full archive processing**: `in_progress` while WI-0042 completes the bounded-storage prerequisite for WI-0041 and WI-0023.

## Current work

**WI-0042 — Add bounded archive hydration and review proxies** remains `in_progress` until the maintainer performs the combined Slices 1–4 review/real-machine acceptance.

Merged implementation:

- Slice 1: PRs #105/#106 — exact versioned review-proxy generation, durable proxy completion and measurement tooling.
- Slice 2: PR #107 — proxy-backed collection thumbnails/previews and explicit authoritative-original resource semantics.
- Slice 2: PR #109 — explicit OneDrive original hydrate/status/view/release with durable ownership and exact revision verification.
- Slice 3: PR #110 — explicit free-space/managed-byte/concurrency policy, aggregate storage telemetry, LRU release of Photo-Identity-owned content, bounded archive analysis hydration, exact proxy generation settings, and analysis/proxy completion separation.

The private 556-image `jpeg-1600-q78` scale run recorded 1,783,639,108 logical source bytes and 112,900,614 proxy bytes, with mean 203,058.7 bytes, median 181,032 bytes, p95 400,427 bytes and a 15.798x source-to-proxy compression ratio. The application still does not hard-code that profile as a production default because explicit human evidence for the earlier 100-image multi-candidate visual tuning decision has not yet been retained.

**Slice 4 is active on `agent/WI-0042-source-reverification` / PR #111.** It closes the remaining source-identity gap:

1. archive sync retains lightweight size/last-write/media observations without opening OneDrive placeholders;
2. previously verified sources become `needs-source-verification` when lightweight metadata diverges, while first-time online-only sources are `unverified`;
3. metadata never creates an immutable revision: only a local SHA-256 read may establish/reselect the authoritative revision;
4. verification requirements are sticky until authoritative bytes are successfully hashed, so later matching metadata cannot clear a prior divergence;
5. first-time/unverified online-only items can use temporary asset-level managed hydration under the same free-space, managed-byte, concurrency and LRU policy as revision-level hydration;
6. once SHA-256 establishes/reselects a revision, managed ownership transfers atomically to that revision so analysis/proxy/release can continue without double hydration;
7. analysis scheduling excludes `needs-source-verification` and `unverified` sources; an exact pre-analysis/proxy hash mismatch explicitly re-enters the source-verification queue;
8. if re-verification establishes a different current revision, any active analysis run queued against the prior revision is cancelled before it resumes;
9. Archive API/UI expose source-verification state separately from OneDrive availability; and
10. **Advance archive** remains available for source verification, online-only analysis and durable proxy retry rather than being disabled when local `PendingImages` is zero.

Focused automated coverage proves that placeholder scans do not open content, metadata divergence requires re-verification, a first-time online-only item is hydrated/hashed and transfers managed ownership to its new revision, and pre-revision hydration obeys the same managed-byte budget. Existing Slice 3 working-set/capacity tests continue under the shared revision/source budget.

## Human acceptance gate

The real Windows/OneDrive acceptance from Slices 2–3 was intentionally deferred by the maintainer and is still **not passed**. After PR #111 is merged, perform the combined review in `docs/operations/bounded-archive-acceptance.md` rather than validating the slices separately.

That review covers:

- the missing 100-image multi-candidate proxy visual acceptance evidence;
- normal proxy browsing without original hydration;
- explicit original `online-only -> downloading -> ready -> releasing -> online-only` behavior;
- preservation of pre-existing local/user-pinned originals;
- configured free-space reserve, managed-byte budget, concurrency and LRU release behavior;
- source re-verification using a disposable OneDrive-backed fixture rather than modifying authoritative production photos;
- restart/recovery across hydration, verification, analysis, proxy and release stages; and
- end-to-end bounded archive advancement with privacy-safe aggregate evidence only.

WI-0041 remains blocked until that combined gate passes and the production hydration policy/profile values are deliberately accepted.

## Completed gates

- The 450–550-image local acceptance pilot passed restart/resume, cross-device review, deterministic export/evaluation, backup and restore.
- SFace FP32 remains the selected local embedder after the governed FP32-versus-INT8 comparison.
- CenterFace confidence `0.5`, single-pass, passed the governed M16 detector gate and its rollout/reconciliation was verified; M16 is complete.
- Collection-ready queries and the neutral manifest passed automated validation plus private Windows/Pixel verification.
- The operator/architecture documentation rewrite and independent clean-setup validation are complete; M15 is complete.
- WI-0042 deterministic review-proxy generation, durable proxy metadata and measurement tooling are merged.
- The private 556-image `jpeg-1600-q78` scale validation recorded a 15.798x source-to-proxy compression ratio.
- Proxy-backed collection browsing and explicit preview/original API semantics are merged in PR #107.
- The automated/core explicit-original lifecycle is merged in PR #109; its real OneDrive acceptance remains pending.
- Slice 3 bounded storage/orchestration is merged in PR #110; its production policy tuning remains part of the combined acceptance.

## Relevant planning files

- `docs/delivery/work-items/WI-0042-bounded-archive-storage.md`
- `docs/delivery/work-items/WI-0041-incremental-archive-ingestion.md`
- `docs/operations/review-proxy-measurement.md`
- `docs/operations/review-proxy-serving.md`
- `docs/operations/bounded-archive-acceptance.md`
- `docs/delivery/status/work-items.yaml`
- `docs/delivery/status/milestones.yaml`

## Automated validation

```powershell
./build.ps1
./test.ps1
./verify-local.ps1 -InstallModels
./verify-review.ps1 -Mode Smoke -Configuration Release
dotnet run --project tools/PhotoIdentity.Docs -- validate
dotnet run --project tools/PhotoIdentity.Docs -- generate --check
```
