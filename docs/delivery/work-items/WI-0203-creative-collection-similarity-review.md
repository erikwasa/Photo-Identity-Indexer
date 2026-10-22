---
id: WI-0203
title: Human-reviewed similar-photo and burst sets for saved Creative Collections
milestone: M39
status_source: ../status/work-items.yaml
depends_on: [WI-0123, WI-0158]
related_adrs: []
affected_modules: [PhotoIdentity.Core, PhotoIdentity.Api, PhotoIdentity.Web, PhotoIdentity.Core.Tests, PhotoIdentity.Integration.Tests, docs]
---

# WI-0203: Human-reviewed similar-photo and burst sets for saved Creative Collections

## Objective

Find visually related alternatives among **already eligible** candidates in a saved Creative Collection. Let the operator compare thumbnails, keep all alternatives, or mark one preferred photo, without changing collection/slideshow selection or original media. Make large, time-extended bursts practical to review.

## Origin, planning and canonical ownership

Maintainer requested this workflow on **2026-10-21** as a follow-up to the completed M37 visual-redundancy research (WI-0194) and the native Archive technical-quality reviewer. It was initially and **incorrectly** tracked only in GitHub issue **#638** and the development PRs used the label **WI-0638**, although there was **no matching repository work-item document, milestone membership or canonical YAML shard**. On **2026-10-22** the maintainer explicitly required repository-native work-item planning. This is the authoritative migration of that history to **WI-0203**. Prior PR titles and historical issue references are evidence only; they do not establish an additional work-item ID.

M26/WI-0123's accepted `m26-dhash64-h8-20s-v1` complete-link selector remains intact. M37/WI-0194's **NO-GO for unattended/global redundancy suppression** remains in effect. Scope here is **manual suggestions and annotations**, not collection filtering; see [WI-0204](WI-0204-reversible-collection-scoped-alternative-omission.md) for the separate not-yet-implemented feature.

## In scope

- Side-by-side local-proxy thumbnails with capture time, exact revision identity, currently selected/not selected status, visual evidence distances and explicit policy version.
- **Keep all alternatives** by default; **Prefer this photo** as a persistent *annotation*. Scope stored choices by saved recipe ID and exact hash of all group revision IDs. Recompute the current grouping on write; reject stale/non-member choices. No silent migration to changed group membership.
- A collapsible review section, compact individually expandable groups, visible choice state, optional hide-reviewed view, and bounded pagination across **every** group.
- Review-only recall-oriented visual grouping, distinct from the production selector: dHash64 H12 to any nearby group member and H18 from the first member, same inferred moment, maximum 45 seconds.
- Group nearby *already visually supported* groups into burst **review sets**, up to **4 minutes between groups**, **10 minutes total**, **40 photos per set**, without crossing inferred-moment boundaries or inserting arbitrary timestamp-only singletons. Display subgroup count because not all photos in one review set are necessarily visually similar.
- Keep normal Creative selection, snapshots/slideshows, Archive, Search, Smart Collections, exclusion boundaries, derivative/source-copy identity and OneDrive originals unchanged.

## Out of scope

- Applying saved review choices to collection membership or slideshow output.
- Automatic omission/suppression, irreversible global Photo Identity exclusions or original-photo deletion.
- Claiming all cross-subgroup photos are duplicates merely because they share a review set.
- A calibrated image-quality score, model inference or renewed M37 held-out tuning. Separate future work requires its own work item and evaluation.

## Delivered implementation and progress

1. **PR #647 (merged):** First operator-controlled native review for search- and Smart-anchored saved recipes. GET/PUT API uses already-local local proxy dHash evidence. Persists recipe/exact-revision-bound manual annotations, with no selection mutation. The initial response exposed at most 80 groups.
2. **2026-10-22 first local acceptance:** **11/11 PASS** — focused tests, smoke, health, detection/thumbnails, Prefer, Keep all, navigation/restart persistence, cross-collection independence, collection/slideshow unchanged, Archive/Search/Smart regressions, and no unexpected original hydration/modification. **1,535 candidates, 157 groups**; 80 initially visible. The operator judged virtually every inspected group to contain redundancy but recorded no formal redundant/distinct counts; review convenience was *partly*.
3. **PR #651 (merged):** Collapse/hide review, compact photo-group disclosures and review state, hide reviewed, complete bounded pagination (12 per page/default; API max 80), stale-view reset on recipe change. Maintainer reported **“The UI looks better.”**
4. **PR #653 (merged):** Independent manual review-only H12-neighbor/H18-first-photo/45s policy; strict H8 complete-link remains the Creative selector rule. User confirmed **100% of inspected groups were redundant**, but many bursts with >2 similar photos still split.
5. **Concrete split-burst maintainer feedback:** **2024-10-05 15:57–16:02:** five visual groups with sizes **2+2+2+4+2**, internal maximum Hamming distances **0/0/0/4/0**, desired *one 12-photo review set*. **2024-12-31 17:24:** groups **2+2+3**, maxima **9/12/15**, desired *one 7-photo set*. Maximum distance is a *within-group* measure; it gives no cross-group evidence.
6. **PR #654 (merged):** Added review-only v3 `wi0638-review-nearby-bursts-gap4m-span10m-v3` coalescing across already-detected visual groups with exact same inferred moment, inter-group gap <=4 min, span <=10 min, maximum 40 photos, deterministic exact-revision keys and recomputed explanatory distances; displayed source visual-subgroup count. Maintainer feedback after merge: **“Much better.”** This is a positive usability observation, **not** evidence that every larger set is safe for automatic omission.
7. **Documentation migration:** Work-item ID normalized to WI-0203 within repo planning; preceding `WI-0638` remains only a historical PR naming artifact.

The detailed current operating procedure and detection contract are in [Creative similarity review runbook](../../operations/creative-similarity-review.md). Neither personal photos nor review JSON or identifiers are included here.

## Acceptance criteria

- [x] A saved Creative Collection exposes bounded local similarity candidates with explainable thumbnail evidence; no original hydration.
- [x] Operator can record Keep all/Prefer one per exact current group and recipe, and choices persist across navigation/restart without affecting other collections.
- [x] Review can be collapsed; individual sets expand; every group is navigable with bounded pagination instead of a first-80 truncation.
- [x] A review-only broader grouping can coalesce time-extended, previously split bursts without altering production H8 selector evidence or automatic slideshow behavior.
- [x] Both named split-burst patterns are represented in deterministic synthetic tests (subject to actual moment/timestamp bounds on real photos).
- [x] User reported improvement in UI and burst review after merged PRs #651 and #654.
- [ ] Final maintained-archive regression of v3 group persistence/page navigation, no unexpectedly unrelated sets, and unchanged Creative preview/slideshow is explicitly reported for the **latest** package, or an explicit acceptance exception is documented before terminal closeout.

## Verification requirements

Automated Core/integration tests for grouping, boundaries, revision-bound choices and pagination; existing review regression smoke; visual maintainer inspection of local dated examples and unrelated merged sets. Do not publish private image paths, thumbnails, revision IDs or the choices JSON. The original **11 PASS** result applies to the first PR #647, not automatically to all later changes.

## Remaining work and decision

- WI-0203 is **in_review** until the final v3 end-to-end/behavior check is accepted or explicitly waived with recorded rationale. It is not a license to enable automatic omission.
- Follow-up [WI-0204](WI-0204-reversible-collection-scoped-alternative-omission.md) retains the original, separate requirement for explicitly opted-in, reversible omission from **this Creative Collection only** and a restoration control. All production automatic suppression remains NO-GO without new evidence.

## Completion notes

- Delivered files: `PhotoVisualRedundancy.cs`, `CreativeCollectionMaterializationService.cs`, `CreativeSimilarityReviewEndpoints.cs`, `CreativeSimilarityReviewStore.cs`, Creative Collections Razor UI/CSS, Core/integration tests and operations runbook.
- Trade-off: v3 intentionally prioritizes recall/usability for a *human review set*, so cross-subgroup visual similarity is not guaranteed. Exact-member choices become stale when a group changes rather than transferring silently.
- Evidence: merged PRs [#647](https://github.com/erikwasa/Photo-Identity-Indexer/pull/647), [#651](https://github.com/erikwasa/Photo-Identity-Indexer/pull/651), [#653](https://github.com/erikwasa/Photo-Identity-Indexer/pull/653), [#654](https://github.com/erikwasa/Photo-Identity-Indexer/pull/654); maintainer private-archive aggregate reports above.
- Historical origin: GitHub issue #638 is retired as planning authority once this migration lands. Do not open a replacement issue.
