---
id: WI-0058
title: Improve Face Details image quality
milestone: M18
status_source: ../status/work-items.yaml
depends_on: [WI-0033, WI-0042]
related_adrs: []
affected_modules: [PhotoIdentity.Api, PhotoIdentity.Worker, PhotoIdentity.Persistence.Sqlite, PhotoIdentity.Imaging.OpenCv, PhotoIdentity.Integration.Tests]
---

# WI-0058: Improve Face Details image quality

## Objective

Make Face Details materially sharper than the gallery card when the original photo contains enough real face pixels, while making both Face Gallery and Face Details independent of whether the authoritative full-resolution photo is currently local or online-only.

## Why

The first WI-0058 implementation attempted to render Face Details from an already-local, revision-verified original at request time and otherwise fell back to the whole-photo review proxy. Windows manual verification on 2026-08-16 showed that this still produced the same roughly proxy-resolution face image as Face Gallery in a representative local-original case. More importantly, tying review quality to the current hydration state of the original is the wrong long-term boundary.

Face review needs a durable local artifact. The full-resolution original should be required only while analysis/backfill creates that artifact; routine review should never need to probe, hydrate or reopen the original.

## In scope

- Generate one durable contextual face-review derivative per detected face from the full-resolution authoritative original while that original is available for archive work.
- Use the existing face-centered context policy (`2.2x` the detected face extent), JPEG quality 90 and a maximum long edge of 960 px.
- Never upscale a crop that contains fewer than 960 real source pixels on its longest edge.
- Store the derivative permanently under the configured review-derivative root, separately from the small aligned recognition crop.
- Serve Face Details directly from the stored derivative when its intrinsic dimensions are within the 960 px request.
- Serve Face Gallery by downscaling the same stored derivative to the card request size (currently 360 px) at response time; do not persist a second gallery-size copy unless later performance evidence justifies it.
- Keep the durable whole-photo review proxy as a temporary compatibility fallback while existing faces are waiting for derivative backfill.
- Backfill already-analyzed current revisions by reusing their persisted face observations/bounding boxes. Backfill may hydrate an online-only original under the existing bounded hydration policy, generate all missing face derivatives for that revision, then release only app-managed hydration.
- Include missing face-review derivatives in archive advancement work so requested archive processing does not report complete while relevant analyzed faces are still waiting for backfill.
- Decode the original through the application's supported image decoder so JPEG, PNG, HEIC and HEIF use the same supported source-format boundary.
- Add automated coverage for durable generation, gallery downscaling, high-resolution details and review behavior after the original becomes online-only.

## Out of scope

- Persisting a separate 360 px gallery derivative before performance measurements show it is needed.
- Automatically hydrating an original because a user opened Face Gallery or Face Details.
- Exposing authoritative source paths or durable derivative paths to the browser.
- Re-running face detection, alignment or embedding solely to backfill review derivatives.
- General image enhancement, super-resolution or AI-based reconstruction.

## Acceptance criteria

- [ ] A durable contextual face-review JPEG is generated from full-resolution source pixels for each detected face, with a maximum long edge of 960 px and no upscaling.
- [ ] The durable face-review derivative is separate from the aligned recognition crop and remains available after the authoritative original becomes online-only.
- [ ] Face Details uses the durable derivative and can return up to approximately 960 px on its longest edge when the original face/context region contains enough real pixels.
- [ ] Face Gallery uses the same durable derivative as its quality source and returns a card-sized response without requiring a second permanently stored gallery image.
- [ ] Opening Face Gallery or Face Details never probes or hydrates the authoritative original.
- [ ] Already-analyzed faces can be backfilled from persisted observations without rerunning detector or embedder inference.
- [ ] Archive advancement keeps derivative backfill pending until relevant current analyzed faces have durable review derivatives and respects the existing managed hydration/release boundary.
- [ ] JPEG, PNG, HEIC and HEIF originals use the application's supported decoder when the durable face derivative is generated.
- [ ] Browser-facing contracts continue to expose only privacy-safe image URLs/metadata and never source or derivative filesystem paths.
- [ ] Automated tests prove that a face derivative generated while the original is local remains the high-resolution Face Details source after the original state changes to online-only.
- [ ] Human verification on Windows confirms that representative Face Details images are visibly sharper than their gallery counterparts and remain unchanged when the original is online-only.

## Verification requirements

Automated renderer/persistence/API coverage plus Windows verification with representative large-face, small-face and online-only-after-backfill examples are required. Manual verification should explicitly confirm that making an original online-only after backfill does not reduce Face Gallery or Face Details quality.

## Implementation history

- PR #147 introduced a request-time original-preference path. Manual verification on 2026-08-16 failed the image-quality criterion and exposed the unwanted runtime dependency on original hydration state.
- The replacement design stores one permanent `<=960 px` face-review derivative generated from the full-resolution original and uses it as the sole high-quality source for both review surfaces.
- Existing analyzed faces are backfilled from their persisted bounding boxes; face inference is not repeated.

## Completion notes

- WI-0058 remains open while the durable derivative implementation and backfill are completed and reverified on Windows.
- WI-0059 and WI-0060 passed the same 2026-08-16 Windows verification session independently of this image-quality failure.
