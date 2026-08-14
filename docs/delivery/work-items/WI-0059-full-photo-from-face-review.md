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

- [ ] Face Details exposes a visible `View full photo` action for the face's exact asset revision.
- [ ] Activating the action opens the existing photo viewer for that revision in a new tab/window.
- [ ] Returning to the original review tab preserves the current queue URL, filters, ordering and review context.
- [ ] The photo viewer initially follows its existing review-preview path and does not hydrate an online-only original implicitly.
- [ ] The browser never receives a local source path, review-proxy path or original-storage path as part of this navigation feature.
- [ ] The relationship remains correct when multiple detected faces belong to the same photo revision.
- [ ] Automated integration coverage proves that a face resolves to the expected asset revision and that unrelated/invalid identifiers cannot expose another source path.
- [ ] Human verification on Windows confirms that full-photo context can be opened during face review and the operator can continue reviewing from the unchanged original tab.

## Verification requirements

Automated contract/integration tests and human Windows verification are required.

## Completion notes

- Files changed:
- Trade-offs:
- Deferred work:
- Commands run:
