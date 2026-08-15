---
id: WI-0059
title: Open the full photo from face review
milestone: M18
status_source: ../status/work-items.yaml
depends_on: [WI-0025, WI-0033]
related_adrs: []
affected_modules: [PhotoIdentity.Api, PhotoIdentity.Web, PhotoIdentity.Persistence.Sqlite, PhotoIdentity.Integration.Tests]
---

# WI-0059: Open the full photo from face review

## Objective

Let an operator inspect the complete photo containing a detected face without losing the current face-review context or weakening the existing local-first original-access policy.

## Why

A face crop is often insufficient for an identity decision. Clothing, nearby people, event context and the rest of the scene can make the identity clear. Photo Identity already has a full-photo viewer at `/photo/{RevisionId}` with privacy-safe review previews and explicit original-access behavior, but the face-review contract does not currently expose the asset revision identifier needed to navigate there.

## In scope

- Make the exact asset revision identifier associated with a face available to the privacy-limited face-details UI contract.
- Add a clear `View full photo` action from Face Details.
- Reuse the existing `/photo/{RevisionId}` viewer instead of introducing a second full-photo viewing implementation.
- Open the full-photo viewer in a new browser tab/window so the originating review queue, filters, loaded range, selection and scroll context remain untouched.
- Preserve the photo viewer's existing rule that normal preview viewing must not automatically hydrate an online-only original.
- Keep source paths and storage implementation details out of browser responses.
- Add integration/UI coverage for the face-to-photo relationship and navigation URL.

## Out of scope

- A new full-screen/lightbox viewer embedded directly in Face Details.
- Automatically downloading an online-only original when `View full photo` is used.
- Changing the existing explicit `Load original`, `Open original` or release workflow.
- Adding full-photo actions to every gallery card in this slice.

## Acceptance criteria

- [x] Face Details exposes a visible `View full photo` action for the face's exact asset revision.
- [x] Activating the action opens the existing photo viewer for that revision in a new tab/window.
- [x] Returning to the original review tab preserves the current queue URL, filters, ordering and review context.
- [x] The photo viewer initially follows its existing review-preview path and does not hydrate an online-only original implicitly.
- [x] The browser never receives a local source path, review-proxy path or original-storage path as part of this navigation feature.
- [x] The relationship remains correct when multiple detected faces belong to the same photo revision.
- [x] Automated integration coverage proves that a face resolves to the expected asset revision while the existing invalid-identifier behavior remains privacy-limited.
- [ ] Human verification on Windows confirms that full-photo context can be opened during face review and the operator can continue reviewing from the unchanged original tab.

## Verification requirements

Automated contract/integration tests and human Windows verification are required.

## Completion notes

- Files changed: `ReviewFaceRevisionResolver.cs`, `ReviewEndpoints.cs`, `SuggestionGalleryEndpoints.cs`, `ReviewContracts.cs`, `FaceDetails.razor`, `FaceDetails.razor.css`, `ReviewFaceDetailImageApplicationTests.cs`.
- Trade-offs: The initial implementation opens the existing photo viewer in a new tab rather than adding another embedded viewer, which keeps original-access and hydration policy centralized and leaves the review queue untouched.
- Deferred work: Human Windows verification of the new-tab workflow and preserved review context is intentionally deferred until WI-0058, WI-0059 and WI-0060 are all merged.
- Commands run: GitHub Actions `build` workflow for PR #147, including Release build, full test suite, living-documentation validation and review/package verification.
