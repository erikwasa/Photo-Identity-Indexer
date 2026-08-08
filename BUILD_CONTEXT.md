# Build context

## Current milestones

- **M15 — Operator documentation and system guide**: `completed`
- **M16 — Face detection recall**: `completed`
- **M12 — Full archive processing**: `in_progress` while WI-0042 completes the bounded-storage prerequisite for WI-0041 and WI-0023.

## Current work

**WI-0042 — Add bounded archive hydration and review proxies** is `in_progress`. Slice 1 is merged in PRs #105 and #106. The exact `jpeg-1600-q78` profile was measured on 556 private pilot images: 1,783,639,108 logical source bytes produced 112,900,614 proxy bytes, with mean 203,058.7 bytes, median 181,032 bytes, p95 400,427 bytes and a 15.798x source-to-proxy compression ratio. Only privacy-safe aggregate measurements are retained in Git.

Proxy-backed browsing is merged in PR #107. Explicit original hydration/release is merged in PR #109: normal original GETs do not hydrate, managed ownership is durable and fail-closed, pre-existing local/user-pinned files are never claimed, and exact original bytes are size/SHA-256 verified before serving.

The real Windows/OneDrive Slice 2 acceptance is **still pending by maintainer request**. Do not mark it passed merely because later automated Slice 3 work proceeds. When the maintainer is available, verify one real online-only original through proxy-no-hydration, explicit hydration, ready/view, managed release back to online-only, plus one pre-existing local/user-pinned original that remains unmanaged.

**Slice 3 is active on `agent/WI-0042-bounded-hydration` / PR #110.** It adds:

1. explicit configuration for minimum free-space reserve, maximum Photo-Identity-managed hydrated bytes and maximum concurrent managed operations;
2. no guessed production defaults: new managed hydration is disabled until all three limits are configured;
3. serialized admission that reserves the requested revision's full logical size before OneDrive pinning;
4. durable `last needed` tracking and least-recently-needed release requests under storage pressure;
5. release-requested bytes remain counted until OneDrive is actually observed online-only;
6. managed revision-verification reads are bounded by the same configured operation limit; and
7. `GET /api/archive/storage` reports privacy-safe logical-source, free-space, managed-original and configured review-proxy byte totals separately.

Focused tests cover disabled/unconfigured policy, free-space reserve rejection, concurrency rejection and least-recently-needed managed eviction. Existing explicit-original tests run under an explicit deterministic test policy rather than relying on production defaults.

Slice 3 still needs exact-head Windows CI plus an end-to-end bounded-processing integration proving cumulative logical source size can exceed the working-set budget while peak managed hydration remains bounded. Slice 4 then adds online-only source re-verification and real-machine/end-to-end acceptance before WI-0041 resumes real-archive verification.

The `jpeg-1600-q78` scale result is not hard-coded as a global proxy default yet. Before permanently freezing the profile, retain explicit human evidence that tuning candidates were compared and that the selected profile is visually acceptable for whole-photo browsing and identity-review context.

WI-0041 remains blocked by WI-0042. Its existing incremental archive coverage, availability and exact-analysis-profile work must remain intact; WI-0042 extends that archive model rather than creating a second catalogue.

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

## Relevant planning files

- `docs/delivery/work-items/WI-0042-bounded-archive-storage.md`
- `docs/delivery/work-items/WI-0041-incremental-archive-ingestion.md`
- `docs/operations/review-proxy-measurement.md`
- `docs/operations/review-proxy-serving.md`
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
