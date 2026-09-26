---
id: WI-0164
title: Add verified DNG archive support
milestone: M32
status_source: ../status/work-items.yaml
depends_on: [WI-0053]
related_adrs: [ADR-0007]
affected_modules: [PhotoIdentity.Core, PhotoIdentity.Imaging.OpenCv, PhotoIdentity.Source.Local, PhotoIdentity.Source.OneDriveSync, PhotoIdentity.Worker, PhotoIdentity.Api, PhotoIdentity.Integration.Tests, PhotoIdentity.Recognition.Tests, docs]
---

# WI-0164: Add verified DNG archive support

## Objective

Support the `.dng` files that are now present in the maintained archive as normal image assets, completing the format-specific RAW follow-up deliberately deferred by WI-0053 until a real RAW variant and private sample existed.

DNG support must cover discovery, catalogue ingestion, metadata, deterministic rendered pixels, review/viewer proxies and governed face processing. It is not sufficient to make a standalone decoder accept a DNG while normal archive synchronisation still ignores the file.

## Scope

- Treat `.dng` as an eligible image extension in the local-folder and OneDrive-synchronised source boundaries once the production decode path is verified.
- Change privacy-safe archive inventory classification so DNG reports `family=raw supported=true` only after the end-to-end path is proven.
- Add an isolated DNG decoding/rendering path that produces the existing deterministic BGR/RGB application image contract.
- Verify orientation, dimensions and colour handling against representative private DNG files from the maintained archive.
- Make the rendering policy explicit: determine from real samples whether the production path uses a full RAW render, an embedded preview, or a bounded combination. The selected policy must be deterministic and sufficiently detailed for review proxies and face processing.
- Keep the original DNG immutable and never write converted JPEG/TIFF/DNG files beside the source original.
- Verify that capture date, dimensions and available GPS/EXIF metadata are extracted correctly where the DNG contains them; absence of metadata remains valid and must not be invented.
- Produce normal thumbnails/review proxies for DNG revisions so browsers do not need native DNG support.
- Allow DNG revisions to run through the same governed detector/alignment/embedding pipeline as other supported images.
- Preserve existing content-hash, immutable revision, retry and duplicate semantics.
- Ensure DNG files that already exist in configured source folders are discovered by a normal rescan/reconciliation after support is deployed; no rename, copy or manual re-import should be required.
- Classify corrupt or genuinely unsupported DNG variants explicitly rather than silently dropping them.
- Measure representative decode/render runtime and peak memory because RAW files can be materially larger than ordinary JPEG/HEIC inputs.
- Keep other RAW variants such as CR2/CR3, NEF, ARW and RAF unsupported until a real archive sample triggers separate format-specific verification.

## Acceptance criteria

- [x] Local and OneDrive-synchronised sources recognize `.dng` as an eligible image type.
- [x] Privacy-safe archive inventory reports DNG as supported only when the production path is available.
- [x] Representative private DNG samples decode successfully with correct orientation and dimensions and are visually acceptable for normal viewing.
- [x] The chosen full-render versus embedded-preview policy is documented and deterministic.
- [x] Available capture-date/GPS metadata from representative DNG files is preserved through normal metadata extraction without inventing missing values.
- [x] DNG thumbnails and review/viewer proxies render through the normal application endpoints without requiring browser-native DNG support.
- [x] Representative DNG revisions can complete the governed face-analysis pipeline with normal zero-face or detection results.
- [x] Existing DNG files already present under configured archive coverage are ingested by normal rescan/reconciliation after deployment.
- [x] Restart/retry does not duplicate assets, revisions, detections or derivatives.
- [x] Corrupt or unsupported DNG inputs receive explicit actionable failure state and are not silently skipped.
- [x] Representative DNG decode/render runtime and peak memory are measured and accepted for bounded archive processing.
- [x] Verification confirms source DNG bytes remain unchanged.
- [x] Other RAW extensions remain unsupported unless separately verified.

## Verification plan

1. Run the privacy-safe archive inventory and record the aggregate DNG count before enabling support.
2. Select representative private DNG samples covering the camera/device variants actually present; keep those files outside Git.
3. Exercise the production decoder/proxy path and visually verify orientation, crop/aspect, colour and detail.
4. Compare extracted capture-date/GPS metadata with trusted metadata from the same files where present.
5. Run governed face processing for representative DNG revisions and confirm durable completion.
6. Measure decode/render elapsed time and peak working set for the representative samples.
7. Run normal source reconciliation against a folder containing already-existing DNG files and confirm they enter the catalogue without rename or manual copy.
8. Re-run reconciliation/processing to confirm idempotence and verify originals are byte-for-byte unchanged.
9. Run focused source, decoder, proxy and integration tests plus the normal documentation/CI validation.

## Privacy and sample policy

Real DNG files may contain faces, GPS, capture history and device metadata. Representative production samples remain private and must not be committed. Automated fixtures may be added only when licensing and privacy permit; otherwise tests should use distributable synthetic/minimal fixtures for format contracts while the private samples provide human acceptance evidence.

## Rendering policy and verification evidence

The maintained archive inventory on 2026-09-26 contained 50 DNG files. The verified variants store a full-resolution 8-bit YCbCr JPEG preview in IFD0 alongside the RAW mosaic. Production decoding deterministically prefers that full-resolution preview when it is a structurally valid single strip, applies the TIFF orientation exactly once and returns the existing packed BGR24 contract. This is not the small embedded thumbnail. A valid DNG without that verified preview layout falls back to a full ImageMagick RAW render with explicit sRGB output and camera white balance. Other TIFF and RAW formats remain rejected.

Three private representatives covering the smallest, median and largest observed DNG sizes decoded at 2316x3088, 6048x8064 and 6048x8064 after orientation. Their selected 1600-long-edge review proxies were visually checked for orientation, aspect, colour and detail. Decode elapsed times were 201 ms, 1,287 ms and 945 ms; proxy render times were 116 ms, 565 ms and 519 ms. Combined decode-plus-proxy verification processes peaked at 230,678,528, 1,088,356,352 and 1,178,103,808 bytes respectively. Archive advancement remains bounded to one image step, so the measured large-sample working set was accepted for the maintainer-controlled local hardware.

All three samples exposed orientation, camera make/model and valid GPS metadata. They did not contain a capture date, and extraction correctly left it absent rather than inventing one. SHA-256 before/after checks confirmed every source remained unchanged. The generated proxies were kept in ignored private verification storage and no source filename, path, pixel data, GPS value or device identity is retained in Git.

An isolated normal archive include/sync ingested a copied representative as one `image/dng` revision. The exact governed CenterFace 0.5 single-pass/SFace FP32 profile completed successfully. Repeating sync reported zero new revisions and one unchanged source; repeating analysis reported the exact profile already complete and scheduled zero work. Privacy-safe inventory reported `family=raw supported=true`. Automated coverage adds a synthetic distributable DNG container for orientation/BGR decoding, signature and corrupt-media behavior, source eligibility, inventory classification and protection of other RAW extensions.
