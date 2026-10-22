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

**At the time of the compact UI PR**, similarity policy remained H8 complete-link:
its conservative A≈B≈C splitting motivated the later, explicitly review-only
broader policy described below. Slideshow selection remains on the old policy;
automatic suppression is not authorized.

## 2026-10-22: broader review-only groups (operator follow-up)

After the compact/paginated review UI was merged (#651), the maintainer reported
that **every inspected group contained a redundant frame** and most bursts of
more than two near-copy photos were split into smaller groups. The operator
explicitly prefers a lower-precision **manual suggestion list** if that results
in larger, more useful curation choices. These observations are qualitative,
not a measured recall/precision study.

For **GET/PUT saved Creative similarity review only**, materialization now
creates an independent, versioned review grouping:

- `wi0638-review-neighbor-h12-anchor-h18-45s-v2`
- Same inferred moment; 45-second maximum span for the whole group.
- A candidate must have Hamming distance **<=12 to at least one group member**
  and distance **<=18 from the first representative**. This joins more
  nearby burst frames without an unbounded connected-component chain.
- The maximum **pairwise** Hamming distance shown in the UI can therefore
  exceed 12; that metric still describes the worst pair, not the admission
  criterion. The display labels remain descriptive, never exclusion evidence.
- Proxy fingerprinting is expanded to the review window **only for explicit
  similarity-review API calls**, using the existing revision-checked local
  proxy/hash cache. Other Creative materializations still use their normal
  workload and the accepted `m26-dhash64-h8-20s-v1` complete-link policy.
- GET displays the new review policy and groups; PUT recomputes and validates
  exact **current** group membership under the same review policy.
  Previous choices remain only if their exact group membership fingerprint
  still matches; changed groups deliberately do not inherit old decisions.

This changes no Creative selector penalties, stored recipe, slideshow snapshots,
Archive, Search, Smart Collection membership, existing Photo Identity exclusions,
source copies, or OneDrive originals. New groups have a higher chance of
including meaningful differences; a human still decides Keep all vs Prefer.
There is **no automatic removal/omission** from collections or slideshows.

### Verification after green CI and merge

1. Update main, restart the updated Photo Identity package, choose the same
   burst-heavy saved Creative Collection used for the first 1,535-candidate review.
2. Open similarity review and check policy
   `wi0638-review-neighbor-h12-anchor-h18-45s-v2`. The total group count
   is expected to change; no new fixed target count is claimed.
3. Inspect examples formerly split among several 2–3-photo groups: ideally
   more burst alternatives appear in one group. Use the group thumbnail
   disclosure and bounded page navigation.
4. Assess whether broader groups contain obviously unrelated/different-moment
   photos, or examples you want to keep for meaningful differences. A lower
   precision is expected, but different moments and >45-second groups remain
   forbidden. Record aggregate counts, approximate typical group size, and
   whether split bursts improved.
5. Verify **Prefer this photo**, **Keep all**, restart persistence and recipe
   isolation for the newly keyed groups. An annotation on a *changed* old
   group must **not** be silently applied to its broader replacement.
6. Compare Creative preview and slideshow before/after recording preferences.
   They must remain unchanged by review decisions. Verify no new original
   hydration, automatic source exclusion, or photo-feature regression.

Do not publish private photo IDs, thumbnails or review-state JSON files.

## 2026-10-22: nearby visual-group review sets (operator examples)

After PR #653, the operator identified two concrete examples in which detected visual
subgroups should be visible as **one manual review choice**:

- **2024-10-05 15:57–16:02:** five detected visual groups of sizes
  2, 2, 2, 4, 2, with group-local maximum distances of 0, 0, 0, 4 and 0.
  Different groups at the *same event* can be minutes apart.
- **2024-12-31 17:24:** three groups of sizes 2, 2, 3, with maximum
  distances 9, 12, 15. The groups share a minute but the hash
  threshold/representative restriction still separates them.

**Important:** A maximum pair distance of zero means that the photos *within that
particular group* have identical hashes; it does not show that different
groups resemble one another. A maximum pair distance of 15 can legitimately
occur under the neighbor H12/anchor H18 rule. The operator's visual
inspection of the private examples is the evidence for combining them for
review, not a cross-group hash-distance measurement.

For saved Creative similarity **review only**, the v2 visual groups are now
collected into v3 **burst review sets**:

- Version `wi0638-review-nearby-bursts-gap4m-span10m-v3`.
- Start with independently detected local visual groups (H12/H18,
  ≤45 seconds). Only those groups' photo members are included; unrelated
  timestamped singletons are not inserted into review sets.
- Merge consecutive groups from the **same inferred moment** if the gap
  between the latest captured photo in the current set and the start of
  the next group is ≤4 minutes, the total set duration is ≤10 minutes,
  and the accumulated count is ≤40 photos. Otherwise start another set.
- Individual subgroups remain evidence of actual dHash proximity.
  **Cross-subgroup similarity is not guaranteed**, so a combined set
  is a *review opportunity*, not a claim that all photos are duplicates.
  The UI displays each set's photo count, time range and number of
  underlying visual subgroups; maximum pair distance and per-photo
  distance are recalculated from the original local fingerprints.
- Existing bounded page navigation, exact revision group keys and
  GET/PUT decision validation still apply. A merged set has a new group
  key and **does not silently inherit annotations from old smaller groups**.
  Unchanged groups retain exact-revision decisions.
- Strict Creative H8 complete-link selection, saved recipes, slideshow
  exposure/selection and snapshots, Archive, Search, Smart membership,
  original photos, cloud hydration and automatic omission are unchanged.

### Focused local check after CI/merge

Compare the same two maintained-archive examples from October 5 and December 31.
Expected review result is one 12-photo set in the first example and one
7-photo set in the second **if the underlying groups belong to the same
derived moment and the precise capture timestamps satisfy the bounds**.
The UI will show the number of visual subgroups in each set.

Also inspect a busy ten-minute period to judge the downside of overly broad
grouping, and check that a 4-minute gap, 10-minute span, 40-photo cap and
moment boundary prevent unrelated long sessions from being collapsed.
Verify pagination and preserved decisions on unchanged groups, and check
newly combined groups require a fresh review decision. Keep-all and
Prefer remain annotations only. Do not interpret a whole review set as
one safe automatic omission decision.
