---
id: WI-0042
title: Add bounded archive hydration and review proxies
milestone: M12
status_source: ../status/work-items.yaml
depends_on: [WI-0014, WI-0025]
affected_modules: [PhotoIdentity.Core, PhotoIdentity.Imaging.OpenCv, PhotoIdentity.Source.OneDriveSync, PhotoIdentity.Persistence.Sqlite, PhotoIdentity.Worker, PhotoIdentity.Cli, PhotoIdentity.Api, PhotoIdentity.Web]
---

# WI-0042: Add bounded archive hydration and review proxies

## Objective

Allow the permanent catalogue to behave as though the complete OneDrive-backed archive has been processed without requiring the complete original archive to remain hydrated on local storage.

The target machine may have substantially less free local disk space than the logical archive size. The initial permanent-archive environment has roughly 150 GB of free local space for an archive of roughly 330 GB, so full hydration cannot be a prerequisite for ingestion, analysis, review or later browsing.

The steady-state storage model is:

1. the OneDrive original remains the authoritative full-resolution source and may normally be `OnlineOnly` after processing;
2. Photo Identity permanently retains catalogue metadata, identities, review history, embeddings and face crops;
3. Photo Identity permanently retains one compact, versioned review proxy for each processed immutable image revision; and
4. full-resolution originals are hydrated only as a bounded working set for analysis or an explicit operator request, then may be released again when Photo Identity owns that hydration.

This work is a prerequisite for the final real-archive verification in WI-0041. It must preserve the source identity and immutable-revision semantics already established by WI-0041 rather than creating a second proxy catalogue or treating reduced-resolution images as new source assets.

## Storage tiers

### Authoritative original

The original file under the permanent OneDrive archive root remains canonical for content hashing, first-time detector analysis, future detector reconciliation and explicit full-resolution viewing. A proxy must never replace the original as the immutable source of truth.

An original may be locally available, downloading, online-only, unavailable or in an availability-error state. Returning a successfully analysed original to `OnlineOnly` must not invalidate its immutable revision, face data, identity review state or exact-profile analysis completion.

### Permanent review proxy

A review proxy is a compact derivative of one immutable asset revision. It is intended for collection browsing, normal photo viewing, context around detected faces and ordinary identity-review workflows while the source original is online-only.

Proxy identity must be versioned independently of the source revision. Durable metadata must identify at least:

- source asset revision;
- proxy protocol/version;
- encoder/format and quality settings;
- maximum dimensions or equivalent resize policy;
- encoded byte length;
- proxy content SHA-256;
- generated-at timestamp; and
- local storage path relative to a configured derivative/output root.

Generating the same proxy protocol for the same immutable revision must be idempotent. Proxy output must live outside the OneDrive source root.

### Temporary hydrated original

Original hydration is a bounded cache/working-set concern, not permanent catalogue state. Photo Identity must distinguish originals that were already local or user-pinned from originals that Photo Identity explicitly requested to hydrate.

Automatic release/dehydration is permitted only for content whose hydration Photo Identity owns and only after all required durable work for that operation has committed successfully. The application must not silently undo the maintainer's own `Always keep on this device` or equivalent choice.

## Processing workflow

For a source revision that requires first-time analysis and is not local, the intended bounded workflow is:

```text
select pending revision
-> verify disk budget
-> explicitly request OneDrive hydration
-> wait until the original is local
-> verify/hash the authoritative original
-> run governed detector + embedder analysis
-> persist analysis outputs and completion
-> generate/persist the review proxy
-> verify durable proxy metadata
-> release the original if Photo Identity owns the hydration and policy allows it
-> continue with the next revision
```

Analysis completion and proxy completion are separate durable states. If detector/embedder analysis succeeds but proxy encoding or storage fails, retrying the proxy must not rerun successful inference. Likewise, an already-complete exact analysis profile must not be invalidated when its source returns to `OnlineOnly`.

Future detector changes continue to require the authoritative original and the established reconciliation workflow. Embedder-only changes should prefer retained deterministic face crops/alignment inputs when the governed model/protocol permits that, avoiding unnecessary original hydration.

## Explicit full-resolution viewing

Normal collection thumbnails, normal photo display and review surfaces should use the permanent proxy when available. Existing collection contracts must not silently pretend a proxy is the original; API/UI contracts should distinguish preview/proxy content from full-resolution original content where that distinction is externally observable.

Full-resolution viewing must be an explicit action such as `View original`:

1. if the authoritative original is already local, serve it after revision/content validation;
2. if it is online-only, explicitly request hydration and expose progress/state to the operator;
3. never trigger hydration merely because a normal `<img>`/GET request touched a placeholder;
4. after viewing, allow an explicit `Release original` action and/or policy-driven cleanup for Photo-Identity-managed hydration only.

## Disk-capacity policy

The application must enforce a configurable local-storage budget before it begins managing archive hydration. At minimum the policy must support:

- a minimum free-space reserve that Photo Identity will not intentionally cross;
- a maximum byte budget for Photo-Identity-managed hydrated originals;
- a maximum number of concurrent hydrations/large-file operations;
- reporting of current managed hydrated bytes, permanent proxy bytes and relevant derivative/output bytes; and
- safe refusal/pausing with an actionable state when a requested hydration would violate the configured reserve.

The initial values must be configurable rather than hard-coded for one machine. Defaults may be selected only after representative proxy-size measurements and local verification.

When capacity must be reclaimed, eviction should prefer the least recently needed Photo-Identity-managed hydrated originals. Eviction must never delete the OneDrive placeholder, catalogue metadata, immutable revision, proxy, face crops, embeddings or human review data.

## Source-change detection while online-only

A previously verified source can change while its bytes are no longer local. Synchronization should retain lightweight source observations that are available without hydration, such as logical size, last-write metadata, availability and observation time.

If those observations differ from the metadata associated with the last verified revision, the asset must enter an explicit state such as `needs-source-verification`. It must be hydrated and hashed before the catalogue decides whether the existing immutable revision is still current or a new revision is required. Metadata alone must not be treated as a replacement for the content hash.

## Proxy-size measurement gate

Proxy defaults must be selected from measurements rather than guessed.

The maintainer does **not** need to measure proxy size before implementation begins. The first implementation slice should provide a deterministic proxy generator plus an aggregate measurement/report command or equivalent operator path. Measurement happens immediately after that slice and before the proxy profile is frozen as the permanent default.

Use the existing private corpora in two stages:

1. **100-image detector-evaluation set — tuning sample.** Generate at least two reasonable candidate proxy profiles and compare aggregate encoded size plus human review usability. Candidate dimensions/quality should remain configurable during this experiment; expected starting points are roughly a 1600-2048 pixel long edge with a web-friendly lossy format.
2. **560-image pilot set — scale validation.** After choosing the preferred candidate from the 100-image set, generate that exact proxy profile for the larger pilot corpus and record aggregate storage statistics to estimate permanent-catalogue proxy cost.

The aggregate report should include at least source image count, total logical source bytes, total proxy bytes, mean/median/p95 proxy bytes, compression ratio and the exact proxy protocol/settings. Private filenames, pixels and identity data must not be committed to Git.

Before finalizing the proxy protocol, the maintainer should visually review a representative subset for normal whole-photo browsing and identity-review context. This is a review/display quality decision, not a detector-accuracy benchmark; canonical detector analysis continues to use the authoritative original.

## Acceptance criteria

- [ ] One durable, versioned review proxy can be generated idempotently for an immutable source revision and stored outside the OneDrive source root.
- [ ] Proxy metadata includes the exact derivative protocol/settings, encoded size and content hash, and proxy completion is tracked separately from detector/embedder analysis completion.
- [ ] Normal collection/review browsing remains usable when the authoritative original is `OnlineOnly`, using the stored proxy without hydrating the original.
- [ ] Full-resolution viewing is an explicit operator action and never occurs as an accidental side effect of a normal thumbnail/photo GET.
- [ ] Photo Identity can explicitly hydrate an online-only authoritative original, wait for local availability, verify/process it, and later release it when and only when Photo Identity owns that hydration.
- [ ] Files already local or user-pinned before Photo Identity touches them are never automatically released by Photo Identity.
- [ ] Archive processing enforces configurable free-space reserve, managed-hydration byte budget and concurrency limits before requesting more source content.
- [ ] A successfully analysed revision can return to `OnlineOnly` without losing its exact-profile completion, face data, embeddings, identities, review history or proxy.
- [ ] A proxy-generation failure can be retried without rerunning already-successful detector/embedder inference.
- [ ] Lightweight source observations can mark an online-only previously verified asset as needing source verification; content is hydrated and rehashed before a new immutable revision is decided.
- [ ] Archive/UI status reports permanent proxy storage and managed hydrated-original storage separately from logical source size.
- [ ] A bounded-processing integration test proves that cumulative logical source size may exceed the configured managed working-set budget while peak Photo-Identity-managed hydration remains within that budget.
- [ ] The 100-image evaluation set is used to choose the permanent proxy profile from measured candidates, with privacy-safe aggregate evidence retained.
- [ ] The chosen proxy profile is validated on the 560-image pilot set and an aggregate storage estimate is recorded before real full-archive verification.
- [ ] WI-0041 real-archive verification resumes only after the bounded storage/hydration workflow is implemented and locally verified.

## Planned implementation slices

### Slice 1 — proxy derivative model and measurement tooling

Add durable proxy metadata/state, deterministic proxy rendering and an operator measurement path that can generate candidate profiles and report aggregate storage. This slice triggers the 100-image proxy measurement gate; the maintainer should perform the first measurement immediately after this slice is merged and runnable locally.

### Slice 2 — proxy-backed browsing and explicit original access

Serve proxies for normal review/collection use, keep original/proxy semantics explicit in API contracts, and add explicit hydrate/view/release-original operator actions without accidental placeholder hydration.

### Slice 3 — bounded hydration orchestration

Add Photo-Identity-managed hydration ownership, free-space/budget checks, bounded concurrency, safe release/eviction and resumable archive processing that can advance through a logical archive larger than local storage.

### Slice 4 — source re-verification, telemetry and local acceptance

Add online-only source-change/reverification state, storage telemetry and end-to-end verification using the selected proxy profile. Validate the chosen settings on the 560-image pilot corpus before WI-0041 proceeds to the real archive progression.

## Scope boundary

This work does not migrate the disposable 560-image pilot catalogue into the permanent catalogue, upload proxies to OneDrive, replace originals with lossy content, or make proxies authoritative detector inputs. The two existing private image sets are measurement/acceptance corpora only; only privacy-safe aggregate results belong in repository evidence.
