---
id: WI-0058
title: Improve Face Details image quality
milestone: M18
status_source: ../status/work-items.yaml
depends_on: [WI-0033, WI-0042]
related_adrs: []
affected_modules: [PhotoIdentity.Api, PhotoIdentity.Web, PhotoIdentity.Imaging.OpenCv, PhotoIdentity.Integration.Tests]
---

# WI-0058: Improve Face Details image quality

## Objective

Make the face crop shown on Face Details materially sharper than the gallery card image when the underlying photo contains enough source pixels, without artificial upscaling or weakening the application's local-first privacy and hydration boundaries.

## Why

The review API requests a larger image for Face Details than for the gallery, but both views ultimately render from the same review-proxy source. The face renderer intentionally never upscales, so a face-with-context crop that contains only roughly 200–300 source pixels stays at that intrinsic size even when Face Details asks for a larger result. The details layout then displays that small image in a much larger panel, making it visibly pixelated.

## In scope

- Keep the gallery image path optimized for card-sized review.
- Add a higher-resolution rendering path for Face Details that can use genuinely higher-resolution source pixels when available.
- Prefer a privacy-safe durable derivative suitable for detail review; an already-local, revision-verified original may be used as a source when appropriate.
- Preserve the existing face-centered crop and surrounding review context unless a better detail-specific crop policy is justified by tests.
- Keep the no-upscale behavior: increasing requested dimensions must not fabricate detail from a smaller crop.
- Fall back gracefully to the existing review crop when no higher-resolution source is available.
- Add automated coverage for source selection, size behavior and privacy/hydration boundaries.

## Out of scope

- Automatically hydrating an online-only OneDrive original merely because Face Details was opened.
- Exposing source paths, derivative paths or other private filesystem details to the browser.
- Replacing the gallery with full-resolution images.
- General image enhancement, super-resolution or AI-based reconstruction.

## Acceptance criteria

- [ ] Face Gallery continues to use a card-appropriate face preview and does not incur the cost of the Face Details rendering path.
- [ ] Face Details can return a face-centered preview up to approximately 960 px on its longest edge when the selected source contains enough real pixels.
- [ ] The renderer never enlarges a smaller source crop solely to satisfy the requested dimensions.
- [ ] A face whose higher-resolution source is unavailable still renders through the existing safe fallback rather than failing the details page.
- [ ] Opening Face Details does not request hydration of an online-only original.
- [ ] Browser-facing contracts expose only privacy-safe image URLs/metadata and never local source or derivative paths.
- [ ] Automated tests distinguish gallery-size rendering, higher-resolution detail rendering, fallback behavior and no-upscale behavior.
- [ ] Human verification on Windows confirms that representative Face Details images are visibly sharper than their gallery counterparts when higher-resolution source pixels exist and do not appear artificially enlarged when they do not.

## Verification requirements

Automated API/rendering tests plus human Windows verification with representative small-face and large-face examples are required.

## Completion notes

- Files changed:
- Trade-offs:
- Deferred work:
- Commands run:
