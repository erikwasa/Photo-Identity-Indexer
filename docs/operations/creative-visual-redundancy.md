# Creative visual redundancy experiment

WI-0123 evaluates a lightweight visual-redundancy layer for Creative Collections using the durable local review proxies that already exist for normal browsing. It does **not** read or hydrate authoritative originals.

The experiment is presentation-only derived evidence:

- immutable asset revision IDs remain the identity boundary;
- exact source duplicates remain a separate source/catalogue concept;
- inferred WI-0118 moments are not changed;
- Smart Collection exact membership is not changed;
- no photos are deleted or merged; and
- visual groups are regenerated from local proxy pixels whenever the experiment runs.

## Fingerprint

The candidate algorithm is versioned as `opencv-dhash64-9x8-gray-v1`.

The review proxy is decoded to grayscale, resized to 9 × 8 pixels, and converted to a 64-bit difference hash by comparing each horizontal pixel pair. Similarity is the Hamming distance between two hashes.

This is intentionally inexpensive and local. It is suitable for detecting obvious burst/near-duplicate frames, not semantic similarity or duplicate truth.

## Conservative grouping

Grouping is constrained in two ways before visual similarity is considered:

1. photos must belong to the same derived Creative moment; and
2. the full group capture span must be at most 20 seconds.

Within those bounds the grouping uses complete-link matching: every member must remain within the configured Hamming threshold of every other member. This avoids a loose A≈B≈C chain from grouping A and C when they are not themselves sufficiently similar.

Three candidate thresholds are exposed for private evaluation:

| Policy | Maximum Hamming distance | Maximum capture span |
| --- | ---: | ---: |
| `m26-dhash64-h4-20s-v1` | 4 | 20 s |
| `m26-dhash64-h6-20s-v1` | 6 | 20 s |
| `m26-dhash64-h8-20s-v1` | 8 | 20 s |

No policy is the normal Creative Collection default yet. Maintainer private review chooses the initial threshold.

## Selector interaction

`CreativeCollectionSelector` can consume a `PhotoVisualRedundancyResult`. The first selected photo from a visual group is unaffected. Each already-selected member of the same group adds a strong `visual-redundancy` score penalty of `-180`.

This is deliberately a preference rather than a hard exclusion. A target larger than the number of distinct visual groups can still include additional frames, and direct-anchor/context relevance remains available to decide which frame becomes the first representative.

## Private experiment endpoint

Use a burst-heavy saved Smart Collection:

```text
GET /api/smart-collections/{collection-id}/creative-redundancy-preview?targetCount=50&momentGapMinutes=30
```

Optional `maxCandidates` defaults to `200` and is bounded to `1..500`.

The endpoint hashes only configured durable review proxies. Missing or unreadable proxies are counted and skipped; the experiment never falls back to source originals.

The response includes privacy-safe aggregate fields that can be copied into delivery notes:

- `totalCandidateCount`
- `candidateSampleCount`
- `candidateSampleTruncated`
- `fingerprintedCandidateCount`
- `missingProxyCount`
- `unreadableProxyCount`
- per-policy `redundantGroupCount`
- per-policy `groupedPhotoCount`
- per-policy `suppressiblePhotoCount`
- per-policy `largestGroupSize`
- per-policy `baselineRepeatedSelectedFrames`
- per-policy `redundancyAwareRepeatedSelectedFrames`

Each policy also returns local thumbnail groups with per-member distance from the first representative. Those thumbnails are for private visual inspection and should not be copied into public delivery notes.

## Maintainer review

For at least two representative private burst groups:

1. compare Hamming 4, 6 and 8;
2. confirm obvious repeated frames are grouped;
3. look specifically for visually distinct frames that were grouped incorrectly;
4. note obvious repeated frames that were missed;
5. prefer the strictest threshold that removes useful repetition without collapsing distinct expressions/compositions;
6. compare `baselineRepeatedSelectedFrames` with `redundancyAwareRepeatedSelectedFrames`.

After that review, record the chosen policy version and wire that policy into the normal Creative materialization path. Until then the existing WI-0120 normal selection behavior remains unchanged.
