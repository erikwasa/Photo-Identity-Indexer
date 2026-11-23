# Moment clustering evaluation

WI-0118 treats a photo moment as **derived, regenerable presentation evidence**. A moment is not a canonical event, is not user-authored archive state, and its identifier is intentionally ephemeral.

## Capture-time semantics

Moment clustering uses canonical `TakenAtLocal` capture metadata. That value is photographic wall-clock time. If the camera did not provide a timezone offset, the application does not invent one and does not convert the timestamp to UTC.

The clustering path never substitutes catalogue observation/import time for a missing capture timestamp. Photos without a usable capture timestamp remain explicitly unclustered.

## Evaluation candidates

The first private evaluation compares two deliberately simple timestamp-only policies over the same source sample:

- `m26-time-gap-30m-v1` — split when the gap between consecutive capture times is greater than 30 minutes.
- `m26-time-gap-90m-v1` — split when the gap between consecutive capture times is greater than 90 minutes.

A gap exactly equal to the configured threshold stays in the current moment. Crossing midnight does not create a boundary by itself.

The private representative review completed on 2026-09-18 and selected `m26-time-gap-30m-v1` as the initial default because its boundaries were more coherent overall than the 90-minute candidate. `m26-time-gap-90m-v1` remains an explicit comparison policy for future tuning; moment membership remains derived and regenerable rather than canonical archive truth.

## Bounded preview

With the application running against the normal catalogue provider, request:

```powershell
Invoke-RestMethod "https://localhost:5001/api/moments/preview?limit=120&gapMinutes=30&comparisonGapMinutes=90"
```

Use the actual local application URL if it differs. The endpoint accepts:

- `offset` — non-negative timestamped-photo offset, default `0`;
- `limit` — sample size from `1` through `200`, default `120`;
- `gapMinutes` — first candidate gap from `1` through `720`, default `30`;
- `comparisonGapMinutes` — second candidate gap from `1` through `720`, default `90`.

Both policies are evaluated over the exact same bounded source page. Each returned member includes the revision ID, capture time and thumbnail URL. `sampleMayTruncateBoundaryMoments` is `true` whenever the source page does not cover the entire timestamped catalogue; treat first/last moment boundaries in such a page as potentially truncated.

The endpoint is read-only and does not persist moment membership.

## Private representative review

Keep filenames, people names, paths, images and private sample details outside the repository. Record only aggregate observations such as:

- sample size and broad composition (ordinary home/family sequences, outings if available);
- approximate number of obvious over-merges and over-splits for each candidate;
- examples described generically, such as "morning routine merged with evening photos" rather than identifying the photos;
- whether a different gap should be evaluated;
- whether optional evidence appears necessary to resolve recurring mistakes.

Review more than one page/sample. Ordinary home and family sequences are required because the feature must not be tuned primarily around travel or GPS-rich photos.

## Optional evidence seam

`PhotoMomentGapPolicy` keeps capture time primary while exposing bounded optional evidence hooks for later refinement:

- shared people, generic tags or source-group evidence may extend an otherwise time-based boundary only inside an explicitly configured extension window;
- nearby coordinates may provide the same bounded support when enabled;
- sufficiently distant coordinates may force a split when an explicit distance threshold is enabled.

The initial 30/90-minute evaluation candidates enable none of these refinements. Missing people, tags, source/path evidence or GPS therefore has no effect on baseline clustering.

## Determinism

For a fixed catalogue state and policy version, members are ordered by `TakenAtLocal` and then revision ID. The result is therefore independent of repository/input enumeration order. Re-running the same state regenerates the same moment membership, member order and ephemeral per-result moment IDs.
