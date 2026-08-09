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

- [ ] A privacy-safe archive inventory records every distinct eligible photo extension/media family and aggregate count.
- [ ] HEIC/HEIF files from representative archive sources decode successfully through the normal production image contract.
- [ ] Every RAW variant present in the archive is either supported and processed or explicitly recorded as an unsupported variant with a deliberate follow-up decision; no variant is silently skipped.
- [ ] Orientation and dimensions are correct for representative HEIC and RAW samples.
- [ ] Decoded HEIC/RAW images can run through the governed CenterFace/SFace archive profile and produce normal durable zero-face or face-analysis completion.
- [ ] Review proxies can be produced for supported HEIC/RAW revisions without modifying originals.
- [ ] Restart/retry does not duplicate assets, revisions, detections or derivatives.
- [ ] Corrupt and genuinely unsupported files receive explicit catalogue/reporting state.
- [ ] Representative decode runtime/memory is measured and accepted for bounded archive processing.

## Verification requirements

Automated tests must cover media recognition, decoder success/failure contracts, orientation, idempotent archive integration and representative downstream processing with distributable fixtures. Human verification must use private real HEIC and RAW files from the archive and retain only privacy-safe aggregate results in Git.

## Completion notes

- Files changed:
- Trade-offs:
- Deferred work:
- Commands run:
