# Bounded archive acceptance

This procedure is the human acceptance gate for WI-0042 after Slices 1–4 are merged. It validates review proxies, explicit originals, bounded hydration, source re-verification and end-to-end archive advancement together.

Do **not** record private source paths, filenames, pixels or identity data in Git. Retain only pass/fail outcomes and aggregate/path-free measurements.

## Before the review

The API host must be configured explicitly with:

```text
PhotoIdentity:ReviewProxyRoot
PhotoIdentity:ReviewProxyProfileId
PhotoIdentity:ReviewProxyMaximumLongEdge
PhotoIdentity:ReviewProxyJpegQuality
PhotoIdentity:ArchiveHydration:MinimumFreeSpaceReserveBytes
PhotoIdentity:ArchiveHydration:MaximumManagedHydrationBytes
PhotoIdentity:ArchiveHydration:MaximumConcurrentOperations
```

There are deliberately no production defaults for the hydration limits. Choose the values during the review based on the actual machine's storage constraints. The exact proxy dimensions and JPEG quality must match the registered proxy profile; the application does not infer encoder settings from the profile name.

Before permanently treating `jpeg-1600-q78` as the selected global profile, retain the missing human evidence that the fixed 100-image tuning set was compared across at least two candidates and that the selected candidate is visually usable for whole-photo browsing and identity-review context. The previously recorded 556-image aggregate scale run proves storage behavior, not visual acceptance.

## 1. Baseline status and storage

Start the local API and open the Archive page. Confirm that the configured archive root is represented only by its display name in the UI/API and that no absolute source path is returned.

Capture the privacy-safe aggregate responses:

```powershell
$Api = "http://localhost:5080"
Invoke-RestMethod "$Api/api/archive/status"
Invoke-RestMethod "$Api/api/archive/storage"
```

Verify that storage status separates logical source bytes, available free bytes, managed hydrated/downloading/releasing/reserved bytes and review-proxy bytes. Confirm the configured reserve, byte budget and concurrency values are the intended review values.

## 2. Proxy browsing must not hydrate originals

Choose an already-analysed image whose authoritative original is online-only and whose durable proxy exists.

Open the collection thumbnail and preview. Verify visually that the proxy is usable and verify in OneDrive/Explorer that the authoritative original remains online-only. Normal browsing must not request hydration.

The preview route must continue to work while the original route is unavailable because the authoritative bytes are not local.

## 3. Explicit original lifecycle

For one known online-only revision, use the explicit original workflow:

```powershell
$Revision = "<asset-revision-id>"
Invoke-RestMethod "$Api/api/collections/photos/$Revision/original/status"
Invoke-RestMethod -Method Post "$Api/api/collections/photos/$Revision/original/hydrate"
Invoke-RestMethod "$Api/api/collections/photos/$Revision/original/status"
```

Observe `online-only -> downloading -> ready`. Open the original only after `ready`; the API must validate exact immutable revision size and SHA-256 before serving it.

Then request release:

```powershell
Invoke-RestMethod -Method Post "$Api/api/collections/photos/$Revision/original/release"
```

Observe `releasing -> online-only`. Managed bytes must remain reserved while release is asynchronous and disappear only after OneDrive reports online-only.

Repeat with an original that was already local or user-pinned before Photo Identity touched it. It must remain unmanaged and automatic release must be refused.

## 4. Bounded capacity and release policy

Use storage telemetry while advancing enough private test items to exercise the configured working-set limit.

Verify all of the following:

- logical archive size may be much larger than the managed local working set;
- a hydration that would cross the configured free-space reserve is refused before pinning;
- a hydration that would cross the managed-byte budget is refused until sufficient managed release has actually completed;
- simultaneous managed downloads do not exceed the configured concurrency limit;
- policy-driven release chooses only Photo-Identity-owned content and never a pre-existing local/user-pinned file; and
- a release request is not counted as free capacity until the file is observed online-only.

## 5. Online-only source re-verification

Do not modify an authoritative production photo merely to test change detection. Use a disposable private OneDrive-backed test root/catalogue or a disposable copied test image.

Validate these cases:

1. Scan a locally available test image once so its immutable revision and lightweight size/last-write/media baseline are verified.
2. Make the test image online-only without changing it and sync again. The scanner must not open/hydrate the placeholder merely to compare metadata, and the source remains `verified` when the lightweight observation still matches the retained baseline.
3. In the disposable fixture, change the source and make it online-only. Sync must report `needs-source-verification`; metadata may enqueue verification but must not create a new immutable revision.
4. Use **Advance archive**. Photo Identity must request bounded hydration, hash authoritative local bytes with SHA-256, and only then establish or reselect the current immutable revision.
5. If a different revision is established while an older analysis run is active, that run must be cancelled before old queued work continues.
6. Confirm analysis and durable proxy generation proceed for the verified revision, and managed content is released only after downstream work no longer needs it.
7. Add a first-time online-only test image that has never been hashed. It must appear as `unverified`, use the same bounded source-verification hydration path, establish its first revision from local bytes, and then enter normal analysis/proxy processing.

The Archive page/item API should distinguish `verified`, `needs-source-verification` and `unverified` from OneDrive availability. These are different dimensions: an item can be online-only and still have a verified lightweight observation, or it can be online-only and require authoritative verification.

## 6. Restart and recovery

Interrupt the application during representative stages and restart it:

- managed hydration downloading;
- source verification after hydration was requested;
- analysis queued/running;
- analysis complete but proxy still missing; and
- managed release requested but not yet observed online-only.

After restart, verify durable ownership and completion state are preserved. Inference must not repeat merely because proxy generation or release was interrupted. Release ownership must not be forgotten while OneDrive is still transitioning.

## 7. Full archive advancement

After the focused checks pass, synchronize the intended permanent archive coverage and use **Advance archive** repeatedly. The operation is intentionally incremental: when OneDrive is still downloading a required source, the UI reports that state and a later advance continues from durable state.

Review aggregate status until there is no remaining source verification, pending/failed analysis or intended proxy work for the selected scope. Confirm ordinary collection browsing remains proxy-backed throughout.

## Completion evidence

WI-0042 can be completed only after the maintainer has reviewed the combined behavior above and the selected production policy/profile values are accepted. Record only:

- pass/fail for each section;
- chosen hydration policy values;
- selected exact proxy profile and confirmation of visual tuning acceptance;
- aggregate storage/working-set observations; and
- any path-free issue references.

After this gate passes, WI-0041 can resume real-archive verification using the bounded storage model rather than requiring the logical archive to fit on local disk.
