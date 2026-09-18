# Creative Collection preview and target selection

WI-0119 keeps a saved Smart Collection's exact matches as **direct anchors** and may add nearby photographs from the same WI-0118 inferred moments as **contextual additions**. WI-0120 then selects a best-effort target number of those candidates using deterministic, metadata-first diversity rules.

Neither stage changes the saved Smart Collection filter or exact membership semantics. Inferred moment membership, contextual additions and final Creative selection are derived/read-only presentation state; they are not persisted as canonical photo metadata.

## Open a preview

Use the saved Smart Collection identifier and optionally request a target count:

```text
GET /api/smart-collections/{collection-id}/creative-preview?targetCount=100
```

`targetCount` defaults to `100` and must be between `1` and `1000`. If fewer unique candidates exist, all available candidates remain valid; the selector never duplicates or invents photos to reach the target.

The accepted initial default uses the WI-0118 30-minute moment policy after private representative comparison with the 90-minute candidate. The 90-minute policy remains available for explicit comparison or future tuning:

```text
GET /api/smart-collections/{collection-id}/creative-preview?targetCount=100&momentGapMinutes=90
```

The response keeps the uncurated WI-0119 candidate set for comparison and also reports the WI-0120 selection:

- `directAnchorCount`, `addedContextCount`, `totalCandidateCount`: the uncurated anchor/context candidate set;
- `kind`: `direct-anchor` or `contextual-addition` for every candidate;
- `contextReasons`: moment identifier and exact anchor revision IDs that admitted a contextual photo;
- `selectionPolicyVersion`: currently `m26-target-diversity-balanced-v1`;
- `requestedTargetCount`, `selectedCount`, `selectedDirectAnchorCount`, `selectedContextCount`: target and selected composition;
- `selectedCandidates`: the final chronological Creative sequence;
- `momentId` and `peopleCombinationKey`: diversity evidence used when available;
- `selectionScore` and `selectionReasons`: inspectable reasons that affected each selected photo;
- `thumbnailUrl`: normal collection thumbnails for visual inspection.

Zero exact matches produce `noAnchors: true`, an empty candidate list and an empty selected list. Creative generation never broadens a zero-anchor query.

## Anchor/context policy

`m26-anchor-context-balanced-v1` admits at most six contextual photos per moment that contains at least one direct anchor. Context candidates are chosen by capture-time proximity to the direct anchors with stable timestamp/revision-ID tie breaking. Context-only photos never become new anchors, so expansion cannot recurse into a later moment.

All catalogue candidates are read through the existing Smart Collection query repository, preserving its deletion/source-visibility boundary.

## Target/diversity policy

`m26-target-diversity-balanced-v1` uses a deterministic greedy selector. Each remaining candidate is rescored after a photo is selected, so useful new coverage has diminishing returns once that coverage is already represented.

The current inspectable weights are:

| Evidence | Score effect |
| --- | ---: |
| Direct Smart Collection anchor | +35 |
| Contextual view | +15 |
| First selected photo from an inferred moment | +90 |
| Additional photo from the same moment | -45 × number already selected from that moment |
| First selected photo from one of 8 time-span buckets | +70 |
| Additional photo from an already represented time bucket | -5 × count, capped at -20 |
| First selected identified-person combination | +45 |
| Repeated identified-person combination | -20 × number already selected with that combination |
| Capture within 2 minutes of an already selected photo | -55 × nearby selected photos |

The absolute score is an implementation aid rather than a quality percentage. What matters is the deterministic ordering it creates. Equal scores use capture time and then immutable revision ID as stable tie-breakers. After selection, the final playback sequence is sorted chronologically by capture time and revision ID.

Location is not required. GPS/place metadata is not part of the WI-0120 selector, so the policy remains usable for archives with sparse or absent location metadata.

## Materialize a Creative slideshow snapshot

The selected sequence can be materialized through the same immutable slideshow response contract used by Classic Smart Collections:

```text
POST /api/smart-collections/{collection-id}/creative-slideshow-snapshot?targetCount=100&momentGapMinutes=30
```

The response contains the selected immutable revision IDs in final chronological order. Playback consumes those revision IDs rather than re-running selection while a session is active. Classic `POST /api/smart-collections/{collection-id}/slideshow-snapshot` behavior is unchanged.

WI-0121 owns productizing Creative recipes/previews and adding a normal user-facing launch surface; WI-0120 establishes and verifies the selection/snapshot boundary without adding another slideshow menu option.

## Combined maintainer verification for WI-0118–WI-0120

Human acceptance can be batched after these implementation work items are merged. Choose three private saved Smart Collections: one large result set, one small result set, and one burst/repetition-heavy result set. For each collection:

1. Open the exact Smart Collection result and note its size and obvious sequences.
2. Open `creative-preview` with `momentGapMinutes=30` and a useful target such as `50` or `100`; compare `Candidates` with `SelectedCandidates` and inspect thumbnails.
3. Repeat with `momentGapMinutes=90` where moment boundaries are material to the collection.
4. For the large collection, confirm the selected list covers visibly broader periods/moments than taking the first chronological target-sized slice.
5. For the repetition-heavy collection, confirm near-consecutive bursts or repeated people combinations occupy fewer selected slots than in the uncurated candidate list.
6. For the small collection, request a target larger than the candidate count and confirm every unique candidate remains available without duplication.
7. Confirm at least one contextual addition improves an understandable family-photo sequence while unrelated later moments remain excluded.
8. Create a `creative-slideshow-snapshot` for the chosen target and confirm its revision IDs match the preview's selected sequence.

Record which moment gap is more coherent and any obvious over/under-selection patterns. If tuning is needed, change the versioned policies rather than exposing raw scoring knobs to slideshow users.

### Maintainer acceptance recorded 2026-09-18

The private representative review selected the 30-minute moment policy as the more coherent initial default. Context additions generally improved same-moment sequence coherence, with an accepted limitation that timestamp-only grouping can occasionally admit an unrelated photo from the same time slot.

For the high-volume sample, simple chronological truncation covered 6 distinct days and 5 months and contained 40 adjacent pairs within two minutes, while the Creative 50 covered 48 distinct days and 22 months with no adjacent pairs within two minutes. The selected set covered 50 inferred moments and 19 identified-person combinations. On a repetition-heavy 84-photo sample, Creative selection reduced adjacent pairs within two minutes from 55 to 21 while selecting 50 photos. A target larger than a 14-photo candidate set returned all 14 unique candidates, and a 50-photo Creative snapshot matched the preview selection exactly by immutable revision ID and order.
