# M20 maintainer review — 2026-08-21

## Purpose

Record the maintainer's consolidated browser/operator review of the M20 work already merged to `main` before WI-0076 is merged. This document is a **planning/acceptance contract only**. No corrective implementation should start until the maintainer approves the documentation PR containing this review.

Reviewed merged items:

- WI-0073 — Polish cards, menus and archive navigation
- WI-0074 — Filter Face Review by suggested person
- WI-0075 — Make GeoNames background timing configurable from launcher settings
- WI-0077 — Simplify Photo Viewer metadata and location editing
- WI-0078 — Reprocess stale photo metadata after extraction-contract changes

## Review outcome

- **WI-0074:** maintainer browser verification passed. Suggested-person filtering, composition and navigation behaved as expected. No corrective product work was requested.
- **WI-0078:** maintainer real-catalogue verification passed. Normal stale refresh, current-row behavior, force refresh and metadata preservation behaved as expected. PR #195 exact-head workflow #1207 (`32307001482`) also passed. The GeoNames language finding below is not a WI-0078 metadata-refresh defect; it belongs to WI-0064/WI-0065.
- **WI-0073, WI-0075 and WI-0077:** implementation is usable but the maintainer requested the corrective changes below before final acceptance.
- **WI-0064/WI-0065:** the review exposed a GeoNames language-policy improvement that should be corrected with the existing enrichment work rather than attached to WI-0078.

## Corrective implementation plan

### WI-0073 — Face Review density and person presentation

#### Suggested-person picker

- The suggested-person result list must **not** be expanded on initial page load.
- Treat the control as a compact searchable picker:
  - closed initially;
  - open when the search field is focused/used;
  - close after selection;
  - close on Escape/outside dismissal where practical;
  - when selected, show the chosen person plus Clear without leaving the candidate list expanded.
- Hidden-person badges in compact picker/list contexts must say **`Hidden`**, not `Hidden from Smart Collections`.
- Compact badges should not wrap into a second line beside a long display name. The person's name may truncate/ellipsis before the action area or badge is lost.

#### Queue controls

- Reduce the vertical footprint of the Face Review queue-controls panel.
- The collapsed suggested-person picker should remove the largest source of excess height.
- Processing run and suggestion model controls may use narrower columns; the layout should reserve more useful width for person selection while retaining all existing filter semantics.
- Keep the controls visible; this is a density/layout correction, not a request to hide functionality.

### WI-0073 — Smart Collection people picker

- Representative portraits in selected/available-person rows must fit comfortably inside the row with balanced vertical spacing; the circle should not visually touch or exceed the row bounds.
- Selected and available lists may remain bounded/scrollable, but the **Add/Remove action must remain visible when the list gains a scrollbar or contains many people**.
- Reserve a stable action column and allow only the middle name/status area to shrink/ellipsis.
- Scrollbar width must not steal the action column.
- Verify selected lists with more than 5–6 people as well as short lists.

### WI-0073 — Maintain People simplification

- Do not show an `Available in Smart Collections` badge. Availability is the normal state and needs no badge.
- For hidden people, use the compact badge text **`Hidden`**.
- Change visibility button text to **`Hide`** / **`Show`**. Longer Smart Collection wording can remain in accessibility text/tooltips or surrounding page explanation.
- In the person-card overview, remove the visible `Featured photo`/`Automatic photo` and `Selected explicitly`/`Selected automatically` copy beneath the representative portrait. The portrait-selection distinction remains available in the dedicated featured-photo controls where it can be changed.
- Replace the technical `labels` count with a user-facing **photo count**.
  - Do not merely relabel the existing `person_labels` row count.
  - Count distinct photo revisions in which the person is known to appear.
  - Include confirmed face/person evidence and manual photo-level person presence without double-counting the same revision.
  - Present the result as `1 photo` / `N photos`.

### WI-0073 — Archive advancement state

Observed behavior: after application startup, Archive reported `Waiting for OneDrive to finish a managed download or release.` Adding a new archive folder caused verification to proceed and all new files to become verified, while the top-level state continued to report Waiting for OneDrive.

The current state classification treats the presence of any managed hydration/release transition as `waiting`, even when other useful work is runnable. Correct the state model so it distinguishes at least:

- useful/runnable archive work;
- active OneDrive download/release transitions;
- OneDrive being the **only** remaining blocker.

Required behavior:

- Report **Running** whenever useful archive work can still progress, even if OneDrive transitions also exist.
- Include transition counts/details in a running message when useful.
- Report **Waiting for OneDrive** only when OneDrive transitions are the sole remaining blocker.
- Add regression coverage for: an existing managed transition is present, a new archive folder is added, synchronization/verification progresses, and the overall state remains Running while that progress is possible.
- If a transition is no longer real but a durable lease remains active, investigate/reconcile stale lease state separately rather than masking it with presentation wording.

### WI-0075 — GeoNames automatic request interval

The maintainer rejected the 30-second hard minimum as an application policy.

Revised contract:

- `PhotoIdentity__GeoNames__AutomaticMinimumRequestIntervalMilliseconds` has a **default of 30000 ms**, but **no 30000 ms minimum**.
- An explicitly configured lower non-negative value must override the default and be applied as requested.
- Do not silently clamp a requested value back to 30000 ms.
- The launcher/API should validate only the actual supported numeric bounds. Use `0` to mean no intentional normal pacing delay if the lower-level provider client can safely represent it.
- Settings/diagnostics must show the effective configured value.
- Provider-directed quota/account/transport backoff remains authoritative and may delay requests longer than the configured normal interval.
- Remove or reconcile the current double-throttle so a lower automatic interval is not silently defeated by an independent lower-level client default. The effective behavior shown to the operator must match actual normal pacing.
- Documentation may warn that aggressive values can spend provider credits quickly, but the application should not reject an explicit lower value merely because it is below the conservative default.

### WI-0077 — Photo Details metadata layout

- Correctly shown/hidden metadata fields from the first WI-0077 slice are accepted.
- Improve the at-a-glance grid so ordinary desktop values do not wrap across three lines.
- Prefer a compact two-column desktop arrangement with one-column fallback on narrow screens.
- `Photo taken` should normally fit on one line at desktop widths.
- Camera make/model may be presented as one **Camera** value when that improves fit/readability.
- Remove the rule that forces GPS coordinates to span the entire metadata width; GPS should use an ordinary metadata cell.
- Structured short values should not use aggressive `overflow-wrap:anywhere` as the normal desktop behavior.

### WI-0077 — Location placement and compact display

- Move the Location section immediately after capture metadata/GPS and before People so the named place is visually associated with the coordinates.
- Keep the complete canonical Place hierarchy unchanged for persistence, editing and Smart Collection filtering.
- Read mode should present a compact human-readable location rather than the full path such as:

  `Sverige/Stockholms län/Stockholms stad/Brännkyrka/Långbro`

- Target normal presentation is **city + most-specific locality**, for example `Stockholm · Långbro` (word order/separator may be adjusted for the UI).
- Do not infer the city merely by taking an arbitrary positional segment from the hierarchy. GeoNames hierarchies have different administrative depths across countries.
- For provider-derived places, expose/persist enough semantic display information to choose a city/locality reliably, or otherwise use a deterministic provider-aware compact-label rule.
- Keep the full hierarchy available in edit mode and optionally as secondary/tooltip detail.
- Manual Places that lack provider semantic metadata still need a deterministic compact fallback, with the full stored path remaining authoritative.

### WI-0064 / WI-0065 — GeoNames language policy

Observed behavior: `lang=local` gives desirable Swedish names in Sweden and English/local names in some other countries, but produces unwanted local-language names for countries whose local language is neither Swedish nor English.

Desired policy:

- **Sweden (`countryCode=SE`): use GeoNames local-language names.**
- **Outside Sweden: use English names.**
- If that policy proves impractical with the provider contract, the fallback preference is Swedish globally rather than arbitrary local languages.

Preferred implementation:

1. Perform the normal lookup using `lang=local` so Swedish coordinates keep Swedish names without extra requests.
2. If the result country is not `SE`, obtain/cache an English (`lang=en`) representation for that coordinate before assigning the canonical automatic Place.
3. Ensure cache/provider-contract keys distinguish the language policy so an old local-language cache entry is not reused as though it satisfied the new policy.
4. Avoid repeated double lookups: once the foreign coordinate's English result is cached under the current contract, later revisions at the same coordinate should reuse it.
5. Preserve all existing manual-place precedence, privacy, no-hydration and provider-backoff rules.
6. Document that the first lookup of a non-Swedish coordinate may use an additional provider request/credits.

Test with at least:

- a Swedish coordinate where Swedish/local names are retained;
- an English-speaking-country coordinate;
- a non-Swedish/non-English local-language coordinate where the resulting Place is English;
- cache reuse under the new policy;
- manual-place protection.

## Items explicitly accepted in this pass

### WI-0074

The maintainer confirmed:

- suggested-person filtering works;
- composition with existing queue controls works;
- navigation/return behavior works;
- favorite-person selector behavior works;
- menu dismissal behavior associated with the reviewed M20 polish works.

No corrective implementation is requested for WI-0074.

### WI-0078

The maintainer confirmed the versioned metadata refresh works on the real catalogue. The final exact-head PR workflow also passed. No corrective metadata-refresh implementation is requested.

The GeoNames language-policy finding is tracked against WI-0064/WI-0065 because it affects reverse-geocoding output, not extraction-contract refresh semantics.

## Implementation gate

This review document and its associated work-item documentation changes are intentionally a **docs-only PR**.

After the maintainer approves/merges that PR:

1. implement the corrective slices without mixing them into WI-0076;
2. keep each corrective PR narrowly scoped to its owning work item/domain;
3. run automated validation appropriate to each slice;
4. return the affected browser/operator behaviors to the maintainer for final acceptance;
5. only then reconcile lifecycle completion for the items still carrying corrective findings.

Do **not** start corrective product-code implementation before the maintainer approves this documented plan.
