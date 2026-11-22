---
id: M39
title: Creative Collection human-review curation and reversible alternatives
status_source: ../status/milestones.yaml
depends_on: [M26, M37]
---

# M39: Creative Collection human-review curation and reversible alternatives

## Outcome

Photo Identity lets the operator inspect visually similar or temporally related *review candidates* in a saved Creative Collection, then optionally and **reversibly** omit individually chosen alternatives from that collection only. This product work follows completed M26 Creative Collections and completed M37 research, without reversing M37's NO-GO for unattended/global redundancy suppression.

## User-visible demonstration

1. Open a saved Creative Collection, expand **Find similar alternatives** and paginate or collapse bounded human-review sets.
2. Compare local thumbnails of candidates and explicitly record **Keep all** or **Prefer this photo**. Until WI-0204 is accepted, these remain annotations only.
3. In the separate WI-0204 phase, explicitly opt in to omit selected exact photo revisions **from that collection's future slideshows only**, inspect resulting selection and restore any omission.

## Work items

- [WI-0203](../work-items/WI-0203-creative-collection-similarity-review.md) — delivered review UI and recall-oriented burst-review candidate grouping; final latest-version maintained-archive verification remains open.
- [WI-0204](../work-items/WI-0204-reversible-collection-scoped-alternative-omission.md) — proposed opt-in reversible collection-only omission, restore UI and independent testing.

## Prior delivery and evidence

WI-0203 migrated the first pilot previously mislabeled **WI-0638** in PRs #647, #651, #653 and #654. Operator first accepted 11/11 local checks and later reported the UI *looks better* and broader burst grouping is *much better*. The original 1,535 candidates, 157 groups and exact dated 12-/7-photo burst examples are retained in the WI-0203 document and the operations runbook.

## Delivery sequence

1. Confirm WI-0203's final v3 regression/safety acceptance; record evidence in the repository YAML.
2. Scope WI-0204's persistent, explicit recipe-local omit/restore semantics; implement tests and maintained Windows A/B.
3. Decide independently whether any automatic application is safe. Default **NO automatic omission** until a separate decision and evidence.

## Exit criteria

- [ ] WI-0203 manual review is accepted on the current package and exact-revision persistence and safety hold.
- [ ] WI-0204 reversible, default-off recipe-only omission and restoration work end to end.
- [ ] No Smart/Archive/Search/global exclusion changes, no originals modified or hydrated unexpectedly, no silent effect on other collections or existing snapshots.

## Risks

dHash64 encodes coarse appearance, not semantic identity. Adjacent subgroups in a burst review set need not be visually similar; an Hamming-distance threshold or four-minute capture proximity cannot justify unattended removal. Review decisions must never cross recipe/revision boundaries.

## Evidence

- [WI-0203](../work-items/WI-0203-creative-collection-similarity-review.md)
- [Creative review operator guide](../../operations/creative-similarity-review.md)
- [M37 decision ledger](M37-decision-ledger.md)
