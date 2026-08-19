# M19 maintainer review — 2026-08-19

This document records the maintainer's consolidated M19 browser/archive review and the follow-up findings discovered during that pass. It is intentionally a triage and design note: the findings below should be grouped into focused work items only after scope and priority are agreed.

## Verification result

The maintainer reported **PASS** for:

- WI-0061 — Photo Details and navigation context;
- WI-0062 — manual photo-level people;
- WI-0063 — first-class Places hierarchy;
- WI-0066 — Smart Collection person visibility;
- WI-0067 — featured representative faces;
- WI-0068 — searchable portrait-led Smart Collection people selection.

Those six work items can move to `completed` with maintainer verification dated 2026-08-19.

WI-0064 and WI-0065 remain `in_review`. Earlier live-provider verification for WI-0064 remains valid, but this integrated pass exposed a gap before automatic GeoNames enrichment: newly archived photos can finish verification and analysis without capture metadata ever being inspected. Until GPS is populated, the WI-0065 worker has nothing to reverse geocode. M19 therefore remains `in_progress`.

## Archive processing observation

A newly included folder containing 190 images displayed:

> Waiting for OneDrive to finish a managed download or release.

The images nevertheless downloaded, were verified and were analyzed, so the archive worker appeared to continue making progress.

This matches the current worker state model. `ArchiveAdvancementHostedService` advances review derivatives and bounded analysis before calculating the status label. It reports `waiting` whenever any managed hydration or release remains in flight, even if other runnable work is still progressing.

### Proposed solution

Treat OneDrive transition state and processing activity as separate dimensions:

- report **Waiting for OneDrive** only when a OneDrive transition is the only remaining blocker;
- otherwise report a running state such as **Processing continues — waiting on 3 OneDrive transitions**;
- expose useful counts for downloading/releasing items and a last-progress timestamp or completed-work delta;
- distinguish a genuinely stalled transition from a pipeline that is still processing other revisions.

This is primarily an operator-state/UI correction; the observed 190-image run does not by itself indicate that processing stopped.

## Capture metadata: current behavior and gap

### When EXIF is read today

Capture metadata is currently read only by the explicit bounded photo-metadata backfill operation (`POST /api/photo-metadata/backfill`). The backfill service:

1. selects revisions that have not yet received a metadata inspection record;
2. skips originals that are not already local;
3. verifies file size and SHA-256 against the immutable catalogue revision;
4. opens the verified local original and invokes `MetadataExtractorPhotoMetadataReader`;
5. persists the resulting revision-bound metadata record.

Archive synchronization/verification/face analysis does **not** invoke this metadata reader. This explains how a newly added archive folder can become fully analyzed while photographic date/GPS remains missing.

The current structured reader extracts only:

- EXIF `DateTimeOriginal` as timezone-less photographic wall-clock time;
- the original timezone offset when present;
- GPS latitude and longitude when `GpsDirectory` yields a complete location.

The current photo viewer does not display these fields; its Details panel currently exposes the filename, people and Place editor.

### Proposed ingestion fix

Make metadata inspection a normal revision lifecycle step while preserving the safety rules established by WI-0050:

- factor the existing verified-local metadata inspection into a reusable service;
- during archive advancement, inspect metadata while a revision is already local and hash-verified, ideally before a Photo Identity-managed hydration is released;
- keep the explicit backfill endpoint for existing catalogue rows and repair/retry scenarios;
- never hydrate an online-only original solely to obtain metadata outside the existing bounded archive policy;
- persist an explicit state that distinguishes **not inspected** from **inspected, no usable metadata**;
- once GPS is persisted, let the existing GeoNames worker pick it up independently. Archive processing must not wait for GeoNames/network pacing.

This closes the missing link needed to verify WI-0064/WI-0065 on newly processed photos.

## Metadata expansion

### Recommended first-class fields

In addition to the existing capture time/GPS fields, useful structured fields are:

- camera make;
- **camera model name**;
- lens model;
- capture date/time and original offset;
- orientation;
- exposure time / shutter speed;
- aperture / F-number;
- ISO;
- focal length and, when available, 35 mm equivalent;
- flash state;
- GPS latitude/longitude;
- GPS altitude when present;
- image description/title when present and reasonably bounded.

These should remain revision-bound and queryable where useful. Capture time should prefer true photographic capture metadata; filesystem modified time or catalogue observation time should not silently masquerade as the photo date.

### Show all metadata tags

For diagnostic/inspection use, persist a separate sanitized raw-metadata snapshot at metadata-scan time instead of adding a database column for every possible EXIF/vendor tag. A practical representation is a bounded list of directory/tag/name/display-value entries.

Safety constraints:

- omit embedded thumbnails/previews and other binary payloads;
- omit or bound very large maker-note/blob values;
- cap individual values and total snapshot size;
- retain the structured canonical fields separately for queries and stable UI contracts;
- continue treating originals as read-only.

The Photo viewer can then show:

- a compact **Key metadata** section for camera/capture/exposure/GPS;
- a collapsible **All metadata** section for the sanitized raw tag snapshot;
- exact GPS coordinates separately from the named Place, with a copy action.

A small format fixture set should cover at least JPEG and HEIC examples with/without `DateTimeOriginal`, timezone offset and GPS. This will distinguish “metadata was never scanned” from “the format/tag was scanned but no supported capture value existed.”

## Smart Collections visual defect

Observed: the featured/preview image appears outside its card.

The result-card markup places its image inside the card, so this should be treated as a CSS containment/browser-layout defect until the exact affected card is reproduced.

### Proposed solution

- enforce `min-width: 0` and `overflow: hidden` on the affected card/container;
- make card images block-level with `display: block; max-width: 100%` and explicit object-fit sizing;
- verify both Smart Collection photo-result thumbnails and portrait-led people cards at narrow and normal widths;
- add a focused browser/manual regression check for rounded-card containment.

## Maintain People layout and hidden-state presentation

Observed:

- person-card text/buttons can spill outside the card;
- hidden state is easy to miss because it uses the same muted badge styling;
- hidden people should appear at the bottom.

The current grid allows cards as narrow as 13 rem while each card contains a portrait, labels/counts and two long action buttons.

### Proposed solution

- increase the practical card minimum width and use `min-width: 0` on card content;
- place actions in a dedicated wrapping/full-width action area;
- make long buttons wrap safely rather than force card overflow;
- give hidden people an explicit high-contrast badge such as **Hidden from Smart Collections**, optionally with a subtle card border/background treatment;
- order active people as visible first, hidden last; inside each group retain favorites-first/name ordering.

The visual treatment should make clear that “hidden” affects Smart Collection discovery only, not identity evidence.

## Dismissible menus and navigation

### Advanced menu

The primary Advanced menu is a native `<details>` element in the persistent layout. It does not currently implement light-dismiss behavior.

### Library / Collections people menu

The people picker on `/collections` is also a native `<details>` element and remains open until explicitly toggled.

### Proposed solution

Use one reusable controlled dismissible-popover/menu behavior for both surfaces:

- close after an item is chosen;
- close on outside pointer/click;
- close on Escape;
- close on route/navigation change;
- keep correct focus and `aria-expanded` semantics.

A small Blazor component plus a minimal document-level pointer helper is preferable to duplicating page-specific dismissal code.

## Favorite people and native select type-ahead

Observed: in Face Details/face gallery person dropdowns, typing the first letters of a favorite person's name does not match that person.

Current native `<select>` option labels prefix favorite names with `★ `. Browser native type-ahead therefore sees the star as the start of the option text instead of the person's display name.

### Proposed solution

Keep favorites sorted/promoted, but make the actual option text start with the person's name, for example:

- `Ada Lovelace ★`, or
- `Ada Lovelace — Favorite`.

Apply the same convention to every native person select (face cards/details, bulk assignment, maintenance rename/merge and any other shared selector). A future searchable combobox can replace native selects if richer matching is needed, but the label-order change is the low-risk fix.

## Archive item return-state loss

Observed: on Archive items, opening **View** and then returning loses the selected filters/page.

The archive page currently stores folder/availability/verification/analysis filters and paging offset only in component-local fields. The View link navigates directly to `/photo/{revisionId}` without a return context.

### Proposed solution

Reuse the navigation-context pattern established by WI-0061:

- encode archive filters and paging in the `/archive` query string;
- build the View link with an exact `returnUrl` containing that archive state;
- teach Photo Details to label its context-aware action **Back to archive**;
- browser/mouse Back and the explicit Back action should restore the same filters and offset.

Keeping the state in the URL also makes refresh/deep-link behavior deterministic.

## New face-review filter: suggested person

The review queue already supports exact suggestion model revision, confidence group and ordering. It does not currently accept a suggested-person criterion.

### Proposed semantics

Add an optional single `suggestedPersonId` filter meaning **the current rank-one/top suggestion is this canonical person**. This is consistent with the existing suggestion-gallery contract and avoids ambiguity about lower-ranked candidates.

The filter should compose independently with:

- review state;
- processing run;
- exact model revision;
- confidence group;
- all existing ordering modes.

### Proposed implementation

- add `suggestedPersonId` to the suggestion-gallery repository/API list and navigation queries;
- add a searchable single-person selector to Review queue controls; hidden-from-Smart-Collections status must not hide people from identity review;
- preserve the parameter in Face Details previous/next queue scope and return URL;
- test person-only filtering, person + confidence group, person + sort, empty results and Face Details queue navigation.

If filtering by **any ranked suggestion** rather than only rank one is later desired, make that a separate explicit mode rather than silently changing the top-suggestion meaning.

## Suggested follow-up grouping

Before assigning work-item IDs, the findings fit three coherent implementation groups:

1. **Metadata ingestion and Photo viewer metadata** — integrate safe metadata extraction into archive lifecycle, expand structured metadata, retain sanitized raw tags, show capture/camera/GPS fields, and unblock automatic GPS-to-GeoNames verification. This is the highest priority because it blocks confidence in WI-0064/WI-0065/M19 completion.
2. **UI and navigation polish** — Smart Collection image containment, Maintain People responsive cards/hidden presentation/order, dismissible menus, favorite select labels, archive return context and clearer archive progress wording. If this grows too broad, archive progress/return-state can be its own item.
3. **Face Review suggested-person filtering** — add `suggestedPersonId` while preserving confidence-group/order/navigation semantics.

M19 should remain open until the metadata-to-GeoNames gap is resolved and the WI-0064/WI-0065 orchestration checks can be completed.