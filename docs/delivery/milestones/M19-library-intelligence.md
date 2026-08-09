---
id: M19
title: Photo metadata and semantic collections
status_source: ../status/milestones.yaml
depends_on: [M12, M14]
---

# M19: Photo metadata and semantic collections

## Outcome

The catalogue can organize photos using information beyond face identity: photographic capture metadata, location and, when a viable local tagging approach has been established, visible-content tags.

## Work items

- [WI-0049](../work-items/WI-0049-visible-content-tagging-experiment.md) — experiment with local visible-content tagging and record a production recommendation
- [WI-0050](../work-items/WI-0050-exif-smart-collections.md) — ingest EXIF capture metadata and create reusable smart collections from metadata and available tags

## Conditional tag integration

WI-0050 does not wait for WI-0049. If a production tag representation exists when WI-0050 is implemented, smart collections include tag predicates in the same slice. Otherwise WI-0050 ships capture-date/location collections with a documented tag extension point, and tag predicates are added after the tagging capability is selected.

## Exit criteria

- Capture time is stored as photographic local time when that is what the source metadata provides rather than being falsely normalized to UTC.
- GPS metadata is retained when available without making location mandatory.
- Smart collections can be saved and reevaluated as catalogue contents change.
- The tagging experiment produces evidence about usefulness, runtime, storage and model-governance implications before a production model is selected.
