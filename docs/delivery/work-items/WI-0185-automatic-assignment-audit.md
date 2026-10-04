---
id: WI-0185
title: Redesign identity audit around cross-person automatic assignments
milestone: M35
status_source: PhotoIdentity.Docs
depends_on: []
related_adrs: []
affected_modules: [PhotoIdentity.Core, PhotoIdentity.Persistence.Postgres, PhotoIdentity.Api, PhotoIdentity.Web, PhotoIdentity.Integration.Tests, docs]
---

# WI-0185: Redesign identity audit around cross-person automatic assignments

## Context

The current `/audit` page is person-centric: the maintainer must select one person from a dropdown, inspect that person's active assignments, return to the selector and repeat. With more than 100 people this does not scale as a routine audit workflow and the page is consequently used very little.

Automatic assignment tuning now creates a second need. A regeneration can create hundreds or thousands of canonical assignments in one run, and threshold changes should be validated by quickly skimming the resulting faces for mistakes. The useful audit unit is therefore a filtered set of assignment events grouped by person, not one preselected person.

Automatic suggestion acceptance already writes canonical `review_actions` and links the accepted suggestion through `identity_suggestion_review_actions.review_action_id`. That relational provenance provides the assignment source, timestamp, accepted suggestion, exact model revision, score and rank-one margin without reconstructing the decision from today's current top suggestion.

Tracked by GitHub issue #503.

## Scope

- Redesign `/audit` so a person selection is not required to see results.
- Add catalogue-wide assignment filtering by assignment source and assignment date range.
- Treat `identity-matcher:auto` and `identity-matcher:auto-multi-evidence` as Automatic while retaining enough source detail to distinguish ordinary and multi-evidence assignments on individual cards.
- Define filtering against canonical assignment `review_actions.created_at_utc`; local From/To date controls convert explicitly to UTC. The selected From date begins at local midnight inclusively and the selected To date covers the whole local calendar day by using the following local midnight as the exclusive upper bound.
- Default audit semantics to currently active assignments. A later manual correction, Unknown or Reject decision must remove the superseded automatic assignment from the normal current audit result without deleting its append-only history.
- Group the continuous result stream by assigned person with a prominent person header. Keep the person context visible while scrolling through a large group so cards do not need to repeat the assigned person's name.
- Keep the audit filters accessible while scrolling instead of requiring a return to the top of a long result set.
- For Automatic results, order faces within each person by lowest accepted score margin first, with deterministic tie-breaking. This makes assignments nearest the configured margin threshold easiest to inspect first.
- Show compact assignment provenance on each automatic card: ordinary versus multi-evidence source, assignment time, accepted score, accepted score margin and exact embedding-model revision.
- Read accepted score/model/margin from the suggestion decision linked to the assignment action. Do not substitute the current top suggestion and do not parse free-text notes when structured relational evidence exists.
- Preserve the existing face-details/history correction flow. Opening a face and returning must restore the audit filters and the user's practical position/context rather than forcing a new person selection.
- Load large result sets in bounded/lazy batches suitable for thousands of assignments and more than 100 people. Do not issue one API request per person.
- Preserve useful current audit capabilities where they remain relevant, including viewing assignment history and optional current-suggestion disagreement information, without letting disagreement state become the definition of assignment correctness.
- Profile the cross-person PostgreSQL query on realistic catalogue volume and add an appropriate migration/index only if the new access pattern needs it.

## Architecture constraints

The canonical current face decision remains the latest unreversed `assign`, `unknown` or `reject` review action for that face. Source/date filtering for the default audit view must apply to that current decision, so an older automatic assignment does not reappear after a later manual correction.

Automatic-assignment provenance should follow `identity_suggestion_review_actions.review_action_id` to the accepted suggestion and its exact-model ranking. The audit must remain correct if current suggestion rankings later change or are regenerated.

Paging must be deterministic and bounded under concurrent catalogue changes. Prefer a stable cursor/keyset contract when practical; if offset paging is retained, tests must prove that grouping/lazy loading cannot loop or duplicate cards under expected mutations.

## Out of scope

- Changing automatic-assignment score or margin thresholds from the audit page.
- Triggering match regeneration from the audit page.
- Policy-version/threshold-history filtering in the first slice.
- Showing superseded historical automatic assignments in the default result stream; a later historical-accuracy mode may expose them deliberately.
- Bulk reassignment/correction actions.
- Automatically declaring an assignment correct because the current suggestion agrees with it.

## Acceptance criteria

- [x] `/audit` can show matching assignments across all people without requiring a person dropdown selection.
- [x] Source filtering can show Automatic assignments, covering both ordinary and multi-evidence automatic actors, while cards distinguish which source made the assignment.
- [x] Optional From/To date controls filter on canonical assignment time with explicit local-day-to-UTC conversion; the From date is inclusive and the To date includes the full selected local calendar day.
- [x] Results are visibly grouped by assigned person, with clear/sticky person context and no repeated assigned-person sentence required on every card.
- [x] Automatic assignments within each person are ordered by score margin ascending with deterministic ties, making the weakest accepted margins appear first.
- [x] Each automatic card exposes the accepted score, margin, exact model revision and assignment time from the assignment-linked suggestion provenance.
- [x] A face manually corrected after automatic assignment no longer appears in the default current Automatic result, while its historical actions remain visible in face history.
- [x] Opening a face for correction and returning restores the audit query/filter context and does not force the maintainer to restart at a person selector.
- [ ] Thousands of matching assignments can be browsed through bounded lazy loading without fetching all thumbnails up front and without one request per person.
- [x] PostgreSQL access for the cross-person audit is set-oriented, deterministic and covered by focused persistence/API tests; any required index is delivered through the normal schema migration path.
- [x] Existing face-history correction/undo semantics and append-only canonical review history are preserved.
- [ ] Maintainer verification on the real catalogue confirms that a date-bounded automatic-assignment run can be skimmed continuously across many people and suspicious assignments can be corrected without losing audit context.

## Verification requirements

Add focused persistence tests for current-decision selection, automatic actor filtering, assignment-time boundaries, accepted-suggestion provenance joins, corrected/superseded assignments and deterministic ordering. Add API tests for cross-person filtering/paging and Web/component coverage for grouping, sticky/filter state, lazy loading and return navigation.

On the maintained Windows catalogue, use a representative automatic-assignment date range containing many people. Verify that the page can move continuously through the result set, weakest margins appear first inside each person group, ordinary/multi-evidence provenance is truthful, correcting a suspicious face removes it from the current Automatic audit after refresh, and navigation back restores the audit context.

Run relevant builds/tests plus `PhotoIdentity.Docs validate` and `PhotoIdentity.Docs generate --check`.

## Implementation — 2026-10-04

PR #506 replaces the person-selector audit with a catalogue-wide assignment stream. `PostgresAssignmentAuditRepository` selects the latest unreversed canonical face decision, filters by assignment source and inclusive-lower/exclusive-upper assignment timestamps, joins accepted suggestion evidence through `identity_suggestion_review_actions.review_action_id`, and orders deterministically by assigned person then accepted score margin. Both ordinary and multi-evidence automatic actors are included under the Automatic source while the exact actor remains available to the UI.

`/audit` now exposes source and local From/To date filters, optional current-model disagreement comparison, grouped person sections with sticky person headers, lazy-loaded face images and bounded 120-item pages. Face-detail links encode the audit query, loaded-item target and person anchor so correction/history navigation can return to the same practical audit context. A later manual correction is excluded from current Automatic results because filtering is applied to the latest canonical decision rather than historical assignment rows.

PR #506's application test covers automatic actor filtering, accepted score/margin/model provenance, weakest-margin ordering, UTC time-bound semantics, manual source results and suppression of a superseded automatic assignment. CI run #2622 passed the full build/test, both integration shards, documentation generation/validation, published review verification, Windows mixed-media verification, package verification and launcher verification.

A follow-up acceptance-coverage change adds:

- `AssignmentAuditEndpointPagingTests` for deterministic API offset pages and non-overlap;
- `AssignmentAuditPagingTests` for overlapping/fully-overlapping client pages, proving de-duplication and forward offset progress rather than a duplicate loop; and
- `AssignmentAuditWebContractTests` for cross-person grouping, source/date controls, lazy images, sticky filters/person context, phone single-column behavior and encoded return context.

No new database index is added in this slice. Synthetic tests establish the query semantics and bounded access shape, but realistic catalogue-volume query/runtime behavior is deliberately left to the maintained-catalogue verification below. If that verification shows an unacceptable query plan or browsing latency, WI-0185 remains open and the index/migration must be delivered before completion.

## Maintainer verification — 2026-10-04 (Europe/Stockholm)

Maintained verification exposed a usability defect in the original `datetime-local` From/To controls before the wider catalogue checks could be completed. Typing a time reset the control, calendar-selected values disappeared, and applying a From/To range appeared to have no filtering effect. The maintainer requested date-only From/To controls because day-level specificity is sufficient for assignment-run auditing.

The follow-up changes the audit UI to standard date inputs while preserving the API's UTC timestamp contract. From converts to local start-of-day inclusively; To is user-inclusive and converts to the following local midnight as the exclusive upper bound. WI-0185 remains `in_review` until this date-only interaction and the remaining large-catalogue checks pass.

## Remaining maintainer verification

On the maintained Windows catalogue, choose a date range from a recent automatic-assignment run containing many people and verify:

1. `/audit` opens directly to cross-person results without requiring a person choice.
2. Automatic includes both ordinary and multi-evidence assignments and the card badge distinguishes them.
3. From/To dates remain selected after calendar entry and Apply filters bounds the expected assignment run, including the whole selected To date.
4. Person groups remain understandable while scrolling and weakest accepted margins appear first inside each person.
5. Score, margin, exact model revision and assignment time look truthful on representative automatic cards.
6. Load more can move through a large result set without duplicates, loops, excessive delay or loading all thumbnails up front.
7. Open one suspicious face, correct it manually, return to the audit and confirm the filters/context are restored; after refresh that face no longer appears in current Automatic results while its history remains available.
8. Note approximate result count and responsiveness. If the maintained catalogue exposes a material query/performance problem, profile it before accepting WI-0185 and add an index migration only with measured evidence.

## Deferred calibration follow-up

After the cross-person audit proves useful, evaluate a separate follow-up for policy-version/threshold filtering and deliberate inclusion of superseded automatic assignments. That would support historical precision measurement across threshold experiments without complicating the first operational audit slice.
