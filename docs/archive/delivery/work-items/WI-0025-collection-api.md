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
- [x] Results can be inspected through the local web interface on Windows and Pixel.
- [x] A neutral collection manifest can feed later slideshow or album applications without exposing unnecessary local paths.
- [x] Query counts and representative results are checked against the pilot catalogue.

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

## Usability and content correction slices

PR #61 replaced the people grid with a searchable, scroll-contained dropdown with checkboxes, filtered selection and removable selected-person chips. It also added opaque local photo delivery while preserving the path-free browser contract.

The next Windows acceptance attempt exposed two additional defects:

- the host document linked only `css/app.css`, so the generated `PhotoIdentity.Web.styles.css` bundle was never loaded and all component-scoped `.razor.css` selectors were inactive; and
- result cards requested the original image bytes, allowing large source dimensions to control the unstyled layout and wasting trusted-LAN bandwidth.

PR #62 linked the generated Blazor isolated-style bundle from `index.html`, restoring component styles across the entire web application. Hosted integration coverage verifies that the bundle is linked, served and contains representative collection selectors.

Collection result URLs now target `/api/collections/photos/{revisionId}/thumbnail`. The API resolves and validates the original through the existing private source boundary, decodes it server-side and returns a fixed 480 × 320 JPEG preview with `Cache-Control: no-store`. The original content route remains available for neutral consumers, but the collection grid does not download original image bytes. Result cards use a matching 3:2 fixed viewport, constrain every grid/card/image layer to the available width and keep opaque identifiers wrapped.

## Neutral manifest slice

PR #63 added `GET /api/collections/manifest`, accepting the same people, match, review-state, date, confidence and exact suggestion-policy parameters as the paginated photo query.

The response media type is `application/vnd.photoidentity.collection-manifest+json`. Schema version 1 contains:

- the stable format identifier `photoidentity.collection-manifest`;
- the effective query policy, including exact model revision and threshold when suggestions are enabled;
- the complete ordered result count and photo list;
- opaque asset and immutable revision identifiers;
- media type and dimensions when known;
- matched-person evidence; and
- absolute thumbnail and original-content URLs derived from the host used to request the manifest.

The endpoint pages through the repository internally in batches of 200 and returns one complete document. Integration coverage uses 201 confirmed photos to prove the page boundary is crossed. It also verifies `Cache-Control: no-store`, the vendor media type, ordering, complete counts, usable thumbnail/content URLs and the absence of source roots, source keys and filenames.

No generated manifest or thumbnail is persisted. A slideshow or album client can request the manifest from the Windows host, render the bounded thumbnail URLs and open or stream the original-content URLs without learning filesystem paths.

## Completion verification

GitHub Actions build #401 passed the Release build, automated tests, documentation validation, generated-document checks, published application smoke verification and Windows PowerShell verification for the final manifest slice.

On 2026-08-02, the operator verified the completed collection workspace against the accepted private catalogue on Windows and Pixel. The verification covered the people selector, checkbox alignment, responsive layout, fixed-size thumbnails, absence of horizontal overflow, pagination, any-person and all-person counts, representative confirmed and advisory results, and the neutral manifest consumer boundary.

Detailed private names, counts and representative-result notes remain outside Git. The canonical status registry records only the privacy-safe completion statement.
