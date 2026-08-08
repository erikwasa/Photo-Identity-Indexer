# Build context

## Current milestones

- **M15 — Operator documentation and system guide**: `completed`
- **M16 — Face detection recall**: `completed`
- **M12 — Full archive processing**: `blocked` while WI-0042 completes the bounded-storage prerequisite for WI-0041 and WI-0023.

## Current work

**WI-0032 — Validate documentation from a clean setup** is complete. On 2026-08-09 the human maintainer confirmed that the remaining clean-setup documentation validation acceptance criteria were satisfied. The repository retains only privacy-safe completion evidence; detailed local validation notes remain outside Git. Completion also closes M15.

**WI-0042 — Add bounded archive hydration and review proxies** is the next active M12 prerequisite. Slice 1 is merged in PRs #105 and #106. The exact `jpeg-1600-q78` profile was measured on 556 private pilot images: 1,783,639,108 logical source bytes produced 112,900,614 proxy bytes, with mean 203,058.7 bytes, median 181,032 bytes, p95 400,427 bytes and a 15.798x source-to-proxy compression ratio. Only these privacy-safe aggregate measurements are retained in Git.

The first Slice 2 increment is merged in PR #107. Collection contracts now distinguish thumbnail, preview and authoritative-original resources. When `PhotoIdentity:ReviewProxyRoot` and `PhotoIdentity:ReviewProxyProfileId` are configured, normal collection thumbnails and previews use durable review proxies without requiring the original to remain local. `/original` is the canonical explicit original route and `/content` remains its compatibility alias; neither falls back to a proxy.

The next concrete WI-0042 step is the remaining Slice 2 original-access workflow:

1. explicitly request hydration for an online-only OneDrive original rather than recalling it through a normal image GET;
2. report original availability/hydration state to the operator;
3. verify the hydrated bytes against the immutable revision SHA-256 before serving the original;
4. distinguish Photo-Identity-owned hydration from content that was already local or user-pinned; and
5. release/dehydrate only Photo-Identity-owned hydration.

Slice 3 then adds configurable free-space reserve, managed-hydration byte budget, bounded concurrency and safe release/eviction. Slice 4 adds online-only source re-verification, storage telemetry and end-to-end local acceptance before WI-0041 resumes real-archive verification.

The `jpeg-1600-q78` scale result is not hard-coded as a global proxy default yet. Before permanently freezing the profile, retain explicit human evidence that the tuning candidates were compared and that the selected profile is visually acceptable for whole-photo browsing and identity-review context.

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
