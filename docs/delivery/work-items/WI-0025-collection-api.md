---
id: WI-0025
title: Add collection-ready queries
milestone: M14
status_source: ../status/work-items.yaml
depends_on: [WI-0015, WI-0016, WI-0029, WI-0033]
affected_modules: [PhotoIdentity.Api, PhotoIdentity.Web, PhotoIdentity.Persistence.Sqlite]
---

# WI-0025: Add collection-ready queries

## Objective

Expose stable local queries and neutral exports for photos containing one or more people, using the reviewed 500-image catalogue as the first acceptance dataset.

## Acceptance criteria

- [x] Any-person and all-person semantics are explicit.
- [x] Confirmed-only results are supported and are the safe default.
- [x] Suggestion-backed results are opt-in and identify their model revision and threshold.
- [x] Date, confidence, review-state and person filters can be combined predictably.
- [ ] Results can be inspected through the local web interface on Windows and Pixel.
- [ ] A neutral collection manifest can feed later slideshow or album applications without exposing unnecessary local paths.
- [ ] Query counts and representative results are checked against the pilot catalogue.

## Confirmed-query foundation

PR #57 added a confirmed-only `/api/collections/photos` query with:

- one or more comma-separated person IDs;
- explicit `match=any` and `match=all` semantics, with `all` as the default;
- optional UTC date bounds and minimum detector confidence;
- stable pagination and total counts; and
- a neutral response containing opaque asset/revision IDs, media metadata and matched people, without source roots, source keys or crop paths.

Confirmed-only remains the default and requires no model parameters.

## Suggestion-query slice

PR #58 added explicitly scoped suggestion evidence.

Suggestion-backed results require all of these explicit query parameters:

- `includeSuggestions=true`;
- one exact `suggestionModelId`;
- the exact 64-character `suggestionModelHash`; and
- a finite `minimumSuggestionScore` cosine threshold between `-1` and `1`.

Confirmed assignments remain authoritative. The advisory path considers only rank-one pending suggestions for unreviewed faces. Assigned and rejected faces cannot enter through suggestion evidence. The response reports the exact model revision and threshold and separates confirmed-face counts from suggested-face counts and maximum suggestion score for every matched person.

Suggestion parameters are rejected unless the opt-in flag is present, and incomplete model or threshold scope is rejected rather than inferred.

## Review-state filter slice

PR #59 added explicit review-state filtering.

The collection endpoint accepts `reviewState` with these semantics:

- omitted without suggestion scope: `assigned`;
- omitted with explicit suggestion scope: `all`;
- `assigned`: active confirmed assignments only, without suggestion parameters;
- `unreviewed`: exact-model rank-one pending suggestions only, requiring suggestion scope; and
- `all`: confirmed assignments plus qualifying unreviewed suggestions, requiring suggestion scope.

`rejected` is not a collection match state because a rejected face does not positively identify one of the selected people. Unsupported or incompatible combinations are rejected instead of being ignored.

Person IDs, `match=any|all`, UTC date bounds and minimum detector confidence are applied to the selected review-state evidence in the same query. The response echoes the effective review state so callers do not need to infer which evidence was used.

## Local collection workspace slice

PR #60 added a `/collections` workspace linked from the primary navigation. It provides:

- multi-person selection with explicit any-person or all-person matching;
- confirmed-only, suggestion-only and combined evidence choices;
- exact suggestion model revision and threshold controls when advisory evidence is selected;
- detector-confidence filtering;
- stable previous/next pagination; and
- visible evidence counts and maximum suggestion scores per matched person.

The first device acceptance attempt found three usability blockers:

- the full people list consumed too much page space;
- browser checkboxes could render above rather than beside names; and
- manifest-only cards did not provide a useful photo-browsing experience.

The same attempt also established that the date controls were misleading. `asset_revisions.observed_at_utc` is the time the catalogue scan first observed that content revision. It is not the photo capture time and is not specifically the face-analysis time. The API retains date bounds for machine consumers and predictable query composition, but the local browser no longer presents them as a primary collection control.

## Usability and content correction slice

PR #61 replaced the people grid with a searchable, scroll-contained dropdown with checkboxes, filtered selection and removable selected-person chips. It also added opaque local photo delivery while preserving the path-free browser contract.

The next Windows acceptance attempt exposed two additional defects:

- the host document linked only `css/app.css`, so the generated `PhotoIdentity.Web.styles.css` bundle was never loaded and all component-scoped `.razor.css` selectors were inactive; and
- result cards requested the original image bytes, allowing large source dimensions to control the unstyled layout and wasting trusted-LAN bandwidth.

The active correction is on `agent/WI-0025-css-thumbnails`.

It links the generated Blazor isolated-style bundle from `index.html`, restoring component styles across the entire web application. Hosted integration coverage verifies that the bundle is linked, served and contains representative collection selectors.

Collection result URLs now target `/api/collections/photos/{revisionId}/thumbnail`. The API resolves and validates the original through the existing private source boundary, decodes it server-side and returns a fixed 480 × 320 JPEG preview with `Cache-Control: no-store`. The original content route remains available for later neutral consumers, but the collection grid does not download original image bytes. Result cards use a matching 3:2 fixed viewport, constrain every grid/card/image layer to the available width and keep opaque identifiers wrapped.

The Windows and Pixel acceptance checkbox remains open until this corrected workspace is exercised against the accepted private catalogue on both devices. The neutral consumer criterion and pilot-count criterion also remain open until their final verification evidence is recorded.
