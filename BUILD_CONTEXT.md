# Build context

## Current milestones

- **M15 — Operator documentation and system guide**: `completed`
- **M16 — Face detection recall**: `completed`
- **M12 — Full archive processing**: `in_progress` while WI-0042 completes the bounded-storage prerequisite for WI-0041 and WI-0023.

## Current work

**WI-0042 — Add bounded archive hydration and review proxies** is `in_progress`. Slice 1 is merged in PRs #105 and #106. The exact `jpeg-1600-q78` profile was measured on 556 private pilot images: 1,783,639,108 logical source bytes produced 112,900,614 proxy bytes, with mean 203,058.7 bytes, median 181,032 bytes, p95 400,427 bytes and a 15.798x source-to-proxy compression ratio. Only privacy-safe aggregates are retained in Git.

Proxy-backed browsing is merged in PR #107. Explicit original hydration/release is merged in PR #109: normal original GETs do not hydrate, managed ownership is durable and fail-closed, pre-existing local/user-pinned files are never claimed, and original bytes are size/SHA-256 verified before serving.

The real Windows/OneDrive Slice 2 acceptance is **still pending by maintainer request**. Do not mark it passed because later automated work proceeds. When the maintainer is available, verify proxy-no-hydration, explicit `online-only -> downloading -> ready`, verified original viewing, managed release back to online-only, and preservation of a pre-existing local/user-pinned original.

**Slice 3 is active on `agent/WI-0042-bounded-hydration` / PR #110.** Current implementation adds:

1. explicit minimum-free-space reserve, maximum managed-hydration bytes and maximum concurrent managed operations; no production values are guessed and managed hydration is disabled until all three are configured;
2. serialized admission reserving each requested immutable revision's full logical size before OneDrive pinning;
3. durable `last needed` tracking and least-recently-needed release of Photo-Identity-owned local originals under capacity pressure;
4. asynchronous releases continue counting against the working set until OneDrive is observed online-only;
5. managed revision-verification reads are bounded and `GET /api/archive/storage` reports logical source, free space, managed local/downloading/releasing/reserved and selected proxy bytes separately;
6. automatic archive analysis can select already-versioned online-only/downloading revisions, request bounded hydration, persist observed availability transitions and proceed without a manual sync once local;
7. exact proxy generation settings are explicit (`ReviewProxyMaximumLongEdge` and `ReviewProxyJpegQuality`) rather than inferred from `ReviewProxyProfileId`;
8. analyzed current revisions missing the selected proxy are handled before more inference, so a proxy failure is retried from durable analysis completion rather than rerunning detector/embedder work; and
9. after proxy durability, release is requested only when Photo Identity owns the hydration.

Automated coverage includes unconfigured-policy refusal, free-space reserve refusal, concurrency refusal, LRU managed eviction, a cumulative logical working set larger than the managed-byte budget while peak reservation remains bounded, explicit-original compatibility under deterministic policy, and local-only versus hydratable archive selection.

Exact-head Windows CI is still required before PR #110 is review-ready. Slice 4 then adds online-only source-change/re-verification state and real-machine/end-to-end acceptance/policy tuning before WI-0041 resumes real-archive verification.

The `jpeg-1600-q78` scale result is not hard-coded as a global proxy default. Before permanently freezing the profile, retain explicit human evidence that tuning candidates were compared and that the selected profile is visually acceptable for whole-photo browsing and identity-review context.

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
