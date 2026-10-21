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
  truncation if there are more than the first 80 groups.
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
2. Click **Find similar alternatives**. A panel should appear with
   source-safe thumbnails and policy version, or an explicit zero-groups
   message if no close moment pairs are detected.
3. For one genuine same-moment group, click **Prefer this photo** and
   confirm the UI records your preference. For another, choose
   **Keep all alternatives**. If no groups are found on the first
   saved collection, a burst-heavy Smart-anchored Creative Collection
   is a more useful evaluation input.
4. Navigate away, return, click **Find similar alternatives** again,
   and verify your preferences are still present. Restart Photo Identity,
   repeat, and verify choices persist and remain independent of any
   *different* saved Creative Collection.
5. Confirm **Save & start slideshow** and the normal Creative preview
   still select exactly as before. Preferences are not yet applied to
   live slideshows; *that is intentional*.
6. Confirm no unexpected OneDrive hydration, global Photo Identity
   exclusions or changes to normal Archive, Search, Smart Collections
   or Slideshow playback.
7. Report only aggregate totals (candidate count, number of proposed
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
