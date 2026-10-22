# Collection similarity review — first operator-controlled pilot (WI-0638)

## Background and accepted scope

The operator verified the completed WI-0629 Archive cleanup workflow:
the full-screen keyboard quality-suggestion reviewer and all existing
feature regressions work as expected after merged PR #644.
**Archive cleanup stays focused exclusively on low-quality photos.**
A different use case is finding **very similar photos among candidates
for a saved Creative Collection**, whose selected photos feed slideshows.

Existing Creative materialization already generates **dHash64** fingerprints
from local durable proxies and groups alternatives only within the same
derived moment and at most 20 seconds apart, using complete-link matching:
EVERY photo must be sufficiently close to EVERY other group member. The
accepted Creative selector H8 penalty is only a preference; it is not a
hard suppression rule or proof that every similar image is disposable.
M37's NO-GO for unattended archive-wide near-copy suppression remains.

## First native product phase

The Creative Collections page now has an explicit **Find similar alternatives**
button for an **already-saved** Creative Collection, either Smart-anchored or
search-anchored. This triggers a bounded existing materialization operation.
The resulting **Review similar photos** panel shows:

- Number of detected groups and candidate count, with explicit
  paginated groups (12 per page by default, up to 80 per API request) so no groups are discarded after the first 80.
- A collapsed-by-default review area, and compact per-group disclosure rows with a representative thumbnail, review status, and expandable side-by-side images. Previously reviewed groups can be hidden on the current page; Next/Previous navigates the full set of groups.
- Each proposed visually similar group's thumbnail images side by side,
  differences relative to the first image (64-bit dHash Hamming distance),
  capture date/time, and whether each is presently selected by the recipe.
- **Keep all alternatives** as the default human choice, or **Prefer this
  photo** for one candidate. The preferences are saved locally to
  `%LOCALAPPDATA%/PhotoIdentity/creative-similarity-review/creative-similarity-review-v1.json`
  for that exact saved recipe ID and exact set of photo revisions.
  Decisions survive application restart and cannot migrate silently to
  an altered group or an unrelated recipe.
- Both actions are currently **review annotations only**. They do
  **NOT** override current Creative selection, Smart Collection
  membership, slideshow output, Archive/Search results, or source
  photos. There is no automatic omission or deletion toggle in this
  initial UI, and no new upload/hydration of source originals.
- The endpoint uses `GET/PUT /api/creative-collections/{id}/similarity-review`.
  PUT recomputes the CURRENT saved recipe's grouping before accepting
  a decision and rejects stale group membership and nonmember preference
  IDs. There is no path from the review API to Photo Identity's
  irreversible source-copy exclusion endpoint.

This is an operator-review baseline and a first substantive part of
issue #638, **not yet collection filtering**. Future reversible
collection-scoped omission and any user-opted-in automatic curation
must be designed, tested and approved independently. It will need
selection/recipe integration and a way to restore omitted photos.
Do not mistake stored preferences for an active filter.

## First maintained-archive verification after green CI and merge

1. Open a **saved Creative Collection** with some burst photos under
   `http://127.0.0.1:5080/creative-collections`. Do not modify its
   recipe or saved selection merely for this evaluation.
2. Click **Find similar alternatives**. The review opens, reports the total group count and policy,
   and shows compact rows for the first 12 groups. Expand a row for side-by-side local thumbnails.
   If there are no close moment pairs, expect an explicit zero-groups message.
3. Expand one genuine same-moment group, click **Prefer this photo** and
   confirm the UI records your preference. For another, choose
   **Keep all alternatives**. If no groups are found on the first
   saved collection, a burst-heavy Smart-anchored Creative Collection
   is a more useful evaluation input.
4. Collapse the entire similarity section with **Hide review**, confirm the recipe editor stays compact,
   and reopen with **Show review**. On an archive with >80 groups, navigate **Next groups** through the
   complete set and return with **Previous groups**, confirming counts, grouping and choices remain stable.
   Toggle **Hide reviewed groups on this page**; this hides entries visually, not from persisted evidence.
5. Navigate away, return, click **Find similar alternatives** again,
   and verify your preferences are still present. Restart Photo Identity,
   repeat, and verify choices persist and remain independent of any
   *different* saved Creative Collection.
6. Confirm **Save & start slideshow** and the normal Creative preview
   still select exactly as before. Preferences are not yet applied to
   live slideshows; *that is intentional*.
7. Confirm no unexpected OneDrive hydration, global Photo Identity
   exclusions or changes to normal Archive, Search, Smart Collections
   or Slideshow playback.
8. Report only aggregate totals (candidate count, number of proposed
   groups, how many were genuinely redundant vs distinct, whether
   Keep all / Prefer decisions were convenient), and any unexpected
   behavior. Do not publish thumbnails, photo IDs or raw decision files.

## Decision gate for the next phase

If the maintainer finds **human-selected alternatives worthwhile**, build
explicit reversible **omit from THIS Creative Collection only**, with
stable recipe-scoped overrides, a restoration view, no changes to
source-copy identity or originals, opt-in defaults, and a regression
baseline preserving currently unaffected collections/slideshows.
Only after live side-by-side review should optional *automatic
collection-only* omission be considered, with its own precision,
fallback and operator acceptance gate.

A distinct [quality rating issue #639](https://github.com/erikwasa/Photo-Identity-Indexer/issues/639)
will define a continuous rating with model/proxy provenance,
uncertainty and licensing/normalization constraints. The existing
technical suggestion priority is NOT a calibrated 0–100 quality score.

## 2026-10-22 maintained-archive feedback and UI follow-up

The operator passed all 11 local checks for merged PR #647 (persistence, recipe isolation,
source safety and existing features). On 1,535 candidate photos, the accepted H8 policy
proposed **157 groups**, with just 80 displayed by the initial safety cap. The review
was only *partly* convenient because its content could not be collapsed. The operator
judged almost every inspected group to include a redundant photo, but did not supply
numerical redundant-vs-distinct counts. Several visually one-of-six bursts were split
across adjacent 2–3-photo groups.

The UI follow-up makes the whole section hideable, shows a compact preview/status row
for each group with a disclosure to compare images, offers a reviewed-only visibility
filter and bounded pagination covering **all** groups without generating giant DOMs.
The page defaults to 12 groups; GET supports `offset` (0-based, nonnegative) and
`pageSize` (1–80), with the same time-bounded materialization and exact revision-bound
PUT verification. Changing the selected recipe clears stale review data. No source or
selection change is introduced.

**Similarity policy deliberately unchanged.** The current accepted complete-link H8,
same-moment, max-20-second algorithm intentionally breaks A≈B≈C chains if A and C
are too far apart. Widening it would risk hiding meaningfully different expressions,
angles or subjects. A separate *read-only nearby-burst review* experiment should display
adjacent same-moment groups together without claiming that all cross-group pairs qualify;
measure what the maintainer regards as one curation choice, with a manual grouping override
only after operator acceptance. Do not silently reclassify groups or apply suppression.
