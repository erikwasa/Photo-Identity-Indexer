---
id: WI-0053
title: Add HEIC and archive RAW image support
milestone: M12
status_source: ../status/work-items.yaml
depends_on: [WI-0006, WI-0012]
related_adrs: [ADR-0007]
affected_modules: [PhotoIdentity.Core, PhotoIdentity.Imaging.OpenCv, PhotoIdentity.Source.Local, PhotoIdentity.Worker, PhotoIdentity.Api]
---

# WI-0053: Add HEIC and archive RAW image support

## Objective

Make permanent archive ingestion recognize and process HEIC/HEIF plus every camera RAW variant that is actually present in the maintained photo archive, without silently omitting unsupported media.

## Why

The permanent catalogue must represent the real archive rather than only the JPEG/PNG subset. HEIC and camera RAW files are expected in source folders, and treating them as invisible would make completeness reporting misleading before version 1 begins.

## Current archive evidence and implementation sequence

As of 2026-08-11, the maintained full archive contains a few HEIC images and no known RAW images. Future iPhone backups are expected to add more HEIC images and may introduce RAW media.

Implementation is therefore intentionally staged:

1. HEIC/HEIF is a supported permanent-archive input and has been verified against representative private archive files;
2. unverified RAW extensions remain visible as unsupported media rather than being silently accepted or dropped; and
3. when a real RAW variant appears, inventory that exact format, add a representative private sample and extend the isolated decoder path with a deliberate rendering policy for that format.

This is not a reduction in the WI-0053 completeness requirement. It avoids implementing camera-specific RAW behavior without any real archive input against which orientation, rendering, runtime and memory can be verified.

## In scope

- Inventory the real archive by extension/media signature using privacy-safe aggregate counts only.
- Recognize HEIC/HEIF as eligible image media.
- Identify the RAW variants actually present in the archive; expected examples may include DNG, CR2/CR3, NEF and ARW, but the inventory rather than this example list defines the required set.
- Select and isolate a decoder path that produces the existing deterministic rendered-RGB image contract for downstream detector/alignment processing.
- Apply orientation correctly and define deterministic behavior for RAW embedded previews versus demosaiced/full rendered pixels.
- Keep original files read-only.
- Use the same decoded representation for archive analysis and review-proxy generation where appropriate, without creating a second canonical asset for the derivative.
- Preserve content-hash/immutable-revision semantics regardless of decoded format.
- Classify corrupt or unsupported variants explicitly with actionable media/failure state.
- Add representative automated fixtures where licensing allows and keep private real-camera samples outside Git.
- Measure representative HEIC/RAW decode time and memory so very large RAW files cannot unexpectedly defeat bounded archive processing.

## Out of scope

- Universal support for every proprietary RAW format ever produced.
- Writing converted JPEG/DNG files back beside originals.
- Editing RAW metadata or sidecar files.
- Video/Live Photo processing.
- Changing the selected face detector or embedder solely because the source file is HEIC/RAW.

## Acceptance criteria

- [x] A privacy-safe archive inventory records every distinct eligible photo extension/media family and aggregate count.
- [x] HEIC/HEIF files from representative archive sources decode successfully through the normal production image contract.
- [x] Every RAW variant present in the archive is either supported and processed or explicitly recorded as an unsupported variant with a deliberate follow-up decision; no variant is silently skipped. No RAW variant is currently present.
- [x] Orientation and dimensions are correct for representative HEIC samples; RAW verification becomes active when RAW media is first observed.
- [x] Decoded HEIC images can run through the governed CenterFace/SFace archive profile and produce normal durable zero-face or face-analysis completion; the same requirement applies to any future supported RAW variant.
- [x] Review proxies can be produced for supported HEIC revisions without modifying originals; the same requirement applies to any future supported RAW variant.
- [x] Restart/retry does not duplicate assets, revisions, detections or derivatives.
- [x] Corrupt and genuinely unsupported files receive explicit catalogue/reporting state.
- [x] Representative HEIC decode runtime/memory is measured and accepted for bounded archive processing; RAW measurement becomes active when RAW media is first observed.

## Verification requirements

Automated tests cover media recognition, decoder success/failure contracts, source recognition, privacy-safe inventory behavior and downstream production integration where distributable fixtures permit. Human verification uses private real archive media and retains only privacy-safe aggregate results in Git.

RAW human verification is conditional on RAW media actually being present. The current archive has no known RAW variants. A future newly observed RAW variant reopens that format-specific verification requirement rather than being silently skipped.

## Verification evidence

On 2026-08-11 the maintainer declared WI-0053 verified after exercising representative private HEIC media. Three private HEIC images decoded successfully through the production decoder at 4032x3024, 3024x4032 and 3024x4032 BGR24. Reported elapsed times were 894 ms, 1757 ms and 919 ms, with peak working sets of 237,879,296 bytes, 226,832,384 bytes and 226,394,112 bytes. All three runs reported `input-unchanged: true`, and the maintainer visually confirmed that the normalized images looked correct.

The merged implementation is PR #114. Its final build workflow run #702 completed successfully. The maintained archive currently has no known RAW media, so RAW decoder implementation is deliberately deferred until inventory observes a real format that can be verified against a private sample.

## Completion notes

- Files changed: HEIC/HEIF source recognition; Magick.NET-backed HEIF decode behind `IImageDecoder`; shared review-proxy decode path; archive proxy measurement; privacy-safe `archive inventory`; decode peak working-set reporting; source/integration/recognition tests; operator and imaging documentation; CI unsupported-media fixture updates.
- Trade-offs: HEIC is implemented now because real archive samples exist. Generic RAW decoding is intentionally not guessed without a real archive format and private sample. Known RAW extensions remain visible as `family=raw supported=false` until format-specific support is proven.
- Deferred work: when a RAW variant first appears, add format-specific decode/rendering policy, private-sample verification, downstream CenterFace/SFace verification, proxy verification and bounded runtime/memory evidence for that exact format.
- Commands run: `archive inventory --database ...`; `decode --input ... --output ... --report ...`; project build/test/CI validation from PR #114; private permanent-archive verification including governed analysis and review-proxy behavior.
