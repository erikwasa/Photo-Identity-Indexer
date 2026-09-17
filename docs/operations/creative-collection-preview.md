# Creative Collection anchor/context preview

WI-0119 adds a read-only preview that keeps a saved Smart Collection's exact matches as **direct anchors** and may add nearby photographs from the same WI-0118 inferred moments as **contextual additions**.

The preview never changes the saved Smart Collection filter or membership semantics. It does not persist inferred context membership and it does not select a final slideshow size; WI-0120 owns final curation.

## Open a preview

Use the saved Smart Collection identifier:

```text
GET /api/smart-collections/{collection-id}/creative-preview
```

The current default uses the WI-0118 30-minute evaluation moment policy. While WI-0118 representative verification remains deferred, the alternative evaluation gap can be inspected explicitly:

```text
GET /api/smart-collections/{collection-id}/creative-preview?momentGapMinutes=90
```

The response reports:

- `directAnchorCount`: exact Smart Collection matches;
- `addedContextCount`: same-moment additions that do not need to satisfy the Smart Collection filter;
- `totalCandidateCount`: anchor plus context candidates before WI-0120 target-size selection;
- `kind`: `direct-anchor` or `contextual-addition` for every candidate;
- `contextReasons`: moment identifier and the exact anchor revision IDs that admitted a contextual photo;
- `thumbnailUrl`: a normal collection thumbnail URL for visual inspection.

Zero exact matches produce `noAnchors: true` and an empty candidate list. The generator never broadens a zero-anchor query.

## Context policy

`m26-anchor-context-balanced-v1` admits at most six contextual photos per moment that contains at least one direct anchor. Context candidates are chosen by capture-time proximity to the direct anchors with stable timestamp/revision-ID tie breaking. Context-only photos never become new anchors, so expansion cannot recurse into a later moment.

All catalogue candidates are read through the existing Smart Collection query repository, preserving its deletion/source-visibility boundary. The preview is derived and read-only.

## Maintainer verification

Human acceptance remains intentionally separate from the automated contract. On a representative private family Smart Collection, compare the strict Smart Collection result with the Creative Collection preview and inspect several anchored moments. Confirm that at least one sequence gains understandable context such as a room, cake, pet, another family member, or scene-setting photo without pulling in unrelated later moments.

Record whether the 30-minute or 90-minute WI-0118 policy produces more coherent boundaries. If context feels too dense or too sparse, tune the versioned context policy rather than exposing raw viewer knobs.
