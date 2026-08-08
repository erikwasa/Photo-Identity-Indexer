# Build context

## Current milestones

- **M15 — Operator documentation and system guide**: `completed`
- **M16 — Face detection recall**: `completed`
- **M12 — Full archive processing**: `in_progress` while WI-0042 completes the bounded-storage prerequisite for WI-0041 and WI-0023.

## Current work

**WI-0032 — Validate documentation from a clean setup** is complete. The human maintainer confirmed the remaining clean-setup acceptance criteria on 2026-08-09; M15 is closed. Only privacy-safe completion evidence is retained in Git.

**WI-0042 — Add bounded archive hydration and review proxies** is `in_progress` on `agent/WI-0042-explicit-original-hydration`.

Slice 1 is merged in PRs #105 and #106. The exact `jpeg-1600-q78` profile was measured on 556 private pilot images: 1,783,639,108 logical source bytes produced 112,900,614 proxy bytes, with mean 203,058.7 bytes, median 181,032 bytes, p95 400,427 bytes and a 15.798x source-to-proxy compression ratio. Only these privacy-safe aggregate measurements are retained in Git.

The first Slice 2 increment is merged in PR #107. Collection contracts distinguish thumbnail, preview and authoritative-original resources. When `PhotoIdentity:ReviewProxyRoot` and `PhotoIdentity:ReviewProxyProfileId` are configured, normal collection thumbnails and previews use durable review proxies without requiring the original to remain local.

The active Slice 2 increment adds the explicit authoritative-original lifecycle:

1. `GET .../original` and legacy `.../content` never trigger hydration and serve bytes only after exact immutable revision size/SHA-256 verification;
2. `GET .../original/status` reports online-only, downloading, ready, releasing, hash-mismatch, unavailable or error state;
3. `POST .../original/hydrate` explicitly requests Windows OneDrive Files On-Demand hydration for an observed online-only item;
4. Photo Identity persists ownership only after that explicit request is accepted, so pre-existing local or already pinned/downloading files are never claimed; and
5. `POST .../original/release` fails closed unless Photo Identity has active durable ownership, then requests online-only state and clears ownership only after OneDrive reports the file online-only again.

The Windows implementation uses documented Files On-Demand pin attributes (`attrib +p` to request hydration, then `attrib -p` followed by `attrib +u` to release managed content). Pin/unpin transitions are asynchronous and are observed through the status endpoint. Command failures are path-free at the API boundary.

Automated tests cover pin-state classification, durable/restart-safe ownership, no implicit hydration on original GET, preservation of pre-existing pinned/local content, explicit managed release and rejection of wrong-hash local bytes.

## Next WI-0042 gate

After PR #109 is green and merged, run one private Windows/OneDrive acceptance check against a real online-only archive original:

1. confirm normal proxy preview does not hydrate the original;
2. call the explicit hydrate action and observe `online-only -> downloading -> ready`;
3. open the exact original and confirm the API serves it only after revision verification;
4. call release and observe `releasing -> online-only`; and
5. repeat with an already-local or user-pinned original and confirm Photo Identity reports it unmanaged and refuses automatic release.

Retain only pass/fail and aggregate/path-free evidence. Do not commit source paths, filenames, pixels or identity data.

Once that real Files On-Demand gate passes, Slice 3 adds configurable free-space reserve, managed-hydration byte budget, bounded concurrency, storage telemetry and safe policy-driven release/eviction. Slice 4 then adds online-only source re-verification and end-to-end local acceptance before WI-0041 resumes real-archive verification.

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
